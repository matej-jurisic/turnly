using Turnly.Core.Enums;
using Turnly.Core.Recurrence;

namespace Turnly.Tests.Unit;

public class RotationBoundaryTests
{
    private static DateTimeOffset At(int year, int month, int day, int hour = 9, int offsetHours = 0) =>
        new(year, month, day, hour, 0, 0, TimeSpan.FromHours(offsetHours));

    [Fact]
    public void EveryOccurrence_always_crosses()
    {
        Assert.True(RotationBoundary.Crossed(RotationPeriod.EveryOccurrence, At(2026, 6, 17), At(2026, 6, 24)));
    }

    [Fact]
    public void Monthly_holds_within_the_same_month()
    {
        Assert.False(RotationBoundary.Crossed(RotationPeriod.Monthly, At(2026, 6, 1), At(2026, 6, 30)));
    }

    [Fact]
    public void Monthly_crosses_into_the_next_month()
    {
        Assert.True(RotationBoundary.Crossed(RotationPeriod.Monthly, At(2026, 6, 24), At(2026, 7, 1)));
    }

    [Fact]
    public void Monthly_crosses_between_the_same_month_of_different_years()
    {
        Assert.True(RotationBoundary.Crossed(RotationPeriod.Monthly, At(2026, 6, 24), At(2027, 6, 24)));
    }

    [Fact]
    public void Monthly_uses_the_stored_local_offset_not_utc()
    {
        // 23:00 on Jun 30 at +02:00 is still June locally, even though the next due (01:00 on Jul 1
        // at +02:00) is 23:00 on Jun 30 in UTC.
        Assert.True(RotationBoundary.Crossed(RotationPeriod.Monthly,
            At(2026, 6, 30, 23, offsetHours: 2), At(2026, 7, 1, 1, offsetHours: 2)));
    }
}
