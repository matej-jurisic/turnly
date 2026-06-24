using Microsoft.EntityFrameworkCore;
using Turnly.Core.Achievements;
using Turnly.Core.Common;
using Turnly.Core.Data;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;
using Turnly.Core.Recurrence;

namespace Turnly.Core.Services;

/// <summary>Evaluates the code-defined <see cref="AchievementCatalog"/> against a user's activity and
/// records earned achievements. Granting is **inline**: the mutations that can move a metric
/// (<c>ChoreService.CompleteAsync</c>, <c>RedemptionService.RedeemAsync</c>,
/// <c>UserService.AdjustPointsAsync</c>) call <see cref="EvaluateForUserAsync"/> after they save, so a
/// badge unlocks at the moment it's earned. That call <b>returns the freshly-unlocked achievements</b> so
/// the mutation can hand them back to the client (which shows a celebration popup) — there is no push/inbox
/// notification for an unlock. Achievements are **permanent** — reversals (undo, cancel, negative
/// adjustment) lower the live metrics but never revoke an earned badge, and this service only ever *adds*
/// rows. The read (<see cref="ListForUserAsync"/>) is side-effect free. Cosmetic — no points.</summary>
public class AchievementService
{
    private readonly TurnlyDbContext _db;

    public AchievementService(TurnlyDbContext db)
    {
        _db = db;
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

    /// <summary>Grants any achievements the user has newly met, then saves, and returns them so the
    /// caller can surface a celebration to the client. Called inline by the metric-moving mutations after
    /// they persist. Already-earned achievements are skipped, so re-crossing a threshold (e.g. undo then
    /// redo) never re-grants. Returns an empty list when nothing new was earned.</summary>
    public async Task<List<AchievementDto>> EvaluateForUserAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct);
        if (user is null)
            return [];

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
            return [];

        foreach (var def in newly)
            _db.UserAchievements.Add(new UserAchievement { UserId = userId, AchievementKey = def.Key, EarnedAt = now });

        await _db.SaveChangesAsync(ct);

        // Newly earned, so progress is (clamped to) the threshold and EarnedAt is now.
        return newly
            .Select(def => new AchievementDto(
                def.Key, def.Name, def.Description, def.Emoji, def.Category, def.Threshold,
                def.Threshold, true, now))
            .ToList();
    }

    /// <summary>Admin: revokes a single earned achievement from a user (removes the
    /// <see cref="UserAchievement"/> row). The badge can be re-earned later if its threshold is met
    /// again, since granting only checks live progress against already-earned keys.</summary>
    public async Task<Result> RevokeAsync(Guid userId, string key, CancellationToken ct = default)
    {
        var row = await _db.UserAchievements
            .FirstOrDefaultAsync(a => a.UserId == userId && a.AchievementKey == key, ct);
        if (row is null)
            return Result.Fail(Error.NotFound("Achievement not earned by this user."));

        _db.UserAchievements.Remove(row);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
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
