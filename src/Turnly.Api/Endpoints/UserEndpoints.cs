using System.Security.Claims;
using Turnly.Core.Dtos;
using Turnly.Core.Services;

namespace Turnly.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").RequireAuthorization("Admin");

        group.MapGet("", async (UserService users, CancellationToken ct) =>
            Results.Ok(await users.ListAsync(ct)));

        group.MapGet("/{id:guid}", async (Guid id, UserService users, CancellationToken ct) =>
        {
            var result = await users.GetAsync(id, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        });

        group.MapPost("", async (CreateUserRequest req, UserService users, CancellationToken ct) =>
        {
            var result = await users.CreateAsync(req, ct);
            return result.Succeeded
                ? Results.Created($"/api/users/{result.Value!.Id}", result.Value)
                : result.Error!.ToProblem();
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest req, UserService users, CancellationToken ct) =>
        {
            var result = await users.UpdateAsync(id, req, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        });

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal principal, UserService users, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } actingUserId)
                return Results.Unauthorized();
            var result = await users.DeleteAsync(id, actingUserId, ct);
            return result.Succeeded ? Results.NoContent() : result.Error!.ToProblem();
        });

        group.MapPost("/{id:guid}/password", async (Guid id, SetPasswordRequest req, UserService users, CancellationToken ct) =>
        {
            var result = await users.SetPasswordAsync(id, req.NewPassword, ct);
            return result.Succeeded ? Results.NoContent() : result.Error!.ToProblem();
        });

        group.MapPost("/{id:guid}/points", async (Guid id, AdjustPointsRequest req, UserService users, CancellationToken ct) =>
        {
            var result = await users.AdjustPointsAsync(id, req, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        });

        // Endpoints accessible to any authenticated member.
        var self = app.MapGroup("/api/users").RequireAuthorization();

        self.MapGet("/leaderboard", async (UserService users, CancellationToken ct) =>
            Results.Ok(await users.GetLeaderboardAsync(ct)));

        self.MapPut("/me", async (UpdateProfileRequest req, ClaimsPrincipal principal,
            UserService users, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var result = await users.UpdateProfileAsync(userId, req, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        });

        self.MapGet("/{id:guid}/points-log", async (Guid id, ClaimsPrincipal principal,
            UserService users, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } actingUserId)
                return Results.Unauthorized();
            if (actingUserId != id && !principal.IsInRole(nameof(Core.Enums.UserRole.Admin)))
                return Results.Forbid();

            var result = await users.GetPointsLogAsync(id, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        });
    }
}
