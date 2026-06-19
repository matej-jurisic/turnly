namespace Turnly.Core.Enums;

/// <summary>The unit for a <see cref="CustomRecurrenceMode.Interval"/> recurrence. Hour-level
/// granularity is deferred to Phase 5 (notifications), so the smallest unit is a day.</summary>
public enum RecurrenceUnit
{
    Day = 0,
    Week = 1,
    Month = 2,
    Year = 3
}
