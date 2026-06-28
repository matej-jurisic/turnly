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

        // The viewer id personalises track-mode chores to the logged-in user (their own track's due
        // date / progress surface on the card so it buckets where it matters to them).
        group.MapGet("", async (ChoreService chores, ClaimsPrincipal principal, CancellationToken ct) =>
            Results.Ok(await chores.ListAsync(principal.GetUserId(), ct)));

        group.MapGet("/{id:guid}", async (Guid id, ChoreService chores, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var result = await chores.GetAsync(id, principal.GetUserId(), ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        });

        group.MapPost("", async (CreateChoreRequest req, ChoreService chores, CancellationToken ct) =>
        {
            var result = await chores.CreateAsync(req, ct);
            return result.Succeeded
                ? Results.Created($"/api/chores/{result.Value!.Id}", result.Value)
                : result.Error!.ToProblem();
        }).RequireAuthorization("Admin");

        // Duplicate an existing chore under a new name (admin only, like create).
        group.MapPost("/{id:guid}/copy", async (Guid id, CopyChoreRequest req, ChoreService chores, CancellationToken ct) =>
        {
            var result = await chores.CopyAsync(id, req.NewName, ct);
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

        // Reassign the current occurrence. Admins move it immediately; a member may only reassign a
        // chore they hold, and it becomes a pending request the target must accept (see below).
        group.MapPost("/{id:guid}/reassign", async (Guid id, ReassignChoreRequest req,
            ClaimsPrincipal principal, ChoreService chores, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var result = await chores.ReassignAsync(id, userId, req, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        });

        // The target of a pending member reassignment accepts (the chore moves to them) or declines
        // (it stays with the requester). The service authorizes that the caller is the target.
        group.MapPost("/{id:guid}/reassign/accept", async (Guid id,
            ClaimsPrincipal principal, ChoreService chores, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var result = await chores.RespondToReassignmentAsync(id, userId, accept: true, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        });

        group.MapPost("/{id:guid}/reassign/decline", async (Guid id,
            ClaimsPrincipal principal, ChoreService chores, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var result = await chores.RespondToReassignmentAsync(id, userId, accept: false, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        });

        // Manually reschedule the current occurrence's due date/time (admin only).
        group.MapPost("/{id:guid}/reschedule", async (Guid id, RescheduleChoreRequest req,
            ChoreService chores, CancellationToken ct) =>
        {
            var result = await chores.RescheduleAsync(id, req, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        }).RequireAuthorization("Admin");

        // Freeze / unfreeze a chore (admin only): suspends completions, skips, auto-advance,
        // and notifications. On unfreeze, overdue recurring chores step forward.
        group.MapPost("/{id:guid}/freeze", async (Guid id, ChoreService chores, CancellationToken ct) =>
        {
            var result = await chores.FreezeAsync(id, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        }).RequireAuthorization("Admin");

        group.MapPost("/{id:guid}/unfreeze", async (Guid id, ChoreService chores, CancellationToken ct) =>
        {
            var result = await chores.UnfreezeAsync(id, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        }).RequireAuthorization("Admin");

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

        // Admin-only deletion of a historical activity entry — reverses points without rewinding
        // the chore's current schedule (distinct from undo above).
        completions.MapDelete("/{id:guid}/activity", async (Guid id, ClaimsPrincipal principal,
            ChoreService chores, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var result = await chores.DeleteActivityAsync(id, userId, ct);
            return result.Succeeded ? Results.NoContent() : result.Error!.ToProblem();
        }).RequireAuthorization("Admin");
    }
}
