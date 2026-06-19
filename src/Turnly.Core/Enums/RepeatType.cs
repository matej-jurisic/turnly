namespace Turnly.Core.Enums;

/// <summary>
/// Recurrence types. The first five are the basic Phase 2 schedules; <see cref="Custom"/>
/// (Phase 3) delegates to a <see cref="CustomRecurrenceMode"/> for richer rules.
/// </summary>
public enum RepeatType
{
    OneTime = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
    Yearly = 4,
    Custom = 5
}
