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

public static class GameEndpoints
{
    static string Uid(ClaimsPrincipal u) => u.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public static void MapGameEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        // Where should the client go after login: character creation or the main menu?
        api.MapGet("/me", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var id = Uid(user);
            var u = await db.Users.FindAsync(id);
            var progress = await db.OnboardingProgress.FindAsync(id);
            var hasCharacter = await db.Characters.AnyAsync(c => c.UserId == id && c.Status == CharacterStatus.Alive);
            return Results.Ok(new { email = u?.Email, onboarding = (progress?.Status ?? OnboardingStatus.NotStarted).ToString(), hasCharacter });
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
            return Results.Ok(await svc.CompleteAsync(Uid(user), id, done, stream, file?.ContentType));
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
}
