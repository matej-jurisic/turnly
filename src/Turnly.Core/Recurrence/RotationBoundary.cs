using Turnly.Core.Enums;

namespace Turnly.Core.Recurrence;

/// <summary>Pure check for whether closing one occurrence and opening the next crosses the chore's
/// <see cref="RotationPeriod"/> boundary, i.e. whether the assignee should hand over.</summary>
public static class RotationBoundary
{
    /// <summary>Compares the two due dates in their own stored offsets (the creating client's local
    /// time), so "the month" is the household's calendar month rather than UTC's. Stateless: a jump of
    /// several periods still counts as a single handover.</summary>
    public static bool Crossed(RotationPeriod period, DateTimeOffset closedDue, DateTimeOffset nextDue) => period switch
    {
        RotationPeriod.Daily => closedDue.Date != nextDue.Date,
        RotationPeriod.Weekly => WeekStart(closedDue) != WeekStart(nextDue),
        RotationPeriod.Monthly => (closedDue.Year, closedDue.Month) != (nextDue.Year, nextDue.Month),
        RotationPeriod.Quarterly => (closedDue.Year, Quarter(closedDue)) != (nextDue.Year, Quarter(nextDue)),
        RotationPeriod.Yearly => closedDue.Year != nextDue.Year,
        _ => true,
    };

    /// <summary>Whether a period can mean anything on a repeat type: the chore has to be able to come
    /// due more than once inside the period, otherwise it's just "every occurrence" under another name.</summary>
    public static bool AppliesTo(RotationPeriod period, RepeatType repeatType) => period switch
    {
        RotationPeriod.EveryOccurrence => true,
        RotationPeriod.Daily or RotationPeriod.Weekly => repeatType is RepeatType.Daily or RepeatType.Custom,
        RotationPeriod.Monthly => repeatType is RepeatType.Daily or RepeatType.Weekly or RepeatType.Custom,
        RotationPeriod.Quarterly or RotationPeriod.Yearly =>
            repeatType is RepeatType.Daily or RepeatType.Weekly or RepeatType.Monthly or RepeatType.Custom,
        _ => false,
    };

    // Weeks run Monday to Sunday, matching the weekly leaderboard.
    private static DateTime WeekStart(DateTimeOffset due) =>
        due.Date.AddDays(-(((int)due.DayOfWeek + 6) % 7));

    private static int Quarter(DateTimeOffset due) => (due.Month - 1) / 3;
}
