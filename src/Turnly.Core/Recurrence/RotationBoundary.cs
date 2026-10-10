using Turnly.Core.Enums;

namespace Turnly.Core.Recurrence;

/// <summary>Pure check for whether closing one occurrence and opening the next crosses the chore's
/// <see cref="RotationPeriod"/> boundary, i.e. whether the assignee should hand over.</summary>
public static class RotationBoundary
{
    /// <summary>Compares the two due dates in their own stored offsets (the creating client's local
    /// time), so "the month" is the household's calendar month rather than UTC's. Stateless: a jump of
    /// several months still counts as a single handover.</summary>
    public static bool Crossed(RotationPeriod period, DateTimeOffset closedDue, DateTimeOffset nextDue) => period switch
    {
        RotationPeriod.Monthly => (closedDue.Year, closedDue.Month) != (nextDue.Year, nextDue.Month),
        _ => true,
    };
}
