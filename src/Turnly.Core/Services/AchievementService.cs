using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Turnly.Core.Achievements;
using Turnly.Core.Data;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;
using Turnly.Core.Notifications;
using Turnly.Core.Recurrence;

namespace Turnly.Core.Services;

/// <summary>Evaluates the code-defined <see cref="AchievementCatalog"/> against a user's activity and
/// records earned achievements. Granting is **inline**: the mutations that can move a metric
/// (<c>ChoreService.CompleteAsync</c>, <c>RedemptionService.RedeemAsync</c>,
/// <c>UserService.AdjustPointsAsync</c>) call <see cref="EvaluateForUserAsync"/> after they save, so a
/// badge unlocks at the moment it's earned. Achievements are **permanent** — reversals (undo, cancel,
/// negative adjustment) lower the live metrics but never revoke an earned badge, and this service only
/// ever *adds* rows. The read (<see cref="ListForUserAsync"/>) is side-effect free. Cosmetic — no points.</summary>
public class AchievementService
{
    private readonly TurnlyDbContext _db;
    private readonly IPushSender _push;

    public AchievementService(TurnlyDbContext db, IPushSender push)
    {
        _db = db;
        _push = push;
    }

    /// <summary>Projects every catalog achievement for one user: definition + current progress +
    /// whether (and when) it was earned. Read-only — granting happens in <see cref="EvaluateAllAsync"/>.</summary>
    public async Task<List<AchievementDto>> ListForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var stats = await ComputeStatsAsync(userId, ct);
        var earned = (await _db.UserAchievements
                .Where(a => a.UserId == userId)
                .ToListAsync(ct))
            .ToDictionary(a => a.AchievementKey, a => a.EarnedAt);

        return AchievementCatalog.All
            .Select(def =>
            {
                var hasEarned = earned.TryGetValue(def.Key, out var at);
                return new AchievementDto(
                    def.Key, def.Name, def.Description, def.Emoji, def.Category, def.Threshold,
                    Math.Min(def.Progress(stats), def.Threshold), hasEarned, hasEarned ? at : null);
            })
            .ToList();
    }

    /// <summary>Grants any achievements the user has newly met (and fires a one-time inbox + push per
    /// unlock), then saves. Called inline by the metric-moving mutations after they persist. Already-earned
    /// achievements are skipped, so re-crossing a threshold (e.g. undo then redo) never re-grants or
    /// re-notifies. Returns the number granted. Best-effort: never throws into the caller's flow.</summary>
    public async Task<int> EvaluateForUserAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct);
        if (user is null)
            return 0;

        var stats = await ComputeStatsAsync(userId, ct);
        var earnedKeys = (await _db.UserAchievements
                .Where(a => a.UserId == userId)
                .Select(a => a.AchievementKey)
                .ToListAsync(ct))
            .ToHashSet();

        var newly = AchievementCatalog.All
            .Where(def => !earnedKeys.Contains(def.Key) && def.Progress(stats) >= def.Threshold)
            .ToList();
        if (newly.Count == 0)
            return 0;

        foreach (var def in newly)
            _db.UserAchievements.Add(new UserAchievement { UserId = userId, AchievementKey = def.Key });

        // Quiet hours are a local wall-clock window, so resolve "now" in the family timezone.
        var tzId = await _db.AppSettings
            .Where(s => s.Key == AppSettingsService.TimeZoneKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        var localNow = TimeOnly.FromTimeSpan(
            TimeZoneInfo.ConvertTime(now, TimeZoneResolver.Resolve(tzId)).TimeOfDay);

        await NotifyAsync(user, newly, localNow, ct);

        await _db.SaveChangesAsync(ct);
        return newly.Count;
    }

    /// <summary>Writes a per-achievement inbox row and (unless the user is in quiet hours) pushes it
    /// to their devices, mirroring <see cref="NotificationService"/>'s send path.</summary>
    private async Task NotifyAsync(User user, List<AchievementDefinition> defs, TimeOnly localNow, CancellationToken ct)
    {
        var subs = await _db.PushSubscriptions.Where(s => s.UserId == user.Id).ToListAsync(ct);
        var quiet = QuietHours.Contains(user.QuietHoursStart, user.QuietHoursEnd, localNow);

        foreach (var def in defs)
        {
            var title = $"Achievement unlocked! {def.Emoji}";
            var body = $"{def.Name} — {def.Description}";

            // Always record the inbox row, even with no reachable device / during quiet hours.
            _db.UserNotifications.Add(new UserNotification { UserId = user.Id, Title = title, Body = body });

            if (quiet)
                continue;

            var payload = JsonSerializer.Serialize(new { title, body, url = "/achievements" });
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
                if (result == PushSendResult.Gone)
                    _db.PushSubscriptions.Remove(sub);
            }
        }
    }

    /// <summary>Computes the metrics the catalog's criteria read. A handful of small queries, run per
    /// user — fine at family scale.</summary>
    public async Task<AchievementStats> ComputeStatsAsync(Guid userId, CancellationToken ct = default)
    {
        // Real completions credited to the user (skips and auto-expiries don't count).
        var myCompletions = await _db.ChoreCompletions
            .Where(c => c.CompletedByUserId == userId && !c.IsSkip && !c.IsExpired)
            .Include(c => c.Chore!)
            .ThenInclude(ch => ch.Tags)
            .ToListAsync(ct);

        var completions = myCompletions.Count;
        var distinctChores = myCompletions.Select(c => c.ChoreId).Distinct().Count();
        var distinctTags = myCompletions
            .SelectMany(c => c.Chore?.Tags.Select(t => t.Name) ?? Enumerable.Empty<string>())
            .Distinct()
            .Count();

        // Lifetime points earned = sum of the positive points-log deltas (completions + grants).
        var pointsEarned = (await _db.PointsLog
                .Where(e => e.UserId == userId)
                .Select(e => e.Delta)
                .ToListAsync(ct))
            .Where(d => d > 0)
            .Sum();

        var redemptions = await _db.Redemptions.CountAsync(r => r.UserId == userId, ct);

        // Longest current on-time streak the user holds across any chore they've completed.
        var choreIds = myCompletions.Select(c => c.ChoreId).Distinct().ToList();
        var maxStreak = 0;
        if (choreIds.Count > 0)
        {
            var rows = await _db.ChoreCompletions
                .Where(c => choreIds.Contains(c.ChoreId))
                .ToListAsync(ct);
            foreach (var group in rows.GroupBy(c => c.ChoreId))
                maxStreak = Math.Max(maxStreak, StreakCalculator.CurrentStreak(group, userId));
        }

        return new AchievementStats(completions, maxStreak, pointsEarned, redemptions, distinctChores, distinctTags);
    }
}
