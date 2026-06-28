using Turnly.Core.Enums;

namespace Turnly.Core.Entities;

/// <summary>A member's request to hand the current occurrence of a chore to another assignee. Unlike
/// an admin reassignment (which moves the chore immediately), a member request is <see cref="Status"/>
/// <see cref="ReassignmentStatus.Pending"/> until the target accepts (the chore moves) or declines
/// (it stays). The chore's <c>CurrentAssigneeId</c> is untouched while pending. At most one Pending
/// request exists per chore at a time — a newer request supersedes an older one (Cancelled).</summary>
public class ChoreReassignmentRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ChoreId { get; set; }
    public Chore? Chore { get; set; }

    /// <summary>The member who requested the reassignment (the chore's current assignee).</summary>
    public Guid FromUserId { get; set; }
    public User? FromUser { get; set; }

    /// <summary>The member being asked to take the chore.</summary>
    public Guid ToUserId { get; set; }
    public User? ToUser { get; set; }

    public ReassignmentStatus Status { get; set; } = ReassignmentStatus.Pending;

    /// <summary>When the target accepted/declined, or the request was cancelled; null while pending.</summary>
    public DateTimeOffset? RespondedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
