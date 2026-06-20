using System.Security.Claims;
using Microsoft.Extensions.Options;
using Turnly.Core.Dtos;
using Turnly.Core.Notifications;
using Turnly.Core.Services;

namespace Turnly.Api.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").RequireAuthorization();

        // The VAPID public key the browser needs to create a push subscription.
        group.MapGet("/vapid-key", (IOptions<VapidOptions> vapid) =>
            Results.Ok(new { publicKey = vapid.Value.PublicKey }));

        group.MapPost("/subscribe", async (
            PushSubscribeRequest req, ClaimsPrincipal principal, HttpContext http, NotificationService notifications, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var userAgent = http.Request.Headers.UserAgent.ToString();
            var result = await notifications.SubscribeAsync(userId, req, userAgent, ct);
            return result.Succeeded ? Results.NoContent() : result.Error!.ToProblem();
        });

        // The user's registered push devices.
        group.MapGet("/devices", async (ClaimsPrincipal principal, NotificationService notifications, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            return Results.Ok(await notifications.ListDevicesAsync(userId, ct));
        });

        group.MapDelete("/devices/{id:guid}", async (
            Guid id, ClaimsPrincipal principal, NotificationService notifications, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var result = await notifications.RemoveDeviceAsync(userId, id, ct);
            return result.Succeeded ? Results.NoContent() : result.Error!.ToProblem();
        });

        group.MapPost("/unsubscribe", async (
            UnsubscribeRequest req, ClaimsPrincipal principal, NotificationService notifications, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var result = await notifications.UnsubscribeAsync(userId, req.Endpoint, ct);
            return result.Succeeded ? Results.NoContent() : result.Error!.ToProblem();
        });

        // The user's in-app notification inbox.
        group.MapGet("/inbox", async (ClaimsPrincipal principal, NotificationService notifications, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            return Results.Ok(await notifications.ListInboxAsync(userId, ct));
        });

        // Mark all of the caller's inbox notifications as read.
        group.MapPost("/inbox/read", async (ClaimsPrincipal principal, NotificationService notifications, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var marked = await notifications.MarkInboxReadAsync(userId, DateTimeOffset.UtcNow, ct);
            return Results.Ok(new { marked });
        });

        // Delete one of the caller's inbox notifications.
        group.MapDelete("/inbox/{id:guid}", async (
            Guid id, ClaimsPrincipal principal, NotificationService notifications, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var result = await notifications.DeleteInboxAsync(userId, id, ct);
            return result.Succeeded ? Results.NoContent() : result.Error!.ToProblem();
        });

        // Clear all of the caller's inbox notifications.
        group.MapDelete("/inbox", async (ClaimsPrincipal principal, NotificationService notifications, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var cleared = await notifications.ClearInboxAsync(userId, ct);
            return Results.Ok(new { cleared });
        });

        // Dev/debug: send an immediate test push to the calling admin's own devices.
        group.MapPost("/test", async (
            ClaimsPrincipal principal, NotificationService notifications, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId)
                return Results.Unauthorized();
            var result = await notifications.SendTestAsync(userId, ct);
            return result.Succeeded ? Results.Ok(new { sent = result.Value }) : result.Error!.ToProblem();
        }).RequireAuthorization("Admin");
    }
}

public record UnsubscribeRequest(string Endpoint);
