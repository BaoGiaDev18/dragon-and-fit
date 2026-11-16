using System.Security.Claims;
using System.Text.Json;
using DragonFit.Api.Data;
using DragonFit.Api.Domain;
using DragonFit.Api.Game;
using DragonFit.Api.Onboarding;
using DragonFit.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace DragonFit.Api.Endpoints;

public record StepRequest(Dictionary<string, JsonElement>? Answers, bool Skip = false);
public record CompleteRequest(string? TimeZone);
public record RestModeRequest(int Days);

public static class GameEndpoints
{
    static string Uid(ClaimsPrincipal u) => u.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public static void MapGameEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        // Where should the client go after login: character creation or the main menu?
        api.MapGet("/me", async (ClaimsPrincipal user, CharacterService svc) => Results.Ok(await svc.MeAsync(Uid(user))));

        var character = api.MapGroup("/character");
        character.MapPost("/rebirth", async (RebirthRequest req, ClaimsPrincipal user, CharacterService svc) =>
            Results.Ok(new { characterId = await svc.RebirthAsync(Uid(user), req) }));
        character.MapPost("/rest-mode", async (RestModeRequest req, ClaimsPrincipal user, CharacterService svc) =>
            Results.Ok(await svc.SetRestModeAsync(Uid(user), req.Days)));
        character.MapPost("/report/seen", async (ClaimsPrincipal user, QuestService quests, CharacterService svc) =>
        {
            var (c, _, _, _) = await quests.LoadAsync(Uid(user));
            await svc.MarkReportsSeenAsync(c);
            return Results.NoContent();
        });

        // Shop and forge (Phase 3)
        api.MapGet("/shop", async (ClaimsPrincipal user, ShopService svc) => Results.Ok(await svc.GetAsync(Uid(user))));
        api.MapPost("/shop/{id:int}/buy", async (int id, ClaimsPrincipal user, ShopService svc) => Results.Ok(await svc.BuyAsync(Uid(user), id)));
        api.MapPost("/shop/rest-pass/use", async (ClaimsPrincipal user, ShopService svc) => Results.Ok(await svc.UseRestPassAsync(Uid(user))));
        var forge = api.MapGroup("/forge");
        forge.MapGet("", async (ClaimsPrincipal user, ForgeService svc) => Results.Ok(await svc.GetAsync(Uid(user))));
        forge.MapPost("", async (ForgeRequest req, ClaimsPrincipal user, ForgeService svc) =>
        {
            await svc.SaveAsync(Uid(user), req);
            return Results.NoContent();
        });
        forge.MapPost("/sprite", async (HttpRequest http, ClaimsPrincipal user, ForgeService svc) =>
        {
            if (!http.HasFormContentType) return Results.BadRequest(new { error = "Cần gửi dạng form." });
            var file = (await http.ReadFormAsync()).Files.GetFile("sprite");
            if (file is null) return Results.BadRequest(new { error = "Thiếu file sprite." });
            if (file.Length > ForgeService.MaxSpriteBytes) return Results.BadRequest(new { error = "File quá lớn (tối đa 1 MB)." });
            await using var s = file.OpenReadStream();
            return Results.Ok(new { url = await svc.UploadSpriteAsync(Uid(user), s) });
        }).DisableAntiforgery();
        forge.MapDelete("/sprite", async (ClaimsPrincipal user, ForgeService svc) =>
        {
            await svc.RemoveSpriteAsync(Uid(user));
            return Results.NoContent();
        });

        // Uploaded sprite sheets are public (they will show on the leaderboard).
        app.MapGet("/sprites/{name}", (string name, ForgeService svc) =>
        {
            var (path, exists) = svc.Open(name);
            return exists ? Results.File(path, "image/png") : Results.NotFound();
        });

        // Food and the daily weigh-in (Phase 2)
        api.MapGet("/foods", async (string? q, MealService svc) => Results.Ok(await svc.SearchFoodsAsync(q)));
        var meals = api.MapGroup("/meals");
        meals.MapGet("/day", async (DateOnly? date, ClaimsPrincipal user, MealService svc) => Results.Ok(await svc.GetDayAsync(Uid(user), date)));
        meals.MapPost("", async (AddMealRequest req, ClaimsPrincipal user, MealService svc) => Results.Ok(new { reward = await svc.AddAsync(Uid(user), req) }));
        meals.MapDelete("/{id:long}", async (long id, ClaimsPrincipal user, MealService svc) =>
        {
            await svc.DeleteAsync(Uid(user), id);
            return Results.NoContent();
        });
        api.MapPost("/body-metrics", async (BodyMetricRequest req, ClaimsPrincipal user, MealService svc) =>
        {
            await svc.SaveBodyMetricAsync(Uid(user), req);
            return Results.NoContent();
        });

        var onboarding = api.MapGroup("/onboarding");
        onboarding.MapGet("", async (ClaimsPrincipal user, OnboardingService svc) => Results.Ok(await svc.GetStateAsync(Uid(user))));
        onboarding.MapPost("/steps/{code}", async (string code, StepRequest req, ClaimsPrincipal user, OnboardingService svc) =>
        {
            var next = await svc.SaveStepAsync(Uid(user), code, req.Answers ?? [], req.Skip);
            return Results.Ok(new { nextStep = next });
        });
        onboarding.MapPost("/complete", async (CompleteRequest req, ClaimsPrincipal user, OnboardingService svc) =>
            Results.Ok(new { characterId = await svc.CompleteAsync(Uid(user), req.TimeZone) }));

        api.MapGet("/status", async (ClaimsPrincipal user, StatusService svc) => Results.Ok(await svc.GetAsync(Uid(user))));

        var quests = api.MapGroup("/quests");
        quests.MapGet("/today", async (ClaimsPrincipal user, QuestService svc) => Results.Ok(await svc.GetTodayAsync(Uid(user))));

        // multipart/form-data: doneValue + optional photo (compressed to WebP in the browser).
        quests.MapPost("/{id:guid}/complete", async (Guid id, HttpRequest http, ClaimsPrincipal user, QuestService svc) =>
        {
            if (!http.HasFormContentType) return Results.BadRequest(new { error = "Cần gửi dạng form." });
            var form = await http.ReadFormAsync();
            if (!int.TryParse(form["doneValue"], out var done)) return Results.BadRequest(new { error = "Thiếu kết quả." });
            var file = form.Files.GetFile("photo");
            if (file is { Length: > LocalProofStorage.MaxBytes }) return Results.BadRequest(new { error = "Ảnh quá lớn (tối đa 1 MB)." });
            await using var stream = file?.OpenReadStream();
            int[]? sets = null;
            if (form.TryGetValue("sets", out var raw) && !string.IsNullOrWhiteSpace(raw))
            {
                var parts = raw.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length > 10 || parts.Any(p => !int.TryParse(p, out var n) || n < 0 || n > 500))
                    return Results.BadRequest(new { error = "Danh sách set không hợp lệ." });
                sets = parts.Select(int.Parse).ToArray();
            }
            return Results.Ok(await svc.CompleteAsync(Uid(user), id, done, stream, file?.ContentType, sets));
        }).DisableAntiforgery();

        quests.MapPost("/{id:guid}/reroll", async (Guid id, ClaimsPrincipal user, QuestService svc) =>
            Results.Ok(await svc.RerollAsync(Uid(user), id)));

        // Proof photos are private: only their owner can fetch them.
        quests.MapGet("/{id:guid}/proof", async (Guid id, ClaimsPrincipal user, AppDbContext db, IProofStorage storage) =>
        {
            var key = await db.QuestInstances.Where(q => q.Id == id && q.UserId == Uid(user)).Select(q => q.ProofUrl).FirstOrDefaultAsync();
            if (key is null) return Results.NotFound();
            var file = await storage.OpenAsync(key);
            return file is null ? Results.NotFound() : Results.Stream(file.Value.Content, file.Value.ContentType);
        });
    }

    /// <summary>
    /// Maintenance jobs, called on a schedule by cron-job.org (the free server may be asleep, so it
    /// can't run timers itself). Protected by the X-Job-Key header; every job is safe to run twice.
    /// </summary>
    public static void MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var jobs = app.MapGroup("/jobs").AddEndpointFilter(async (ctx, next) =>
        {
            var key = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Jobs:Key"];
            var sent = ctx.HttpContext.Request.Headers["X-Job-Key"].ToString();
            if (string.IsNullOrEmpty(key) || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(key), System.Text.Encoding.UTF8.GetBytes(sent)))
                return Results.Unauthorized();
            return await next(ctx);
        });
        jobs.MapPost("/settle", async (CharacterService svc) => Results.Ok(new { characters = await svc.SettleAllAsync() }));
        jobs.MapPost("/cleanup", async (CharacterService svc) =>
        {
            var (characters, photos) = await svc.CleanupAsync();
            return Results.Ok(new { characters, photos });
        });
    }
}
