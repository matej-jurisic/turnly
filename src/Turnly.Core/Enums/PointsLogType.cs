namespace Turnly.Core.Enums;

/// <summary>Why a points-log entry was created. Phase 2 only writes <see cref="Completion"/>;
/// <see cref="Redemption"/> arrives with awards (Phase 7).</summary>
public enum PointsLogType
{
    Completion = 0,
    Redemption = 1,
    Adjustment = 2
}
