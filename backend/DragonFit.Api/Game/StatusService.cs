using DragonFit.Api.Data;
using DragonFit.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DragonFit.Api.Game;

/// <summary>Everything the Status screen (B4) needs, in one call.</summary>
public class StatusService(AppDbContext db, QuestService quests, StatService stats, CharacterService characters)
{
    public async Task<object> GetAsync(string userId)
    {
        var (character, profile, today, _) = await quests.LoadAsync(userId);
        var avatar = await db.AvatarModels.FindAsync(character.AvatarModelId);
        var title = character.ActiveTitleId is { } tid ? await db.Titles.FindAsync(tid) : null;
        var titles = await db.CharacterTitles.Where(t => t.CharacterId == character.Id).Include(t => t.Title)
            .OrderBy(t => t.Title!.MinLevel).Select(t => new { t.Title!.Code, t.Title.Name, t.Title.MinLevel }).ToListAsync();
        var wallet = await db.Wallets.FindAsync(userId);
        var (current, previous) = await stats.RefreshAsync(character, profile, today);
        var metrics = await db.BodyMetrics.Where(m => m.UserId == userId).OrderBy(m => m.Date).ToListAsync();
        var latest = metrics.LastOrDefault();
        var first = metrics.FirstOrDefault();
        var todayQuests = await db.QuestInstances.Where(q => q.CharacterId == character.Id && q.Date == today)
            .Select(q => new { q.IsRequired, q.Status }).ToListAsync();

        var weight = latest?.WeightKg ?? 60m;
        var targets = Nutrition.Calculate(profile.Sex, weight, profile.HeightCm, today.Year - profile.BirthYear, profile.TrainingDaysPerWeek, profile.Goal);
        var (into, needed) = Progression.LevelProgress(character.TotalXp);

        int Delta(Func<StatSnapshot, int> f) => previous is null ? 0 : f(current) - f(previous);

        // Skill wheel (B4): tier per exercise group from the first trial and from finished quests;
        // a node glows when the group was trained in the last 7 days.
        var baselineTiers = await db.WorkoutSets
            .Where(s => db.WorkoutSessions.Any(w => w.Id == s.SessionId && w.UserId == userId && w.SessionType == SessionType.Baseline))
            .Select(s => new { s.Exercise!.Group, s.Exercise.Tier }).ToListAsync();
        var done = await db.QuestInstances
            .Where(q => q.CharacterId == character.Id && q.Status == QuestStatus.Completed)
            .Select(q => new { q.Date, Group = q.Exercise != null ? q.Exercise.Group : q.Template!.Kind, Tier = q.Exercise != null ? q.Exercise.Tier : 1 })
            .ToListAsync();
        var weekAgo = today.AddDays(-6);
        var groups = new[] { ("push", "Đẩy"), ("pull", "Kéo"), ("legs", "Chân"), ("core", "Core"), ("steps", "Cardio"), ("mobility", "Dẻo dai") };
        var skills = groups.Select(g =>
        {
            var tier = baselineTiers.Where(b => b.Group == g.Item1).Select(b => b.Tier)
                .Concat(done.Where(d => d.Group == g.Item1).Select(d => d.Tier)).DefaultIfEmpty(1).Max();
            var count = done.Count(d => d.Group == g.Item1);
            if (g.Item1 is "steps" or "mobility") tier = Math.Min(5, 1 + count / 7);
            return new { group = g.Item1, name = g.Item2, tier, maxTier = 5, active = done.Any(d => d.Group == g.Item1 && d.Date >= weekAgo), completed = count };
        });

        return new
        {
            character = new
            {
                character.Name, character.Level, character.TotalXp, xpIntoLevel = into, xpForNext = needed,
                hp = character.CurrentHp, maxHp = 100, mp = latest?.Date == today ? latest.EnergyScore ?? 5 : 5, maxMp = 5,
                hpState = character.CurrentHp switch { > 30 => "normal", > 10 => "critical", _ => "dying" },
                restModeUntil = character.RestModeUntil >= today ? character.RestModeUntil : null,
                restDaysLeft = await characters.RestDaysLeftAsync(character, today),
                status = character.Status.ToString(), character.StreakDays, character.EvolutionStage,
                title = title?.Name,
                avatar = avatar is null ? null : new { avatar.Code, avatar.Name, avatar.SpriteSheetUrl },
                nextEvolutionLevel = Progression.EvolutionLevels.FirstOrDefault(l => l > character.Level),
            },
            gems = wallet?.Gems ?? 0,
            report = await characters.UnseenReportAsync(character),
            titles,
            stats = new[]
            {
                new { key = "STR", name = "Sức mạnh", value = current.Str, delta = Delta(s => s.Str),
                      hint = "Bậc bài tập và số rep tốt nhất của 4 nhóm Đẩy, Kéo, Chân, Core trong 28 ngày" },
                new { key = "VIT", name = "Thể chất", value = current.Vit, delta = Delta(s => s.Vit),
                      hint = "Tỉ lệ vòng bụng / chiều cao (trung vị 3 lần đo gần nhất); giảm 1 cm vòng bụng ≈ +1" },
                new { key = "END", name = "Sức bền", value = current.End, delta = Delta(s => s.End),
                      hint = "Số bước trung bình 7 ngày ÷ 150" },
                new { key = "DIS", name = "Kỷ luật", value = current.Dis, delta = Delta(s => s.Dis),
                      hint = "Tỉ lệ hoàn thành nhiệm vụ bắt buộc 28 ngày và chuỗi ngày" },
                new { key = "WIS", name = "Trí tuệ", value = current.Wis, delta = Delta(s => s.Wis),
                      hint = "Ngày ghi bữa ăn, ngày ăn đúng mục tiêu kcal và ngày đạt protein trong 28 ngày" },
            },
            skills,
            body = new
            {
                weightKg = latest?.WeightKg, waistCm = latest?.WaistCm, bodyFatPct = latest?.BodyFatPct,
                waistChangeCm = latest?.WaistCm - first?.WaistCm,
                trend = metrics.TakeLast(84).Select(m => new { m.Date, m.WeightKg, m.WaistCm }),
            },
            targets = new { kcal = targets.TargetKcal, proteinG = targets.ProteinG, maintenanceKcal = targets.MaintenanceKcal, steps = profile.BaselineSteps },
            today = new
            {
                date = today,
                required = todayQuests.Count(q => q.IsRequired),
                done = todayQuests.Count(q => q.IsRequired && q.Status == QuestStatus.Completed),
                questTime = profile.QuestTime.ToString("HH:mm"),
                measured = latest?.Date == today,
            },
        };
    }
}
