namespace Turnly.Core.Enums;

/// <summary>The flavour of a <see cref="RepeatType.Custom"/> chore — selects which custom
/// recurrence parameters on the chore are meaningful.</summary>
public enum CustomRecurrenceMode
{
    /// <summary>Every {IntervalCount} {IntervalUnit} (e.g. every 2 weeks).</summary>
    Interval = 0,
    /// <summary>Repeats on the selected <c>Weekdays</c> (e.g. Mon, Wed, Fri).</summary>
    DaysOfWeek = 1,
    /// <summary>Repeats on the selected <c>DaysOfMonth</c> within the selected <c>Months</c>.</summary>
    DaysOfMonth = 2,
    /// <summary>Must be completed {FrequencyCount} times per {FrequencyPeriod}; days not fixed.</summary>
    Frequency = 3
}
