using System.Security.Claims;
using Turnly.Core.Dtos;
using Turnly.Core.Services;

namespace Turnly.Api.Endpoints;

public static class GachaEndpoints
{
    public static void MapGachaEndpoints(this IEndpointRouteBuilder app)
    {
        // All member-open: a user only ever pulls/crafts/equips for their own account.
        var gacha = app.MapGroup("/api/gacha").RequireAuthorization();

        // Full state: balances, pricing, pity, odds, and the catalog with the caller's ownership.
        gacha.MapGet("", async (ClaimsPrincipal principal, GachaService service, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var result = await service.GetStateAsync(userId, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        });

        // Spend points on a pull (1 or 10). Returns each roll's outcome.
        gacha.MapPost("/pull", async (PullRequest req, ClaimsPrincipal principal,
            GachaService service, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var result = await service.PullAsync(userId, req.Count, DateTimeOffset.UtcNow, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        });

        // Spend dust to craft a specific cosmetic.
        gacha.MapPost("/craft", async (CraftRequest req, ClaimsPrincipal principal,
            GachaService service, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var result = await service.CraftAsync(userId, req.Key, ct);
            return result.Succeeded ? Results.NoContent() : result.Error!.ToProblem();
        });

        // Equip or unequip (null key) a cosmetic in a slot.
        gacha.MapPost("/equip", async (EquipRequest req, ClaimsPrincipal principal,
            GachaService service, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var result = await service.EquipAsync(userId, req.Slot, req.Key, ct);
            return result.Succeeded ? Results.NoContent() : result.Error!.ToProblem();
        });
    }
}
