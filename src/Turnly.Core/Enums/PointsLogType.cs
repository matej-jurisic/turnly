namespace Turnly.Core.Enums;

/// <summary>Why a points-log entry was created. Chore completions write <see cref="Completion"/>;
/// award redemptions write <see cref="Redemption"/>.</summary>
public enum PointsLogType
{
    Completion = 0,
    Redemption = 1,
    Adjustment = 2
}
