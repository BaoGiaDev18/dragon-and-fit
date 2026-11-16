using DragonFit.Api.Data;
using DragonFit.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DragonFit.Api.Game;

/// <summary>The town shop (plan B2): utility items and the real-life rewards each player set for themselves.</summary>
public class ShopService(AppDbContext db, TimeProvider clock, QuestService quests)
{
    public async Task<object> GetAsync(string userId)
    {
        var (character, _, today, _) = await quests.LoadAsync(userId);
        var wallet = await db.Wallets.FindAsync(userId);
        var items = await db.ShopItems.Where(i => i.IsActive && (i.OwnerUserId == null || i.OwnerUserId == userId))
            .OrderBy(i => i.Kind).ThenBy(i => i.Price).ToListAsync();
        var bought = await db.Purchases.Where(p => p.UserId == userId)
            .GroupBy(p => p.ShopItemId).Select(g => new { g.Key, Count = g.Count(), Last = g.Max(p => p.PurchasedAt) }).ToListAsync();
        var restPasses = await UnusedRestPassesAsync(userId);

        return new
        {
            gems = wallet?.Gems ?? 0,
            restPasses,
            restPassToday = character.RestPassDate == today,
            items = items.Select(i => new
            {
                i.Id, i.Code, i.Name, i.Description, i.Kind, i.Size, i.Price,
                timesBought = bought.FirstOrDefault(b => b.Key == i.Id)?.Count ?? 0,
                lastBought = bought.FirstOrDefault(b => b.Key == i.Id)?.Last,
            }),
            history = await db.Purchases.Where(p => p.UserId == userId).OrderByDescending(p => p.PurchasedAt).Take(10)
                .Select(p => new { p.Id, p.ShopItem!.Name, p.Price, p.PurchasedAt, p.UsedForDate }).ToListAsync(),
        };
    }

    Task<int> UnusedRestPassesAsync(string userId) =>
        db.Purchases.CountAsync(p => p.UserId == userId && p.ShopItem!.Code == "rest_pass" && p.UsedForDate == null);

    public async Task<object> BuyAsync(string userId, int itemId)
    {
        await quests.LoadAsync(userId); // settles first, and refuses a dead character
        var item = await db.ShopItems.FirstOrDefaultAsync(i => i.Id == itemId && i.IsActive && (i.OwnerUserId == null || i.OwnerUserId == userId))
                   ?? throw new GameException("Không tìm thấy vật phẩm.", 404);

        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Wallets\" WHERE \"UserId\" = {userId} FOR UPDATE");
        var wallet = await db.Wallets.FindAsync(userId);
        if (wallet is not null) await db.Entry(wallet).ReloadAsync();
        if (wallet is null || wallet.Gems < item.Price)
            throw new GameException($"Cần {item.Price:N0} Gem, ngươi đang có {wallet?.Gems ?? 0:N0}.".Replace(',', '.'));

        wallet.Gems -= item.Price;
        var purchase = new Purchase { UserId = userId, ShopItemId = item.Id, Price = item.Price, PurchasedAt = clock.GetUtcNow().UtcDateTime };
        db.Purchases.Add(purchase);
        await db.SaveChangesAsync();
        db.GemTransactions.Add(new GemTransaction { UserId = userId, Amount = -item.Price, Reason = "shop", RefId = purchase.Id.ToString() });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new { gems = wallet.Gems, item.Name, item.Kind };
    }

    /// <summary>
    /// Uses one rest pass for today: no quest damage at the 4:00 settlement and the streak survives.
    /// Food still counts. Only for today, never for a day that already went badly.
    /// </summary>
    public async Task<object> UseRestPassAsync(string userId)
    {
        var (character, _, today, _) = await quests.LoadAsync(userId);
        if (character.RestPassDate == today) throw new GameException("Hôm nay đã dùng vé nghỉ rồi.");
        var pass = await db.Purchases.Where(p => p.UserId == userId && p.ShopItem!.Code == "rest_pass" && p.UsedForDate == null)
                       .OrderBy(p => p.PurchasedAt).FirstOrDefaultAsync()
                   ?? throw new GameException("Ngươi chưa có vé nghỉ. Mua ở cửa hàng với giá 300 Gem.");

        pass.UsedForDate = today;
        character.RestPassDate = today;
        // Keep the quest streak alive: today counts as a kept day without adding to it.
        if (character.LastStreakDate == today.AddDays(-1)) character.LastStreakDate = today;
        await db.SaveChangesAsync();
        return new { restPassDate = today, left = await UnusedRestPassesAsync(userId) };
    }
}
