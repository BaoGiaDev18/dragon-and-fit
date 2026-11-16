using DragonFit.Api.Data;
using DragonFit.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DragonFit.Api.Game;

/// <summary>
/// Gathers the real data behind the five stats and keeps one snapshot per week (keyed by that
/// week's Monday). The current week's snapshot is refreshed on every call; last week's snapshot
/// stays frozen and gives the "+2 since last week" numbers.
/// </summary>
public class StatService(AppDbContext db)
{
    const int Window = 28;

    public static DateOnly WeekStart(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    /// <summary>A streak only counts if its last perfect day was today or yesterday.</summary>
    public static int EffectiveStreak(Character c, DateOnly today) =>
        c.LastStreakDate is { } last && last >= today.AddDays(-1) ? c.StreakDays : 0;

    public async Task<(StatSnapshot Current, StatSnapshot? Previous)> RefreshAsync(Character character, PlayerProfile profile, DateOnly today)
    {
        var values = StatCalculator.Calculate(await GatherAsync(character, profile, today));

        var week = WeekStart(today);
        var current = await db.StatSnapshots.FirstOrDefaultAsync(s => s.CharacterId == character.Id && s.Date == week);
        if (current is null)
        {
            current = new StatSnapshot { CharacterId = character.Id, Date = week };
            db.StatSnapshots.Add(current);
        }
        (current.Str, current.Vit, current.End, current.Dis, current.Wis) = (values.Str, values.Vit, values.End, values.Dis, values.Wis);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { /* a parallel request created this week's row; its values are just as fresh */ }

        var previous = await db.StatSnapshots.Where(s => s.CharacterId == character.Id && s.Date < week)
            .OrderByDescending(s => s.Date).FirstOrDefaultAsync();
        return (current, previous);
    }

    async Task<StatInputs> GatherAsync(Character character, PlayerProfile profile, DateOnly today)
    {
        var from = today.AddDays(-(Window - 1));
        var userId = character.UserId;

        var quests = await db.QuestInstances
            .Where(q => q.CharacterId == character.Id && q.Date >= from && q.Date <= today)
            .Select(q => new
            {
                q.Date, q.IsRequired, q.Status, q.DoneValue, q.TargetValue, Kind = q.Template!.Kind,
                Group = q.Exercise != null ? q.Exercise.Group : null,
                Tier = q.Exercise != null ? q.Exercise.Tier : 0,
                IsSeconds = q.Exercise != null && q.Exercise.Unit == "seconds",
            })
            .ToListAsync();

        // STR: best score per group from quests and from the first trial if it is recent enough.
        var strength = new Dictionary<string, GroupBest>();
        void Consider(string? group, int tier, int value, bool seconds)
        {
            if (group is null || value <= 0 || !StatCalculator.StrengthGroups.Contains(group)) return;
            var candidate = new GroupBest(tier, value, seconds);
            if (!strength.TryGetValue(group, out var best) || StatCalculator.GroupScore(candidate) > StatCalculator.GroupScore(best))
                strength[group] = candidate;
        }
        foreach (var q in quests.Where(q => q.Group != null && q.DoneValue > 0))
            Consider(q.Group, q.Tier, q.DoneValue!.Value, q.IsSeconds);

        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(-1);
        var trial = await db.WorkoutSets
            .Where(s => db.WorkoutSessions.Any(w => w.Id == s.SessionId && w.UserId == userId && w.StartedAt >= fromUtc))
            .Select(s => new { s.Exercise!.Group, s.Exercise.Tier, s.Reps, IsSeconds = s.Exercise.Unit == "seconds" })
            .ToListAsync();
        foreach (var s in trial) Consider(s.Group, s.Tier, s.Reps, s.IsSeconds);

        // VIT: last three waist measurements.
        var waists = await db.BodyMetrics.Where(m => m.UserId == userId && m.WaistCm != null)
            .OrderByDescending(m => m.Date).Take(3).Select(m => m.WaistCm!.Value).ToListAsync();

        // END: steps reported on the step quest in the last 7 days; before any report, the onboarding answer.
        var stepReports = quests.Where(q => q.Kind == "steps" && q.Date > today.AddDays(-7) && q.DoneValue > 0).Select(q => (double)q.DoneValue!.Value).ToList();
        double? avgSteps = stepReports.Count > 0 ? stepReports.Average() : profile.BaselineSteps;

        // DIS: per-day completion of required quests since the character started (max 28 days).
        // Past days with no quests at all count as 0 — not opening the game is a missed day.
        // Today counts only once every required quest is done, so an unfinished morning isn't a penalty.
        var started = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(character.CreatedAt, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(profile.TimeZoneId)));
        var dayFrom = started > from ? started : from;
        var daily = new List<double>();
        for (var d = dayFrom; d <= today; d = d.AddDays(1))
        {
            var req = quests.Where(q => q.Date == d && q.IsRequired).ToList();
            var rate = req.Count == 0 ? 0 : req.Count(q => q.Status == QuestStatus.Completed) / (double)req.Count;
            if (d == today && rate < 1) continue;
            daily.Add(rate);
        }

        // WIS: protein from the protein quest for now. Meal logging (Phase 2) will supply the log and kcal rates.
        var days = Math.Max(1, daily.Count);
        var proteinDays = quests.Count(q => q.Kind == "protein" && q.Status == QuestStatus.Completed);

        return new StatInputs(strength, waists, profile.HeightCm, avgSteps, daily, EffectiveStreak(character, today),
            MealLogRate: 0, KcalHitRate: 0, ProteinHitRate: Math.Min(1, proteinDays / (double)days));
    }
}
