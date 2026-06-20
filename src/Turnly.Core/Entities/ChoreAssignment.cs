namespace Turnly.Core.Entities;

/// <summary>A single assignment of a chore to a user — written when the chore is created (initial
/// assignee) and on each rotation as the chore advances to a new occurrence. Backs the
/// <see cref="Enums.AssignmentStrategy.LeastAssigned"/> count and lets undo reverse a rotation via
/// <see cref="ChoreCompletionId"/>.</summary>
public class ChoreAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ChoreId { get; set; }
    public Chore? Chore { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The completion whose rotation produced this assignment, if any; null for the
    /// initial assignment made at chore creation. Lets undo delete the rotation it caused.</summary>
    public Guid? ChoreCompletionId { get; set; }

    /// <summary>For a manual reassignment, who the chore was assigned to before this row; null for
    /// the initial assignment and for rotations. Together with <see cref="AssignedByUserId"/> this
    /// marks the row as a user-initiated reassignment in the history feed.</summary>
    public Guid? PreviousAssigneeId { get; set; }
    public User? PreviousAssignee { get; set; }

    /// <summary>For a manual reassignment, the user who performed it; null for the initial
    /// assignment and for automatic rotations (which have no acting user).</summary>
    public Guid? AssignedByUserId { get; set; }
    public User? AssignedBy { get; set; }
}
