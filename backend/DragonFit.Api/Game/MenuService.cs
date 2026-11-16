using DragonFit.Api.Data;
using DragonFit.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DragonFit.Api.Game;

public record MenuItemInput(int FoodId, decimal Servings, string Slot);

/// <summary>Daily menu gacha: one free roll a day, rerolls cost 30 Gems; the player tunes it, confirms it, and logs each dish with one tap.</summary>
public class MenuService(AppDbContext db, TimeProvider clock, QuestService quests, MealService meals)
{
    public const int RerollCost = 30;

    public async Task<object> TodayAsync(string userId)
    {
        var (character, profile, today, _) = await quests.LoadAsync(userId);
        var draw = await db.MealDraws.Where(d => d.UserId == userId && d.Date == today).OrderByDescending(d => d.RollNo)
            .Include(d => d.Items).ThenInclude(i => i.Food).FirstOrDefaultAsync();
        var targets = await TargetsAsync(character, profile, today);
        return new
        {
            date = today, rerollCost = RerollCost, freeRoll = draw is null,
            targets = new { kcal = targets.Kcal, proteinG = targets.ProteinG },
            draw = draw is null ? null : Dto(draw, targets.Kcal, targets.ProteinG),
        };
    }

    static object Dto(MealDraw d, int targetKcal, int targetProtein)
    {
        var kcal = (int)Math.Round(d.Items.Sum(i => i.Food!.Kcal * i.Servings));
        var protein = (int)Math.Round(d.Items.Sum(i => i.Food!.ProteinG * i.Servings));
        return new
        {
            d.Id, d.RollNo, status = d.Status.ToString(), d.CostGems,
            totals = new { kcal, proteinG = protein, priceVnd = (int)Math.Round(d.Items.Sum(i => i.Food!.PriceVnd * i.Servings)) },
            onTarget = SettlementRules.KcalOnTarget(targetKcal, kcal),
            proteinOk = protein >= targetProtein * 0.9,
            items = d.Items.OrderBy(i => SlotOrder(i.Slot)).ThenBy(i => i.Id).Select(i => new
            {
                i.Id, i.Slot, i.Servings, i.Rarity, logged = i.MealLogId != null,
                food = new { i.Food!.Id, i.Food.NameVi, i.Food.Serving, i.Food.Kcal, i.Food.ProteinG },
                kcal = (int)Math.Round(i.Food.Kcal * i.Servings), proteinG = Math.Round(i.Food.ProteinG * i.Servings, 1),
            }),
        };
    }

    static int SlotOrder(string s) => Array.IndexOf(MealService.Slots, s);

    async Task<(int Kcal, int ProteinG)> TargetsAsync(Character character, PlayerProfile profile, DateOnly day)
    {
        var weight = await db.BodyMetrics.Where(m => m.UserId == character.UserId && m.WeightKg != null)
            .OrderByDescending(m => m.Date).Select(m => m.WeightKg).FirstOrDefaultAsync() ?? 60m;
        var t = Nutrition.Calculate(profile.Sex, weight, profile.HeightCm, day.Year - profile.BirthYear, profile.TrainingDaysPerWeek, profile.Goal);
        return (t.TargetKcal, t.ProteinG);
    }

    public async Task<object> RollAsync(string userId)
    {
        var (character, profile, today, _) = await quests.LoadAsync(userId);
        var last = await db.MealDraws.Where(d => d.UserId == userId && d.Date == today).MaxAsync(d => (int?)d.RollNo) ?? 0;
        if (await db.MealDraws.AnyAsync(d => d.UserId == userId && d.Date == today && d.Status == MealDrawStatus.Confirmed))
            throw new GameException("Thực đơn hôm nay đã chốt.");
        var cost = last == 0 ? 0 : RerollCost;

        await using var tx = await db.Database.BeginTransactionAsync();
        if (cost > 0)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Wallets\" WHERE \"UserId\" = {userId} FOR UPDATE");
            var wallet = await db.Wallets.FindAsync(userId);
            if (wallet is not null) await db.Entry(wallet).ReloadAsync();
            if (wallet is null || wallet.Gems < cost) throw new GameException($"Quay lại cần {cost} Gem.");
            wallet.Gems -= cost;
            db.GemTransactions.Add(new GemTransaction { UserId = userId, Amount = -cost, Reason = "menu_reroll" });
        }

        var targets = await TargetsAsync(character, profile, today);
        var foods = await db.Foods.Where(f => f.IsActive).ToListAsync();
        var result = MenuGacha.Draw(foods, targets.Kcal, targets.ProteinG, profile.Dislikes, profile.FoodBudget, Random.Shared);
        var draw = new MealDraw
        {
            UserId = userId, Date = today, RollNo = last + 1, TargetKcal = targets.Kcal, TargetProteinG = targets.ProteinG,
            CostGems = cost, CreatedAt = clock.GetUtcNow().UtcDateTime,
            Items = result.Items.Select(p => new MealDrawItem { FoodId = p.Food.Id, Servings = p.Servings, Slot = p.Slot, Rarity = p.Rarity }).ToList(),
        };
        db.MealDraws.Add(draw);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { throw new GameException("Đang quay, hãy thử lại."); }
        await tx.CommitAsync();
        return await TodayAsync(userId);
    }

    async Task<MealDraw> OwnDrawAsync(string userId, long drawId)
    {
        var (_, _, today, _) = await quests.LoadAsync(userId);
        var draw = await db.MealDraws.Include(d => d.Items).ThenInclude(i => i.Food)
                       .FirstOrDefaultAsync(d => d.Id == drawId && d.UserId == userId)
                   ?? throw new GameException("Không tìm thấy thực đơn.", 404);
        if (draw.Date != today) throw new GameException("Thực đơn này không còn dùng được.");
        return draw;
    }

    /// <summary>The nutrition scale: replace the menu's dishes before confirming. Totals and warnings update on the client.</summary>
    public async Task<object> UpdateItemsAsync(string userId, long drawId, List<MenuItemInput> items)
    {
        var draw = await OwnDrawAsync(userId, drawId);
        if (draw.Status == MealDrawStatus.Confirmed) throw new GameException("Thực đơn đã chốt.");
        if (items.Count > 15) throw new GameException("Tối đa 15 món.");
        var ids = items.Select(i => i.FoodId).Distinct().ToList();
        var foods = await db.Foods.Where(f => ids.Contains(f.Id) && f.IsActive).ToDictionaryAsync(f => f.Id);
        if (items.Any(i => !foods.ContainsKey(i.FoodId) || i.Servings is <= 0 or > 10 || !MealService.Slots.Contains(i.Slot)))
            throw new GameException("Món, số phần hoặc bữa ăn không hợp lệ.");

        db.MealDrawItems.RemoveRange(draw.Items);
        draw.Items = items.Select(i => new MealDrawItem
        {
            FoodId = i.FoodId, Food = foods[i.FoodId], Servings = i.Servings, Slot = i.Slot, Rarity = MenuGacha.Rarity(foods[i.FoodId]),
        }).ToList();
        await db.SaveChangesAsync();
        return Dto(draw, draw.TargetKcal, draw.TargetProteinG);
    }

    public async Task<object> ConfirmAsync(string userId, long drawId)
    {
        var draw = await OwnDrawAsync(userId, drawId);
        draw.Status = MealDrawStatus.Confirmed;
        await db.SaveChangesAsync();
        return Dto(draw, draw.TargetKcal, draw.TargetProteinG);
    }

    /// <summary>One tap: the dish was eaten, so it goes into the meal log (and feeds the protein quest).</summary>
    public async Task<RewardResult?> LogItemAsync(string userId, long itemId)
    {
        var item = await db.MealDrawItems.FirstOrDefaultAsync(i => i.Id == itemId) ?? throw new GameException("Không tìm thấy món.", 404);
        var draw = await OwnDrawAsync(userId, item.MealDrawId);
        if (item.MealLogId is not null) throw new GameException("Món này đã được ghi.");
        var reward = await meals.AddAsync(userId, new AddMealRequest(item.FoodId, item.Servings, item.Slot, draw.Date));
        item.MealLogId = await db.MealLogs.Where(m => m.UserId == userId && m.Date == draw.Date && m.FoodId == item.FoodId)
            .OrderByDescending(m => m.Id).Select(m => m.Id).FirstAsync();
        await db.SaveChangesAsync();
        return reward;
    }
}
