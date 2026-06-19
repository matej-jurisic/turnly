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
}
