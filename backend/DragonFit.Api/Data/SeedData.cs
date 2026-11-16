using DragonFit.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DragonFit.Api.Data;

/// <summary>
/// Reference data shared by every player. Seeding is idempotent: rows are matched by Code,
/// so it is safe to run on every start-up and to add new rows later.
/// </summary>
public static class SeedData
{
    public static async Task RunAsync(AppDbContext db)
    {
        await SeedTitles(db);
        await SeedAvatars(db);
        await SeedExercises(db);
        await SeedQuestTemplates(db);
        await SeedOnboarding(db);
        await PatchQuestions(db);
    }

    // Character names are shown in the Pixelify Sans pixel font, which has no Vietnamese
    // diacritics, so names use unaccented Latin letters, digits, single spaces and - _ '.
    public const string NamePattern = @"^[A-Za-z](?:[A-Za-z0-9_'-]| (?! ))*$";
    const string NameMessage = "Tên chỉ dùng chữ cái không dấu (A–Z), số, khoảng trắng đơn và - _ '. Bắt đầu bằng chữ cái.";
    const string NameHelp = "2–16 ký tự không dấu, ví dụ: Bao, Iron Bao, Bao_99. Sẽ hiện trên bảng xếp hạng.";

    static OnboardingQuestion NameQuestion()
    {
        var q = Q("char_name", "Chiến binh, ngươi tên là gì?", "text", req: true, min: 2, max: 16, help: NameHelp);
        q.Pattern = NamePattern;
        q.PatternMessage = NameMessage;
        return q;
    }

    /// <summary>Applies rule changes to questions in databases that were seeded earlier.</summary>
    static async Task PatchQuestions(AppDbContext db)
    {
        var name = await db.OnboardingQuestions.FirstOrDefaultAsync(q => q.Code == "char_name");
        if (name is not null && name.Pattern is null)
        {
            name.Pattern = NamePattern;
            name.PatternMessage = NameMessage;
            name.HelpText = NameHelp;
            await db.SaveChangesAsync();
        }
    }

    // Level titles (B2). Achievement titles are added when achievements arrive in Phase 2.
    public static readonly (int Level, string Code, string Name)[] LevelTitles =
    [
        (1, "wandering_squire", "Wandering Squire"),
        (5, "oathbound_recruit", "Oathbound Recruit"),
        (10, "iron_vanguard", "Iron Vanguard"),
        (20, "knight_of_the_dawn", "Knight of the Dawn"),
        (30, "stormforged_blade", "Stormforged Blade"),
        (40, "warden_of_the_ember_keep", "Warden of the Ember Keep"),
        (50, "dragonheart_champion", "Dragonheart Champion"),
        (60, "runebound_warlord", "Runebound Warlord"),
        (70, "slayer_of_titans", "Slayer of Titans"),
        (80, "mythic_paladin", "Mythic Paladin"),
        (90, "herald_of_the_eternal_flame", "Herald of the Eternal Flame"),
        (99, "ascended_dragonlord", "Ascended Dragonlord"),
    ];

    public static readonly int[] EvolutionLevels = [1, 20, 40, 60, 80, 99];

    static async Task SeedTitles(AppDbContext db)
    {
        var existing = await db.Titles.Select(t => t.Code).ToListAsync();
        foreach (var (level, code, name) in LevelTitles.Where(t => !existing.Contains(t.Code)))
            db.Titles.Add(new Title { Code = code, Name = name, Source = "Level", MinLevel = level });
        await db.SaveChangesAsync();
    }

    static async Task SeedAvatars(AppDbContext db)
    {
        var avatars = new[]
        {
            new AvatarModel { Code = "ember_knight", Name = "Hiệp sĩ Than Hồng", BodyType = "knight", SpriteSheetUrl = "/avatars/ember_knight.png", IsDefault = true },
            new AvatarModel { Code = "frost_blade", Name = "Kiếm sĩ Băng Lam", BodyType = "knight", SpriteSheetUrl = "/avatars/frost_blade.png" },
            new AvatarModel { Code = "verdant_warden", Name = "Hộ vệ Lục Diệp", BodyType = "knight", SpriteSheetUrl = "/avatars/verdant_warden.png" },
        };
        var existing = await db.AvatarModels.Select(a => a.Code).ToListAsync();
        foreach (var a in avatars.Where(a => !existing.Contains(a.Code)))
        {
            for (var i = 0; i < EvolutionLevels.Length; i++)
                a.Evolutions.Add(new AvatarEvolution { Stage = i + 1, MinLevel = EvolutionLevels[i], RowIndex = i });
            db.AvatarModels.Add(a);
        }
        await db.SaveChangesAsync();
    }

    static async Task SeedExercises(AppDbContext db)
    {
        // Progression ladders per group. Tier 1 is the easiest variation.
        var list = new List<Exercise>
        {
            Ex("wall_pushup", "Wall push-up", "Hít đất chống tường", "push", "wrist,shoulder", "none", 1, 0.3m, "Thân thẳng như tấm ván, hạ ngực chạm gần tường."),
            Ex("incline_pushup", "Incline push-up", "Hít đất chống bàn", "push", "wrist,shoulder", "none", 2, 0.35m, "Tay đặt lên bàn chắc chắn, siết bụng khi hạ người."),
            Ex("knee_pushup", "Knee push-up", "Hít đất chống gối", "push", "wrist,shoulder", "none", 3, 0.4m, "Gối, hông, vai thành một đường thẳng."),
            Ex("pushup", "Push-up", "Hít đất chuẩn", "push", "wrist,shoulder", "none", 4, 0.5m, "Khuỷu tay khép khoảng 45 độ so với thân."),
            Ex("decline_pushup", "Decline push-up", "Hít đất chân kê cao", "push", "wrist,shoulder", "none", 5, 0.6m, "Chân trên ghế, giữ hông không võng."),

            Ex("glute_bridge", "Glute bridge", "Nâng hông", "legs", "hip", "none", 1, 0.3m, "Đẩy bằng gót chân, siết mông ở điểm cao nhất."),
            Ex("chair_squat", "Chair squat", "Squat chạm ghế", "legs", "knee", "none", 1, 0.35m, "Ngồi chạm nhẹ ghế rồi đứng lên, gối hướng theo mũi chân."),
            Ex("bodyweight_squat", "Bodyweight squat", "Squat không tạ", "legs", "knee", "none", 2, 0.4m, "Hạ đến khi đùi song song sàn, lưng thẳng."),
            Ex("reverse_lunge", "Reverse lunge", "Lunge lùi", "legs", "knee", "none", 3, 0.45m, "Bước lùi, gối sau gần chạm sàn; tính mỗi chân một rep."),
            Ex("split_squat", "Split squat", "Split squat", "legs", "knee", "none", 4, 0.5m, "Giữ thân thẳng, trọng tâm ở chân trước."),
            Ex("bulgarian_split_squat", "Bulgarian split squat", "Bulgarian split squat", "legs", "knee", "bench", 5, 0.6m, "Chân sau gác lên ghế, hạ chậm 2 giây."),

            Ex("band_row", "Band row", "Kéo dây kháng lực", "pull", "shoulder", "band", 1, 0.3m, "Kéo khuỷu tay ra sau, ép hai bả vai lại."),
            Ex("backpack_row", "Backpack row", "Kéo balo", "pull", "lower_back", "none", 2, 0.35m, "Gập hông, lưng thẳng, kéo balo về phía bụng."),
            Ex("table_row", "Table row", "Kéo người dưới bàn", "pull", "shoulder", "none", 3, 0.45m, "Chỉ dùng bàn thật chắc; thân thẳng khi kéo ngực lên."),
            Ex("negative_pullup", "Negative pull-up", "Xà đơn hạ chậm", "pull", "shoulder", "pullup_bar", 4, 0.6m, "Nhảy lên vị trí cao, hạ người chậm 3–5 giây."),
            Ex("pullup", "Pull-up", "Kéo xà", "pull", "shoulder", "pullup_bar", 5, 0.8m, "Kéo cằm qua xà, không đung đưa."),

            Ex("dead_bug", "Dead bug", "Dead bug", "core", "lower_back", "none", 1, 0.2m, "Lưng dưới luôn áp sàn; tính mỗi bên một rep."),
            ExSec("plank", "Plank", "Plank", "core", "wrist,shoulder", 2, 0.08m, "Siết bụng và mông, không để hông võng."),
            ExSec("side_plank", "Side plank", "Plank nghiêng", "core", "shoulder", 3, 0.08m, "Thân một đường thẳng, tính tổng thời gian hai bên."),
            ExSec("hollow_hold", "Hollow hold", "Hollow hold", "core", "lower_back", 4, 0.1m, "Lưng dưới áp sàn, tay chân duỗi xa."),

            ExSec("mobility_flow", "Mobility flow", "Giãn cơ toàn thân", "mobility", "", 1, 0.05m, "Giãn hông, vai, lưng; thở chậm và đều."),
        };
        var existing = await db.Exercises.Select(e => e.Code).ToListAsync();
        db.Exercises.AddRange(list.Where(e => !existing.Contains(e.Code)));
        await db.SaveChangesAsync();

        static Exercise Ex(string code, string name, string vi, string group, string part, string equip, int tier, decimal kcal, string tips) =>
            new() { Code = code, Name = name, NameVi = vi, Group = group, BodyPart = part, Equipment = equip, Tier = tier, Unit = "reps", KcalPerRep = kcal, FormTips = tips };
        static Exercise ExSec(string code, string name, string vi, string group, string part, int tier, decimal kcal, string tips) =>
            new() { Code = code, Name = name, NameVi = vi, Group = group, BodyPart = part, Equipment = "none", Tier = tier, Unit = "seconds", KcalPerRep = kcal, FormTips = tips };
    }

    static async Task SeedQuestTemplates(AppDbContext db)
    {
        var list = new[]
        {
            new QuestTemplate { Code = "daily_push", Type = "daily", Kind = "exercise", BaseXp = 40, BaseGems = 20, BaseDamage = 5 },
            new QuestTemplate { Code = "daily_legs", Type = "daily", Kind = "exercise", BaseXp = 40, BaseGems = 20, BaseDamage = 5 },
            new QuestTemplate { Code = "daily_pull", Type = "daily", Kind = "exercise", BaseXp = 40, BaseGems = 20, BaseDamage = 5 },
            new QuestTemplate { Code = "daily_core", Type = "daily", Kind = "exercise", BaseXp = 30, BaseGems = 15, BaseDamage = 5 },
            new QuestTemplate { Code = "daily_mobility", Type = "daily", Kind = "exercise", BaseXp = 25, BaseGems = 10, BaseDamage = 5 },
            new QuestTemplate { Code = "daily_steps", Type = "daily", Kind = "steps", BaseXp = 30, BaseGems = 15, BaseDamage = 5 },
            new QuestTemplate { Code = "daily_protein", Type = "daily", Kind = "protein", BaseXp = 30, BaseGems = 15, BaseDamage = 5 },
        };
        var existing = await db.QuestTemplates.Select(q => q.Code).ToListAsync();
        db.QuestTemplates.AddRange(list.Where(q => !existing.Contains(q.Code)));
        await db.SaveChangesAsync();
    }

    // ------------------------------------------------------------ Character creation (B8)

    static async Task SeedOnboarding(AppDbContext db)
    {
        if (await db.OnboardingSteps.AnyAsync()) return; // questions are edited in the database after the first seed

        var steps = new List<OnboardingStep>
        {
            Step("awakening", 1, "Thức tỉnh",
                "Ngọn lửa trong lò rèn bập bùng. Một chiến binh vừa mở mắt giữa doanh trại cũ... Hãy cho ta biết ngươi là ai.", false,
                NameQuestion(),
                Q("sex", "Giới tính", "single_choice", req: true, opts: [("Nam", "male"), ("Nữ", "female")], help: "Dùng để tính mức năng lượng cơ thể cần mỗi ngày.")),

            Step("form", 2, "Chọn hình hài",
                "Mỗi chiến binh mang một hình hài. Hình hài ấy sẽ tiến hóa theo từng chặng đường ngươi đi qua.", false,
                Q("avatar", "Hãy chọn hình hài của ngươi", "avatar_picker", req: true)),

            Step("measure", 3, "Đo sức mạnh nguyên bản",
                "Trước khi lên đường, ta cần ghi lại sức mạnh nguyên bản của ngươi. Những con số này chỉ mình ngươi thấy.", false,
                Q("birth_year", "Năm sinh", "number", req: true, min: 1950, max: 2012),
                Q("height_cm", "Chiều cao", "number", req: true, min: 120, max: 220, unit: "cm"),
                Q("weight_kg", "Cân nặng (buổi sáng)", "number", req: true, min: 30, max: 250, unit: "kg", sensitive: true),
                Q("waist_cm", "Vòng bụng, đo ngang rốn", "number", req: true, min: 40, max: 200, unit: "cm", sensitive: true),
                Q("body_fat", "Tỉ lệ mỡ (nếu biết)", "number", min: 3, max: 60, unit: "%", sensitive: true)),

            Step("oath", 4, "Lời thề",
                "Mọi chuyến phiêu lưu bắt đầu bằng một lời thề. Ngươi chiến đấu vì điều gì, và sẽ tự thưởng gì khi chiến thắng?", false,
                Q("goal", "Mục tiêu chính", "single_choice", req: true,
                    opts: [("Giảm mỡ + tăng cơ (recomposition)", "recomposition"), ("Giảm mỡ", "fat_loss"), ("Tăng cơ", "muscle_gain")]),
                Q("goal_target", "Mục tiêu cụ thể", "text", max: 60, help: "Ví dụ: vòng bụng −6 cm"),
                Q("goal_weeks", "Thời hạn", "single_choice", req: true, opts: [("8 tuần", "8"), ("12 tuần", "12"), ("24 tuần", "24")]),
                Q("rewards", "Ngươi muốn nhận phần thưởng gì khi chiến thắng?", "reward_list", req: true,
                    help: "Tối đa 3 phần thưởng đời thực. Cỡ nhỏ 1.500 Gem, vừa 3.000 Gem, lớn 6.000 Gem.")),

            Step("trial", 5, "Thử thách đầu tiên",
                "Hãy cho ta thấy sức mạnh hiện tại. Làm mỗi bài một lần, đến khi không giữ được đúng kỹ thuật nữa thì dừng.", true,
                Q("test_pushup_type", "Kiểu hít đất ngươi làm được", "single_choice",
                    opts: [("Chống tường", "wall_pushup"), ("Chống gối", "knee_pushup"), ("Chuẩn", "pushup")]),
                Q("test_pushup", "Số hít đất tối đa", "number", min: 0, max: 200, unit: "rep"),
                Q("test_squat", "Số squat tối đa", "number", min: 0, max: 500, unit: "rep"),
                Q("test_plank", "Plank giữ được bao lâu", "number", min: 0, max: 1200, unit: "giây"),
                Q("test_row", "Số lần kéo balo tối đa", "number", min: 0, max: 300, unit: "rep"),
                Q("avg_steps", "Số bước trung bình mỗi ngày", "number", min: 0, max: 50000, unit: "bước", help: "Xem trong ứng dụng sức khỏe trên điện thoại.")),

            Step("gear", 6, "Hành trang",
                "Một chiến binh khôn ngoan biết rõ thời gian và vũ khí của mình. Hãy kể ta nghe một ngày của ngươi.", false,
                Q("work_hours", "Giờ làm việc", "time_range"),
                Q("train_days", "Số buổi tập mỗi tuần", "single_choice", req: true, opts: [("3 buổi", "3"), ("4 buổi", "4"), ("5 buổi", "5")]),
                Q("train_time", "Giờ tập ưa thích", "single_choice", req: true, opts: [("Sáng", "morning"), ("Trưa", "noon"), ("Tối", "evening")]),
                Q("session_min", "Số phút mỗi buổi", "single_choice", req: true, opts: [("30 phút", "30"), ("45 phút", "45"), ("60 phút", "60")]),
                Q("quest_time", "Mỗi sáng, ngươi muốn nhận nhiệm vụ lúc mấy giờ?", "time", req: true, min: 270, max: 660, help: "Từ 4:30 đến 11:00."),
                QM("equipment", "Dụng cụ ở nhà", "multi_choice", req: true,
                    opts: [("Không có", "none", true), ("Dây kháng lực", "band", false), ("Tạ đơn", "dumbbell", false), ("Xà đơn", "pullup_bar", false), ("Ghế tập", "bench", false)]),
                QM("avoid_parts", "Chỗ đau cần tránh", "multi_choice", req: true, sensitive: true,
                    opts: [("Không có", "none", true), ("Đầu gối", "knee", false), ("Lưng dưới", "lower_back", false), ("Cổ tay", "wrist", false), ("Vai", "shoulder", false)]),
                Q("sleep", "Giờ đi ngủ và thức dậy", "time_range")),

            Step("provisions", 7, "Lương thực",
                "Không chiến binh nào thắng trận với cái bụng rỗng. Kể ta nghe ngươi thường ăn uống thế nào.", true,
                Q("meals_per_day", "Số bữa mỗi ngày", "single_choice", opts: [("2 bữa", "2"), ("3 bữa", "3"), ("3 bữa + ăn vặt", "3+snacks")]),
                QM("eat_where", "Thường ăn ở đâu", "multi_choice",
                    opts: [("Tự nấu", "home", false), ("Căng tin", "canteen", false), ("Quán", "street", false), ("Đặt giao", "delivery", false)]),
                Q("sugary_drinks", "Đồ uống có đường mỗi tuần", "single_choice", opts: [("0", "0"), ("1–3", "1-3"), ("4–7", "4-7"), ("Trên 7", "7+")]),
                Q("alcohol", "Rượu bia mỗi tuần", "single_choice", opts: [("0", "0"), ("1–3", "1-3"), ("4–7", "4-7"), ("Trên 7", "7+")]),
                Q("dislikes", "Món không ăn", "text", max: 200, help: "Cách nhau bằng dấu phẩy, ví dụ: bò, hải sản"),
                Q("food_budget", "Ngân sách ăn mỗi ngày", "number", min: 0, max: 2000000, unit: "VND"),
                Q("can_cook", "Ngươi có nấu ăn được không?", "single_choice", opts: [("Có", "yes"), ("Cơ bản", "basic"), ("Không", "no")])),
        };
        db.OnboardingSteps.AddRange(steps);
        await db.SaveChangesAsync();
    }

    static OnboardingStep Step(string code, int order, string title, string narration, bool skippable, params OnboardingQuestion[] qs)
    {
        for (var i = 0; i < qs.Length; i++) qs[i].SortOrder = i + 1;
        return new OnboardingStep { Code = code, SortOrder = order, Title = title, Narration = narration, IsSkippable = skippable, Questions = qs.ToList() };
    }

    static OnboardingQuestion Q(string code, string prompt, string type, bool req = false, decimal? min = null, decimal? max = null,
        string? unit = null, string? help = null, bool sensitive = false, (string Label, string Value)[]? opts = null) =>
        new()
        {
            Code = code, Prompt = prompt, InputType = type, IsRequired = req, MinValue = min, MaxValue = max, Unit = unit,
            HelpText = help, IsSensitive = sensitive,
            Options = (opts ?? []).Select((o, i) => new QuestionOption { Label = o.Label, Value = o.Value, SortOrder = i + 1 }).ToList(),
        };

    static OnboardingQuestion QM(string code, string prompt, string type, bool req = false, bool sensitive = false,
        (string Label, string Value, bool Exclusive)[]? opts = null) =>
        new()
        {
            Code = code, Prompt = prompt, InputType = type, IsRequired = req, IsSensitive = sensitive,
            Options = (opts ?? []).Select((o, i) => new QuestionOption { Label = o.Label, Value = o.Value, IsExclusive = o.Exclusive, SortOrder = i + 1 }).ToList(),
        };
}
