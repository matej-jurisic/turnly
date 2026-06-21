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
    IReadOnlyCollection<int>? Months = null,
    IReadOnlyCollection<int>? WeeksOfMonth = null,
    IReadOnlyCollection<TimeOnly>? TimesOfDay = null)
{
    public IReadOnlyCollection<DayOfWeek> Weekdays { get; init; } = Weekdays ?? Array.Empty<DayOfWeek>();
    public IReadOnlyCollection<int> DaysOfMonth { get; init; } = DaysOfMonth ?? Array.Empty<int>();
    public IReadOnlyCollection<int> Months { get; init; } = Months ?? Array.Empty<int>();

    /// <summary>Restricts <see cref="CustomRecurrenceMode.DaysOfWeek"/> to specific occurrences within
    /// each month: 1–4 for the nth weekday, -1 for the last. Empty means every week.</summary>
    public IReadOnlyCollection<int> WeeksOfMonth { get; init; } = WeeksOfMonth ?? Array.Empty<int>();

    /// <summary>Fixed times-of-day the chore is due on each qualifying day — e.g. 08:00 and 20:00 for
    /// "twice a day". Each time is its own occurrence. Empty means a single daily slot at the
    /// schedule's own time-of-day. Only meaningful for day-resolution schedules (Daily and the custom
    /// DaysOfWeek / DaysOfMonth calendar modes); see <see cref="RecurrenceCalculator"/>.</summary>
    public IReadOnlyCollection<TimeOnly> TimesOfDay { get; init; } = TimesOfDay ?? Array.Empty<TimeOnly>();

    public static RecurrenceRule FromChore(Chore c) => new(
        c.RepeatType, c.CustomMode, c.IntervalCount, c.IntervalUnit,
        c.Weekdays, c.DaysOfMonth, c.Months, c.WeeksOfMonth, c.TimesOfDay);
}
