namespace DragonFit.Api.Storage;

/// <summary>
/// Where proof photos live. Development uses the local disk; production will use Cloudflare R2
/// (R2ProofStorage, added once the R2 bucket and keys exist). Photos are private: they are only
/// served back to their owner through an authenticated endpoint, never as a public URL.
/// </summary>
public interface IProofStorage
{
    Task<string> SaveAsync(string userId, Guid questId, Stream content, string contentType);
    Task<(Stream Content, string ContentType)?> OpenAsync(string key);
}

public class LocalProofStorage(IWebHostEnvironment env) : IProofStorage
{
    public const long MaxBytes = 1_000_000;
    static readonly Dictionary<string, string> Extensions = new()
    {
        ["image/webp"] = ".webp", ["image/jpeg"] = ".jpg", ["image/png"] = ".png",
    };

    string Root => Path.Combine(env.ContentRootPath, "uploads", "proofs");

    public async Task<string> SaveAsync(string userId, Guid questId, Stream content, string contentType)
    {
        if (!Extensions.TryGetValue(contentType, out var ext))
            throw new Game.GameException("Ảnh phải là WebP, JPEG hoặc PNG.");
        var safeUser = new string(userId.Where(char.IsLetterOrDigit).ToArray());
        var dir = Path.Combine(Root, safeUser);
        Directory.CreateDirectory(dir);
        var key = $"{safeUser}/{questId:N}{ext}";

        await using var file = File.Create(Path.Combine(Root, key));
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await content.ReadAsync(buffer)) > 0)
        {
            total += read;
            if (total > MaxBytes)
            {
                file.Close();
                File.Delete(Path.Combine(Root, key));
                throw new Game.GameException("Ảnh quá lớn (tối đa 1 MB).");
            }
            await file.WriteAsync(buffer.AsMemory(0, read));
        }
        return key;
    }

    public Task<(Stream Content, string ContentType)?> OpenAsync(string key)
    {
        var path = Path.GetFullPath(Path.Combine(Root, key));
        if (!path.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar) || !File.Exists(path))
            return Task.FromResult<(Stream, string)?>(null);
        var type = Extensions.First(e => path.EndsWith(e.Value)).Key;
        return Task.FromResult<(Stream, string)?>((File.OpenRead(path), type));
    }
}
