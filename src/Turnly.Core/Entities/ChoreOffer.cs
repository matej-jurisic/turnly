using Turnly.Core.Enums;

namespace Turnly.Core.Entities;

/// <summary>A member putting the current occurrence of a chore up for someone else to take, optionally
/// sweetened with a points bounty. An offer is either <b>open</b> (<see cref="ToUserId"/> null — any of
/// the chore's other assignees may claim it, first come first served) or <b>targeted</b> at one person,
/// who may claim or decline it. Either way the chore's <c>CurrentAssigneeId</c> is untouched while the
/// offer is <see cref="OfferStatus.Pending"/>; it only moves on a claim.
///
/// <para><b>Bounty escrow:</b> <see cref="BountyPoints"/> is deducted from <see cref="FromUser"/> when the
/// offer is created and credited to the claimer when it's claimed, so the payout can never bounce. Every
/// other terminal status (declined, withdrawn, superseded, voided) refunds it. Admin reassignment does
/// not go through offers at all.</para>
///
/// <para>At most one Pending offer exists per chore at a time — a newer offer supersedes an older one
/// (Cancelled + refunded).</para></summary>
public class ChoreOffer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ChoreId { get; set; }
    public Chore? Chore { get; set; }

    /// <summary>The member who offered the chore (its current assignee) and who funds the bounty.</summary>
    public Guid FromUserId { get; set; }
    public User? FromUser { get; set; }

    /// <summary>The one member being asked, or <c>null</c> for an open offer any other assignee may claim.</summary>
    public Guid? ToUserId { get; set; }
    public User? ToUser { get; set; }

    /// <summary>Points the offerer pays whoever takes the chore. Zero for a plain "anyone want this?".
    /// Held in escrow (already deducted from <see cref="FromUser"/>) while the offer is pending.</summary>
    public int BountyPoints { get; set; }

    /// <summary>Who actually took the chore; null until claimed. For a targeted offer this equals
    /// <see cref="ToUserId"/>; for an open offer it's whoever got there first.</summary>
    public Guid? ClaimedByUserId { get; set; }
    public User? ClaimedBy { get; set; }

    public OfferStatus Status { get; set; } = OfferStatus.Pending;

    /// <summary>When the offer was claimed/declined/withdrawn/voided; null while pending.</summary>
    public DateTimeOffset? RespondedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
