namespace Turnly.Core.Enums;

/// <summary>How often a rotating chore hands over to the next assignee. Decouples the handover from
/// the occurrence, so a chore can come due weekly but stay with one person for a whole month.
/// Every value but <see cref="EveryOccurrence"/> is a calendar period: the handover happens when the
/// next occurrence falls in a different period than the one that just closed.</summary>
public enum RotationPeriod
{
    /// <summary>Hand over every time an occurrence closes (the default).</summary>
    EveryOccurrence = 0,
    /// <summary>Hand over when the next occurrence falls in a different calendar month.</summary>
    Monthly = 1,
    /// <summary>Hand over when the next occurrence falls on a different calendar day (for chores
    /// that come due several times a day).</summary>
    Daily = 2,
    /// <summary>Hand over when the next occurrence falls in a different Monday-to-Sunday week.</summary>
    Weekly = 3,
    /// <summary>Hand over when the next occurrence falls in a different calendar quarter.</summary>
    Quarterly = 4,
    /// <summary>Hand over when the next occurrence falls in a different calendar year.</summary>
    Yearly = 5
}
