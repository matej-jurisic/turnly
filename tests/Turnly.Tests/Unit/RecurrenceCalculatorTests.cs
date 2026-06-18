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
    public void Weekly_picks_next_selected_weekday()
    {
        // From Wednesday, next selected day is Friday.
        var next = RecurrenceCalculator.Next(RepeatType.Weekly, [DayOfWeek.Monday, DayOfWeek.Friday], Wed);
        Assert.Equal(DayOfWeek.Friday, next!.Value.DayOfWeek);
        Assert.Equal(Wed.AddDays(2), next);
    }

    [Fact]
    public void Weekly_wraps_to_next_week_when_no_later_day_this_week()
    {
        // From Wednesday with only Monday selected, the next is the following Monday.
        var next = RecurrenceCalculator.Next(RepeatType.Weekly, [DayOfWeek.Monday], Wed);
        Assert.Equal(DayOfWeek.Monday, next!.Value.DayOfWeek);
        Assert.Equal(Wed.AddDays(5), next);
    }

    [Fact]
    public void Weekly_preserves_time_of_day()
    {
        var next = RecurrenceCalculator.Next(RepeatType.Weekly, [DayOfWeek.Friday], Wed);
        Assert.Equal(Wed.TimeOfDay, next!.Value.TimeOfDay);
    }
}
