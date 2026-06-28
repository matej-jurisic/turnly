using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Cosmetics;
using Turnly.Core.Data;

namespace Turnly.Core.Services;

/// <summary>Admin "fresh start": wipes all activity, point history, gacha state, and achievements,
/// resetting every user's balance to zero, while leaving chores (and their schedules) untouched.
/// The goal is to keep a configured chore set but clear the slate, e.g. at the start of a new month.</summary>
public class ResetService
{
    private readonly TurnlyDbContext _db;

    public ResetService(TurnlyDbContext db) => _db = db;

    /// <summary>Deletes completions/skips/expired activity, assignment history, the points log,
    /// redemptions, achievements, owned cosmetics, and the notification inbox; then zeroes every
    /// user's points, dust, and pity counter and resets their equipped cosmetics back to the free
    /// defaults. Chores, tags, awards, users, push devices, and notification schedules are kept.
    /// Runs as one transaction so a failure leaves the data untouched.</summary>
    public async Task<Result> FreshStartAsync(CancellationToken ct = default)
    {
        // The default avatar color everyone owns for free (the one Default cosmetic).
        var defaultColor = CosmeticCatalog.All.First(c => c.Default).Value ?? "#6366f1";

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Activity & history. The *Id back-links between these (PointsLogEntry.ChoreCompletionId,
        // ChoreAssignment.ChoreCompletionId, etc.) are plain columns, not FK constraints, so delete
        // order is unconstrained.
        await _db.ChoreCompletions.ExecuteDeleteAsync(ct);
        await _db.ChoreAssignments.ExecuteDeleteAsync(ct);
        await _db.ChoreReassignmentRequests.ExecuteDeleteAsync(ct);
        await _db.PointsLog.ExecuteDeleteAsync(ct);
        await _db.Redemptions.ExecuteDeleteAsync(ct);
        await _db.UserAchievements.ExecuteDeleteAsync(ct);
        await _db.UserCosmetics.ExecuteDeleteAsync(ct);
        await _db.UserNotifications.ExecuteDeleteAsync(ct);

        // Reset per-user balances and equipped cosmetics to the free defaults. The chore's own
        // CurrentAssigneeId / DueAt / tracks are left alone, so each chore keeps its schedule.
        await _db.Users.ExecuteUpdateAsync(s => s
            .SetProperty(u => u.Points, 0)
            .SetProperty(u => u.Dust, 0)
            .SetProperty(u => u.PullsSinceLegendary, 0)
            .SetProperty(u => u.EquippedFrameKey, (string?)null)
            .SetProperty(u => u.EquippedThemeKey, (string?)null)
            .SetProperty(u => u.AvatarEmoji, (string?)null)
            .SetProperty(u => u.AvatarColor, defaultColor), ct);

        await tx.CommitAsync(ct);
        return Result.Success();
    }
}
