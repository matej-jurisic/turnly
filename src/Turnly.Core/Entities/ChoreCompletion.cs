namespace Turnly.Core.Entities;

/// <summary>A single logged completion of a chore. Undoing a completion deletes this row,
/// reverses the awarded points, and restores the chore's <see cref="OccurrenceDueAt"/>.
/// A <see cref="IsSkip"/> row is a points-less "skip" — it advances the recurrence without
/// awarding points and shares the same undo path.</summary>
public class ChoreCompletion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ChoreId { get; set; }
    public Chore? Chore { get; set; }

    /// <summary>True when this row records a skipped occurrence rather than a real completion:
    /// no points are awarded and it's excluded from completion stats/counts.</summary>
    public bool IsSkip { get; set; }

    /// <summary>True when this row was written by the auto-advance background service because the
    /// completion window expired with unfilled slots. No points, not undoable, no actor.</summary>
    public bool IsExpired { get; set; }

    /// <summary>Null only for auto-expired entries (<see cref="IsExpired"/> = true).</summary>
    public Guid? CompletedByUserId { get; set; }
    public User? CompletedBy { get; set; }

    /// <summary>On a paired chore, the completer's partner: credited with the same points and counted
    /// in their stats as if they had logged it themselves. On an auto-expired row it's the partner of
    /// the responsible assignee (<see cref="PreviousAssigneeId"/>), who shares the miss. Null otherwise
    /// (skips included).</summary>
    public Guid? PartnerUserId { get; set; }
    public User? Partner { get; set; }

    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Notes { get; set; }

    /// <summary>Snapshot of the chore's point value at completion time (for correct undo/history).</summary>
    public int PointsAwarded { get; set; }

    /// <summary>The chore's <c>DueAt</c> when it was completed; restored on undo.</summary>
    public DateTimeOffset? OccurrenceDueAt { get; set; }

    /// <summary>The chore's current assignee before this completion (possibly) rotated it;
    /// restored on undo. A plain snapshot, not a navigation.</summary>
    public Guid? PreviousAssigneeId { get; set; }

    /// <summary>On a paired chore, who held the turn together with <see cref="PreviousAssigneeId"/>
    /// (<see cref="Chore.HeldPartnerId"/> at the time); restored on undo. A plain snapshot.</summary>
    public Guid? PreviousPartnerId { get; set; }

    /// <summary>A late completion logged by someone other than the person (or pair) who held the
    /// chore: they stepped in for an overdue chore, so it counts as late for nobody. Always false on
    /// <see cref="Enums.AssignmentStrategy.Independent"/> chores, which snapshot no holder.</summary>
    /// <param name="lateGrace">How long past its due instant the occurrence still counts as on time
    /// (<see cref="Chore.LateGrace"/>).</param>
    public bool IsRescue(TimeSpan lateGrace) =>
        IsRescue(IsSkip, IsExpired, CompletedAt, OccurrenceDueAt, CompletedByUserId, PartnerUserId, PreviousAssigneeId, PreviousPartnerId, lateGrace);

    /// <summary>The same rule over bare columns, for readers that project instead of loading rows.</summary>
    public static bool IsRescue(bool isSkip, bool isExpired, DateTimeOffset completedAt, DateTimeOffset? occurrenceDueAt,
        Guid? completedByUserId, Guid? partnerUserId, Guid? previousAssigneeId, Guid? previousPartnerId, TimeSpan lateGrace) =>
        !isSkip && !isExpired
        && occurrenceDueAt is { } due && completedAt > due + lateGrace
        && previousAssigneeId is { } holder && holder != completedByUserId && holder != partnerUserId
        && (previousPartnerId is not { } coHolder || (coHolder != completedByUserId && coHolder != partnerUserId));
}
