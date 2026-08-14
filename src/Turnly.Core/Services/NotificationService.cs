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
    private readonly IFcmSender _fcm;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(TurnlyDbContext db, IPushSender push, IFcmSender fcm, ILogger<NotificationService> logger)
    {
        _db = db;
        _push = push;
        _fcm = fcm;
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

    /// <summary>Registers (or refreshes) an FCM registration token for the native app. Keyed by
    /// token, so re-registering the same install updates rather than duplicates.</summary>
    public async Task<Result> SubscribeFcmAsync(Guid userId, string token, string? userAgent = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Result.Fail(Error.Validation("An FCM subscription requires a token."));

        var label = DeviceLabelFromUserAgent(userAgent);
        var existing = await _db.FcmDevices.FirstOrDefaultAsync(d => d.Token == token, ct);
        if (existing is null)
            _db.FcmDevices.Add(new FcmDevice { UserId = userId, Token = token, DeviceLabel = label });
        else
        {
            // A device can change hands (different user signs in); rebind it to the current user.
            existing.UserId = userId;
            if (label is not null)
                existing.DeviceLabel = label;
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Removes an FCM token (e.g. on sign-out), so the device stops receiving pushes.</summary>
    public async Task<Result> UnsubscribeFcmAsync(string token, CancellationToken ct = default)
    {
        var device = await _db.FcmDevices.FirstOrDefaultAsync(d => d.Token == token, ct);
        if (device is not null)
        {
            _db.FcmDevices.Remove(device);
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
        var hasSub = await _db.PushSubscriptions.AnyAsync(s => s.UserId == userId, ct);
        if (!hasSub)
            return Result.Fail<int>(Error.Validation(
                "No push subscription on this account. Enable notifications on this device first."));

        var sent = await NotifyUserAsync(userId, "Turnly", "Test notification: push is working. 🎉",
            choreId: null, NotificationCategory.System, ct);
        await _db.SaveChangesAsync(ct);
        return Result.Success(sent);
    }

    /// <summary>Writes an in-app inbox row for the user and pushes the same message to all of their
    /// Web Push + FCM devices (best-effort; dead devices are pruned, <see cref="PushSendResult.Gone"/>).
    /// Does <b>not</b> call <c>SaveChanges</c> — the caller commits, so this can join a larger
    /// transaction. Used for ad-hoc, non-scheduled notifications (test push, chore offers).
    /// Subject to the recipient's <paramref name="category"/> preference, chore mute and quiet hours.
    /// Returns the number of devices the push services accepted.</summary>
    public Task<int> NotifyUserAsync(Guid userId, string title, string body, Guid? choreId,
        NotificationCategory category, CancellationToken ct = default) =>
        NotifyUsersAsync([userId], title, body, choreId, category, ct);

    /// <summary>Fans one message out to several recipients, loading the routing context (preferences,
    /// mutes, quiet hours) once for the whole set rather than once per user. Each recipient is routed
    /// independently, so one person muting the chore doesn't affect anyone else.</summary>
    public async Task<int> NotifyUsersAsync(IReadOnlyCollection<Guid> userIds, string title, string body,
        Guid? choreId, NotificationCategory category, CancellationToken ct = default)
    {
        if (userIds.Count == 0)
            return 0;

        var now = DateTimeOffset.UtcNow;
        var routing = await LoadRoutingAsync(userIds, now, ct);

        var url = choreId is { } cid ? $"/chores?chore={cid}" : "/chores";
        var payload = JsonSerializer.Serialize(new { title, body, url, choreId });

        var pushIds = new List<Guid>();
        foreach (var userId in userIds.Distinct())
        {
            var channel = routing.Resolve(userId, category, choreId);
            if (channel == DeliveryChannel.Off)
                continue;

            _db.UserNotifications.Add(new UserNotification { UserId = userId, Title = title, Body = body, ChoreId = choreId });
            if (channel == DeliveryChannel.PushAndInbox)
                pushIds.Add(userId);
        }

        if (pushIds.Count == 0)
            return 0;

        var sent = 0;
        var subs = await _db.PushSubscriptions.Where(s => pushIds.Contains(s.UserId)).ToListAsync(ct);
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

        // Also fire to the recipients' native (FCM) devices.
        var fcmDevices = await _db.FcmDevices.Where(d => pushIds.Contains(d.UserId)).ToListAsync(ct);
        foreach (var device in fcmDevices)
        {
            PushSendResult result;
            try
            {
                result = await _fcm.SendAsync(device.Token, title, body, url, choreId, ct);
            }
            catch
            {
                continue;
            }

            if (result == PushSendResult.Ok)
                sent++;
            else if (result == PushSendResult.Gone)
                _db.FcmDevices.Remove(device);
        }

        return sent;
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

    /// <summary>The caller's full preference matrix: every category, with the user's stored channel or
    /// the default where they have no row. Returned complete (rather than sparse) so the UI can render
    /// the matrix without knowing the defaults.</summary>
    public async Task<List<NotificationPreferenceDto>> ListPreferencesAsync(Guid userId, CancellationToken ct = default)
    {
        var stored = (await _db.UserNotificationPreferences
                .Where(p => p.UserId == userId)
                .ToListAsync(ct))
            .ToDictionary(p => p.Category, p => p.Channel);

        return Enum.GetValues<NotificationCategory>()
            .Select(category => new NotificationPreferenceDto(
                category,
                stored.TryGetValue(category, out var channel) ? channel : DeliveryChannel.PushAndInbox,
                NotificationRouter.IsConfigurable(category)))
            .ToList();
    }

    /// <summary>Sets one category's channel for the caller. Writing the default deletes the row, so
    /// storage stays sparse and "never touched it" and "set it back to default" converge.</summary>
    public async Task<Result<List<NotificationPreferenceDto>>> UpdatePreferenceAsync(
        Guid userId, UpdateNotificationPreferenceRequest req, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(req.Category))
            return Result.Fail<List<NotificationPreferenceDto>>(Error.Validation("Unknown notification category."));
        if (!Enum.IsDefined(req.Channel))
            return Result.Fail<List<NotificationPreferenceDto>>(Error.Validation("Unknown delivery channel."));
        if (!NotificationRouter.IsConfigurable(req.Category))
            return Result.Fail<List<NotificationPreferenceDto>>(
                Error.Validation($"{req.Category} notifications cannot be turned off."));

        var existing = await _db.UserNotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Category == req.Category, ct);

        if (req.Channel == DeliveryChannel.PushAndInbox)
        {
            if (existing is not null)
                _db.UserNotificationPreferences.Remove(existing);
        }
        else if (existing is null)
        {
            _db.UserNotificationPreferences.Add(new UserNotificationPreference
            {
                UserId = userId,
                Category = req.Category,
                Channel = req.Channel
            });
        }
        else
        {
            existing.Channel = req.Channel;
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success(await ListPreferencesAsync(userId, ct));
    }

    /// <summary>Mutes a chore for the calling user. Re-muting an already-muted chore updates the
    /// expiry rather than failing, so the UI can offer "mute for a day" over an indefinite mute.</summary>
    public async Task<Result> MuteChoreAsync(Guid userId, Guid choreId, DateTimeOffset? until, CancellationToken ct = default)
    {
        if (until is { } u && u <= DateTimeOffset.UtcNow)
            return Result.Fail(Error.Validation("A mute must end in the future."));

        if (!await _db.Chores.AnyAsync(c => c.Id == choreId, ct))
            return Result.Fail(Error.NotFound("Chore not found."));

        var existing = await _db.ChoreMutes.FirstOrDefaultAsync(m => m.UserId == userId && m.ChoreId == choreId, ct);
        if (existing is null)
            _db.ChoreMutes.Add(new ChoreMute { UserId = userId, ChoreId = choreId, Until = until });
        else
            existing.Until = until;

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Unmutes a chore for the calling user. Idempotent — unmuting something that isn't muted
    /// succeeds, so the client never has to check first.</summary>
    public async Task<Result> UnmuteChoreAsync(Guid userId, Guid choreId, CancellationToken ct = default)
    {
        var existing = await _db.ChoreMutes.FirstOrDefaultAsync(m => m.UserId == userId && m.ChoreId == choreId, ct);
        if (existing is not null)
        {
            _db.ChoreMutes.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }
        return Result.Success();
    }

    /// <summary>Everything <see cref="NotificationRouter.Resolve"/> needs for a set of recipients,
    /// loaded once per scan (or once per fan-out) instead of once per notification.</summary>
    private sealed class RoutingContext
    {
        public required TimeOnly LocalNow { get; init; }
        public required Dictionary<Guid, (TimeOnly? Start, TimeOnly? End)> Quiet { get; init; }
        public required Dictionary<(Guid UserId, NotificationCategory Category), DeliveryChannel> Preferences { get; init; }
        public required HashSet<(Guid UserId, Guid ChoreId)> Mutes { get; init; }

        /// <summary>The effective channel for one recipient. A missing preference row means the
        /// category is at its default, which is how sparse storage stays invisible to callers.</summary>
        public DeliveryChannel Resolve(Guid userId, NotificationCategory category, Guid? choreId)
        {
            var preference = Preferences.TryGetValue((userId, category), out var p) ? p : DeliveryChannel.PushAndInbox;
            var muted = choreId is { } cid && Mutes.Contains((userId, cid));
            var quiet = Quiet.TryGetValue(userId, out var q) && QuietHours.Contains(q.Start, q.End, LocalNow);
            return NotificationRouter.Resolve(category, preference, muted, quiet);
        }
    }

    /// <summary>Loads preferences, active chore mutes and quiet-hours windows for the given recipients.
    /// Quiet hours is a local wall-clock window, so "now" is converted to the configured family
    /// timezone (falling back to the server's local zone when unset) rather than compared in UTC.</summary>
    private async Task<RoutingContext> LoadRoutingAsync(
        IReadOnlyCollection<Guid> userIds, DateTimeOffset now, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();

        var tzId = await _db.AppSettings
            .Where(s => s.Key == AppSettingsService.TimeZoneKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        var localNow = TimeOnly.FromTimeSpan(TimeZoneInfo.ConvertTime(now, TimeZoneResolver.Resolve(tzId)).TimeOfDay);

        var quiet = (await _db.Users
                .Where(u => ids.Contains(u.Id) && u.QuietHoursStart != null)
                .Select(u => new { u.Id, u.QuietHoursStart, u.QuietHoursEnd })
                .ToListAsync(ct))
            .ToDictionary(u => u.Id, u => (u.QuietHoursStart, u.QuietHoursEnd));

        var preferences = (await _db.UserNotificationPreferences
                .Where(p => ids.Contains(p.UserId))
                .ToListAsync(ct))
            .ToDictionary(p => (p.UserId, p.Category), p => p.Channel);

        // An expired mute is simply not loaded, so it stops applying without anything sweeping for it.
        // SQLite can't compare a DateTimeOffset in SQL, so the expiry filter runs client-side.
        var mutes = (await _db.ChoreMutes
                .Where(m => ids.Contains(m.UserId))
                .Select(m => new { m.UserId, m.ChoreId, m.Until })
                .ToListAsync(ct))
            .Where(m => m.Until == null || m.Until > now)
            .Select(m => (m.UserId, m.ChoreId))
            .ToHashSet();

        return new RoutingContext
        {
            LocalNow = localNow,
            Quiet = quiet,
            Preferences = preferences,
            Mutes = mutes
        };
    }

    /// <summary>Scans every scheduled chore and fires any notification entry whose moment has
    /// arrived (and hasn't already fired for this occurrence). Returns the number of entries fired.</summary>
    public async Task<int> ProcessDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var chores = await _db.Chores
            .Include(c => c.Notifications)
            .Include(c => c.Assignees)
            .Include(c => c.AssigneeTracks)
            .Where(c => c.DueAt != null && c.Notifications.Any() && !c.IsFrozen)
            .AsSplitQuery()
            .ToListAsync(ct);

        if (chores.Count == 0)
            return 0;

        var choreIds = chores.Select(c => c.Id).ToList();

        // Dedup set keyed by (entry, occurrence, track owner). UtcTicks avoids any DateTimeOffset
        // round-trip drift; UserId is null for rotating chores (one row per occurrence) and the track
        // owner for Independent chores (one row per assignee per occurrence).
        var delivered = (await _db.NotificationDeliveries
                .Where(d => choreIds.Contains(d.ChoreId))
                .ToListAsync(ct))
            .Select(d => (d.ChoreNotificationId, d.OccurrenceDueAt.UtcTicks, d.UserId))
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

        // Native (FCM) devices for the same recipients - fired alongside Web Push.
        var fcmByUser = (await _db.FcmDevices
                .Where(d => recipientIds.Contains(d.UserId))
                .ToListAsync(ct))
            .GroupBy(d => d.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Per-recipient preferences, chore mutes and quiet-hours windows, loaded once for the scan.
        var routing = await LoadRoutingAsync(recipientIds, now, ct);

        // Frozen users are skipped entirely — their occurrences are paused, not merely silenced.
        var frozenUserIds = (await _db.Users
                .Where(u => recipientIds.Contains(u.Id) && u.IsFrozen)
                .Select(u => u.Id)
                .ToListAsync(ct))
            .ToHashSet();

        var fired = 0;
        foreach (var chore in chores)
        {
            foreach (var entry in chore.Notifications)
            {
                if (chore.AssignmentStrategy == AssignmentStrategy.Independent)
                {
                    // Fire per assignee track, off that track's own due date, to the track owner.
                    // Skip frozen users — their tracks are paused.
                    foreach (var track in chore.AssigneeTracks)
                    {
                        if (track.DueAt is not { } trackDue) continue;
                        if (frozenUserIds.Contains(track.UserId)) continue;
                        if (await TryFireAsync(chore, entry, trackDue, track.UserId, [track.UserId],
                                delivered, subsByUser, fcmByUser, routing, now, ct))
                            fired++;
                    }
                }
                else
                {
                    var dueAt = chore.DueAt!.Value;
                    var recipients = (entry.Recipients == NotificationRecipients.AllAssignees
                            ? chore.Assignees.Select(a => a.Id).Where(id => !frozenUserIds.Contains(id))
                            : chore.CurrentAssigneeId is { } cur ? [cur] : Enumerable.Empty<Guid>())
                        .Distinct()
                        .ToList();
                    if (await TryFireAsync(chore, entry, dueAt, dedupUserId: null, recipients,
                            delivered, subsByUser, fcmByUser, routing, now, ct))
                        fired++;
                }
            }
        }

        if (fired > 0)
            await _db.SaveChangesAsync(ct);
        return fired;
    }

    /// <summary>Fires a single entry for one occurrence if its moment has arrived and it hasn't already
    /// fired (per the dedup key). <paramref name="dedupUserId"/> is the track owner in Independent mode
    /// (null otherwise) and is also the assignee whose name drives the message.</summary>
    private async Task<bool> TryFireAsync(
        Chore chore, ChoreNotification entry, DateTimeOffset occurrenceDueAt, Guid? dedupUserId,
        IReadOnlyList<Guid> recipientIds, HashSet<(Guid, long, Guid?)> delivered,
        Dictionary<Guid, List<PushSubscription>> subsByUser,
        Dictionary<Guid, List<FcmDevice>> fcmByUser,
        RoutingContext routing, DateTimeOffset now, CancellationToken ct)
    {
        var fireAt = NotificationPlanner.FireTime(entry, occurrenceDueAt);
        if (fireAt > now || fireAt < now - StaleWindow)
            return false;

        var key = (entry.Id, occurrenceDueAt.UtcTicks, dedupUserId);
        if (!delivered.Add(key))
            return false;

        // In track mode the message names the track owner; otherwise the chore's current assignee.
        await SendEntryAsync(chore, entry, occurrenceDueAt, recipientIds,
            dedupUserId ?? chore.CurrentAssigneeId, subsByUser, fcmByUser, routing, now, ct);

        _db.NotificationDeliveries.Add(new NotificationDelivery
        {
            ChoreId = chore.Id,
            ChoreNotificationId = entry.Id,
            OccurrenceDueAt = occurrenceDueAt,
            UserId = dedupUserId,
            SentAt = now
        });
        return true;
    }

    private async Task SendEntryAsync(
        Chore chore, ChoreNotification entry, DateTimeOffset occurrenceDueAt, IReadOnlyList<Guid> recipientIds,
        Guid? messageAssigneeId, Dictionary<Guid, List<PushSubscription>> subsByUser,
        Dictionary<Guid, List<FcmDevice>> fcmByUser,
        RoutingContext routing, DateTimeOffset now, CancellationToken ct)
    {
        var (title, body) = BuildMessage(chore, entry, occurrenceDueAt, messageAssigneeId, now);
        var url = $"/chores?chore={chore.Id}";
        var payload = JsonSerializer.Serialize(new { title, body, url, choreId = chore.Id });
        var category = NotificationRouter.CategoryOf(entry.Type);

        var attempted = 0;
        var wanted = 0;
        foreach (var userId in recipientIds)
        {
            // Each recipient is routed independently: their category preference, whether they muted
            // this chore, and their quiet hours. Applies to both Web Push and native FCM.
            var channel = routing.Resolve(userId, category, chore.Id);
            if (channel == DeliveryChannel.Off)
                continue;

            // Record the in-app inbox item even if the user has no reachable push device.
            _db.UserNotifications.Add(new UserNotification
            {
                UserId = userId,
                Title = title,
                Body = body,
                ChoreId = chore.Id
            });

            if (channel != DeliveryChannel.PushAndInbox)
                continue;

            wanted++;

            // Web Push devices.
            if (subsByUser.TryGetValue(userId, out var subs))
            {
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

            // Native (FCM) devices.
            if (fcmByUser.TryGetValue(userId, out var devices))
            {
                foreach (var device in devices)
                {
                    attempted++;
                    PushSendResult result;
                    try
                    {
                        result = await _fcm.SendAsync(device.Token, title, body, url, chore.Id, ct);
                    }
                    catch
                    {
                        continue;
                    }

                    if (result == PushSendResult.Gone)
                        _db.FcmDevices.Remove(device);
                }
            }
        }

        // Only a recipient who *wanted* a push and had no device is worth warning about; someone who
        // silenced the category or muted the chore got exactly what they asked for.
        if (attempted == 0 && wanted > 0)
            _logger.LogWarning(
                "Chore '{Chore}' notification fired but no recipient ({Recipients}) has a push subscription — nothing was sent.",
                chore.Name, entry.Recipients);
    }

    private static (string Title, string Body) BuildMessage(Chore chore, ChoreNotification entry,
        DateTimeOffset occurrenceDueAt, Guid? assigneeId, DateTimeOffset now)
    {
        var title = string.IsNullOrWhiteSpace(chore.Emoji) ? chore.Name : $"{chore.Emoji} {chore.Name}";

        // The named assignee (current assignee, or the track owner in Independent mode) is one of the
        // loaded Assignees, so resolve the name without an extra include.
        var assignee = chore.Assignees.FirstOrDefault(a => a.Id == assigneeId)?.DisplayName;
        var prefix = assignee is null ? "" : $"{assignee} - ";
        var points = chore.Points > 0 ? $" ({chore.Points} pts)" : "";
        var dueAt = occurrenceDueAt;

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
