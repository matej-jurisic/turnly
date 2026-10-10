namespace Turnly.Core.Enums;

/// <summary>How often a rotating chore hands over to the next assignee. Decouples the handover from
/// the occurrence, so a chore can come due weekly but stay with one person for a whole month.</summary>
public enum RotationPeriod
{
    /// <summary>Hand over every time an occurrence closes (the default).</summary>
    EveryOccurrence = 0,
    /// <summary>Hand over only when the next occurrence falls in a different calendar month than the
    /// one that just closed.</summary>
    Monthly = 1
}
