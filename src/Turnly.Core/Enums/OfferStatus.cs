namespace Turnly.Core.Enums;

/// <summary>Lifecycle of a <see cref="Turnly.Core.Entities.ChoreOffer"/>. An offer starts
/// <see cref="Pending"/> and ends when someone <see cref="Claimed"/> it (the chore moves to them and the
/// bounty is paid out), the offerer <see cref="Withdrawn"/> it, or it was <see cref="Cancelled"/> —
/// superseded by a newer offer, or voided because the chore moved on (admin reassign, freeze rotation,
/// completion, auto-advance). Every terminal status except <see cref="Claimed"/> refunds the escrowed
/// bounty. <see cref="Declined"/> is no longer written (offers used to be targetable at one person,
/// who could turn them down); it stays so old rows still parse.</summary>
public enum OfferStatus
{
    Pending = 0,
    Claimed = 1,
    Declined = 2,
    Cancelled = 3,
    Withdrawn = 4
}
