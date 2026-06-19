using Turnly.Core.Enums;
using Turnly.Core.Recurrence;

namespace Turnly.Tests.Unit;

public class RecurrenceCalculatorTests
{
    private static readonly DateTimeOffset Wed = new(2026, 6, 17, 9, 0, 0, TimeSpan.Zero); // Wednesday

    [Fact]
    public void OneTime_has_no_next_occurrence()
    {
        Assert.Null(RecurrenceCalculator.Next(RepeatType.OneTime, [], Wed));
    }

    [Fact]
    public void Daily_advances_one_day()
    {
        Assert.Equal(Wed.AddDays(1), RecurrenceCalculator.Next(RepeatType.Daily, [], Wed));
    }

    [Fact]
    public void Monthly_advances_one_month_and_clamps_month_end()
    {
        var jan31 = new DateTimeOffset(2026, 1, 31, 8, 0, 0, TimeSpan.Zero);
        var next = RecurrenceCalculator.Next(RepeatType.Monthly, [], jan31);
        Assert.Equal(new DateTimeOffset(2026, 2, 28, 8, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Yearly_advances_one_year()
    {
        Assert.Equal(Wed.AddYears(1), RecurrenceCalculator.Next(RepeatType.Yearly, [], Wed));
    }

    [Fact]
    public void Weekly_advances_seven_days()
    {
        var next = RecurrenceCalculator.Next(RepeatType.Weekly, [], Wed);
        Assert.Equal(Wed.AddDays(7), next);
    }
}
