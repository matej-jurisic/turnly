using System.Security.Claims;
using Turnly.Core.Services;

namespace Turnly.Api.Endpoints;

public static class AchievementEndpoints
{
    public static void MapAchievementEndpoints(this IEndpointRouteBuilder app)
    {
        var achievements = app.MapGroup("/api/achievements").RequireAuthorization();

        // The caller's own achievements (full catalog with progress + earned status).
        achievements.MapGet("", async (ClaimsPrincipal principal, AchievementService service, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            return Results.Ok(await service.ListForUserAsync(userId, ct));
        });
    }
}
