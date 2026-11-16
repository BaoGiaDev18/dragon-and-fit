using DragonFit.Api.Data;
using DragonFit.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DragonFit.Api.Game;

public record AddMealRequest(int FoodId, decimal Servings, string Slot, DateOnly? Date);
public record BodyMetricRequest(decimal? WeightKg, decimal? WaistCm, decimal? SleepHours, int? EnergyScore);

/// <summary>Meal log and daily weigh-in (Phase 2). Both feed the 4:00 settlement and the stats.</summary>
public class MealService(AppDbContext db, TimeProvider clock, QuestService quests)
{
    public static readonly string[] Slots = ["breakfast", "lunch", "dinner", "snack"];

    public async Task<object> SearchFoodsAsync(string? q)
    {
        var query = db.Foods.Where(f => f.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(f => f.NameVi.ToLower().Contains(term) || f.Code.Contains(term));
        }
        return await query.OrderBy(f => f.NameVi).Take(60)
            .Select(f => new { f.Id, f.Code, f.NameVi, f.Serving, f.Kcal, f.ProteinG, f.CarbG, f.FatG, f.PriceVnd, f.Tags })
            .ToListAsync();
    }

    /// <summary>The meal log of an open day (today, or yesterday until 4:00) with totals against the targets.</summary>
    public async Task<object> GetDayAsync(string userId, DateOnly? date = null)
    {
        var (character, profile, today, _) = await quests.LoadAsync(userId);
        var day = date ?? today;
        var clockDay = DayClock.For(clock, profile.TimeZoneId);
        var logs = await db.MealLogs.Where(m => m.UserId == userId && m.Date == day).Include(m => m.Food)
            .OrderBy(m => m.CreatedAt).ToListAsync();
        var targets = await TargetsAsync(character, profile, day);
        var kcal = (int)Math.Round(logs.Sum(m => m.Food!.Kcal * m.Servings));
        var protein = (int)Math.Round(logs.Sum(m => m.Food!.ProteinG * m.Servings));
        return new
        {
            date = day,
            open = clockDay.IsOpen(day),
            targets = new { kcal = targets.Kcal, proteinG = targets.ProteinG, burnedKcal = targets.Burned },
            totals = new
            {
                kcal, proteinG = protein,
                carbG = (int)Math.Round(logs.Sum(m => m.Food!.CarbG * m.Servings)),
                fatG = (int)Math.Round(logs.Sum(m => m.Food!.FatG * m.Servings)),
            },
            onTarget = logs.Count > 0 && SettlementRules.KcalOnTarget(targets.Kcal, kcal),
            logs = logs.Select(m => new
            {
                m.Id, m.Slot, m.Servings, food = new { m.Food!.Id, m.Food.NameVi, m.Food.Serving },
                kcal = (int)Math.Round(m.Food.Kcal * m.Servings), proteinG = Math.Round(m.Food.ProteinG * m.Servings, 1),
            }),
        };
    }

    /// <summary>Same target the settlement will use: Nutrition target + half of today's estimated training burn.</summary>
    async Task<(int Kcal, int ProteinG, int Burned)> TargetsAsync(Character character, PlayerProfile profile, DateOnly day)
    {
        var weight = await db.BodyMetrics.Where(m => m.UserId == character.UserId && m.Date <= day && m.WeightKg != null)
            .OrderByDescending(m => m.Date).Select(m => m.WeightKg).FirstOrDefaultAsync() ?? 60m;
        var t = Nutrition.Calculate(profile.Sex, weight, profile.HeightCm, day.Year - profile.BirthYear, profile.TrainingDaysPerWeek, profile.Goal);
        var burned = (int)Math.Round(await db.QuestInstances
            .Where(q => q.CharacterId == character.Id && q.Date == day && q.Exercise != null && q.DoneValue != null)
            .SumAsync(q => q.Exercise!.KcalPerRep * q.DoneValue!.Value * q.Sets));
        return (t.TargetKcal + burned / 2, t.ProteinG, burned);
    }

    public async Task<RewardResult?> AddAsync(string userId, AddMealRequest req)
    {
        var (character, profile, today, _) = await quests.LoadAsync(userId);
        var day = req.Date ?? today;
        if (!DayClock.For(clock, profile.TimeZoneId).IsOpen(day)) throw new GameException("Ngày này đã chốt sổ lúc 4:00.");
        if (!Slots.Contains(req.Slot)) throw new GameException("Bữa ăn không hợp lệ.");
        if (req.Servings is <= 0 or > 10) throw new GameException("Số phần từ 0,25 đến 10.");
        if (!await db.Foods.AnyAsync(f => f.Id == req.FoodId && f.IsActive)) throw new GameException("Không tìm thấy món.", 404);

        db.MealLogs.Add(new MealLog { UserId = userId, Date = day, Slot = req.Slot, FoodId = req.FoodId, Servings = req.Servings });
        await db.SaveChangesAsync();
        return await SyncProteinAsync(character, day);
    }

    public async Task DeleteAsync(string userId, long id)
    {
        var (character, profile, _, _) = await quests.LoadAsync(userId);
        var log = await db.MealLogs.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId) ?? throw new GameException("Không tìm thấy.", 404);
        if (!DayClock.For(clock, profile.TimeZoneId).IsOpen(log.Date)) throw new GameException("Ngày này đã chốt sổ lúc 4:00.");
        db.MealLogs.Remove(log);
        await db.SaveChangesAsync();
        await SyncProteinAsync(character, log.Date);
    }

    async Task<RewardResult?> SyncProteinAsync(Character character, DateOnly day)
    {
        var protein = (int)Math.Round(await db.MealLogs.Where(m => m.UserId == character.UserId && m.Date == day)
            .SumAsync(m => m.Food!.ProteinG * m.Servings));
        return await quests.SyncProteinAsync(character, day, protein);
    }

    /// <summary>Today's weigh-in. Weight and waist feed VIT and the kcal target; sleep feeds healing; energy is MP.</summary>
    public async Task SaveBodyMetricAsync(string userId, BodyMetricRequest r)
    {
        var (_, _, today, _) = await quests.LoadAsync(userId);
        if (r.WeightKg is < 30 or > 250) throw new GameException("Cân nặng từ 30 đến 250 kg.");
        if (r.WaistCm is < 40 or > 200) throw new GameException("Vòng bụng từ 40 đến 200 cm.");
        if (r.SleepHours is < 0 or > 16) throw new GameException("Giờ ngủ từ 0 đến 16.");
        if (r.EnergyScore is < 1 or > 5) throw new GameException("Năng lượng từ 1 đến 5.");

        var m = await db.BodyMetrics.FirstOrDefaultAsync(x => x.UserId == userId && x.Date == today);
        if (m is null) { m = new BodyMetric { UserId = userId, Date = today }; db.BodyMetrics.Add(m); }
        if (r.WeightKg is not null) m.WeightKg = r.WeightKg;
        if (r.WaistCm is not null) m.WaistCm = r.WaistCm;
        if (r.SleepHours is not null) m.SleepHours = r.SleepHours;
        if (r.EnergyScore is not null) m.EnergyScore = r.EnergyScore;
        await db.SaveChangesAsync();
    }
}
