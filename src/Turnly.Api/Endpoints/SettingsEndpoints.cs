using Turnly.Core.Dtos;
using Turnly.Core.Services;

namespace Turnly.Api.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").RequireAuthorization();

        group.MapGet("", async (AppSettingsService settings, CancellationToken ct) =>
            Results.Ok(await settings.GetAsync(ct)));

        group.MapPut("", async (UpdateAppSettingsRequest req, AppSettingsService settings, CancellationToken ct) =>
        {
            var result = await settings.SetTimeZoneAsync(req.TimeZone, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        }).RequireAuthorization("Admin");

        // "Fresh start": wipe all activity/point history/gacha/achievements and zero balances,
        // keeping chores (and their schedules) intact. Irreversible, admin-only.
        group.MapPost("/fresh-start", async (ResetService reset, CancellationToken ct) =>
        {
            var result = await reset.FreshStartAsync(ct);
            return result.Succeeded ? Results.Ok() : result.Error!.ToProblem();
        }).RequireAuthorization("Admin");
    }
}
