using Turnly.Core.Enums;

namespace Turnly.Core.Entities;

/// <summary>One change to a user's point balance — earnings (per completion) and, later,
/// deductions (per redemption). The source of truth for a user's points history.</summary>
public class PointsLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Signed change to the balance: positive for earnings, negative for deductions.</summary>
    public int Delta { get; set; }

    public PointsLogType Type { get; set; } = PointsLogType.Completion;

    /// <summary>Links an earning to the completion that produced it, so undo can reverse it.</summary>
    public Guid? ChoreCompletionId { get; set; }

    /// <summary>Human-readable context, e.g. a snapshot of the chore name.</summary>
    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
