namespace Turnly.Core.Enums;

/// <summary>Lifecycle of a member-initiated reassignment request. A request starts
/// <see cref="Pending"/> and the target either <see cref="Accepted"/> it (the chore moves to them)
/// or <see cref="Declined"/> it (the chore stays put). <see cref="Cancelled"/> marks a request that
/// was superseded by a newer one, or voided because an admin/rotation moved the chore first.</summary>
public enum ReassignmentStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    Cancelled = 3
}
