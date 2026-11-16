using System.Text.Json;
using DragonFit.Api.Domain;
using DragonFit.Api.Game;
using DragonFit.Api.Onboarding;

namespace DragonFit.Tests;

public class ProgressionTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(39, 1)]
    [InlineData(40, 2)]
    [InlineData(120, 3)]
    [InlineData(194_040, 99)]
    [InlineData(10_000_000, 99)]
    public void LevelFromXp_follows_40L_curve(long xp, int level) => Assert.Equal(level, Progression.LevelFromXp(xp));

    [Fact]
    public void Level_99_needs_194040_xp() => Assert.Equal(194_040, Progression.CumulativeXp(99));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(19, 1)]
    [InlineData(20, 2)]
    [InlineData(80, 5)]
    [InlineData(99, 6)]
    public void Evolution_stage_by_level(int level, int stage) => Assert.Equal(stage, Progression.EvolutionStage(level));

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(7, 1.1)]
    [InlineData(30, 1.25)]
    public void Streak_multiplier(int days, double mult) => Assert.Equal((decimal)mult, Progression.StreakMultiplier(days));
}

public class NutritionTests
{
    [Fact]
    public void Recomposition_uses_300_kcal_deficit()
    {
        var t = Nutrition.Calculate("male", 68, 170, 27, 4, "recomposition");
        Assert.Equal(300, t.Deficit);
        Assert.Equal(t.MaintenanceKcal - 300, t.TargetKcal);
        Assert.Equal(120, t.ProteinG);
    }

    [Fact]
    public void Target_never_drops_below_floor()
    {
        var t = Nutrition.Calculate("female", 40, 150, 60, 3, "fat_loss");
        Assert.True(t.TargetKcal >= 1200);
        Assert.True(t.TargetKcal >= t.BmrKcal);
        Assert.True(t.Deficit <= 500);
    }
}

public class QuestGeneratorTests
{
    static readonly List<Exercise> Exercises =
    [
        new() { Id = 1, Code = "knee_pushup", NameVi = "Hít đất chống gối", Group = "push", BodyPart = "wrist,shoulder", Tier = 3 },
        new() { Id = 2, Code = "wall_pushup", NameVi = "Hít đất chống tường", Group = "push", BodyPart = "wrist,shoulder", Tier = 1 },
        new() { Id = 3, Code = "bodyweight_squat", NameVi = "Squat", Group = "legs", BodyPart = "knee", Tier = 2 },
        new() { Id = 4, Code = "glute_bridge", NameVi = "Nâng hông", Group = "legs", BodyPart = "hip", Tier = 1 },
        new() { Id = 5, Code = "band_row", NameVi = "Kéo dây", Group = "pull", BodyPart = "shoulder", Equipment = "band", Tier = 1 },
        new() { Id = 6, Code = "backpack_row", NameVi = "Kéo balo", Group = "pull", BodyPart = "lower_back", Tier = 2 },
        new() { Id = 7, Code = "plank", NameVi = "Plank", Group = "core", BodyPart = "wrist,shoulder", Tier = 2, Unit = "seconds" },
        new() { Id = 8, Code = "mobility_flow", NameVi = "Giãn cơ", Group = "mobility", Tier = 1, Unit = "seconds" },
    ];

    static QuestContext Ctx(DateOnly date, string[]? avoid = null, Dictionary<int, LastTarget>? last = null, int hp = 100) =>
        new(date, 3, 45, [], avoid ?? [], new Dictionary<string, int> { ["push"] = 3, ["legs"] = 2, ["pull"] = 2, ["core"] = 2 },
            new Dictionary<int, int> { [1] = 12, [3] = 20 }, last ?? [], 5500, 120, hp);

    static readonly DateOnly Monday = new(2026, 11, 16);

    [Fact]
    public void Training_day_has_four_exercises_steps_and_protein()
    {
        var q = QuestGenerator.Generate(Exercises, Ctx(Monday));
        Assert.Equal(["daily_push", "daily_legs", "daily_pull", "daily_core", "daily_steps", "daily_protein"], q.Select(x => x.TemplateCode));
        Assert.Equal(7, q[0].TargetValue);          // 60% of 12 knee push-ups
        Assert.Equal(6500, q[4].TargetValue);       // baseline steps + 1000
    }

    [Fact]
    public void Rest_day_gives_mobility_instead_of_strength()
    {
        var q = QuestGenerator.Generate(Exercises, Ctx(Monday.AddDays(1)));
        Assert.Equal("daily_mobility", q[0].TemplateCode);
        Assert.DoesNotContain(q, x => x.TemplateCode == "daily_push");
    }

    [Fact]
    public void Painful_knee_swaps_squat_for_glute_bridge_and_missing_band_is_skipped()
    {
        var q = QuestGenerator.Generate(Exercises, Ctx(Monday, avoid: ["knee"]));
        Assert.Equal(4, q.Single(x => x.TemplateCode == "daily_legs").ExerciseId);
        Assert.Equal(6, q.Single(x => x.TemplateCode == "daily_pull").ExerciseId);
    }

    [Fact]
    public void Finished_quest_adds_one_rep_missed_quest_keeps_target()
    {
        var up = QuestGenerator.Generate(Exercises, Ctx(Monday, last: new() { [1] = new(10, true) }));
        var same = QuestGenerator.Generate(Exercises, Ctx(Monday, last: new() { [1] = new(10, false) }));
        Assert.Equal(11, up[0].TargetValue);
        Assert.Equal(10, same[0].TargetValue);
    }

    [Fact]
    public void Low_hp_makes_quests_lighter_but_keeps_xp()
    {
        var normal = QuestGenerator.Generate(Exercises, Ctx(Monday));
        var weak = QuestGenerator.Generate(Exercises, Ctx(Monday, hp: 20));
        Assert.True(weak[0].TargetValue < normal[0].TargetValue);
        Assert.Equal(normal[0].Xp, weak[0].Xp);
    }
}

public class AnswerValidatorTests
{
    static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;

    static OnboardingQuestion Multi() => new()
    {
        Code = "avoid_parts", InputType = "multi_choice", IsRequired = true,
        Options = [new() { Value = "none", IsExclusive = true }, new() { Value = "knee" }, new() { Value = "wrist" }],
    };

    [Fact]
    public void Exclusive_none_cannot_be_combined() =>
        Assert.NotNull(AnswerValidator.Validate(Multi(), J("""["none","knee"]""")).Error);

    [Fact]
    public void Valid_multi_choice_passes() =>
        Assert.Null(AnswerValidator.Validate(Multi(), J("""["knee","wrist"]""")).Error);

    [Fact]
    public void Number_out_of_range_fails()
    {
        var q = new OnboardingQuestion { InputType = "number", MinValue = 120, MaxValue = 220, IsRequired = true };
        Assert.NotNull(AnswerValidator.Validate(q, J("300")).Error);
        Assert.Null(AnswerValidator.Validate(q, J("\"170,5\"")).Error);
    }

    [Fact]
    public void Quest_time_must_be_between_430_and_1100()
    {
        var q = new OnboardingQuestion { InputType = "time", MinValue = 270, MaxValue = 660, IsRequired = true };
        Assert.NotNull(AnswerValidator.Validate(q, J("\"03:00\"")).Error);
        Assert.Null(AnswerValidator.Validate(q, J("\"06:00\"")).Error);
    }

    [Fact]
    public void Reward_list_limits_three_and_known_sizes()
    {
        var q = new OnboardingQuestion { InputType = "reward_list", IsRequired = true };
        Assert.NotNull(AnswerValidator.Validate(q, J("""[{"name":"Giày","size":"huge"}]""")).Error);
        Assert.NotNull(AnswerValidator.Validate(q, J("""[{"name":"aa","size":"small"},{"name":"bb","size":"small"},{"name":"cc","size":"small"},{"name":"dd","size":"small"}]""")).Error);
        Assert.Null(AnswerValidator.Validate(q, J("""[{"name":"Giày chạy","size":"large"}]""")).Error);
    }

    [Fact]
    public void Optional_empty_answer_is_allowed_required_is_not()
    {
        Assert.Null(AnswerValidator.Validate(new OnboardingQuestion { InputType = "number" }, null).Error);
        Assert.NotNull(AnswerValidator.Validate(new OnboardingQuestion { InputType = "text", IsRequired = true }, J("\"  \"")).Error);
    }
}

public class CharacterNameTests
{
    static OnboardingQuestion NameQ() => new()
    {
        InputType = "text", IsRequired = true, MinValue = 2, MaxValue = 16,
        Pattern = DragonFit.Api.Data.SeedData.NamePattern, PatternMessage = "bad",
    };

    [Theory]
    [InlineData("Bao")]
    [InlineData("Iron Bao")]
    [InlineData("Bao_99")]
    [InlineData("O'Neil-Fit")]
    public void Unaccented_names_pass(string name) =>
        Assert.Null(AnswerValidator.Validate(NameQ(), JsonDocument.Parse(JsonSerializer.Serialize(name)).RootElement).Error);

    [Theory]
    [InlineData("Bảo")]
    [InlineData("Đỗ")]
    [InlineData("9Lives")]
    [InlineData("Iron  Bao")]
    [InlineData("Bao!")]
    [InlineData("Bao😀")]
    public void Accents_symbols_and_double_spaces_fail(string name) =>
        Assert.Equal("bad", AnswerValidator.Validate(NameQ(), JsonDocument.Parse(JsonSerializer.Serialize(name)).RootElement).Error);
}

public class StatCalculatorTests
{
    [Fact]
    public void Strength_example_from_plan_is_35()
    {
        var groups = new Dictionary<string, GroupBest>
        {
            ["push"] = new(3, 12, false),  // knee push-up 12 → 14
            ["legs"] = new(2, 20, false),  // squat 20 → 10
            ["pull"] = new(2, 10, false),  // backpack row 10 → 8
            ["core"] = new(2, 40, true),   // plank 40 s → 10
        };
        Assert.Equal(35, StatCalculator.Strength(groups));
    }

    [Fact]
    public void Strength_caps_at_100()
    {
        var max = StatCalculator.StrengthGroups.ToDictionary(g => g, _ => new GroupBest(5, 50, false));
        Assert.Equal(100, StatCalculator.Strength(max));
    }

    [Theory]
    [InlineData(86, 85)]
    [InlineData(80, 91)]
    [InlineData(60, 100)]   // very low ratio earns nothing extra
    public void Vitality_from_waist_to_height(int waist, int expected) =>
        Assert.Equal(expected, StatCalculator.Vitality([waist], 170));

    [Fact]
    public void Vitality_uses_median_so_one_bad_measurement_does_not_count() =>
        Assert.Equal(StatCalculator.Vitality([86], 170), StatCalculator.Vitality([86, 70, 86], 170));

    [Theory]
    [InlineData(5500, 37)]
    [InlineData(10000, 67)]
    [InlineData(30000, 100)]
    public void Endurance_from_steps(int steps, int expected) => Assert.Equal(expected, StatCalculator.Endurance(steps));

    [Fact]
    public void Discipline_example_is_79()
    {
        var days = Enumerable.Repeat(0.9, 28).ToList();
        Assert.Equal(79, StatCalculator.Discipline(days, 10));
    }

    [Fact]
    public void Wisdom_example_is_76() => Assert.Equal(76, StatCalculator.Wisdom(25 / 28.0, 18 / 28.0, 20 / 28.0));

    [Fact]
    public void Week_starts_on_monday()
    {
        Assert.Equal(new DateOnly(2026, 11, 16), DragonFit.Api.Game.StatService.WeekStart(new DateOnly(2026, 11, 17)));
        Assert.Equal(new DateOnly(2026, 11, 16), DragonFit.Api.Game.StatService.WeekStart(new DateOnly(2026, 11, 22)));
    }
}
