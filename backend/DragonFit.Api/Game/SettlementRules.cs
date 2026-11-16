namespace DragonFit.Api.Game;

public record QuestResult(string Title, int Target, int Done, bool Completed);

public record SettlementInput(
    int TargetKcal,
    int ActualKcal,
    int MealsLogged,
    IReadOnlyList<QuestResult> RequiredQuests,
    bool ScheduledRestDay,        // not a training day: quest damage is waived (B10), food damage is not
    bool RestMode,                // illness/injury mode: no damage and no healing
    int MissedQuestCountIfNone,   // training day with no quests at all (the game was never opened)
    decimal? SleepHours,
    int PreviousPerfectStreak,
    int HpBefore,
    int MaxHp = 100,
    bool BossSkipped = false);   // a boss quest was offered and not even tried

public record HpLine(int Amount, string Reason, string Detail);

public record SettlementResult(IReadOnlyList<HpLine> Lines, int Damage, int Heal, bool IsPerfect, int PerfectStreak, int HpAfter, decimal QuestCompletion);

/// <summary>Damage and healing for one finished day (plan B10). Pure, unit tested against the plan's examples.</summary>
public static class SettlementRules
{
    public const int NoFoodLogDamage = 10;
    public const int QuestDamage = 5;
    public const int MaxFoodDamage = 25;
    public const int BossSkipDamage = 10;
    public const decimal DailyCapShare = 0.30m;

    public static bool KcalOnTarget(int target, int actual) => Math.Abs(actual - target) <= target * 0.10m;

    public static SettlementResult Settle(SettlementInput i)
    {
        var lines = new List<HpLine>();
        var completion = i.RequiredQuests.Count == 0 ? 0m
            : i.RequiredQuests.Average(q => q.Completed ? 1m : Math.Min(1m, q.Done / (decimal)Math.Max(1, q.Target)));

        if (i.RestMode)
            return new SettlementResult([new HpLine(0, "RestMode", "Đang dưỡng thương: không mất máu, không hồi máu")],
                0, 0, false, i.PreviousPerfectStreak, i.HpBefore, completion);

        // Food
        if (i.MealsLogged == 0)
            lines.Add(new HpLine(-NoFoodLogDamage, "NoFoodLog", "Không ghi bữa ăn nào"));
        else
        {
            var tolerance = i.TargetKcal * 0.10m;
            var off = Math.Abs(i.ActualKcal - i.TargetKcal) - tolerance;
            if (off > 0)
            {
                var dmg = Math.Min(MaxFoodDamage, 2 * (int)Math.Ceiling(off / 100m));
                var over = i.ActualKcal > i.TargetKcal;
                lines.Add(new HpLine(-dmg, over ? "Overeat" : "Undereat",
                    $"{(over ? "Ăn lố" : "Ăn quá ít")}: {i.ActualKcal:N0} / {i.TargetKcal:N0} kcal".Replace(',', '.')));
            }
        }

        // Quests
        if (!i.ScheduledRestDay)
        {
            if (i.RequiredQuests.Count == 0 && i.MissedQuestCountIfNone > 0)
                lines.Add(new HpLine(-QuestDamage * i.MissedQuestCountIfNone, "QuestMissed", "Không mở bảng nhiệm vụ"));
            foreach (var q in i.RequiredQuests.Where(q => !q.Completed))
            {
                var ratio = Math.Min(1m, q.Done / (decimal)Math.Max(1, q.Target));
                var dmg = (int)Math.Round(QuestDamage * (1 - ratio), MidpointRounding.AwayFromZero);
                if (dmg > 0)
                    lines.Add(new HpLine(-dmg, ratio == 0 ? "QuestMissed" : "QuestPartial",
                        ratio == 0 ? $"Bỏ: {q.Title}" : $"Làm thiếu ({q.Done}/{q.Target}): {q.Title}"));
            }
        }

        if (i.BossSkipped) lines.Add(new HpLine(-BossSkipDamage, "BossSkipped", "Bỏ không đánh boss"));

        var raw = -lines.Sum(l => l.Amount);
        var cap = (int)Math.Floor(i.MaxHp * DailyCapShare);
        if (raw > cap) lines.Add(new HpLine(raw - cap, "DailyCap", $"Giới hạn mất tối đa {cap} máu mỗi ngày"));
        var damage = Math.Min(raw, cap);

        // Healing only on a perfect day: every required quest done, meals logged, kcal within ±10%.
        var perfect = i.RequiredQuests.Count > 0 && i.RequiredQuests.All(q => q.Completed)
                      && i.MealsLogged > 0 && KcalOnTarget(i.TargetKcal, i.ActualKcal);
        var streak = perfect ? i.PreviousPerfectStreak + 1 : 0;
        var heal = 0;
        if (perfect)
        {
            heal = 5 + 2 * Math.Min(streak, 5);
            lines.Add(new HpLine(heal, "PerfectDay", $"Ngày hoàn hảo thứ {streak} liên tiếp"));
            if (i.SleepHours >= 7)
            {
                heal += 2;
                lines.Add(new HpLine(2, "Sleep", $"Ngủ đủ {i.SleepHours:0.#} giờ"));
            }
        }

        var after = Math.Clamp(i.HpBefore - damage + heal, 0, i.MaxHp);
        return new SettlementResult(lines, damage, heal, perfect, streak, after, completion);
    }
}
