namespace Turnly.Core.Entities;

/// <summary>A single logged completion of a chore. Undoing a completion deletes this row,
/// reverses the awarded points, and restores the chore's <see cref="OccurrenceDueAt"/>.</summary>
public class ChoreCompletion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ChoreId { get; set; }
    public Chore? Chore { get; set; }

    public Guid CompletedByUserId { get; set; }
    public User? CompletedBy { get; set; }

    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Notes { get; set; }

    /// <summary>Snapshot of the chore's point value at completion time (for correct undo/history).</summary>
    public int PointsAwarded { get; set; }

    /// <summary>The chore's <c>DueAt</c> when it was completed; restored on undo.</summary>
    public DateTimeOffset? OccurrenceDueAt { get; set; }

    /// <summary>The chore's current assignee before this completion (possibly) rotated it;
    /// restored on undo. A plain snapshot, not a navigation.</summary>
    public Guid? PreviousAssigneeId { get; set; }
}
