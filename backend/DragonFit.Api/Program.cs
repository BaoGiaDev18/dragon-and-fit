using DragonFit.Api.Data;
using DragonFit.Api.Domain;
using DragonFit.Api.Endpoints;
using DragonFit.Api.Game;
using DragonFit.Api.Onboarding;
using DragonFit.Api.Storage;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Render (and most container hosts) pass the port in $PORT.
if (Environment.GetEnvironmentVariable("PORT") is { } port)
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddAuthorization();
builder.Services
    .AddIdentityApiEndpoints<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<AppDbContext>();

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IProofStorage, LocalProofStorage>();
builder.Services.AddScoped<OnboardingService>();
builder.Services.AddScoped<QuestService>();
builder.Services.AddScoped<StatusService>();

var app = builder.Build();

// Game and validation errors become a JSON body the UI can show; anything else is a plain 500.
app.UseExceptionHandler(e => e.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    switch (ex)
    {
        case OnboardingException oe:
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { error = "Thông tin chưa hợp lệ.", fields = oe.Errors });
            break;
        case GameException ge:
            ctx.Response.StatusCode = ge.Status;
            await ctx.Response.WriteAsJsonAsync(new { error = ge.Message });
            break;
        default:
            ctx.Response.StatusCode = 500;
            await ctx.Response.WriteAsJsonAsync(new { error = "Có lỗi xảy ra. Hãy thử lại." });
            break;
    }
}));

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.RunAsync(db);
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGroup("/api/auth").MapIdentityApi<AppUser>();
app.MapGameEndpoints();

app.Run();

public partial class Program;
