namespace DragonFit.Api.Game;

/// <summary>Best result in one exercise group over the last 28 days.</summary>
public record GroupBest(int Tier, int Value, bool IsSeconds);

public record StatInputs(
    IReadOnlyDictionary<string, GroupBest> StrengthGroups, // push / pull / legs / core
    IReadOnlyList<decimal> RecentWaistsCm,                // newest first, up to 3
    decimal HeightCm,
    double? AvgSteps7d,
    IReadOnlyList<double> DailyCompletion,                // one entry per day in the window, 0..1
    int StreakDays,
    double MealLogRate,                                   // share of days with a meal logged
    double KcalHitRate,                                   // share of days within ±10% of the kcal target
    double ProteinHitRate);                               // share of days reaching 90% of the protein target

public record StatValues(int Str, int Vit, int End, int Dis, int Wis);

/// <summary>
/// The five character stats on a 1–100 scale (plan B1). They come only from real data and use recent
/// windows, so they go down again when training or eating habits stop. Pure functions, unit tested.
/// </summary>
public static class StatCalculator
{
    public static readonly string[] StrengthGroups = ["push", "pull", "legs", "core"];

    /// <summary>
    /// Per group: 6 × (tier − 1) + ⌊reps/5⌋ (reps capped at 30) or ⌊seconds/10⌋ for holds (capped at 60).
    /// Max 30 per group, four groups, ÷ 1.2 → 100.
    /// </summary>
    public static int GroupScore(GroupBest b)
    {
        var volume = b.IsSeconds ? Math.Min(b.Value, 60) / 10 : Math.Min(b.Value, 30) / 5;
        return 6 * (Math.Clamp(b.Tier, 1, 5) - 1) + volume;
    }

    public static int Strength(IReadOnlyDictionary<string, GroupBest> groups) =>
        Clamp(Math.Round(StrengthGroups.Sum(g => groups.TryGetValue(g, out var b) ? GroupScore(b) : 0) / 1.2));

    /// <summary>
    /// Waist-to-height ratio from the median of the last 3 measurements: 100 − 170 × (W/H − 0.42).
    /// Capped at 100: going below 0.42 earns nothing more, so the game never rewards getting too thin.
    /// About +1 point per cm off the waist for a 170 cm player.
    /// </summary>
    public static int Vitality(IReadOnlyList<decimal> waists, decimal heightCm)
    {
        if (waists.Count == 0 || heightCm <= 0) return 1;
        var sorted = waists.Take(3).OrderBy(w => w).ToList();
        var median = sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
        var ratio = (double)(median / heightCm);
        return Clamp(Math.Round(100 - 170 * (ratio - 0.42)));
    }

    /// <summary>7-day average steps ÷ 150 (10 000 steps ≈ 67, 15 000 steps = 100).</summary>
    public static int Endurance(double? avgSteps) => avgSteps is null ? 1 : Clamp(Math.Round(avgSteps.Value / 150));

    /// <summary>80 × completion rate of required quests (28 days) + 20 × min(streak, 30) / 30.</summary>
    public static int Discipline(IReadOnlyList<double> dailyCompletion, int streakDays)
    {
        var rate = dailyCompletion.Count == 0 ? 0 : dailyCompletion.Average();
        return Clamp(Math.Round(80 * rate + 20 * Math.Min(streakDays, 30) / 30.0));
    }

    /// <summary>40 × days with meals logged + 30 × days on kcal target + 30 × days on protein target (28 days).</summary>
    public static int Wisdom(double logRate, double kcalRate, double proteinRate) =>
        Clamp(Math.Round(40 * logRate + 30 * kcalRate + 30 * proteinRate));

    public static StatValues Calculate(StatInputs i) => new(
        Strength(i.StrengthGroups),
        Vitality(i.RecentWaistsCm, i.HeightCm),
        Endurance(i.AvgSteps7d),
        Discipline(i.DailyCompletion, i.StreakDays),
        Wisdom(i.MealLogRate, i.KcalHitRate, i.ProteinHitRate));

    static int Clamp(double v) => (int)Math.Clamp(v, 1, 100);
}
