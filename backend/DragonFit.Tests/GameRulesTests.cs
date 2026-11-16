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

public class SettlementRulesTests
{
    static readonly QuestResult Done = new("q", 10, 10, true);
    static readonly QuestResult Missed = new("q", 10, 0, false);

    static SettlementInput Day(int actual, int done, int meals = 3, decimal? sleep = null, int streak = 0, int hp = 100, bool restDay = false, bool restMode = false) =>
        new(2000, actual, meals, Enumerable.Range(0, 4).Select(i => i < done ? Done : Missed).ToList(),
            restDay, restMode, 4, sleep, streak, hp);

    // The example table in plan B10.
    [Fact] public void Perfect_first_day_heals_7() => Assert.Equal(7, SettlementRules.Settle(Day(2150, 4)).Heal);
    [Fact] public void Perfect_day_with_sleep_heals_9() => Assert.Equal(9, SettlementRules.Settle(Day(2150, 4, sleep: 7.5m)).Heal);
    [Fact] public void Overeating_2600_costs_8() => Assert.Equal(8, SettlementRules.Settle(Day(2600, 4)).Damage);
    [Fact] public void Two_missed_quests_cost_10() => Assert.Equal(10, SettlementRules.Settle(Day(2000, 2)).Damage);
    [Fact] public void Undereating_1300_costs_10() => Assert.Equal(10, SettlementRules.Settle(Day(1300, 4)).Damage);
    [Fact] public void No_food_log_costs_10() => Assert.Equal(10, SettlementRules.Settle(Day(0, 4, meals: 0)).Damage);

    [Fact]
    public void Worst_day_is_capped_at_30()
    {
        var r = SettlementRules.Settle(Day(3500, 0));
        Assert.Equal(30, r.Damage);
        Assert.Equal(70, r.HpAfter);
    }

    [Fact]
    public void Partial_quest_scales_damage()
    {
        var r = SettlementRules.Settle(new SettlementInput(2000, 2000, 3, [new("Squat", 36, 20, false)], false, false, 4, null, 0, 100));
        Assert.Equal(2, r.Damage); // 5 × (1 − 20/36) = 2.2
    }

    [Fact] public void Fifth_perfect_day_heals_15() => Assert.Equal(15, SettlementRules.Settle(Day(2000, 4, streak: 4)).Heal);

    [Fact]
    public void Rest_day_waives_quest_damage_but_not_food()
    {
        var r = SettlementRules.Settle(Day(2600, 0, restDay: true));
        Assert.Equal(8, r.Damage);
    }

    [Fact]
    public void Rest_mode_no_damage_no_heal_and_keeps_streak()
    {
        var r = SettlementRules.Settle(Day(3500, 0, streak: 3, restMode: true));
        Assert.Equal((0, 0, 3), (r.Damage, r.Heal, r.PerfectStreak));
    }

    [Fact]
    public void Never_opening_the_game_on_a_training_day_costs_food_and_quests()
    {
        var r = SettlementRules.Settle(new SettlementInput(2000, 0, 0, [], false, false, 4, null, 0, 100));
        Assert.Equal(30, r.Damage); // 10 food + 20 quests
    }

    [Fact]
    public void Four_worst_days_kill_from_full_hp()
    {
        var hp = 100;
        for (var i = 0; i < 4; i++) hp = SettlementRules.Settle(Day(3500, 0, hp: hp)).HpAfter;
        Assert.Equal(0, hp);
    }

    [Fact]
    public void Day_clock_keeps_yesterday_open_until_4am()
    {
        var early = new DayClock(new DateTime(2026, 11, 18, 3, 59, 0));
        var late = new DayClock(new DateTime(2026, 11, 18, 4, 0, 0));
        Assert.True(early.IsOpen(new DateOnly(2026, 11, 17)));
        Assert.False(late.IsOpen(new DateOnly(2026, 11, 17)));
        Assert.Equal(new DateOnly(2026, 11, 16), early.LastClosedDay);
        Assert.Equal(new DateOnly(2026, 11, 17), late.LastClosedDay);
    }
}

public class SpriteSheetTests
{
    static byte[] Png(uint w, uint h)
    {
        var b = new byte[64];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
        "IHDR"u8.ToArray().CopyTo(b, 12);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(16), w);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(20), h);
        return b;
    }

    [Fact] public void Correct_sheet_passes() => Assert.Null(ForgeService.ValidateSheet(Png(256, 384)));
    [Fact] public void Wrong_size_fails() => Assert.Contains("256 × 384", ForgeService.ValidateSheet(Png(64, 64)));
    [Fact] public void Non_png_fails() => Assert.Equal("File phải là ảnh PNG.", ForgeService.ValidateSheet(new byte[100]));
}

public class MenuGachaTests
{
    static Food F(int id, string code, string name, decimal kcal, decimal p, params string[] tags) =>
        new() { Id = id, Code = code, NameVi = name, Kcal = kcal, ProteinG = p, Tags = [.. tags], PriceVnd = 20000 };

    static readonly List<Food> Foods =
    [
        F(1, "com_trang", "Cơm trắng", 195, 4, "carb", "home"),
        F(2, "banh_mi_trung", "Bánh mì trứng", 380, 15, "meal", "breakfast"),
        F(3, "trung_luoc", "Trứng gà luộc", 75, 6.5m, "protein", "home", "breakfast"),
        F(4, "pho_bo", "Phở bò", 450, 25, "meal", "noodle"),
        F(5, "bun_cha", "Bún chả", 550, 25, "meal"),
        F(6, "uc_ga", "Ức gà áp chảo", 260, 45, "protein", "home"),
        F(7, "bo_xao", "Thịt bò xào rau", 300, 25, "protein", "home"),
        F(8, "rau", "Rau luộc", 50, 3, "veg", "home"),
        F(9, "chuoi", "Chuối", 105, 1.3m, "fruit", "snack"),
        F(10, "tra_sua", "Trà sữa", 400, 4, "drink", "sugary"),
        F(11, "sua_chua", "Sữa chua Hy Lạp", 97, 9, "dairy", "snack", "protein"),
    ];

    [Fact]
    public void Draw_lands_near_the_kcal_target_with_enough_protein()
    {
        var r = MenuGacha.Draw(Foods, 2000, 110, [], null, new Random(42));
        Assert.InRange(r.Kcal, 1800, 2200);
        Assert.True(r.ProteinG >= 99, $"protein {r.ProteinG}");
    }

    [Fact]
    public void Sugary_drinks_never_drawn() =>
        Assert.DoesNotContain(MenuGacha.Draw(Foods, 2000, 110, [], null, new Random(1)).Items, i => i.Food.Code == "tra_sua");

    [Fact]
    public void Dislikes_match_without_accents()
    {
        for (var seed = 0; seed < 20; seed++)
            Assert.DoesNotContain(MenuGacha.Draw(Foods, 2000, 110, ["bo"], null, new Random(seed)).Items,
                i => i.Food.Code is "bo_xao" or "pho_bo");
    }

    [Theory]
    [InlineData(260, 45, "Epic")]   // 17 g per 100 kcal
    [InlineData(380, 25, "Rare")]   // 6.6
    [InlineData(195, 4, "Common")]
    public void Rarity_by_protein_density(int kcal, int p, string rarity) =>
        Assert.Equal(rarity, MenuGacha.Rarity(new Food { Kcal = kcal, ProteinG = p }));
}

public class BossTests
{
    [Theory]
    [InlineData(12, 1.08, 13)]
    [InlineData(30, 1.08, 33)]
    [InlineData(40, 1.10, 44)]
    [InlineData(5, 1.10, 6)]
    public void Boss_target_beats_the_best(int best, double factor, int expected) =>
        Assert.Equal(expected, QuestGenerator.BossTarget(best, (decimal)factor));

    [Fact]
    public void Boss_days()
    {
        Assert.True(QuestGenerator.IsWeeklyBossDay(new DateOnly(2026, 11, 22)));
        Assert.True(QuestGenerator.IsMonthlyBossDay(new DateOnly(2026, 11, 30)));
        Assert.False(QuestGenerator.IsMonthlyBossDay(new DateOnly(2026, 11, 29)));
    }

    [Fact]
    public void Skipping_a_boss_costs_10_losing_costs_nothing()
    {
        var day = new SettlementInput(2000, 2000, 3, [new("q", 1, 1, true)], true, false, 0, null, 0, 100, BossSkipped: true);
        Assert.Equal(10, SettlementRules.Settle(day).Damage);
        Assert.Equal(0, SettlementRules.Settle(day with { BossSkipped = false }).Damage);
    }
}
