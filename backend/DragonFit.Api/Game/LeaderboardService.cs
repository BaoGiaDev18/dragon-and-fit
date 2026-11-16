using DragonFit.Api.Data;
using DragonFit.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DragonFit.Api.Game;

/// <summary>
/// Leaderboard (plan B2): weekly XP, level, and current streak. Only the public face of a character
/// is shown — name, avatar, level, title. Body measurements and meals are never part of it.
/// </summary>
public class LeaderboardService(AppDbContext db, QuestService quests)
{
    public static readonly string[] Boards = ["week", "level", "streak"];

    public async Task<object> GetAsync(string userId, string board)
    {
        if (!Boards.Contains(board)) throw new GameException("Bảng xếp hạng không tồn tại.");
        var (me, _, today, _) = await quests.LoadAsync(userId);
        var weekStart = StatService.WeekStart(today);
        var yesterday = today.AddDays(-1);

        var rows = await db.Characters.Where(c => c.Status == CharacterStatus.Alive)
            .Select(c => new
            {
                c.Id, c.Name, c.Level, c.TotalXp, c.EvolutionStage, c.CustomSpriteKey,
                c.StreakDays, c.LastStreakDate,
                Title = c.ActiveTitle != null ? c.ActiveTitle.Name : null,
                Sprite = c.AvatarModel != null ? c.AvatarModel.SpriteSheetUrl : null,
                WeekXp = db.QuestInstances.Where(q => q.CharacterId == c.Id && q.Date >= weekStart).Sum(q => (int?)q.AwardedXp) ?? 0,
            })
            .ToListAsync();

        var scored = rows.Select(r => new
        {
            r,
            streak = r.LastStreakDate >= yesterday ? r.StreakDays : 0,
        }).Select(x => new
        {
            x.r, x.streak,
            score = board switch { "week" => x.r.WeekXp, "streak" => x.streak, _ => x.r.TotalXp },
        })
        .OrderByDescending(x => x.score).ThenByDescending(x => x.r.TotalXp).ThenBy(x => x.r.Name)
        .Select((x, i) => new
        {
            rank = i + 1, isMe = x.r.Id == me.Id,
            name = x.r.Name, level = x.r.Level, title = x.r.Title, stage = x.r.EvolutionStage,
            sprite = ForgeService.SpriteUrl(x.r.CustomSpriteKey) ?? x.r.Sprite,
            weekXp = x.r.WeekXp, streak = x.streak, score = x.score,
        })
        .ToList();

        return new
        {
            board, weekStart, players = scored.Count,
            top = scored.Take(50),
            me = scored.FirstOrDefault(s => s.isMe),
        };
    }
}
