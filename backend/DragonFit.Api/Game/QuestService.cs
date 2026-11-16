using DragonFit.Api.Data;
using DragonFit.Api.Domain;
using DragonFit.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace DragonFit.Api.Game;

public class GameException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}

public record RewardResult(
    int XpGained, int GemsGained, int LevelBefore, int LevelAfter, int StreakDays,
    IReadOnlyList<string> NewTitles, int? EvolvedToStage, string QuestStatus, string? Bonus = null);

public class QuestService(AppDbContext db, TimeProvider clock, IProofStorage storage, IConfiguration config, SettlementService settlement)
{
    public const int RerollCost = 20;

    public async Task<(Character Character, PlayerProfile Profile, DateOnly Today, DateTime LocalNow)> LoadAsync(string userId)
    {
        var character = await db.Characters.FirstOrDefaultAsync(c => c.UserId == userId && c.Status == CharacterStatus.Alive)
                        ?? throw new GameException("Chưa có nhân vật.", 404);
        var profile = await db.PlayerProfiles.FindAsync(userId) ?? throw new GameException("Chưa có hồ sơ.", 404);
        // Close any finished days first, so HP and the board are always up to date.
        await settlement.CatchUpAsync(character, profile);
        if (character.Status != CharacterStatus.Alive) throw new GameException("Nhân vật đã tử trận.", 410);
        var day = DayClock.For(clock, profile.TimeZoneId);
        return (character, profile, day.Today, day.LocalNow);
    }

    /// <summary>Today's quest board. Quests appear at the player's chosen time (B3) and are generated once per day.</summary>
    public async Task<object> GetTodayAsync(string userId)
    {
        var (character, profile, today, localNow) = await LoadAsync(userId);
        var unlockAt = today.ToDateTime(profile.QuestTime);
        var firstDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(character.CreatedAt, TimeZoneInfo.FindSystemTimeZoneById(profile.TimeZoneId))) == today;

        // The day the character is created, quests are given straight away.
        if (localNow < unlockAt && !firstDay)
            return new { date = today, locked = true, unlockAt = profile.QuestTime.ToString("HH:mm"), quests = Array.Empty<object>() };

        await EnsureGeneratedAsync(character, profile, today);
        var quests = await db.QuestInstances.Where(q => q.CharacterId == character.Id && q.Date == today)
            .Include(q => q.Exercise).Include(q => q.Template).OrderBy(q => q.Slot).AsNoTracking().ToListAsync();
        return new
        {
            date = today, locked = false, unlockAt = profile.QuestTime.ToString("HH:mm"),
            proofRequired = ProofRequired,
            rerollCost = RerollCost,
            quests = quests.Select(Dto),
        };
    }

    bool ProofRequired => config.GetValue("Game:RequireProofPhoto", true);

    static object Dto(QuestInstance q) => new
    {
        q.Id, q.Slot, q.Title, q.Narration, q.Sets, q.TargetValue, q.Unit, q.Xp, q.Gems, q.IsRequired, q.BossKind,
        status = q.Status.ToString(), q.DoneValue, hasProof = q.ProofUrl != null, q.AwardedXp, q.AwardedGems,
        kind = q.Template?.Kind,
        exercise = q.Exercise is null ? null : new { q.Exercise.Code, q.Exercise.NameVi, q.Exercise.Group, q.Exercise.Tier, q.Exercise.FormTips },
        canReroll = q.Status == QuestStatus.Pending && q.ExerciseId != null,
        // The protein quest fills itself from the meal log (Phase 2), so it has no report button.
        canReport = q.Template?.Kind != "protein" && q.Status != QuestStatus.Completed,
    };

    async Task EnsureGeneratedAsync(Character character, PlayerProfile profile, DateOnly today)
    {
        if (await db.QuestInstances.AnyAsync(q => q.CharacterId == character.Id && q.Date == today)) return;

        var exercises = await db.Exercises.AsNoTracking().ToListAsync();
        var ctx = await BuildContextAsync(character, profile, today, exercises);
        var templates = await db.QuestTemplates.ToDictionaryAsync(t => t.Code);
        foreach (var d in QuestGenerator.Generate(exercises, ctx))
            db.QuestInstances.Add(new QuestInstance
            {
                UserId = character.UserId, CharacterId = character.Id, TemplateId = templates[d.TemplateCode].Id, Date = today,
                Slot = d.Slot, ExerciseId = d.ExerciseId, Sets = d.Sets, TargetValue = d.TargetValue, Unit = d.Unit,
                Title = d.Title, Narration = d.Narration, Xp = d.Xp, Gems = d.Gems, IsRequired = d.IsRequired, BossKind = d.BossKind,
            });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            // Another request generated today's quests at the same moment; the unique index kept one copy.
            db.ChangeTracker.Clear();
        }
    }

    async Task<QuestContext> BuildContextAsync(Character character, PlayerProfile profile, DateOnly today, List<Exercise> exercises)
    {
        var baselineSets = await db.WorkoutSets
            .Where(s => db.WorkoutSessions.Any(w => w.Id == s.SessionId && w.UserId == character.UserId && w.SessionType == SessionType.Baseline))
            .ToListAsync();
        var baseline = baselineSets.GroupBy(s => s.ExerciseId).ToDictionary(g => g.Key, g => g.Max(s => s.Reps));
        var byId = exercises.ToDictionary(e => e.Id);
        int? Reps(string code) => baselineSets.FirstOrDefault(s => byId[s.ExerciseId].Code == code)?.Reps;
        var pushSet = baselineSets.FirstOrDefault(s => byId[s.ExerciseId].Group == "push");
        var tiers = QuestGenerator.TiersFromTrial(pushSet is null ? null : byId[pushSet.ExerciseId].Code, pushSet?.Reps,
            Reps("bodyweight_squat"), Reps("plank"), Reps("backpack_row"));

        // Most recent target per exercise, so reps grow by one after each finished quest.
        var recent = await db.QuestInstances
            .Where(q => q.CharacterId == character.Id && q.ExerciseId != null && q.Date < today)
            .OrderByDescending(q => q.Date).Take(60)
            .Select(q => new { q.ExerciseId, q.TargetValue, q.Status }).ToListAsync();
        var last = recent.GroupBy(q => q.ExerciseId!.Value)
            .ToDictionary(g => g.Key, g => new LastTarget(g.First().TargetValue, g.First().Status == QuestStatus.Completed));

        var weight = await db.BodyMetrics.Where(m => m.UserId == character.UserId && m.WeightKg != null)
            .OrderByDescending(m => m.Date).Select(m => m.WeightKg).FirstOrDefaultAsync() ?? 60m;
        var protein = Nutrition.Calculate(profile.Sex, weight, profile.HeightCm, today.Year - profile.BirthYear, profile.TrainingDaysPerWeek, profile.Goal).ProteinG;

        // Personal bests of the last 28 days (single set) for the boss targets.
        var since = today.AddDays(-28);
        var sinceUtc = since.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var setBests = await db.WorkoutSets
            .Where(s => db.WorkoutSessions.Any(w => w.Id == s.SessionId && w.UserId == character.UserId && w.StartedAt >= sinceUtc))
            .GroupBy(s => s.ExerciseId).Select(g => new { g.Key, Best = g.Max(s => s.Reps) }).ToListAsync();
        var questBests = await db.QuestInstances
            .Where(q => q.CharacterId == character.Id && q.ExerciseId != null && q.DoneValue != null && q.Date >= since)
            .GroupBy(q => q.ExerciseId!.Value).Select(g => new { g.Key, Best = g.Max(q => q.DoneValue!.Value) }).ToListAsync();
        var bests = setBests.Concat(questBests).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Max(x => x.Best));

        return new QuestContext(today, profile.TrainingDaysPerWeek, profile.SessionMinutes, profile.Equipment, profile.ExcludedBodyParts,
            tiers, baseline, last, profile.BaselineSteps, protein, character.CurrentHp, bests);
    }

    /// <summary>Report a quest (fully or partly). Rewards are paid only for the part not already paid.</summary>
    public async Task<RewardResult> CompleteAsync(string userId, Guid questId, int doneValue, Stream? photo, string? contentType, int[]? setReps = null)
    {
        var (character, profile, _, _) = await LoadAsync(userId);
        var quest = await db.QuestInstances.Include(q => q.Template)
                        .FirstOrDefaultAsync(q => q.Id == questId && q.UserId == userId)
                    ?? throw new GameException("Không tìm thấy nhiệm vụ.", 404);
        if (!DayClock.For(clock, profile.TimeZoneId).IsOpen(quest.Date))
            throw new GameException("Ngày này đã chốt sổ lúc 4:00, không báo cáo thêm được.");
        if (quest.Template?.Kind == "protein") throw new GameException("Nhiệm vụ protein tự hoàn thành khi ngươi ghi bữa ăn.");
        var today = quest.Date;
        if (quest.Status == QuestStatus.Completed) throw new GameException("Nhiệm vụ đã hoàn thành.");
        if (doneValue < 0 || doneValue > quest.TargetValue * 5) throw new GameException("Số liệu không hợp lệ.");
        if (doneValue <= (quest.DoneValue ?? 0)) throw new GameException("Kết quả mới phải cao hơn lần báo trước.");

        if (photo is not null)
            quest.ProofUrl = await storage.SaveAsync(userId, quest.Id, photo, contentType ?? "image/webp");
        if (ProofRequired && quest.ProofUrl is null) throw new GameException("Cần ảnh chụp làm bằng chứng.");

        // From a battle the exact total counts (7 + 6 + 7 of 3 × 7 = 95%), not the rounded per-set average.
        var ratio = setReps is { Length: > 0 }
            ? Math.Min(1m, setReps.Sum() / (decimal)(quest.TargetValue * quest.Sets))
            : Math.Min(1m, doneValue / (decimal)quest.TargetValue);
        var mult = Progression.StreakMultiplier(character.StreakDays);
        var xpTotal = (int)Math.Round(quest.Xp * ratio * mult);
        var gemsTotal = (int)Math.Round(quest.Gems * ratio);
        var xp = Math.Max(0, xpTotal - quest.AwardedXp);
        var gems = Math.Max(0, gemsTotal - quest.AwardedGems);

        quest.DoneValue = doneValue;
        quest.Status = ratio >= 1 ? QuestStatus.Completed : QuestStatus.Partial;
        quest.CompletedAt = clock.GetUtcNow().UtcDateTime;
        quest.AwardedXp += xp;
        quest.AwardedGems += gems;

        // A battle (Phase 3) sends the reps of every set; they become a workout session for later records.
        if (setReps is { Length: > 0 } && quest.ExerciseId is { } exId)
        {
            var ex = await db.Exercises.FindAsync(exId);
            var session = new WorkoutSession { UserId = userId, SessionType = SessionType.Normal, QuestInstanceId = quest.Id, StartedAt = clock.GetUtcNow().UtcDateTime };
            foreach (var reps in setReps.Where(r => r > 0).Take(10))
                session.Sets.Add(new WorkoutSet { ExerciseId = exId, Reps = reps, KcalBurned = (ex?.KcalPerRep ?? 0) * reps });
            db.WorkoutSessions.Add(session);
        }

        // Save the quest first so the "all required quests done today" check below sees it.
        await db.SaveChangesAsync();

        var allDone = !await db.QuestInstances.AnyAsync(q =>
            q.CharacterId == character.Id && q.Date == today && q.IsRequired && q.Status != QuestStatus.Completed);
        if (allDone && character.LastStreakDate != today)
        {
            character.StreakDays = character.LastStreakDate == today.AddDays(-1) ? character.StreakDays + 1 : 1;
            character.LastStreakDate = today;
        }

        var result = await GrantAsync(character, xp, gems, "quest", quest.Id.ToString());
        result = result with { QuestStatus = quest.Status.ToString() };
        if (quest.BossKind is not null && quest.Status == QuestStatus.Completed)
            result = await BossRewardAsync(character, quest, result);
        return result;
    }

    // Twelve fantasy moons for the monthly boss titles.
    public static readonly string[] Moons =
    [
        "Frostfang Moon", "Ashen Moon", "Thawing Moon", "Bloom Moon", "Ember Moon", "Sunfire Moon",
        "Storm Moon", "Dragon Moon", "Harvest Moon", "Shadow Moon", "Iron Moon", "Winter Crown",
    ];

    /// <summary>Weekly boss: a free rest pass (the rare drop). Monthly boss: all four beaten → that month's title.</summary>
    async Task<RewardResult> BossRewardAsync(Character character, QuestInstance quest, RewardResult result)
    {
        if (quest.BossKind == "weekly")
        {
            var pass = await db.ShopItems.FirstOrDefaultAsync(i => i.Code == "rest_pass");
            if (pass is null) return result;
            db.Purchases.Add(new Purchase { UserId = character.UserId, ShopItemId = pass.Id, Price = 0, PurchasedAt = clock.GetUtcNow().UtcDateTime });
            await db.SaveChangesAsync();
            return result with { Bonus = "Vật phẩm hiếm: 1 Vé nghỉ" };
        }

        var pending = await db.QuestInstances.AnyAsync(q => q.CharacterId == character.Id && q.Date == quest.Date
                                                            && q.BossKind == "monthly" && q.Status != QuestStatus.Completed);
        if (pending) return result;
        var code = $"moon_{quest.Date.Month:00}";
        var title = await db.Titles.FirstOrDefaultAsync(t => t.Code == code);
        if (title is null || await db.CharacterTitles.AnyAsync(t => t.CharacterId == character.Id && t.TitleId == title.Id))
            return result with { Bonus = "Đã hạ Rồng Than Hồng tháng này" };
        db.CharacterTitles.Add(new CharacterTitle { CharacterId = character.Id, TitleId = title.Id });
        await db.SaveChangesAsync();
        return result with { NewTitles = [.. result.NewTitles, title.Name], Bonus = "Hạ cả bốn Rồng Than Hồng" };
    }

    /// <summary>Adds XP and Gems, levels up, unlocks titles and evolution stages.</summary>
    public async Task<RewardResult> GrantAsync(Character character, int xp, int gems, string reason, string? refId)
    {
        var before = character.Level;
        character.TotalXp += xp;
        character.Level = Progression.LevelFromXp(character.TotalXp);

        if (gems != 0)
        {
            var wallet = await db.Wallets.FindAsync(character.UserId) ?? db.Wallets.Add(new Wallet { UserId = character.UserId }).Entity;
            wallet.Gems += gems;
            db.GemTransactions.Add(new GemTransaction { UserId = character.UserId, Amount = gems, Reason = reason, RefId = refId });
        }

        var newTitles = new List<string>();
        int? evolved = null;
        if (character.Level > before)
        {
            var owned = await db.CharacterTitles.Where(t => t.CharacterId == character.Id).Select(t => t.TitleId).ToListAsync();
            var earned = await db.Titles.Where(t => t.Source == "Level" && t.MinLevel <= character.Level && !owned.Contains(t.Id))
                .OrderBy(t => t.MinLevel).ToListAsync();
            foreach (var t in earned)
            {
                db.CharacterTitles.Add(new CharacterTitle { CharacterId = character.Id, TitleId = t.Id });
                newTitles.Add(t.Name);
            }
            // Auto-equip the newest level title unless the player picked an achievement title.
            var active = character.ActiveTitleId is { } id ? await db.Titles.FindAsync(id) : null;
            if (earned.Count > 0 && (active is null || active.Source == "Level")) character.ActiveTitleId = earned[^1].Id;

            var stage = Progression.EvolutionStage(character.Level);
            if (stage > character.EvolutionStage) { character.EvolutionStage = stage; evolved = stage; }
        }

        await db.SaveChangesAsync();
        return new RewardResult(xp, gems, before, character.Level, character.StreakDays, newTitles, evolved, "");
    }

    /// <summary>Called after a meal is logged or removed: the protein quest follows the meal log.</summary>
    public async Task<RewardResult?> SyncProteinAsync(Character character, DateOnly date, int proteinG)
    {
        var quest = await db.QuestInstances.Include(q => q.Template)
            .FirstOrDefaultAsync(q => q.CharacterId == character.Id && q.Date == date && q.Template!.Kind == "protein");
        if (quest is null || quest.Status == QuestStatus.Completed) return null;
        quest.DoneValue = proteinG;
        if (proteinG < quest.TargetValue)
        {
            quest.Status = proteinG > 0 ? QuestStatus.Partial : QuestStatus.Pending;
            await db.SaveChangesAsync();
            return null;
        }
        quest.Status = QuestStatus.Completed;
        quest.CompletedAt = clock.GetUtcNow().UtcDateTime;
        var xp = (int)Math.Round(quest.Xp * Progression.StreakMultiplier(character.StreakDays)) - quest.AwardedXp;
        var gems = quest.Gems - quest.AwardedGems;
        quest.AwardedXp += xp;
        quest.AwardedGems += gems;
        await db.SaveChangesAsync();

        var allDone = !await db.QuestInstances.AnyAsync(q =>
            q.CharacterId == character.Id && q.Date == date && q.IsRequired && q.Status != QuestStatus.Completed);
        if (allDone && character.LastStreakDate != date)
        {
            character.StreakDays = character.LastStreakDate == date.AddDays(-1) ? character.StreakDays + 1 : 1;
            character.LastStreakDate = date;
        }
        var result = await GrantAsync(character, xp, gems, "quest", quest.Id.ToString());
        return result with { QuestStatus = quest.Status.ToString() };
    }

    /// <summary>Swap a pending exercise quest for another variation of the same group (costs 20 Gems).</summary>
    public async Task<object> RerollAsync(string userId, Guid questId)
    {
        var (character, profile, today, _) = await LoadAsync(userId);
        var quest = await db.QuestInstances.Include(q => q.Exercise).FirstOrDefaultAsync(q => q.Id == questId && q.UserId == userId)
                    ?? throw new GameException("Không tìm thấy nhiệm vụ.", 404);
        if (quest.Date != today || quest.Status != QuestStatus.Pending || quest.Exercise is null)
            throw new GameException("Không đổi được nhiệm vụ này.");

        var wallet = await db.Wallets.FindAsync(userId);
        if (wallet is null || wallet.Gems < RerollCost) throw new GameException($"Cần {RerollCost} Gem để đổi nhiệm vụ.");

        var exercises = await db.Exercises.AsNoTracking().ToListAsync();
        var ctx = await BuildContextAsync(character, profile, today, exercises);
        var current = quest.Exercise;
        var takenToday = await db.QuestInstances.Where(q => q.CharacterId == character.Id && q.Date == today && q.ExerciseId != null)
            .Select(q => q.ExerciseId!.Value).ToListAsync();
        var options = QuestGenerator.Allowed(exercises, ctx)
            .Where(e => e.Group == current.Group && e.Id != current.Id && !takenToday.Contains(e.Id) && Math.Abs(e.Tier - current.Tier) <= 1)
            .ToList();
        if (options.Count == 0) throw new GameException("Không có bài thay thế phù hợp với dụng cụ và chỗ đau của ngươi.");

        var pick = options[Random.Shared.Next(options.Count)];
        var target = QuestGenerator.TargetFor(pick, ctx);
        quest.ExerciseId = pick.Id;
        quest.TargetValue = target;
        quest.Unit = pick.Unit;
        quest.Title = $"{quest.Sets} × {target} {(pick.Unit == "seconds" ? "giây " : "")}{pick.NameVi}";
        quest.Xp = (int)Math.Round((current.Group == "core" ? 30 : 40) * Progression.TierMultiplier(pick.Tier));

        wallet.Gems -= RerollCost;
        db.GemTransactions.Add(new GemTransaction { UserId = userId, Amount = -RerollCost, Reason = "reroll", RefId = quest.Id.ToString() });
        await db.SaveChangesAsync();
        await db.Entry(quest).Reference(q => q.Template).LoadAsync();
        await db.Entry(quest).Reference(q => q.Exercise).LoadAsync();
        return Dto(quest);
    }
}
