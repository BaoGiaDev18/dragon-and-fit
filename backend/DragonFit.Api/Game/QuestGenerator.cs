using DragonFit.Api.Domain;

namespace DragonFit.Api.Game;

public record QuestDraft(
    string TemplateCode, int Slot, int? ExerciseId, int Sets, int TargetValue, string Unit,
    string Title, string Narration, int Xp, int Gems, bool IsRequired, string? BossKind = null);

public record LastTarget(int TargetValue, bool Completed);

public record QuestContext(
    DateOnly Date,
    int TrainingDaysPerWeek,
    int SessionMinutes,
    IReadOnlyCollection<string> Equipment,
    IReadOnlyCollection<string> ExcludedBodyParts,
    IReadOnlyDictionary<string, int> GroupTier,              // push/legs/pull/core → tier the player can do
    IReadOnlyDictionary<int, int> BaselineReps,             // exerciseId → max reps (or seconds) from the first trial
    IReadOnlyDictionary<int, LastTarget> LastTargets,       // exerciseId → last target given and whether it was finished
    int? BaselineSteps,
    int ProteinTargetG,
    int HpPercent,
    IReadOnlyDictionary<int, int>? BestValues = null);     // exerciseId → best reps/seconds in the last 28 days (bosses)

/// <summary>
/// Rule-based daily quest generator for the MVP (Phase 1). The AI Dungeon Master (Phase 4)
/// will choose and narrate from the same allowed list; these rules stay as the validator and fallback.
/// </summary>
public static class QuestGenerator
{
    public static bool IsTrainingDay(DateOnly date, int daysPerWeek)
    {
        var d = date.DayOfWeek;
        return daysPerWeek switch
        {
            >= 5 => d is >= DayOfWeek.Monday and <= DayOfWeek.Friday,
            4 => d is DayOfWeek.Monday or DayOfWeek.Tuesday or DayOfWeek.Thursday or DayOfWeek.Friday,
            _ => d is DayOfWeek.Monday or DayOfWeek.Wednesday or DayOfWeek.Friday,
        };
    }

    public static IEnumerable<Exercise> Allowed(IEnumerable<Exercise> all, QuestContext ctx) =>
        all.Where(e => (e.Equipment == "none" || ctx.Equipment.Contains(e.Equipment))
                    && !e.BodyPart.Split(',', StringSplitOptions.RemoveEmptyEntries).Any(ctx.ExcludedBodyParts.Contains));

    public static Exercise? Pick(IEnumerable<Exercise> allowed, string group, int wantedTier)
    {
        var inGroup = allowed.Where(e => e.Group == group).ToList();
        return inGroup.Where(e => e.Tier <= wantedTier).OrderByDescending(e => e.Tier).FirstOrDefault()
            ?? inGroup.OrderBy(e => e.Tier).FirstOrDefault();
    }

    public static List<QuestDraft> Generate(IReadOnlyCollection<Exercise> exercises, QuestContext ctx)
    {
        var allowed = Allowed(exercises, ctx).ToList();
        var drafts = new List<QuestDraft>();
        var slot = 1;
        var lowHp = ctx.HpPercent < 30;

        if (IsTrainingDay(ctx.Date, ctx.TrainingDaysPerWeek))
        {
            var groups = new List<string> { "push", "legs" };
            if (ctx.SessionMinutes >= 45) groups.AddRange(["pull", "core"]);
            else groups.Add(ctx.Date.DayNumber % 2 == 0 ? "pull" : "core");

            foreach (var group in groups)
            {
                var ex = Pick(allowed, group, ctx.GroupTier.GetValueOrDefault(group, 1));
                if (ex is null) continue;
                var target = TargetFor(ex, ctx);
                if (lowHp) target = Math.Max(ex.Unit == "seconds" ? 10 : 3, (int)Math.Round(target * 0.7));
                var tpl = $"daily_{group}";
                var baseXp = group == "core" ? 30 : 40;
                var baseGems = group == "core" ? 15 : 20;
                drafts.Add(new QuestDraft(tpl, slot++, ex.Id, 3, target, ex.Unit,
                    $"3 × {target} {(ex.Unit == "seconds" ? "giây " : "")}{ex.NameVi}",
                    Narrate(group, ctx.Date),
                    (int)Math.Round(baseXp * Progression.TierMultiplier(ex.Tier)), baseGems, true));
            }
        }
        else
        {
            var mob = allowed.FirstOrDefault(e => e.Group == "mobility");
            if (mob is not null)
                drafts.Add(new QuestDraft("daily_mobility", slot++, mob.Id, 1, 600, "seconds",
                    "10 phút giãn cơ toàn thân", Narrate("mobility", ctx.Date), 25, 10, true));
        }

        var steps = Math.Clamp(RoundTo((ctx.BaselineSteps ?? 5000) + 1000, 500), 5000, 12000);
        drafts.Add(new QuestDraft("daily_steps", slot++, null, 1, steps, "steps",
            $"Đi bộ {steps:N0} bước".Replace(',', '.'), Narrate("steps", ctx.Date), 30, 15, true));
        drafts.Add(new QuestDraft("daily_protein", slot, null, 1, ctx.ProteinTargetG, "g",
            $"Nạp đủ {ctx.ProteinTargetG} g protein", Narrate("protein", ctx.Date), 30, 15, true));
        drafts.AddRange(BossDrafts(allowed, ctx));
        return drafts;
    }

    public static readonly string[] BossGroups = ["push", "legs", "pull", "core"];

    /// <summary>Weekly boss on Sunday (one group, rotating), monthly boss on the last day of the month (all four groups).</summary>
    public static bool IsWeeklyBossDay(DateOnly d) => d.DayOfWeek == DayOfWeek.Sunday;
    public static bool IsMonthlyBossDay(DateOnly d) => d.AddDays(1).Day == 1;

    /// <summary>
    /// Boss target = the player's best single set of the last 28 days + 8% (weekly) or + 10% (monthly),
    /// at least one more than the best (plan: "a little harder than usual"). One all-out set.
    /// </summary>
    public static int BossTarget(int best, decimal factor) => Math.Max(best + 1, (int)Math.Ceiling(best * factor));

    static IEnumerable<QuestDraft> BossDrafts(List<Exercise> allowed, QuestContext ctx)
    {
        QuestDraft? Boss(string group, string kind, int slot, decimal factor, int xp, int gems)
        {
            var ex = Pick(allowed, group, ctx.GroupTier.GetValueOrDefault(group, 1));
            if (ex is null) return null;
            var best = ctx.BestValues?.GetValueOrDefault(ex.Id) ?? 0;
            if (best <= 0) best = ctx.BaselineReps.GetValueOrDefault(ex.Id);
            if (best <= 0) best = TargetFor(ex, ctx);
            var target = BossTarget(best, factor);
            var unit = ex.Unit == "seconds" ? "giây " : "";
            var title = kind == "weekly" ? $"BOSS TUẦN · {target} {unit}{ex.NameVi} liên tục" : $"BOSS THÁNG · {target} {unit}{ex.NameVi} liên tục";
            var line = kind == "weekly"
                ? $"Một kẻ địch mang vương miện chặn đường. Kỷ lục của ngươi là {best}; hôm nay phải vượt qua nó."
                : $"Rồng Than Hồng thức giấc cuối tháng. Vượt kỷ lục {best} của ngươi để hạ nó.";
            return new QuestDraft($"boss_{kind}", slot, ex.Id, 1, target, ex.Unit, title, line, xp, gems, false, kind);
        }

        if (IsWeeklyBossDay(ctx.Date))
        {
            var group = BossGroups[ctx.Date.DayNumber / 7 % BossGroups.Length];
            if (Boss(group, "weekly", 20, 1.08m, 200, 100) is { } w) yield return w;
        }
        if (IsMonthlyBossDay(ctx.Date))
            for (var i = 0; i < BossGroups.Length; i++)
                if (Boss(BossGroups[i], "monthly", 30 + i, 1.10m, 150, 75) is { } m) yield return m;
    }

    /// <summary>
    /// Reps per set: +1 after a finished quest, same target after a missed one, never more than
    /// 20% above the last target (B3). First time: 60% of the trial result (70% for holds).
    /// </summary>
    public static int TargetFor(Exercise ex, QuestContext ctx)
    {
        var min = ex.Unit == "seconds" ? 15 : 5;
        var max = ex.Unit == "seconds" ? 120 : 30;
        if (ctx.LastTargets.TryGetValue(ex.Id, out var last))
        {
            var step = ex.Unit == "seconds" ? 5 : 1;
            var next = last.Completed ? last.TargetValue + step : last.TargetValue;
            return Math.Clamp(Math.Min(next, (int)Math.Floor(last.TargetValue * 1.2m) + step), min, max);
        }
        if (ctx.BaselineReps.TryGetValue(ex.Id, out var baseline) && baseline > 0)
            return Math.Clamp((int)Math.Round(baseline * (ex.Unit == "seconds" ? 0.7m : 0.6m)), min, max);
        return ex.Unit == "seconds" ? 20 : 8;
    }

    /// <summary>Tier per group from the first trial (B8 step 5).</summary>
    public static Dictionary<string, int> TiersFromTrial(string? pushType, int? pushReps, int? squatReps, int? plankSec, int? rowReps)
    {
        var push = pushType switch { "pushup" => 4, "knee_pushup" => 3, "wall_pushup" => 1, _ => 2 };
        if (pushReps >= 20) push = Math.Min(5, push + 1);
        var legs = squatReps switch { null => 2, < 10 => 1, < 25 => 2, < 40 => 3, _ => 4 };
        var core = plankSec switch { null => 1, < 20 => 1, < 60 => 2, < 90 => 3, _ => 4 };
        var pull = rowReps switch { null => 2, < 8 => 1, < 20 => 2, _ => 3 };
        return new() { ["push"] = push, ["legs"] = legs, ["core"] = core, ["pull"] = pull };
    }

    static readonly Dictionary<string, string[]> Lines = new()
    {
        ["push"] =
        [
            "Một toán quân bóng tối đang áp sát cổng thành. Hãy đẩy lùi chúng bằng đôi tay của ngươi!",
            "Tảng đá chắn lối vào hang rồng. Chỉ sức đẩy của một chiến binh mới dịch được nó.",
            "Khiên của ngươi phải đủ vững để đỡ ngọn lửa đầu tiên. Rèn đôi vai đi, chiến binh.",
        ],
        ["legs"] =
        [
            "Con đường lên núi Than Hồng rất dốc. Đôi chân ngươi sẽ quyết định ngươi có đến nơi hay không.",
            "Lính canh đang tuần tra. Hạ thấp người, di chuyển chắc chắn, đừng để chúng phát hiện.",
            "Cầu treo rung lắc dưới chân. Giữ trọng tâm, từng bước một.",
        ],
        ["pull"] =
        [
            "Đồng đội đang treo lơ lửng bên vách đá. Kéo họ lên, chiến binh!",
            "Cánh cổng sắt bị kẹt. Dùng sức lưng mà kéo nó mở ra.",
            "Sợi xích của chiếc rương báu rất nặng. Kéo nó lên khỏi hố sâu.",
        ],
        ["core"] =
        [
            "Gió bão quất ngang chiến trường. Giữ vững thân mình như một bức tường đá.",
            "Mũi tên lao tới từ mọi phía. Chỉ một thân thể vững chãi mới đứng yên được.",
            "Hãy trụ vững trên thuyền giữa cơn sóng dữ.",
        ],
        ["mobility"] = ["Hôm nay quân đội nghỉ ngơi. Một chiến binh khôn ngoan biết chăm sóc cơ thể mình."],
        ["steps"] =
        [
            "Ngôi làng bên kia thung lũng đang cần tin tức. Hãy đi bộ mang thư đến cho họ.",
            "Bản đồ kho báu chỉ về phía rừng xa. Mỗi bước chân đưa ngươi đến gần hơn.",
        ],
        ["protein"] =
        [
            "Lò rèn cần nguyên liệu để đúc lại bộ giáp của ngươi. Hãy nạp đủ đạm hôm nay.",
            "Cơ bắp được rèn từ thức ăn. Đừng để lò rèn nguội lửa.",
        ],
    };

    static string Narrate(string key, DateOnly date)
    {
        var lines = Lines[key];
        return lines[date.DayNumber % lines.Length];
    }

    static int RoundTo(int value, int step) => (int)Math.Round(value / (double)step) * step;
}
