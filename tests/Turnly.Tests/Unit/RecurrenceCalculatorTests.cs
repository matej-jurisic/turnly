using Turnly.Core.Enums;
using Turnly.Core.Recurrence;

namespace Turnly.Tests.Unit;

public class RecurrenceCalculatorTests
{
    private static readonly DateTimeOffset Wed = new(2026, 6, 17, 9, 0, 0, TimeSpan.Zero); // Wednesday

    private static DateTimeOffset? Next(RecurrenceRule rule, DateTimeOffset scheduledDue,
        SchedulingPreference pref = SchedulingPreference.FromScheduledDate,
        DateTimeOffset? completedAt = null, DateTimeOffset? now = null) =>
        RecurrenceCalculator.NextDue(rule, pref, scheduledDue, completedAt ?? scheduledDue, now ?? scheduledDue);

    [Fact]
    public void OneTime_has_no_next_occurrence()
    {
        Assert.Null(Next(new RecurrenceRule(RepeatType.OneTime), Wed));
    }

    [Fact]
    public void Daily_advances_one_day()
    {
        Assert.Equal(Wed.AddDays(1), Next(new RecurrenceRule(RepeatType.Daily), Wed));
    }

    [Fact]
    public void Weekly_advances_seven_days()
    {
        Assert.Equal(Wed.AddDays(7), Next(new RecurrenceRule(RepeatType.Weekly), Wed));
    }

    [Fact]
    public void Monthly_advances_one_month_and_clamps_month_end()
    {
        var jan31 = new DateTimeOffset(2026, 1, 31, 8, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 2, 28, 8, 0, 0, TimeSpan.Zero),
            Next(new RecurrenceRule(RepeatType.Monthly), jan31));
    }

    [Fact]
    public void Yearly_advances_one_year()
    {
        Assert.Equal(Wed.AddYears(1), Next(new RecurrenceRule(RepeatType.Yearly), Wed));
    }

    [Fact]
    public void Custom_interval_advances_by_count_and_unit()
    {
        var rule = new RecurrenceRule(RepeatType.Custom, CustomRecurrenceMode.Interval,
            IntervalCount: 2, IntervalUnit: RecurrenceUnit.Week);
        Assert.Equal(Wed.AddDays(14), Next(rule, Wed));
    }

    [Fact]
    public void DaysOfWeek_finds_next_selected_weekday()
    {
        // From Wednesday, next Mon/Fri slot is Friday.
        var rule = new RecurrenceRule(RepeatType.Custom, CustomRecurrenceMode.DaysOfWeek,
            Weekdays: new[] { DayOfWeek.Monday, DayOfWeek.Friday });
        Assert.Equal(new DateTimeOffset(2026, 6, 19, 9, 0, 0, TimeSpan.Zero), Next(rule, Wed));
    }

    [Fact]
    public void DaysOfMonth_skips_months_and_impossible_days()
    {
        // Day 31 only in months with 31 days; from mid-June the next is Jul 31.
        var rule = new RecurrenceRule(RepeatType.Custom, CustomRecurrenceMode.DaysOfMonth,
            DaysOfMonth: new[] { 31 }, Months: new[] { 6, 7 });
        Assert.Equal(new DateTimeOffset(2026, 7, 31, 9, 0, 0, TimeSpan.Zero), Next(rule, Wed));
    }

    [Fact]
    public void DaysOfMonth_resolves_feb_29_in_the_next_leap_year()
    {
        // Day 29 in February only is a legitimate every-leap-year chore.
        var rule = new RecurrenceRule(RepeatType.Custom, CustomRecurrenceMode.DaysOfMonth,
            DaysOfMonth: new[] { 29 }, Months: new[] { 2 });
        Assert.Equal(new DateTimeOffset(2028, 2, 29, 9, 0, 0, TimeSpan.Zero),
            RecurrenceCalculator.FirstOccurrence(rule, Wed));
    }

    [Fact]
    public void DaysOfMonth_resolves_feb_29_across_an_eight_year_gap()
    {
        // 2100 is not a leap year, so from 2096 the next Feb 29 is 8 years out — this only
        // resolves because the scan cap clears that gap.
        var rule = new RecurrenceRule(RepeatType.Custom, CustomRecurrenceMode.DaysOfMonth,
            DaysOfMonth: new[] { 29 }, Months: new[] { 2 });
        var start = new DateTimeOffset(2096, 3, 1, 9, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2104, 2, 29, 9, 0, 0, TimeSpan.Zero),
            RecurrenceCalculator.FirstOccurrence(rule, start));
    }

    [Fact]
    public void ToFirstNextRepeat_skips_missed_occurrences()
    {
        // Daily chore last due long ago; completing "to first next repeat" jumps past now.
        var rule = new RecurrenceRule(RepeatType.Daily);
        var now = Wed.AddDays(10).AddHours(1);
        var next = Next(rule, Wed, SchedulingPreference.ToFirstNextRepeat, completedAt: now, now: now);
        Assert.True(next > now);
        Assert.Equal(Wed.AddDays(11), next); // first daily slot strictly after now
    }

    [Fact]
    public void FirstOccurrence_for_fixed_slot_lands_on_or_after_start()
    {
        // Start is a Wednesday; first Mon/Fri slot on/after is the same-week Friday.
        var rule = new RecurrenceRule(RepeatType.Custom, CustomRecurrenceMode.DaysOfWeek,
            Weekdays: new[] { DayOfWeek.Monday, DayOfWeek.Friday });
        Assert.Equal(new DateTimeOffset(2026, 6, 19, 9, 0, 0, TimeSpan.Zero),
            RecurrenceCalculator.FirstOccurrence(rule, Wed));
    }

    [Fact]
    public void FirstOccurrence_for_interval_is_the_start()
    {
        Assert.Equal(Wed, RecurrenceCalculator.FirstOccurrence(new RecurrenceRule(RepeatType.Daily), Wed));
    }
}
