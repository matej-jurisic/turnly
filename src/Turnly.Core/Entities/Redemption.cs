using Turnly.Core.Enums;

namespace Turnly.Core.Entities;

/// <summary>A logged redemption of an award by a user. Snapshots the award's name, emoji and cost
/// so the record survives the award being edited or deleted. Cancelling a (still pending)
/// redemption deletes this row and reverses the linked points-log deduction.</summary>
public class Redemption
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The award redeemed; nulled (not deleted) if the award is later removed.</summary>
    public Guid? AwardId { get; set; }
    public Award? Award { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Snapshot of the award at redemption time, for stable history.</summary>
    public required string AwardName { get; set; }
    public string? AwardEmoji { get; set; }
    public int PointsSpent { get; set; }

    public RedemptionStatus Status { get; set; } = RedemptionStatus.Pending;

    public DateTimeOffset RedeemedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FulfilledAt { get; set; }
}
