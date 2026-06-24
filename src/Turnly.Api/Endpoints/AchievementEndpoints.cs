using System.Security.Claims;
using Turnly.Core.Enums;
using Turnly.Core.Services;

namespace Turnly.Api.Endpoints;

public static class AchievementEndpoints
{
    public static void MapAchievementEndpoints(this IEndpointRouteBuilder app)
    {
        var achievements = app.MapGroup("/api/achievements").RequireAuthorization();

        // The caller's own achievements (full catalog with progress + earned status). Admins may pass
        // ?userId= to view any user's, for the admin management view.
        achievements.MapGet("", async (Guid? userId, ClaimsPrincipal principal,
            AchievementService service, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } callerId)
                return Results.Unauthorized();
            var target = callerId;
            if (userId is { } requested && requested != callerId)
            {
                if (!principal.IsInRole(nameof(UserRole.Admin)))
                    return Results.Forbid();
                target = requested;
            }
            return Results.Ok(await service.ListForUserAsync(target, ct));
        });

        // Admin: revoke an earned achievement from a user.
        achievements.MapDelete("/{userId:guid}/{key}", async (Guid userId, string key,
            AchievementService service, CancellationToken ct) =>
        {
            var result = await service.RevokeAsync(userId, key, ct);
            return result.Succeeded ? Results.NoContent() : result.Error!.ToProblem();
        }).RequireAuthorization("Admin");
    }
}
