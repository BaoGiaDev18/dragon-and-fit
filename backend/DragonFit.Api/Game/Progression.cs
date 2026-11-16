namespace DragonFit.Api.Game;

/// <summary>Pure rules for levels, streaks and evolution (B2). No database access, easy to unit test.</summary>
public static class Progression
{
    public const int MaxLevel = 99;
    public static readonly int[] EvolutionLevels = [1, 20, 40, 60, 80, 99];

    /// <summary>XP needed to go from level L to L+1: 40 × L.</summary>
    public static int XpToNext(int level) => 40 * level;

    /// <summary>Total XP needed to reach level L (sum of 40·k for k = 1..L-1 = 20·L·(L-1)).</summary>
    public static long CumulativeXp(int level) => 20L * level * (level - 1);

    public static int LevelFromXp(long totalXp)
    {
        var level = 1;
        while (level < MaxLevel && CumulativeXp(level + 1) <= totalXp) level++;
        return level;
    }

    public static decimal StreakMultiplier(int streakDays) => streakDays switch
    {
        >= 30 => 1.25m,
        >= 7 => 1.1m,
        _ => 1m,
    };

    public static int EvolutionStage(int level)
    {
        var stage = 1;
        for (var i = 0; i < EvolutionLevels.Length; i++)
            if (level >= EvolutionLevels[i]) stage = i + 1;
        return stage;
    }

    /// <summary>XP progress inside the current level, for the XP bar.</summary>
    public static (long Into, int Needed) LevelProgress(long totalXp)
    {
        var level = LevelFromXp(totalXp);
        if (level >= MaxLevel) return (0, 0);
        return (totalXp - CumulativeXp(level), XpToNext(level));
    }

    /// <summary>Quest XP scales with exercise tier: ×1.0 at tier 1 up to ×1.5 at tier 5.</summary>
    public static decimal TierMultiplier(int tier) => 1m + 0.125m * (Math.Clamp(tier, 1, 5) - 1);
}
