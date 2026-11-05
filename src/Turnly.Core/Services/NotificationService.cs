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
        var fcm = await _db.FcmDevices.Where(d => d.UserId == userId).ToListAsync(ct);
        // Sorted client-side: SQLite can't order a DateTimeOffset.
        return subs.Select(PushDeviceDto.FromEntity)
            .Concat(fcm.Select(PushDeviceDto.FromEntity))
            .OrderByDescending(d => d.CreatedAt)
            .ToList();
    }

    /// <summary>Removes one of the user's own push devices (browser or Android app) by id.</summary>
    public async Task<Result> RemoveDeviceAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var sub = await _db.PushSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);
        var fcm = sub is null ? await _db.FcmDevices.FirstOrDefaultAsync(d => d.Id == id, ct) : null;
        var ownerId = sub?.UserId ?? fcm?.UserId;
        if (ownerId is null)
            return Result.Fail(Error.NotFound("Device not found."));
        if (ownerId != userId)
            return Result.Fail(Error.Forbidden("You can only remove your own devices."));

        if (sub is not null)
            _db.PushSubscriptions.Remove(sub);
        else
            _db.FcmDevices.Remove(fcm!);
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
        if (!await HasPushDeviceAsync(userId, ct))
            return Result.Fail<int>(Error.Validation(
                "No push subscription on this account. Enable notifications on this device first."));

        var sent = await NotifyUserAsync(userId, "Turnly", text => text.TestPush(),
            choreId: null, NotificationCategory.System, ct);
        await _db.SaveChangesAsync(ct);
        return Result.Success(sent);
    }

    /// <summary>Writes an in-app inbox row for the user and pushes the same message to all of their
    /// Web Push + FCM devices (best-effort; dead devices are pruned, <see cref="PushSendResult.Gone"/>).
    /// Does <b>not</b> call <c>SaveChanges</c> — the caller commits, so this can join a larger
    /// transaction. Used for ad-hoc, non-scheduled notifications (test push, chore offers).
    /// Subject to the recipient's <paramref name="category"/> preference, chore mute and quiet hours.
    /// <paramref name="body"/> is rendered in each recipient's own language.
    /// Returns the number of devices the push services accepted.</summary>
    public Task<int> NotifyUserAsync(Guid userId, string title, Func<NotificationText, string> body, Guid? choreId,
        NotificationCategory category, CancellationToken ct = default) =>
        NotifyUsersAsync([userId], title, body, choreId, category, ct);

    /// <summary>Fans one message out to several recipients, loading the routing context (preferences,
    /// mutes, quiet hours) once for the whole set rather than once per user. Each recipient is routed
    /// independently, so one person muting the chore doesn't affect anyone else.</summary>
    public Task<int> NotifyUsersAsync(IReadOnlyCollection<Guid> userIds, string title,
        Func<NotificationText, string> body, Guid? choreId, NotificationCategory category, CancellationToken ct = default) =>
        NotifyUsersAsync(userIds, _ => title, body, choreId, category, ct);

    /// <summary>As above, with the title also worded per recipient (for messages that aren't titled
    /// by a chore name).</summary>
    public async Task<int> NotifyUsersAsync(IReadOnlyCollection<Guid> userIds, Func<NotificationText, string> title,
        Func<NotificationText, string> body, Guid? choreId, NotificationCategory category, CancellationToken ct = default)
    {
        if (userIds.Count == 0)
            return 0;

        var now = DateTimeOffset.UtcNow;
        var routing = await LoadRoutingAsync(userIds, now, ct);

        var url = choreId is { } cid ? $"/chores?chore={cid}" : "/chores";

        // Each recipient gets the text in their own language: the inbox row is stored that way too.
        var pushes = new Dictionary<Guid, (string Title, string Body)>();
        foreach (var userId in userIds.Distinct())
        {
            var channel = routing.Resolve(userId, category, choreId);
            if (channel == DeliveryChannel.Off)
                continue;

            var text = routing.TextFor(userId);
            var message = (Title: title(text), Body: body(text));
            _db.UserNotifications.Add(new UserNotification { UserId = userId, Title = message.Title, Body = message.Body, ChoreId = choreId });
            if (channel == DeliveryChannel.PushAndInbox)
                pushes[userId] = message;
        }

        if (pushes.Count == 0)
            return 0;

        var pushIds = pushes.Keys.ToList();
        var sent = 0;
        var subs = await _db.PushSubscriptions.Where(s => pushIds.Contains(s.UserId)).ToListAsync(ct);
        foreach (var sub in subs)
        {
            var push = pushes[sub.UserId];
            var payload = JsonSerializer.Serialize(new { title = push.Title, body = push.Body, url, choreId });
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
                var push = pushes[device.UserId];
                result = await _fcm.SendAsync(device.Token, push.Title, push.Body, url, choreId, ct);
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

    /// <summary>Replaces the calling user's own notification entries on a chore. They take the place
    /// of the chore's shared entries for that user only; an empty list drops them, so the user follows
    /// the chore's schedule again. Returns the entries as saved.</summary>
    public async Task<Result<ChoreNotificationDto[]>> SetPersonalNotificationsAsync(
        Guid userId, Guid choreId, ChoreNotificationInput[] entries, CancellationToken ct = default)
    {
        if (entries.Length > Validators.MaxNotificationsPerChore)
            return Result.Fail<ChoreNotificationDto[]>(Error.Validation($"A chore can have at most {Validators.MaxNotificationsPerChore} notifications."));
        foreach (var n in entries)
            if (Validators.NotificationOffset(n.Timing, n.OffsetValue) is { } offsetError)
                return Result.Fail<ChoreNotificationDto[]>(offsetError);

        var chore = await _db.Chores.FirstOrDefaultAsync(c => c.Id == choreId, ct);
        if (chore is null)
            return Result.Fail<ChoreNotificationDto[]>(Error.NotFound("Chore not found."));
        // Every notification fires relative to a due time, which an on-demand chore never has.
        if (chore.RepeatType == RepeatType.OnDemand && entries.Length > 0)
            return Result.Fail<ChoreNotificationDto[]>(Error.Validation("An on-demand chore has no due time to notify about."));

        var existing = await _db.ChoreNotifications.Where(n => n.ChoreId == choreId && n.UserId == userId).ToListAsync(ct);
        _db.ChoreNotifications.RemoveRange(existing);

        var saved = entries.Select(n => new ChoreNotification
        {
            ChoreId = choreId,
            UserId = userId,
            Type = n.Type,
            Timing = n.Timing,
            OffsetValue = n.Timing == NotificationTiming.AtDue ? 0 : n.OffsetValue,
            OffsetUnit = n.OffsetUnit,
            Recipients = n.Recipients
        }).ToList();
        _db.ChoreNotifications.AddRange(saved);

        await _db.SaveChangesAsync(ct);
        return Result.Success(saved.Select(ChoreNotificationDto.FromEntity).ToArray());
    }

    /// <summary>Everything <see cref="NotificationRouter.Resolve"/> needs for a set of recipients,
    /// loaded once per scan (or once per fan-out) instead of once per notification.</summary>
    private sealed class RoutingContext
    {
        public required TimeOnly LocalNow { get; init; }
        public required Dictionary<Guid, (TimeOnly? Start, TimeOnly? End)> Quiet { get; init; }
        public required Dictionary<(Guid UserId, NotificationCategory Category), DeliveryChannel> Preferences { get; init; }
        public required HashSet<(Guid UserId, Guid ChoreId)> Mutes { get; init; }
        public required Dictionary<Guid, string?> Languages { get; init; }

        /// <summary>The wording in the recipient's language (English when unset).</summary>
        public NotificationText TextFor(Guid userId) =>
            NotificationText.For(Languages.GetValueOrDefault(userId));

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

        var users = await _db.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.QuietHoursStart, u.QuietHoursEnd, u.Language })
            .ToListAsync(ct);
        var quiet = users
            .Where(u => u.QuietHoursStart != null)
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
            Mutes = mutes,
            Languages = users.ToDictionary(u => u.Id, u => u.Language)
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
            .Include(c => c.SlotTurns)
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
            // Anyone with entries of their own on this chore gets those in place of the shared ones.
            var personalUserIds = chore.Notifications
                .Where(n => n.UserId != null)
                .Select(n => n.UserId!.Value)
                .ToHashSet();

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
                        if (!Reaches(entry, track.UserId, personalUserIds)) continue;
                        if (await TryFireAsync(chore, entry, trackDue, track.UserId, [track.UserId], (track.UserId, null),
                                delivered, subsByUser, fcmByUser, routing, now, ct))
                            fired++;
                    }
                }
                else if (chore.TurnPerSlot && chore.SlotTurns.Count > 0)
                {
                    // Fire per slot, off that slot's own due date, to whoever holds that slot. Each slot's
                    // occurrence is its own instant, so the per-occurrence dedup already tells them apart.
                    foreach (var slot in chore.SlotTurns)
                    {
                        if (slot.DueAt is not { } slotDue) continue;
                        var recipients = (entry.Recipients == NotificationRecipients.AllAssignees
                                ? chore.Assignees.Select(a => a.Id).Where(id => !frozenUserIds.Contains(id))
                                : chore.SlotHolderIds(slot))
                            .Where(id => Reaches(entry, id, personalUserIds))
                            .Distinct()
                            .ToList();
                        if (await TryFireAsync(chore, entry, slotDue, dedupUserId: null, recipients,
                                (slot.CurrentAssigneeId, chore.PartnerOf(slot.CurrentAssigneeId, slot.CurrentPartnerId)?.Id),
                                delivered, subsByUser, fcmByUser, routing, now, ct))
                            fired++;
                    }
                }
                else
                {
                    var dueAt = chore.DueAt!.Value;
                    var recipients = (entry.Recipients == NotificationRecipients.AllAssignees
                            ? chore.Assignees.Select(a => a.Id).Where(id => !frozenUserIds.Contains(id))
                            // The current assignee, plus their partner on a paired chore.
                            : chore.CurrentHolderIds())
                        .Where(id => Reaches(entry, id, personalUserIds))
                        .Distinct()
                        .ToList();
                    if (await TryFireAsync(chore, entry, dueAt, dedupUserId: null, recipients,
                            (chore.CurrentAssigneeId, chore.CurrentPartner()?.Id),
                            delivered, subsByUser, fcmByUser, routing, now, ct))
                        fired++;
                }
            }
        }

        if (fired > 0)
            await _db.SaveChangesAsync(ct);
        return fired;
    }

    /// <summary>Sends each user whose digest time has come their daily digest: the chores they hold
    /// that are overdue or due today (household day). Skipped
    /// when there's nothing to say, frozen users get none, and muted chores are left out. One per
    /// household-local date (<see cref="User.DigestSentOn"/>). Sent as
    /// <see cref="NotificationCategory.System"/>, so it always pushes: quiet hours postpone it instead
    /// (<see cref="DailyDigest.IsDue"/>). Returns the number of digests sent.</summary>
    public async Task<int> ProcessDigestsAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var (tz, localNow) = await HouseholdNowAsync(now, ct);
        var localDate = DateOnly.FromDateTime(localNow);
        var localTime = TimeOnly.FromDateTime(localNow);

        var users = (await _db.Users
                .Where(u => u.DigestTime != null && !u.IsFrozen)
                .ToListAsync(ct))
            .Where(u => u.DigestSentOn != localDate
                && DailyDigest.IsDue(u.DigestTime!.Value, localTime, u.QuietHoursStart, u.QuietHoursEnd))
            .ToList();
        if (users.Count == 0)
            return 0;

        var source = await LoadDigestSourceAsync(users.Select(u => u.Id).ToList(), now, tz, localNow, ct);

        var sent = 0;
        foreach (var user in users)
        {
            user.DigestSentOn = localDate;

            var (today, overdue) = BuildDigest(user.Id, source);
            if (today.Count == 0 && overdue.Count == 0)
                continue;

            await NotifyUsersAsync([user.Id], text => text.DigestTitle(),
                text => Truncate(text.Digest(today, overdue), 512),
                choreId: null, NotificationCategory.System, ct);
            sent++;
        }

        await _db.SaveChangesAsync(ct);
        return sent;
    }

    /// <summary>Sends the caller their daily digest right now, as it would read at this moment, so an
    /// admin can check what it looks like. Sent even on a day with nothing to say, and leaves
    /// <see cref="User.DigestSentOn"/> alone so the real one still goes out.</summary>
    public async Task<Result<int>> SendTestDigestAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        if (!await HasPushDeviceAsync(userId, ct))
            return Result.Fail<int>(Error.Validation(
                "No push subscription on this account. Enable notifications on this device first."));

        var (tz, localNow) = await HouseholdNowAsync(now, ct);
        var source = await LoadDigestSourceAsync([userId], now, tz, localNow, ct);
        var (today, overdue) = BuildDigest(userId, source);

        var sent = await NotifyUsersAsync([userId], text => text.DigestTitle(),
            text => Truncate(today.Count == 0 && overdue.Count == 0
                ? text.DigestEmpty()
                : text.Digest(today, overdue), 512),
            choreId: null, NotificationCategory.System, ct);
        await _db.SaveChangesAsync(ct);
        return Result.Success(sent);
    }

    private async Task<bool> HasPushDeviceAsync(Guid userId, CancellationToken ct) =>
        await _db.PushSubscriptions.AnyAsync(s => s.UserId == userId, ct)
        || await _db.FcmDevices.AnyAsync(d => d.UserId == userId, ct);

    private async Task<(TimeZoneInfo Tz, DateTime LocalNow)> HouseholdNowAsync(DateTimeOffset now, CancellationToken ct)
    {
        var tzId = await _db.AppSettings
            .Where(s => s.Key == AppSettingsService.TimeZoneKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        var tz = TimeZoneResolver.Resolve(tzId);
        return (tz, TimeZoneInfo.ConvertTime(now, tz).DateTime);
    }

    /// <summary>Everything a batch of digests reads, loaded once.</summary>
    private sealed record DigestSource(
        TimeZoneInfo Tz, DateTime LocalNow, TimeSpan DayStart, List<Chore> Chores,
        HashSet<(Guid UserId, Guid ChoreId)> Mutes);

    private async Task<DigestSource> LoadDigestSourceAsync(
        List<Guid> userIds, DateTimeOffset now, TimeZoneInfo tz, DateTime localNow, CancellationToken ct)
    {
        var dayStart = await AppSettingsService.ReadDayStartAsync(_db, ct);

        var chores = await _db.Chores
            .Include(c => c.Assignees)
            .Include(c => c.AssigneeTracks)
            .Include(c => c.SlotTurns)
            .Where(c => c.DueAt != null && !c.IsFrozen)
            .AsSplitQuery()
            .ToListAsync(ct);

        var mutes = (await _db.ChoreMutes
                .Where(m => userIds.Contains(m.UserId))
                .Select(m => new { m.UserId, m.ChoreId, m.Until })
                .ToListAsync(ct))
            .Where(m => m.Until == null || m.Until > now)
            .Select(m => (m.UserId, m.ChoreId))
            .ToHashSet();

        return new DigestSource(tz, localNow, dayStart, chores, mutes);
    }

    /// <summary>One user's digest content: names of the chores they hold that are due today and
    /// overdue (soonest first, muted chores left out).</summary>
    private static (List<string> Today, List<string> Overdue) BuildDigest(Guid userId, DigestSource source)
    {
        var mine = new List<(DateTimeOffset Due, string Name, DailyDigest.Bucket Bucket)>();
        foreach (var chore in source.Chores)
        {
            if (source.Mutes.Contains((userId, chore.Id)))
                continue;
            // Every slot they hold on a slot-turn chore, each listed under its own label.
            IEnumerable<DateTimeOffset?> dues = chore.AssignmentStrategy == AssignmentStrategy.Independent
                ? [chore.AssigneeTracks.FirstOrDefault(t => t.UserId == userId)?.DueAt]
                : chore.TurnPerSlot && chore.SlotTurns.Count > 0
                    ? chore.SlotTurns.Where(st => chore.SlotHolderIds(st).Contains(userId)).Select(st => st.DueAt)
                    : [chore.CurrentHolderIds().Contains(userId) ? chore.DueAt : null];
            foreach (var due in dues)
            {
                if (due is not { } dueAt)
                    continue;

                var bucket = DailyDigest.Classify(
                    TimeZoneInfo.ConvertTime(dueAt, source.Tz).DateTime, chore.HasDueTime, source.LocalNow, source.DayStart);
                if (bucket == DailyDigest.Bucket.Later)
                    continue;
                var name = chore.SlotLabel(dueAt) is { } label ? $"{chore.Name} ({label})" : chore.Name;
                mine.Add((dueAt, name, bucket));
            }
        }

        // Sorted client-side: SQLite can't order a DateTimeOffset.
        mine.Sort((x, y) => x.Due.CompareTo(y.Due));
        var today = mine.Where(m => m.Bucket == DailyDigest.Bucket.Today).Select(m => m.Name).ToList();
        var overdue = mine.Where(m => m.Bucket == DailyDigest.Bucket.Overdue).Select(m => m.Name).ToList();
        return (today, overdue);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

    /// <summary>Whether <paramref name="entry"/> may reach <paramref name="userId"/>: a personal entry
    /// only its owner, a shared one everyone without entries of their own on the chore.</summary>
    private static bool Reaches(ChoreNotification entry, Guid userId, HashSet<Guid> personalUserIds) =>
        entry.UserId is { } owner ? userId == owner : !personalUserIds.Contains(userId);

    /// <summary>Fires a single entry for one occurrence if its moment has arrived and it hasn't already
    /// fired (per the dedup key). <paramref name="dedupUserId"/> is the track owner in Independent mode
    /// (null otherwise). <paramref name="named"/> is who the message says owes it: the holder (the
    /// track owner, or a slot's holder) and their partner on a paired chore.</summary>
    private async Task<bool> TryFireAsync(
        Chore chore, ChoreNotification entry, DateTimeOffset occurrenceDueAt, Guid? dedupUserId,
        IReadOnlyList<Guid> recipientIds, (Guid? Holder, Guid? Partner) named, HashSet<(Guid, long, Guid?)> delivered,
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

        await SendEntryAsync(chore, entry, occurrenceDueAt, recipientIds,
            named, subsByUser, fcmByUser, routing, now, ct);

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
        (Guid? Holder, Guid? Partner) named, Dictionary<Guid, List<PushSubscription>> subsByUser,
        Dictionary<Guid, List<FcmDevice>> fcmByUser,
        RoutingContext routing, DateTimeOffset now, CancellationToken ct)
    {
        var (title, render) = BuildMessage(chore, entry, occurrenceDueAt, named, now);
        var url = $"/chores?chore={chore.Id}";
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

            // Rendered in the recipient's language, for the inbox and their devices alike.
            var body = render(routing.TextFor(userId));
            var payload = JsonSerializer.Serialize(new { title, body, url, choreId = chore.Id });

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

    private static (string Title, Func<NotificationText, string> Body) BuildMessage(Chore chore,
        ChoreNotification entry, DateTimeOffset occurrenceDueAt, (Guid? Holder, Guid? Partner) named, DateTimeOffset now)
    {
        // A labelled slot names itself in the title ("Feed the dog (Morning)") so the two pushes of a
        // twice-a-day chore don't read identically.
        var name = chore.SlotLabel(occurrenceDueAt) is { } label ? $"{chore.Name} ({label})" : chore.Name;
        var title = string.IsNullOrWhiteSpace(chore.Emoji) ? name : $"{chore.Emoji} {name}";

        // The named holder (current assignee, a slot's holder, or the track owner in Independent mode)
        // is one of the loaded Assignees, so resolve the name without an extra include.
        var assignee = chore.Assignees.FirstOrDefault(a => a.Id == named.Holder)?.DisplayName;
        // A paired chore names both people doing it.
        List<string>? who = assignee is null ? null : [assignee];
        if (who is not null && chore.Assignees.FirstOrDefault(a => a.Id == named.Partner) is { } partner)
            who.Add(partner.DisplayName);
        var points = chore.Points;
        var dueAt = occurrenceDueAt;

        return (title, text =>
        {
            var people = who is null ? null : text.People(who);
            return entry.Type switch
            {
                NotificationType.Due => text.DueNow(people, points),
                NotificationType.FollowUp => text.Overdue(people, now - dueAt, points),
                _ => text.DueIn(people, dueAt - now, points)
            };
        });
    }
}
