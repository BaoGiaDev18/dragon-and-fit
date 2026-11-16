using DragonFit.Api.Data;
using DragonFit.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DragonFit.Api.Game;

/// <summary>
/// Closes finished days: compares what was eaten and trained with the targets, applies damage or
/// healing, and kills the character at 0 HP (plan B10). It runs from the 4:00 job and also lazily
/// whenever the player opens the game, catching up any days the job missed. The character row is
/// locked for the whole catch-up and each day is unique per character, so HP is never taken twice.
/// </summary>
public class SettlementService(AppDbContext db, TimeProvider clock)
{
    const int MaxCatchUpDays = 30;
    public const int PurgeAfterDays = 30;

    public async Task CatchUpAsync(Character character, PlayerProfile profile)
    {
        if (character.Status != CharacterStatus.Alive) return;
        var day = DayClock.For(clock, profile.TimeZoneId);
        var lastClosed = day.LastClosedDay;
        var created = DayClock.LocalDate(character.CreatedAt, profile.TimeZoneId);

        var lastSettled = await db.DailySettlements.Where(s => s.CharacterId == character.Id)
            .MaxAsync(s => (DateOnly?)s.Date);
        var from = lastSettled?.AddDays(1) ?? created;
        if (from > lastClosed) return;
        if (lastClosed.DayNumber - from.DayNumber >= MaxCatchUpDays) from = lastClosed.AddDays(-(MaxCatchUpDays - 1));

        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Characters\" WHERE \"Id\" = {character.Id} FOR UPDATE");
        await db.Entry(character).ReloadAsync();
        lastSettled = await db.DailySettlements.Where(s => s.CharacterId == character.Id).MaxAsync(s => (DateOnly?)s.Date);
        if (lastSettled >= from) from = lastSettled.Value.AddDays(1);

        for (var d = from; d <= lastClosed && character.Status == CharacterStatus.Alive; d = d.AddDays(1))
            await SettleDayAsync(character, profile, d);

        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    async Task SettleDayAsync(Character character, PlayerProfile profile, DateOnly date)
    {
        var meals = await db.MealLogs.Where(m => m.UserId == character.UserId && m.Date == date)
            .Select(m => new { m.Servings, m.Food!.Kcal, m.Food.ProteinG }).ToListAsync();
        var quests = await db.QuestInstances.Where(q => q.CharacterId == character.Id && q.Date == date && q.IsRequired)
            .Select(q => new { q.Title, q.TargetValue, q.DoneValue, q.Status, q.Sets, Kcal = q.Exercise != null ? q.Exercise.KcalPerRep : 0 })
            .ToListAsync();

        var weight = await db.BodyMetrics.Where(m => m.UserId == character.UserId && m.Date <= date && m.WeightKg != null)
            .OrderByDescending(m => m.Date).Select(m => m.WeightKg).FirstOrDefaultAsync() ?? 60m;
        var targets = Nutrition.Calculate(profile.Sex, weight, profile.HeightCm, date.Year - profile.BirthYear, profile.TrainingDaysPerWeek, profile.Goal);
        // Half of the estimated training burn is added back (estimates usually run high).
        var burned = (int)Math.Round(quests.Sum(q => q.Kcal * (q.DoneValue ?? 0) * q.Sets));
        var targetKcal = targets.TargetKcal + burned / 2;
        var actualKcal = (int)Math.Round(meals.Sum(m => m.Kcal * m.Servings));
        var protein = (int)Math.Round(meals.Sum(m => m.ProteinG * m.Servings));

        var sleep = await db.BodyMetrics.Where(m => m.UserId == character.UserId && m.Date == date).Select(m => m.SleepHours).FirstOrDefaultAsync();
        var previous = await db.DailySettlements.Where(s => s.CharacterId == character.Id && s.Date == date.AddDays(-1))
            .Select(s => (int?)s.PerfectStreak).FirstOrDefaultAsync() ?? 0;
        var training = QuestGenerator.IsTrainingDay(date, profile.TrainingDaysPerWeek);
        var restMode = character.RestModeFrom <= date && character.RestModeUntil >= date;

        var result = SettlementRules.Settle(new SettlementInput(
            targetKcal, actualKcal, meals.Count,
            quests.Select(q => new QuestResult(q.Title, q.TargetValue, q.DoneValue ?? 0, q.Status == QuestStatus.Completed)).ToList(),
            ScheduledRestDay: !training || character.RestPassDate == date, RestMode: restMode, MissedQuestCountIfNone: training ? 4 : 0,
            SleepHours: sleep, PreviousPerfectStreak: previous, HpBefore: character.CurrentHp));

        db.DailySettlements.Add(new DailySettlement
        {
            CharacterId = character.Id, Date = date, TargetKcal = targetKcal, ActualKcal = actualKcal, BurnedKcal = burned,
            ProteinG = protein, ProteinTargetG = targets.ProteinG, MealsLogged = meals.Count, QuestCompletion = result.QuestCompletion,
            Damage = result.Damage, Heal = result.Heal, IsPerfect = result.IsPerfect, PerfectStreak = result.PerfectStreak,
            IsRestMode = restMode, HpBefore = character.CurrentHp, HpAfter = result.HpAfter, ProcessedAt = clock.GetUtcNow().UtcDateTime,
        });
        foreach (var line in result.Lines)
            db.HpEvents.Add(new HpEvent { CharacterId = character.Id, Date = date, Amount = line.Amount, Reason = line.Reason, Detail = line.Detail });

        character.CurrentHp = result.HpAfter;
        if (character.CurrentHp <= 0) await KillAsync(character);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// 0 HP: the character is locked at once and deleted after 30 days (the window only exists to fix
    /// a scoring bug, not to revive). Gems go with it; health records stay with the account.
    /// </summary>
    async Task KillAsync(Character character)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        character.Status = CharacterStatus.Dead;
        character.DiedAt = now;
        character.PurgeAt = now.AddDays(PurgeAfterDays);
        var wallet = await db.Wallets.FindAsync(character.UserId);
        if (wallet is { Gems: > 0 })
        {
            db.GemTransactions.Add(new GemTransaction { UserId = character.UserId, Amount = -wallet.Gems, Reason = "death", RefId = character.Id.ToString() });
            wallet.Gems = 0;
        }
    }
}
