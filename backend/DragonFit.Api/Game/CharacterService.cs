using System.Text.Json;
using DragonFit.Api.Data;
using DragonFit.Api.Domain;
using DragonFit.Api.Onboarding;
using DragonFit.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace DragonFit.Api.Game;

public record RebirthRequest(string Name, string Avatar);

/// <summary>Death screen, rebirth, rest mode, the morning HP report, and the maintenance jobs.</summary>
public class CharacterService(AppDbContext db, TimeProvider clock, SettlementService settlement, IProofStorage storage)
{
    public const int RestDaysPerMonth = 7;

    /// <summary>Where the client should go: character creation, the game, or the death screen.</summary>
    public async Task<object> MeAsync(string userId)
    {
        var user = await db.Users.FindAsync(userId);
        var progress = await db.OnboardingProgress.FindAsync(userId);
        var alive = await db.Characters.FirstOrDefaultAsync(c => c.UserId == userId && c.Status == CharacterStatus.Alive);
        var profile = await db.PlayerProfiles.FindAsync(userId);
        if (alive is not null && profile is not null)
        {
            await settlement.CatchUpAsync(alive, profile);
            if (alive.Status != CharacterStatus.Alive) alive = null;
        }

        object? death = null;
        if (alive is null)
        {
            var dead = await db.Characters.Where(c => c.UserId == userId && c.Status == CharacterStatus.Dead)
                .OrderByDescending(c => c.DiedAt).FirstOrDefaultAsync();
            if (dead is not null)
            {
                var title = dead.ActiveTitleId is { } t ? await db.Titles.Where(x => x.Id == t).Select(x => x.Name).FirstOrDefaultAsync() : null;
                var avatar = ForgeService.SpriteUrl(dead.CustomSpriteKey)
                             ?? await db.AvatarModels.Where(a => a.Id == dead.AvatarModelId).Select(a => a.SpriteSheetUrl).FirstOrDefaultAsync();
                var days = await db.DailySettlements.Where(s => s.CharacterId == dead.Id).OrderByDescending(s => s.Date).Take(5)
                    .Select(s => new { s.Date, s.Damage, s.Heal, s.HpAfter, s.ActualKcal, s.TargetKcal, s.MealsLogged, s.QuestCompletion }).ToListAsync();
                death = new
                {
                    dead.Name, dead.Level, title, dead.EvolutionStage, avatar, dead.DiedAt, dead.PurgeAt,
                    daysAlive = (dead.DiedAt!.Value.Date - dead.CreatedAt.Date).Days + 1,
                    questsDone = await db.QuestInstances.CountAsync(q => q.CharacterId == dead.Id && q.Status == QuestStatus.Completed),
                    lastDays = days,
                };
            }
        }
        return new
        {
            email = user?.Email,
            onboarding = (progress?.Status ?? OnboardingStatus.NotStarted).ToString(),
            hasCharacter = alive is not null,
            death,
        };
    }

    /// <summary>After death: a new character from steps 1–2 only (name and avatar). Stats come from real data, so they carry over.</summary>
    public async Task<Guid> RebirthAsync(string userId, RebirthRequest req)
    {
        var progress = await db.OnboardingProgress.FindAsync(userId);
        if (progress?.Status != OnboardingStatus.Completed) throw new GameException("Hãy hoàn tất tạo nhân vật trước.");
        if (await db.Characters.AnyAsync(c => c.UserId == userId && c.Status == CharacterStatus.Alive))
            throw new GameException("Ngươi vẫn còn một nhân vật đang sống.");

        var nameQ = await db.OnboardingQuestions.Include(q => q.Options).SingleAsync(q => q.Code == "char_name");
        var avatarQ = await db.OnboardingQuestions.SingleAsync(q => q.Code == "avatar");
        var avatarCodes = (await db.AvatarModels.Where(a => a.IsActive).Select(a => a.Code).ToListAsync()).ToHashSet();
        var errors = new Dictionary<string, string>();
        var n = AnswerValidator.Validate(nameQ, JsonSerializer.SerializeToElement(req.Name ?? ""));
        if (n.Error is not null) errors["char_name"] = n.Error;
        var a = AnswerValidator.Validate(avatarQ, JsonSerializer.SerializeToElement(req.Avatar ?? ""), avatarCodes);
        if (a.Error is not null) errors["avatar"] = a.Error;
        if (errors.Count > 0) throw new OnboardingException(errors);

        var avatarCode = req.Avatar;
        var avatar = await db.AvatarModels.SingleAsync(x => x.Code == avatarCode);
        var firstTitle = await db.Titles.SingleAsync(t => t.Code == "wandering_squire");
        var character = new Character
        {
            UserId = userId, Name = req.Name.Trim(), AvatarModelId = avatar.Id, ActiveTitleId = firstTitle.Id,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.Characters.Add(character);
        db.CharacterTitles.Add(new CharacterTitle { CharacterId = character.Id, TitleId = firstTitle.Id });
        await db.SaveChangesAsync();
        return character.Id;
    }

    /// <summary>Illness or injury: no damage and no healing, at most 7 days per calendar month, starting today.</summary>
    public async Task<object> SetRestModeAsync(string userId, int days)
    {
        var character = await db.Characters.FirstOrDefaultAsync(c => c.UserId == userId && c.Status == CharacterStatus.Alive)
                        ?? throw new GameException("Chưa có nhân vật.", 404);
        var profile = await db.PlayerProfiles.FindAsync(userId) ?? throw new GameException("Chưa có hồ sơ.", 404);
        var today = DayClock.For(clock, profile.TimeZoneId).Today;

        if (days == 0)
        {
            if (character.RestModeUntil >= today) character.RestModeUntil = today.AddDays(-1);
            await db.SaveChangesAsync();
            return new { restModeUntil = (DateOnly?)null };
        }
        if (days is < 1 or > RestDaysPerMonth) throw new GameException($"Chọn từ 1 đến {RestDaysPerMonth} ngày.");

        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var used = await db.DailySettlements.CountAsync(s => s.CharacterId == character.Id && s.IsRestMode && s.Date >= monthStart);
        var end = today.AddDays(days - 1);
        var inMonth = Math.Min(end.DayNumber, monthStart.AddMonths(1).AddDays(-1).DayNumber) - today.DayNumber + 1;
        if (used + inMonth > RestDaysPerMonth)
            throw new GameException($"Tháng này chỉ còn {Math.Max(0, RestDaysPerMonth - used)} ngày dưỡng thương.");

        character.RestModeFrom = today;
        character.RestModeUntil = end;
        await db.SaveChangesAsync();
        return new { restModeUntil = end };
    }

    public async Task<int> RestDaysLeftAsync(Character character, DateOnly today)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var used = await db.DailySettlements.CountAsync(s => s.CharacterId == character.Id && s.IsRestMode && s.Date >= monthStart);
        return Math.Max(0, RestDaysPerMonth - used);
    }

    /// <summary>The newest settlement the player hasn't seen yet, for the morning report.</summary>
    public async Task<object?> UnseenReportAsync(Character character)
    {
        var s = await db.DailySettlements.Where(x => x.CharacterId == character.Id && x.SeenAt == null)
            .OrderByDescending(x => x.Date).FirstOrDefaultAsync();
        if (s is null) return null;
        var unseenDays = await db.DailySettlements.CountAsync(x => x.CharacterId == character.Id && x.SeenAt == null);
        var lines = await db.HpEvents.Where(e => e.CharacterId == character.Id && e.Date == s.Date).OrderBy(e => e.Id)
            .Select(e => new { e.Amount, e.Reason, e.Detail }).ToListAsync();
        return new
        {
            s.Id, s.Date, s.TargetKcal, s.ActualKcal, s.ProteinG, s.ProteinTargetG, s.MealsLogged, s.QuestCompletion,
            s.Damage, s.Heal, s.IsPerfect, s.PerfectStreak, s.IsRestMode, s.HpBefore, s.HpAfter, unseenDays, lines,
        };
    }

    public async Task MarkReportsSeenAsync(Character character)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await db.DailySettlements.Where(x => x.CharacterId == character.Id && x.SeenAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.SeenAt, now));
    }

    // ------------------------------------------------------------------ jobs (called by cron-job.org)

    public async Task<int> SettleAllAsync()
    {
        var alive = await db.Characters.Where(c => c.Status == CharacterStatus.Alive).ToListAsync();
        foreach (var c in alive)
        {
            var profile = await db.PlayerProfiles.FindAsync(c.UserId);
            if (profile is not null) await settlement.CatchUpAsync(c, profile);
        }
        return alive.Count;
    }

    /// <summary>Deletes characters 30 days after death and proof photos older than 30 days.</summary>
    public async Task<(int Characters, int Photos)> CleanupAsync()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expired = await db.Characters.Where(c => c.Status == CharacterStatus.Dead && c.PurgeAt < now).Select(c => c.Id).ToListAsync();
        foreach (var id in expired)
        {
            await db.HpEvents.Where(x => x.CharacterId == id).ExecuteDeleteAsync();
            await db.DailySettlements.Where(x => x.CharacterId == id).ExecuteDeleteAsync();
            await db.StatSnapshots.Where(x => x.CharacterId == id).ExecuteDeleteAsync();
            await db.CharacterTitles.Where(x => x.CharacterId == id).ExecuteDeleteAsync();
            var proofs = await db.QuestInstances.Where(q => q.CharacterId == id && q.ProofUrl != null).Select(q => q.ProofUrl!).ToListAsync();
            foreach (var key in proofs) await storage.DeleteAsync(key);
            await db.QuestInstances.Where(x => x.CharacterId == id).ExecuteDeleteAsync();
            await db.Characters.Where(x => x.Id == id).ExecuteDeleteAsync();
        }

        var cutoff = now.AddDays(-30);
        var old = await db.QuestInstances.Where(q => q.ProofUrl != null && q.CompletedAt < cutoff).ToListAsync();
        foreach (var q in old)
        {
            await storage.DeleteAsync(q.ProofUrl!);
            q.ProofUrl = null;
        }
        await db.SaveChangesAsync();
        return (expired.Count, old.Count);
    }
}
