using System.Text.Json;
using System.Text.Json.Nodes;
using DragonFit.Api.Data;
using DragonFit.Api.Domain;
using DragonFit.Api.Game;
using Microsoft.EntityFrameworkCore;

namespace DragonFit.Api.Onboarding;

public class OnboardingException(Dictionary<string, string> errors) : Exception("Onboarding validation failed")
{
    public Dictionary<string, string> Errors { get; } = errors;
}

/// <summary>Character creation (B8/B9): stores raw answers, copies them to the profile, creates the character.</summary>
public class OnboardingService(AppDbContext db, TimeProvider clock)
{
    public async Task<object> GetStateAsync(string userId)
    {
        var steps = await db.OnboardingSteps.Where(s => s.IsActive).OrderBy(s => s.SortOrder)
            .Include(s => s.Questions.Where(q => q.IsActive).OrderBy(q => q.SortOrder))
            .ThenInclude(q => q.Options.OrderBy(o => o.SortOrder))
            .AsNoTracking().ToListAsync();
        var progress = await db.OnboardingProgress.FindAsync(userId);
        var answers = await db.PlayerAnswers.Where(a => a.UserId == userId)
            .Join(db.OnboardingQuestions, a => a.QuestionId, q => q.Id, (a, q) => new { q.Code, a.ValueJson })
            .ToListAsync();
        var avatars = await db.AvatarModels.Where(a => a.IsActive).OrderBy(a => a.Id)
            .Select(a => new { a.Code, a.Name, a.SpriteSheetUrl, a.IsDefault, Evolutions = a.Evolutions.OrderBy(e => e.Stage).Select(e => new { e.Stage, e.MinLevel }) })
            .ToListAsync();

        return new
        {
            status = (progress?.Status ?? OnboardingStatus.NotStarted).ToString(),
            currentStep = progress?.CurrentStepId is { } id ? steps.FirstOrDefault(s => s.Id == id)?.Code : steps.FirstOrDefault()?.Code,
            steps = steps.Select(s => new
            {
                s.Code, s.Title, s.Narration, s.IsSkippable,
                questions = s.Questions.Select(q => new
                {
                    q.Code, q.Prompt, q.HelpText, q.InputType, q.Unit, min = q.MinValue, max = q.MaxValue, q.Pattern, q.PatternMessage, q.IsRequired, q.IsSensitive,
                    options = q.Options.Select(o => new { o.Label, o.Value, o.IsExclusive }),
                }),
            }),
            answers = answers.ToDictionary(a => a.Code, a => JsonNode.Parse(a.ValueJson)),
            avatars,
        };
    }

    public async Task<string?> SaveStepAsync(string userId, string stepCode, Dictionary<string, JsonElement> input, bool skip)
    {
        var steps = await db.OnboardingSteps.Where(s => s.IsActive).OrderBy(s => s.SortOrder)
            .Include(s => s.Questions.Where(q => q.IsActive)).ThenInclude(q => q.Options).ToListAsync();
        var step = steps.FirstOrDefault(s => s.Code == stepCode)
                   ?? throw new OnboardingException(new() { ["_"] = "Bước không tồn tại." });

        var progress = await db.OnboardingProgress.FindAsync(userId);
        if (progress?.Status == OnboardingStatus.Completed)
            throw new OnboardingException(new() { ["_"] = "Nhân vật đã được tạo." });
        if (skip && !step.IsSkippable)
            throw new OnboardingException(new() { ["_"] = "Bước này không bỏ qua được." });

        var avatarCodes = (await db.AvatarModels.Where(a => a.IsActive).Select(a => a.Code).ToListAsync()).ToHashSet();
        var errors = new Dictionary<string, string>();
        var values = new Dictionary<OnboardingQuestion, JsonNode?>();
        if (!skip)
        {
            foreach (var q in step.Questions)
            {
                JsonElement? raw = input.TryGetValue(q.Code, out var el) ? el : null;
                var r = AnswerValidator.Validate(q, raw, avatarCodes);
                if (r.Error is not null) errors[q.Code] = r.Error;
                else values[q] = r.Value;
            }
            if (errors.Count > 0) throw new OnboardingException(errors);
        }

        await using var tx = await db.Database.BeginTransactionAsync();
        var profile = await db.PlayerProfiles.FindAsync(userId);
        if (profile is null) { profile = new PlayerProfile { UserId = userId }; db.PlayerProfiles.Add(profile); }

        var existing = await db.PlayerAnswers.Where(a => a.UserId == userId).ToListAsync();
        foreach (var (q, value) in values)
        {
            var row = existing.FirstOrDefault(a => a.QuestionId == q.Id);
            if (value is null)
            {
                if (row is not null) db.PlayerAnswers.Remove(row);
                continue;
            }
            if (row is null) { row = new PlayerAnswer { UserId = userId, QuestionId = q.Id }; db.PlayerAnswers.Add(row); }
            row.ValueJson = value.ToJsonString();
            row.AnsweredAt = clock.GetUtcNow().UtcDateTime;
            ApplyToProfile(profile, q.Code, value);
        }

        var next = steps.SkipWhile(s => s.Id != step.Id).Skip(1).FirstOrDefault();
        if (progress is null) { progress = new OnboardingProgress { UserId = userId }; db.OnboardingProgress.Add(progress); }
        progress.Status = OnboardingStatus.InProgress;
        // Only move forward: editing an earlier step from the review screen keeps the furthest step reached.
        var current = steps.FirstOrDefault(s => s.Id == progress.CurrentStepId);
        if (current is null || (next is not null && next.SortOrder > current.SortOrder)) progress.CurrentStepId = next?.Id ?? step.Id;

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return next?.Code;
    }

    /// <summary>
    /// The allow-list of where each answer may be written (B9 safety note). TargetField is never
    /// turned into dynamic SQL; unknown codes are simply kept in PlayerAnswers only.
    /// </summary>
    static void ApplyToProfile(PlayerProfile p, string code, JsonNode? v)
    {
        string? S() => v?.GetValue<string>();
        decimal? D() => v?.GetValue<decimal>();
        List<string> L() => v is JsonArray a ? a.Select(x => x!.GetValue<string>()).Where(x => x != "none").ToList() : [];
        (TimeOnly?, TimeOnly?) R() => v is JsonObject o ? (TimeOnly.Parse(o["start"]!.GetValue<string>()), TimeOnly.Parse(o["end"]!.GetValue<string>())) : (null, null);

        switch (code)
        {
            case "sex": p.Sex = S()!; break;
            case "birth_year": p.BirthYear = (int)D()!.Value; break;
            case "height_cm": p.HeightCm = D()!.Value; break;
            case "goal": p.Goal = S()!; break;
            case "goal_target": p.GoalTarget = S(); break;
            case "avg_steps": p.BaselineSteps = D() is { } st ? (int)st : null; break;
            case "work_hours": (p.WorkStart, p.WorkEnd) = R(); break;
            case "train_days": p.TrainingDaysPerWeek = int.Parse(S()!); break;
            case "train_time": p.PreferredTrainingTime = S()!; break;
            case "session_min": p.SessionMinutes = int.Parse(S()!); break;
            case "quest_time": p.QuestTime = TimeOnly.Parse(S()!); break;
            case "equipment": p.Equipment = L(); break;
            case "avoid_parts": p.ExcludedBodyParts = L(); break;
            case "sleep": (p.Bedtime, p.WakeTime) = R(); break;
            case "meals_per_day": p.MealsPerDay = S(); break;
            case "eat_where": p.EatingPlaces = L(); break;
            case "dislikes": p.Dislikes = (S() ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList(); break;
            case "food_budget": p.FoodBudget = D() is { } fb ? (int)fb : null; break;
            case "can_cook": p.CanCook = S(); break;
        }
    }

    public async Task<Guid> CompleteAsync(string userId, string? timeZoneId)
    {
        var progress = await db.OnboardingProgress.FindAsync(userId);
        if (progress?.Status == OnboardingStatus.Completed)
            throw new OnboardingException(new() { ["_"] = "Nhân vật đã được tạo." });

        var questions = await db.OnboardingQuestions.Where(q => q.IsActive)
            .Join(db.OnboardingSteps, q => q.StepId, s => s.Id, (q, s) => new { q.Id, q.Code, q.IsRequired, s.IsSkippable })
            .ToListAsync();
        var answers = await db.PlayerAnswers.Where(a => a.UserId == userId).ToListAsync();
        var byCode = questions.Join(answers, q => q.Id, a => a.QuestionId, (q, a) => new { q.Code, Node = JsonNode.Parse(a.ValueJson) })
            .ToDictionary(x => x.Code, x => x.Node);

        var missing = questions.Where(q => q.IsRequired && !q.IsSkippable && !byCode.ContainsKey(q.Code))
            .ToDictionary(q => q.Code, _ => "Câu này bắt buộc.");
        if (missing.Count > 0) throw new OnboardingException(missing);

        string Str(string c) => byCode[c]!.GetValue<string>();
        decimal? Num(string c) => byCode.TryGetValue(c, out var n) && n is not null ? n.GetValue<decimal>() : null;
        int? Int(string c) => Num(c) is { } d ? (int)d : null;

        var now = clock.GetUtcNow();
        var profile = await db.PlayerProfiles.FindAsync(userId) ?? throw new InvalidOperationException("Profile missing");
        profile.TimeZoneId = ValidTimeZone(timeZoneId) ?? profile.TimeZoneId;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(profile.TimeZoneId)).DateTime);
        profile.GoalDeadline = today.AddDays(7 * int.Parse(Str("goal_weeks")));

        var weight = Num("weight_kg")!.Value;
        var targets = Nutrition.Calculate(profile.Sex, weight, profile.HeightCm, today.Year - profile.BirthYear, profile.TrainingDaysPerWeek, profile.Goal);
        profile.MaintenanceKcal = targets.MaintenanceKcal;
        profile.DailyDeficit = targets.Deficit;

        await using var tx = await db.Database.BeginTransactionAsync();

        var avatarCode = Str("avatar");
        var avatar = await db.AvatarModels.SingleAsync(a => a.Code == avatarCode);
        var firstTitle = await db.Titles.SingleAsync(t => t.Code == "wandering_squire");
        var character = new Character
        {
            UserId = userId, Name = Str("char_name"), AvatarModelId = avatar.Id, ActiveTitleId = firstTitle.Id,
            CreatedAt = now.UtcDateTime,
        };
        db.Characters.Add(character);
        db.CharacterTitles.Add(new CharacterTitle { CharacterId = character.Id, TitleId = firstTitle.Id });

        var metric = await db.BodyMetrics.FirstOrDefaultAsync(m => m.UserId == userId && m.Date == today);
        if (metric is null) { metric = new BodyMetric { UserId = userId, Date = today }; db.BodyMetrics.Add(metric); }
        metric.WeightKg = weight;
        metric.WaistCm = Num("waist_cm");
        metric.BodyFatPct = Num("body_fat");

        // Trial results become the Baseline session (B9).
        var exercises = await db.Exercises.ToDictionaryAsync(e => e.Code);
        var pushCode = byCode.TryGetValue("test_pushup_type", out var pt) && pt is not null ? pt.GetValue<string>() : "knee_pushup";
        var baseline = new WorkoutSession { UserId = userId, SessionType = SessionType.Baseline, StartedAt = now.UtcDateTime };
        void AddSet(string exCode, int? reps)
        {
            if (reps is not > 0 || !exercises.TryGetValue(exCode, out var ex)) return;
            baseline.Sets.Add(new WorkoutSet { ExerciseId = ex.Id, Reps = reps.Value, KcalBurned = ex.KcalPerRep * reps.Value });
        }
        AddSet(pushCode, Int("test_pushup"));
        AddSet("bodyweight_squat", Int("test_squat"));
        AddSet("plank", Int("test_plank"));
        AddSet("backpack_row", Int("test_row"));
        if (baseline.Sets.Count > 0) db.WorkoutSessions.Add(baseline);

        // Real-life rewards the player set for themselves (B2 shop).
        if (byCode.TryGetValue("rewards", out var rewards) && rewards is JsonArray arr)
            foreach (var r in arr)
            {
                var size = r!["size"]!.GetValue<string>();
                db.ShopItems.Add(new ShopItem
                {
                    Name = r["name"]!.GetValue<string>(), Kind = "personal", Size = size, OwnerUserId = userId,
                    Price = size switch { "large" => 6000, "medium" => 3000, _ => 1500 },
                });
            }

        var wallet = await db.Wallets.FindAsync(userId);
        if (wallet is null) { wallet = new Wallet { UserId = userId }; db.Wallets.Add(wallet); }
        wallet.Gems += 100;
        db.GemTransactions.Add(new GemTransaction { UserId = userId, Amount = 100, Reason = "onboarding", CreatedAt = now.UtcDateTime });

        progress ??= db.OnboardingProgress.Add(new OnboardingProgress { UserId = userId }).Entity;
        progress.Status = OnboardingStatus.Completed;
        progress.CompletedAt = now.UtcDateTime;

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return character.Id;
    }

    static string? ValidTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id).Id; }
        catch (TimeZoneNotFoundException) { return null; }
    }
}
