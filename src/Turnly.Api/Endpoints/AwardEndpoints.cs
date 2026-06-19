using System.Security.Claims;
using Turnly.Core.Dtos;
using Turnly.Core.Enums;
using Turnly.Core.Services;

namespace Turnly.Api.Endpoints;

public static class AwardEndpoints
{
    public static void MapAwardEndpoints(this IEndpointRouteBuilder app)
    {
        var awards = app.MapGroup("/api/awards").RequireAuthorization();

        awards.MapGet("", async (AwardService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(ct)));

        awards.MapPost("", async (CreateAwardRequest req, AwardService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(req, ct);
            return result.Succeeded
                ? Results.Created($"/api/awards/{result.Value!.Id}", result.Value)
                : result.Error!.ToProblem();
        }).RequireAuthorization("Admin");

        awards.MapPut("/{id:guid}", async (Guid id, UpdateAwardRequest req, AwardService service, CancellationToken ct) =>
        {
            var result = await service.UpdateAsync(id, req, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        }).RequireAuthorization("Admin");

        awards.MapDelete("/{id:guid}", async (Guid id, AwardService service, CancellationToken ct) =>
        {
            var result = await service.DeleteAsync(id, ct);
            return result.Succeeded ? Results.NoContent() : result.Error!.ToProblem();
        }).RequireAuthorization("Admin");

        // Any member can redeem for themselves.
        awards.MapPost("/{id:guid}/redeem", async (Guid id, ClaimsPrincipal principal,
            RedemptionService redemptions, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var result = await redemptions.RedeemAsync(userId, id, ct);
            return result.Succeeded
                ? Results.Created($"/api/redemptions/{result.Value!.Id}", result.Value)
                : result.Error!.ToProblem();
        });

        var redemptionsGroup = app.MapGroup("/api/redemptions").RequireAuthorization();

        // Admins see all redemptions; members see only their own.
        redemptionsGroup.MapGet("", async (ClaimsPrincipal principal,
            RedemptionService redemptions, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var isAdmin = principal.IsInRole(nameof(UserRole.Admin));
            return Results.Ok(await redemptions.ListAsync(userId, isAdmin, ct));
        });

        redemptionsGroup.MapPost("/{id:guid}/fulfill", async (Guid id,
            RedemptionService redemptions, CancellationToken ct) =>
        {
            var result = await redemptions.FulfillAsync(id, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        }).RequireAuthorization("Admin");

        redemptionsGroup.MapPost("/{id:guid}/cancel", async (Guid id,
            RedemptionService redemptions, CancellationToken ct) =>
        {
            var result = await redemptions.CancelAsync(id, ct);
            return result.Succeeded ? Results.NoContent() : result.Error!.ToProblem();
        }).RequireAuthorization("Admin");
    }
}
