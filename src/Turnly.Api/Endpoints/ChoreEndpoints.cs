using System.Security.Claims;
using Turnly.Core.Dtos;
using Turnly.Core.Services;

namespace Turnly.Api.Endpoints;

public static class ChoreEndpoints
{
    public static void MapChoreEndpoints(this IEndpointRouteBuilder app)
    {
        // Any authenticated member can view chores and mark them complete; only admins
        // create/edit/delete.
        var group = app.MapGroup("/api/chores").RequireAuthorization();

        group.MapGet("", async (ChoreService chores, CancellationToken ct) =>
            Results.Ok(await chores.ListAsync(ct)));

        group.MapGet("/{id:guid}", async (Guid id, ChoreService chores, CancellationToken ct) =>
        {
            var result = await chores.GetAsync(id, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        });

        group.MapPost("", async (CreateChoreRequest req, ChoreService chores, CancellationToken ct) =>
        {
            var result = await chores.CreateAsync(req, ct);
            return result.Succeeded
                ? Results.Created($"/api/chores/{result.Value!.Id}", result.Value)
                : result.Error!.ToProblem();
        }).RequireAuthorization("Admin");

        group.MapPut("/{id:guid}", async (Guid id, UpdateChoreRequest req, ChoreService chores, CancellationToken ct) =>
        {
            var result = await chores.UpdateAsync(id, req, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        }).RequireAuthorization("Admin");

        group.MapDelete("/{id:guid}", async (Guid id, ChoreService chores, CancellationToken ct) =>
        {
            var result = await chores.DeleteAsync(id, ct);
            return result.Succeeded ? Results.NoContent() : result.Error!.ToProblem();
        }).RequireAuthorization("Admin");

        group.MapPost("/{id:guid}/complete", async (Guid id, CompleteChoreRequest req,
            ClaimsPrincipal principal, ChoreService chores, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var result = await chores.CompleteAsync(id, userId, req, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        });

        // Skip the current occurrence of a recurring chore (admin only — skipping advances a
        // chore past its due date with no points, so it's not a self-service member action).
        group.MapPost("/{id:guid}/skip", async (Guid id, SkipChoreRequest req,
            ClaimsPrincipal principal, ChoreService chores, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var result = await chores.SkipAsync(id, userId, req, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        }).RequireAuthorization("Admin");

        // One-off reassignment of the current occurrence (any member).
        group.MapPost("/{id:guid}/reassign", async (Guid id, ReassignChoreRequest req,
            ClaimsPrincipal principal, ChoreService chores, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var result = await chores.ReassignAsync(id, userId, req, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        });

        // Undo a completion (any member may undo their own; admins may undo any).
        var completions = app.MapGroup("/api/completions").RequireAuthorization();
        completions.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal principal,
            ChoreService chores, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var result = await chores.UndoCompletionAsync(id, userId, ct);
            return result.Succeeded ? Results.NoContent() : result.Error!.ToProblem();
        });
    }
}
