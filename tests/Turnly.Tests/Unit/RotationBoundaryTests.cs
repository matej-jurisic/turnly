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
    public void Daily_holds_within_the_day_and_crosses_overnight()
    {
        Assert.False(RotationBoundary.Crossed(RotationPeriod.Daily, At(2026, 6, 17, 8), At(2026, 6, 17, 20)));
        Assert.True(RotationBoundary.Crossed(RotationPeriod.Daily, At(2026, 6, 17, 20), At(2026, 6, 18, 8)));
    }

    [Fact]
    public void Weekly_holds_monday_to_sunday_and_crosses_on_monday()
    {
        // 2026-06-15 is a Monday, 2026-06-21 the Sunday that closes its week.
        Assert.False(RotationBoundary.Crossed(RotationPeriod.Weekly, At(2026, 6, 15), At(2026, 6, 21)));
        Assert.True(RotationBoundary.Crossed(RotationPeriod.Weekly, At(2026, 6, 21), At(2026, 6, 22)));
    }

    [Fact]
    public void Weekly_holds_across_a_year_end_inside_one_week()
    {
        // Thu 2026-12-31 and Fri 2027-01-01 share a week.
        Assert.False(RotationBoundary.Crossed(RotationPeriod.Weekly, At(2026, 12, 31), At(2027, 1, 1)));
    }

    [Fact]
    public void Quarterly_holds_within_the_quarter_and_crosses_into_the_next()
    {
        Assert.False(RotationBoundary.Crossed(RotationPeriod.Quarterly, At(2026, 4, 1), At(2026, 6, 30)));
        Assert.True(RotationBoundary.Crossed(RotationPeriod.Quarterly, At(2026, 6, 30), At(2026, 7, 1)));
        Assert.True(RotationBoundary.Crossed(RotationPeriod.Quarterly, At(2026, 5, 1), At(2027, 5, 1)));
    }

    [Fact]
    public void Yearly_holds_within_the_year_and_crosses_into_the_next()
    {
        Assert.False(RotationBoundary.Crossed(RotationPeriod.Yearly, At(2026, 1, 1), At(2026, 12, 31)));
        Assert.True(RotationBoundary.Crossed(RotationPeriod.Yearly, At(2026, 12, 31), At(2027, 1, 1)));
    }

    [Theory]
    [InlineData(RotationPeriod.Daily, RepeatType.Daily, true)]
    [InlineData(RotationPeriod.Daily, RepeatType.Weekly, false)]
    [InlineData(RotationPeriod.Weekly, RepeatType.Custom, true)]
    [InlineData(RotationPeriod.Weekly, RepeatType.Weekly, false)]
    [InlineData(RotationPeriod.Monthly, RepeatType.Weekly, true)]
    [InlineData(RotationPeriod.Monthly, RepeatType.Monthly, false)]
    [InlineData(RotationPeriod.Quarterly, RepeatType.Monthly, true)]
    [InlineData(RotationPeriod.Yearly, RepeatType.Monthly, true)]
    [InlineData(RotationPeriod.Yearly, RepeatType.Yearly, false)]
    [InlineData(RotationPeriod.Yearly, RepeatType.OneTime, false)]
    public void AppliesTo_requires_a_repeat_faster_than_the_period(RotationPeriod period, RepeatType repeat, bool expected)
    {
        Assert.Equal(expected, RotationBoundary.AppliesTo(period, repeat));
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
