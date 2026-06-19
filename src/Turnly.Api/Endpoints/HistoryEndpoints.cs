using Turnly.Core.Services;

namespace Turnly.Api.Endpoints;

public static class HistoryEndpoints
{
    public static void MapHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        group.MapGet("/history", async (
            string? tag, Guid? userId, Guid? choreId,
            ChoreService chores, CancellationToken ct) =>
            Results.Ok(await chores.GetHistoryAsync(tag, userId, choreId, ct)));

        group.MapGet("/stats", async (UserService users, CancellationToken ct) =>
            Results.Ok(await users.GetStatsAsync(ct)));
    }
}
