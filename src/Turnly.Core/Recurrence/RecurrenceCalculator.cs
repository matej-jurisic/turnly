using Turnly.Core.Enums;

namespace Turnly.Core.Recurrence;

/// <summary>
/// Pure recurrence math for Phase 2's basic repeat types. Given the occurrence that was just
/// completed, returns the next occurrence's due date — or null when nothing follows
/// (one-time chores). Uses the "from scheduled date" rule; scheduling preferences and custom
/// recurrence are Phase 3.
/// </summary>
public static class RecurrenceCalculator
{
    /// <param name="weekdays">Selected weekdays; only used for <see cref="RepeatType.Weekly"/>.</param>
    /// <param name="current">The scheduled due date of the occurrence that was completed.</param>
    public static DateTimeOffset? Next(RepeatType type, IReadOnlyCollection<DayOfWeek> weekdays, DateTimeOffset current) =>
        type switch
        {
            RepeatType.OneTime => null,
            RepeatType.Daily => current.AddDays(1),
            RepeatType.Weekly => NextWeekday(weekdays, current),
            RepeatType.Monthly => current.AddMonths(1),
            RepeatType.Yearly => current.AddYears(1),
            _ => null
        };

    /// <summary>The next selected weekday strictly after <paramref name="current"/>,
    /// preserving the time of day. Falls back to +7 days when no weekdays are selected.</summary>
    private static DateTimeOffset NextWeekday(IReadOnlyCollection<DayOfWeek> weekdays, DateTimeOffset current)
    {
        if (weekdays is null || weekdays.Count == 0)
            return current.AddDays(7);

        for (var offset = 1; offset <= 7; offset++)
        {
            var candidate = current.AddDays(offset);
            if (weekdays.Contains(candidate.DayOfWeek))
                return candidate;
        }

        return current.AddDays(7);
    }
}
