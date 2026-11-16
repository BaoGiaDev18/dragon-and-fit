using System.Buffers.Binary;
using DragonFit.Api.Data;
using DragonFit.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DragonFit.Api.Game;

public record ForgeRequest(string? Avatar, string? TitleCode);

/// <summary>Character forge (plan B4): change avatar, pick a title, upload a custom sprite sheet.</summary>
public class ForgeService(AppDbContext db, QuestService quests, IWebHostEnvironment env)
{
    public const int SheetWidth = 256, SheetHeight = 384;
    public const long MaxSpriteBytes = 1_000_000;

    string SpriteRoot => Path.Combine(env.ContentRootPath, "uploads", "sprites");

    public async Task<object> GetAsync(string userId)
    {
        var (character, _, _, _) = await quests.LoadAsync(userId);
        var avatars = await db.AvatarModels.Where(a => a.IsActive).OrderBy(a => a.Id)
            .Select(a => new { a.Code, a.Name, a.SpriteSheetUrl, a.IsDefault, Evolutions = a.Evolutions.OrderBy(e => e.Stage).Select(e => new { e.Stage, e.MinLevel }) })
            .ToListAsync();
        var titles = await db.CharacterTitles.Where(t => t.CharacterId == character.Id).Include(t => t.Title)
            .OrderBy(t => t.Title!.MinLevel).Select(t => new { t.Title!.Code, t.Title.Name, t.Title.Source, t.Title.MinLevel, t.UnlockedAt }).ToListAsync();
        var lockedTitles = await db.Titles.Where(t => t.Source == "Level" && t.MinLevel > character.Level)
            .OrderBy(t => t.MinLevel).Select(t => new { t.Name, t.MinLevel }).ToListAsync();
        var current = await db.AvatarModels.Where(a => a.Id == character.AvatarModelId).Select(a => a.Code).FirstAsync();
        var activeTitle = character.ActiveTitleId is { } id ? await db.Titles.Where(t => t.Id == id).Select(t => t.Code).FirstOrDefaultAsync() : null;
        return new
        {
            avatar = current, activeTitle, character.EvolutionStage, character.Level,
            customSprite = SpriteUrl(character.CustomSpriteKey),
            avatars, titles, lockedTitles,
            template = new { width = SheetWidth, height = SheetHeight, frame = 64, columns = 4, rows = 6 },
        };
    }

    public static string? SpriteUrl(string? key) => key is null ? null : $"/sprites/{key}";

    public async Task SaveAsync(string userId, ForgeRequest req)
    {
        var (character, _, _, _) = await quests.LoadAsync(userId);
        if (req.Avatar is not null)
        {
            var avatar = await db.AvatarModels.FirstOrDefaultAsync(a => a.Code == req.Avatar && a.IsActive)
                         ?? throw new GameException("Hình hài không tồn tại.");
            character.AvatarModelId = avatar.Id;
        }
        if (req.TitleCode is not null)
        {
            var title = await db.CharacterTitles.Include(t => t.Title)
                            .FirstOrDefaultAsync(t => t.CharacterId == character.Id && t.Title!.Code == req.TitleCode)
                        ?? throw new GameException("Ngươi chưa có danh hiệu này.");
            character.ActiveTitleId = title.TitleId;
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Checks that the bytes are a PNG of exactly 256 × 384 px (4 frames × 6 stages of 64 × 64)
    /// by reading the IHDR header, without decoding the image.
    /// </summary>
    public static string? ValidateSheet(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length > MaxSpriteBytes) return "File quá lớn (tối đa 1 MB).";
        if (bytes.Length < 33 || !bytes[..8].SequenceEqual(signature)) return "File phải là ảnh PNG.";
        if (!bytes.Slice(12, 4).SequenceEqual("IHDR"u8)) return "File PNG không hợp lệ.";
        var w = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4));
        var h = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4));
        if (w != SheetWidth || h != SheetHeight)
            return $"Ảnh phải đúng {SheetWidth} × {SheetHeight} px (4 khung × 6 bậc, mỗi khung 64 × 64). Ảnh của ngươi là {w} × {h}.";
        return null;
    }

    public async Task<string> UploadSpriteAsync(string userId, Stream content)
    {
        var (character, _, _, _) = await quests.LoadAsync(userId);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(buffer)) > 0)
        {
            if (ms.Length + read > MaxSpriteBytes) throw new GameException("File quá lớn (tối đa 1 MB).");
            ms.Write(buffer, 0, read);
        }
        if (ValidateSheet(ms.GetBuffer().AsSpan(0, (int)ms.Length)) is { } error) throw new GameException(error);

        Directory.CreateDirectory(SpriteRoot);
        var key = $"{character.Id:N}-{Guid.NewGuid():N}"[..49] + ".png";
        await File.WriteAllBytesAsync(Path.Combine(SpriteRoot, key), ms.ToArray());
        DeleteFile(character.CustomSpriteKey);
        character.CustomSpriteKey = key;
        await db.SaveChangesAsync();
        return SpriteUrl(key)!;
    }

    public async Task RemoveSpriteAsync(string userId)
    {
        var (character, _, _, _) = await quests.LoadAsync(userId);
        DeleteFile(character.CustomSpriteKey);
        character.CustomSpriteKey = null;
        await db.SaveChangesAsync();
    }

    void DeleteFile(string? key)
    {
        if (key is null) return;
        var path = Path.Combine(SpriteRoot, Path.GetFileName(key));
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Sprites appear on the leaderboard, so they are public; the name is validated to stay inside the folder.</summary>
    public (string Path, bool Exists) Open(string name)
    {
        if (name.Length > 80 || !name.EndsWith(".png") || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '.')))
            return ("", false);
        var path = Path.Combine(SpriteRoot, name);
        return (path, File.Exists(path));
    }
}
