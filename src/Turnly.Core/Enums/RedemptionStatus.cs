namespace Turnly.Core.Enums;

/// <summary>Lifecycle of an award redemption. A redemption starts <see cref="Pending"/> and an
/// admin either marks it <see cref="Fulfilled"/> (delivered) or cancels it (refunding the points,
/// which deletes the row).</summary>
public enum RedemptionStatus
{
    Pending = 0,
    Fulfilled = 1
}
