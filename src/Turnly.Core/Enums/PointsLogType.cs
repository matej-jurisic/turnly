namespace Turnly.Core.Enums;

/// <summary>Why a points-log entry was created. Chore completions write <see cref="Completion"/>;
/// award redemptions write <see cref="Redemption"/>; gacha pulls write <see cref="GachaPull"/>;
/// chore-offer bounties write <see cref="Bounty"/>.</summary>
public enum PointsLogType
{
    Completion = 0,
    Redemption = 1,
    Adjustment = 2,
    GachaPull = 3,

    /// <summary>A chore-offer bounty moving between members: the debit when an offer is created
    /// (escrow), the credit when it's claimed, or the refund when it ends any other way. Because these
    /// are zero-sum transfers within the family they are excluded from lifetime-points achievements —
    /// see <c>AchievementService</c>.</summary>
    Bounty = 4
}
