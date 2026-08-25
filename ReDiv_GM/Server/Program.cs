using Microsoft.AspNetCore.Diagnostics;
using ReDiv.GM.Server;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://127.0.0.1:5168");
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:3000", "http://127.0.0.1:3000")
    .AllowAnyHeader()
    .AllowAnyMethod()));
builder.Services.AddSingleton<SpacetimeRepository>();

var app = builder.Build();

app.UseCors();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    Exception? error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    context.Response.StatusCode = error is SpacetimeCommandException ? 502 : 500;
    await context.Response.WriteAsJsonAsync(new
    {
        error = error?.Message ?? "GM 服务发生未知错误",
    });
}));

// 自定义请求头会触发浏览器 CORS 预检，避免别的网站用简单表单偷偷调用本机写接口。
app.Use(async (context, next) =>
{
    if (context.Request.Method is not ("GET" or "HEAD" or "OPTIONS") &&
        context.Request.Headers["X-ReDiv-GM"] != "1")
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "缺少本机 GM 写操作标记" });
        return;
    }

    await next();
});

app.MapGet("/api/health", () => Results.Ok(new
{
    ok = true,
    database = Environment.GetEnvironmentVariable("REDIV_DATABASE") ?? "rediv",
    localOnly = true,
}));

app.MapGet("/api/dashboard", async (SpacetimeRepository repository, CancellationToken cancellationToken) =>
    Results.Ok(await repository.GetDashboardAsync(cancellationToken)));

app.MapGet("/api/logs", async (
    int? lines,
    string? level,
    SpacetimeRepository repository,
    CancellationToken cancellationToken) =>
{
    int safeLines = Math.Clamp(lines ?? 120, 10, 500);
    string? safeLevel = level?.ToLowerInvariant() switch
    {
        "trace" or "debug" or "info" or "warn" or "error" or "panic" => level.ToLowerInvariant(),
        _ => null,
    };
    return Results.Ok(await repository.GetLogsAsync(safeLines, safeLevel, cancellationToken));
});

app.MapPatch("/api/accounts/{accountId:long}/slots", async (
    long accountId,
    UpdateSlotsRequest request,
    SpacetimeRepository repository,
    CancellationToken cancellationToken) =>
{
    if (accountId <= 0 || request.CharacterSlots is < 1 or > 8)
    {
        return Results.BadRequest(new { error = "角色栏位必须在 1 到 8 之间" });
    }

    return Results.Ok(await repository.UpdateCharacterSlotsAsync(
        (ulong)accountId, request.CharacterSlots, cancellationToken));
});

app.MapPatch("/api/characters/{characterId:long}", async (
    long characterId,
    UpdateCharacterRequest request,
    SpacetimeRepository repository,
    CancellationToken cancellationToken) =>
{
    if (characterId <= 0 || request is { Level: null, Exp: null, Star: null })
    {
        return Results.BadRequest(new { error = "没有可修改的角色字段" });
    }

    if (request.Level is < 1 or > 999 || request.Star is < 1 or > 6)
    {
        return Results.BadRequest(new { error = "等级必须为 1–999，星级必须为 1–6" });
    }

    return Results.Ok(await repository.UpdateCharacterAsync(
        (ulong)characterId, request, cancellationToken));
});

app.MapPost("/api/world-time", async (
    SetWorldTimeRequest request,
    SpacetimeRepository repository,
    CancellationToken cancellationToken) =>
{
    if (request.OverrideBandId > 3)
    {
        return Results.BadRequest(new { error = "时段控制只接受 0（自动）或 1/2/3" });
    }

    return Results.Ok(await repository.SetWorldTimeAsync(request.OverrideBandId, cancellationToken));
});

app.Run();

namespace ReDiv.GM.Server
{
    public sealed record UpdateSlotsRequest(uint CharacterSlots);
    public sealed record UpdateCharacterRequest(uint? Level, ulong? Exp, uint? Star);
    public sealed record SetWorldTimeRequest(uint OverrideBandId);
}
