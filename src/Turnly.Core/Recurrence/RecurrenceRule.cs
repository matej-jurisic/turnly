using Turnly.Core.Entities;
using Turnly.Core.Enums;

namespace Turnly.Core.Recurrence;

/// <summary>An immutable, EF-decoupled snapshot of a chore's recurrence configuration, so the
/// recurrence math can be exercised in pure unit tests without a database.</summary>
public record RecurrenceRule(
    RepeatType Type,
    CustomRecurrenceMode? CustomMode = null,
    int? IntervalCount = null,
    RecurrenceUnit? IntervalUnit = null,
    IReadOnlyCollection<DayOfWeek>? Weekdays = null,
    IReadOnlyCollection<int>? DaysOfMonth = null,
    IReadOnlyCollection<int>? Months = null)
{
    public IReadOnlyCollection<DayOfWeek> Weekdays { get; init; } = Weekdays ?? Array.Empty<DayOfWeek>();
    public IReadOnlyCollection<int> DaysOfMonth { get; init; } = DaysOfMonth ?? Array.Empty<int>();
    public IReadOnlyCollection<int> Months { get; init; } = Months ?? Array.Empty<int>();

    public static RecurrenceRule FromChore(Chore c) => new(
        c.RepeatType, c.CustomMode, c.IntervalCount, c.IntervalUnit,
        c.Weekdays, c.DaysOfMonth, c.Months);
}
