using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Turnly.Core.Common;
using Turnly.Core.Data;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;
using Turnly.Core.Enums;
using Turnly.Core.Notifications;

namespace Turnly.Core.Services;

/// <summary>Manages Web Push subscriptions and fires due chore notifications. The scan
/// (<see cref="ProcessDueAsync"/>) is driven by a background scheduler; "stop on completion" falls
/// out of keying deliveries by the occurrence's <c>DueAt</c> — completing a chore advances it.</summary>
public class NotificationService
{
    /// <summary>Don't fire a notification whose moment passed more than this long ago (avoids a
    /// blast of stale notifications after the server was down).</summary>
    private static readonly TimeSpan StaleWindow = TimeSpan.FromDays(1);

    private readonly TurnlyDbContext _db;
    private readonly IPushSender _push;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(TurnlyDbContext db, IPushSender push, ILogger<NotificationService> logger)
    {
        _db = db;
        _push = push;
        _logger = logger;
    }

    /// <summary>Registers (or refreshes) a browser/device push subscription for the user. Keyed by
    /// endpoint, so re-subscribing the same device updates rather than duplicates. The optional
    /// <paramref name="userAgent"/> is turned into a friendly device label.</summary>
    public async Task<Result> SubscribeAsync(Guid userId, PushSubscribeRequest req, string? userAgent = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Endpoint) || string.IsNullOrWhiteSpace(req.P256dh) || string.IsNullOrWhiteSpace(req.Auth))
            return Result.Fail(Error.Validation("A push subscription requires endpoint, p256dh and auth."));

        var label = DeviceLabelFromUserAgent(userAgent);
        var existing = await _db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == req.Endpoint, ct);
        if (existing is null)
        {
            _db.PushSubscriptions.Add(new PushSubscription
            {
                UserId = userId,
                Endpoint = req.Endpoint,
                P256dh = req.P256dh,
                Auth = req.Auth,
                DeviceLabel = label
            });
        }
        else
        {
            existing.UserId = userId;
            existing.P256dh = req.P256dh;
            existing.Auth = req.Auth;
            if (label is not null)
                existing.DeviceLabel = label;
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UnsubscribeAsync(Guid userId, string endpoint, CancellationToken ct = default)
    {
        var sub = await _db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);
        if (sub is not null)
        {
            _db.PushSubscriptions.Remove(sub);
            await _db.SaveChangesAsync(ct);
        }
        return Result.Success();
    }

    /// <summary>The user's registered push devices (newest first).</summary>
    public async Task<List<PushDeviceDto>> ListDevicesAsync(Guid userId, CancellationToken ct = default)
    {
        var subs = await _db.PushSubscriptions.Where(s => s.UserId == userId).ToListAsync(ct);
        return subs
            .OrderByDescending(s => s.CreatedAt)
            .Select(PushDeviceDto.FromEntity)
            .ToList();
    }

    /// <summary>Removes one of the user's own push devices by id.</summary>
    public async Task<Result> RemoveDeviceAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var sub = await _db.PushSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sub is null)
            return Result.Fail(Error.NotFound("Device not found."));
        if (sub.UserId != userId)
            return Result.Fail(Error.Forbidden("You can only remove your own devices."));

        _db.PushSubscriptions.Remove(sub);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Derives a short "Browser · OS" label from a User-Agent string (best-effort).</summary>
    private static string? DeviceLabelFromUserAgent(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua))
            return null;

        var browser =
            ua.Contains("Edg", StringComparison.OrdinalIgnoreCase) ? "Edge" :
            ua.Contains("OPR", StringComparison.OrdinalIgnoreCase) || ua.Contains("Opera", StringComparison.OrdinalIgnoreCase) ? "Opera" :
            ua.Contains("Firefox", StringComparison.OrdinalIgnoreCase) ? "Firefox" :
            ua.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ? "Chrome" :
            ua.Contains("Safari", StringComparison.OrdinalIgnoreCase) ? "Safari" :
            "Browser";

        var os =
            ua.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows" :
            ua.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android" :
            ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || ua.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iOS" :
            ua.Contains("Mac OS", StringComparison.OrdinalIgnoreCase) || ua.Contains("Macintosh", StringComparison.OrdinalIgnoreCase) ? "macOS" :
            ua.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux" :
            null;

        return os is null ? browser : $"{browser} · {os}";
    }

    /// <summary>Sends an immediate test notification to every push subscription on the user's
    /// account (a dev/debug affordance, bypassing the schedule). Returns the number of devices the
    /// push service accepted.</summary>
    public async Task<Result<int>> SendTestAsync(Guid userId, CancellationToken ct = default)
    {
        var subs = await _db.PushSubscriptions.Where(s => s.UserId == userId).ToListAsync(ct);
        if (subs.Count == 0)
            return Result.Fail<int>(Error.Validation(
                "No push subscription on this account — enable notifications on this device first."));

        const string title = "Turnly";
        const string body = "Test notification — push is working. 🎉";
        var payload = JsonSerializer.Serialize(new { title, body, url = "/chores" });

        _db.UserNotifications.Add(new UserNotification { UserId = userId, Title = title, Body = body });

        var sent = 0;
        foreach (var sub in subs)
        {
            PushSendResult result;
            try
            {
                result = await _push.SendAsync(sub, payload, ct);
            }
            catch
            {
                continue;
            }

            if (result == PushSendResult.Ok)
                sent++;
            else if (result == PushSendResult.Gone)
                _db.PushSubscriptions.Remove(sub);
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success(sent);
    }

    /// <summary>The user's in-app notification inbox (newest first, most recent 100).</summary>
    public async Task<List<NotificationInboxDto>> ListInboxAsync(Guid userId, CancellationToken ct = default)
    {
        var items = await _db.UserNotifications
            .Where(n => n.UserId == userId)
            .ToListAsync(ct);
        return items
            .OrderByDescending(n => n.CreatedAt)
            .Take(100)
            .Select(NotificationInboxDto.FromEntity)
            .ToList();
    }

    /// <summary>Marks all of the user's unread inbox notifications as read. Returns the count marked.</summary>
    public async Task<int> MarkInboxReadAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        var unread = await _db.UserNotifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ToListAsync(ct);
        foreach (var n in unread)
            n.ReadAt = now;
        if (unread.Count > 0)
            await _db.SaveChangesAsync(ct);
        return unread.Count;
    }

    /// <summary>Deletes one of the caller's inbox notifications.</summary>
    public async Task<Result> DeleteInboxAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
    {
        var item = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, ct);
        if (item is null)
            return Result.Fail(new Error(ErrorType.NotFound, "Notification not found."));
        _db.UserNotifications.Remove(item);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Deletes all of the caller's inbox notifications. Returns the count removed.</summary>
    public async Task<int> ClearInboxAsync(Guid userId, CancellationToken ct = default)
    {
        var items = await _db.UserNotifications
            .Where(n => n.UserId == userId)
            .ToListAsync(ct);
        if (items.Count > 0)
        {
            _db.UserNotifications.RemoveRange(items);
            await _db.SaveChangesAsync(ct);
        }
        return items.Count;
    }

    /// <summary>Scans every scheduled chore and fires any notification entry whose moment has
    /// arrived (and hasn't already fired for this occurrence). Returns the number of entries fired.</summary>
    public async Task<int> ProcessDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var chores = await _db.Chores
            .Include(c => c.Notifications)
            .Include(c => c.Assignees)
            .Where(c => c.DueAt != null && c.Notifications.Any())
            .AsSplitQuery()
            .ToListAsync(ct);

        if (chores.Count == 0)
            return 0;

        var choreIds = chores.Select(c => c.Id).ToList();

        // Dedup set keyed by (entry, occurrence). UtcTicks avoids any DateTimeOffset round-trip drift.
        var delivered = (await _db.NotificationDeliveries
                .Where(d => choreIds.Contains(d.ChoreId))
                .ToListAsync(ct))
            .Select(d => (d.ChoreNotificationId, d.OccurrenceDueAt.UtcTicks))
            .ToHashSet();

        // Preload subscriptions for everyone who could receive one of these chores.
        var recipientIds = chores
            .SelectMany(c => c.Assignees.Select(a => a.Id).Append(c.CurrentAssigneeId ?? Guid.Empty))
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        var subsByUser = (await _db.PushSubscriptions
                .Where(s => recipientIds.Contains(s.UserId))
                .ToListAsync(ct))
            .GroupBy(s => s.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var fired = 0;
        foreach (var chore in chores)
        {
            var dueAt = chore.DueAt!.Value;
            foreach (var entry in chore.Notifications)
            {
                var fireAt = NotificationPlanner.FireTime(entry, dueAt);
                if (fireAt > now || fireAt < now - StaleWindow)
                    continue;

                var key = (entry.Id, dueAt.UtcTicks);
                if (!delivered.Add(key))
                    continue;

                await SendEntryAsync(chore, entry, subsByUser, now, ct);

                _db.NotificationDeliveries.Add(new NotificationDelivery
                {
                    ChoreId = chore.Id,
                    ChoreNotificationId = entry.Id,
                    OccurrenceDueAt = dueAt,
                    SentAt = now
                });
                fired++;
            }
        }

        if (fired > 0)
            await _db.SaveChangesAsync(ct);
        return fired;
    }

    private async Task SendEntryAsync(
        Chore chore, ChoreNotification entry, Dictionary<Guid, List<PushSubscription>> subsByUser,
        DateTimeOffset now, CancellationToken ct)
    {
        var recipientIds = (entry.Recipients == NotificationRecipients.AllAssignees
                ? chore.Assignees.Select(a => a.Id)
                : chore.CurrentAssigneeId is { } cur ? [cur] : Enumerable.Empty<Guid>())
            .Distinct()
            .ToList();

        var (title, body) = BuildMessage(chore, entry, now);
        var payload = JsonSerializer.Serialize(new
        {
            title,
            body,
            url = $"/chores?chore={chore.Id}",
            choreId = chore.Id
        });

        var attempted = 0;
        foreach (var userId in recipientIds)
        {
            // Record the in-app inbox item even if the user has no reachable push device.
            _db.UserNotifications.Add(new UserNotification
            {
                UserId = userId,
                Title = title,
                Body = body,
                ChoreId = chore.Id
            });

            if (!subsByUser.TryGetValue(userId, out var subs))
                continue;

            foreach (var sub in subs)
            {
                attempted++;
                PushSendResult result;
                try
                {
                    result = await _push.SendAsync(sub, payload, ct);
                }
                catch
                {
                    // Never let a single bad send abort the scan.
                    continue;
                }

                if (result == PushSendResult.Gone)
                    _db.PushSubscriptions.Remove(sub);
            }
        }

        if (attempted == 0)
            _logger.LogWarning(
                "Chore '{Chore}' notification fired but no recipient ({Recipients}) has a push subscription — nothing was sent.",
                chore.Name, entry.Recipients);
    }

    private static (string Title, string Body) BuildMessage(Chore chore, ChoreNotification entry, DateTimeOffset now)
    {
        var title = string.IsNullOrWhiteSpace(chore.Emoji) ? chore.Name : $"{chore.Emoji} {chore.Name}";

        // CurrentAssignee is one of the loaded Assignees, so resolve the name without an extra include.
        var assignee = chore.Assignees.FirstOrDefault(a => a.Id == chore.CurrentAssigneeId)?.DisplayName;
        var prefix = assignee is null ? "" : $"{assignee} - ";
        var points = chore.Points > 0 ? $" ({chore.Points} pts)" : "";
        var dueAt = chore.DueAt ?? now;

        var body = entry.Type switch
        {
            NotificationType.Due => $"{prefix}due now{points}",
            NotificationType.FollowUp => $"{prefix}overdue by {Humanize(now - dueAt)}{points}",
            _ => $"{prefix}due in {Humanize(dueAt - now)}{points}"
        };
        return (title, body);
    }

    /// <summary>Coarse human-friendly duration: minutes under an hour, then hours, then days.</summary>
    private static string Humanize(TimeSpan span)
    {
        var total = span < TimeSpan.Zero ? TimeSpan.Zero : span;
        if (total.TotalMinutes < 1)
            return "less than a minute";
        if (total.TotalMinutes < 60)
            return Plural((int)Math.Round(total.TotalMinutes), "minute");
        if (total.TotalHours < 24)
            return Plural((int)Math.Round(total.TotalHours), "hour");
        return Plural((int)Math.Round(total.TotalDays), "day");
    }

    private static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")}";
}
