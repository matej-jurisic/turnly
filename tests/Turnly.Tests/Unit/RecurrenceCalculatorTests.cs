using Turnly.Core.Enums;
using Turnly.Core.Recurrence;

namespace Turnly.Tests.Unit;

public class RecurrenceCalculatorTests
{
    private static readonly DateTimeOffset Wed = new(2026, 6, 17, 9, 0, 0, TimeSpan.Zero); // Wednesday

    private static DateTimeOffset? Next(RecurrenceRule rule, DateTimeOffset scheduledDue,
        SchedulingPreference pref = SchedulingPreference.FromScheduledDate,
        DateTimeOffset? completedAt = null, DateTimeOffset? now = null, TimeSpan? grace = null) =>
        RecurrenceCalculator.NextDue(rule, pref, scheduledDue, completedAt ?? scheduledDue, now ?? scheduledDue, grace);

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
    public void DaysOfWeek_restricts_to_selected_occurrences_in_the_month()
    {
        // June 2026 Mondays: 1, 8, 15, 22, 29. "1st & 3rd Monday" → from Wed Jun 17 the next
        // matching slot skips Jun 22 (4th) and Jun 29 (last) to land on Jul 6 (1st Monday).
        var rule = new RecurrenceRule(RepeatType.Custom, CustomRecurrenceMode.DaysOfWeek,
            Weekdays: new[] { DayOfWeek.Monday }, WeeksOfMonth: new[] { 1, 3 });
        Assert.Equal(new DateTimeOffset(2026, 7, 6, 9, 0, 0, TimeSpan.Zero), Next(rule, Wed));
    }

    [Fact]
    public void DaysOfWeek_last_occurrence_picks_the_final_weekday_of_the_month()
    {
        // "Last Monday" → from Wed Jun 17, Jun 22 isn't last but Jun 29 is.
        var rule = new RecurrenceRule(RepeatType.Custom, CustomRecurrenceMode.DaysOfWeek,
            Weekdays: new[] { DayOfWeek.Monday }, WeeksOfMonth: new[] { -1 });
        Assert.Equal(new DateTimeOffset(2026, 6, 29, 9, 0, 0, TimeSpan.Zero), Next(rule, Wed));
    }

    [Fact]
    public void DaysOfWeek_to_first_next_repeat_advances_past_the_due_date_when_completed_early()
    {
        // "Last Sunday" due Jun 28; completed a week early on Jun 21. ToFirstNextRepeat must still
        // advance off the due date (not snap back to Jun 28) → next last Sunday is Jul 26.
        var rule = new RecurrenceRule(RepeatType.Custom, CustomRecurrenceMode.DaysOfWeek,
            Weekdays: new[] { DayOfWeek.Sunday }, WeeksOfMonth: new[] { -1 });
        var due = new DateTimeOffset(2026, 6, 28, 9, 0, 0, TimeSpan.Zero);
        var early = new DateTimeOffset(2026, 6, 21, 9, 0, 0, TimeSpan.Zero);
        var next = Next(rule, due, SchedulingPreference.ToFirstNextRepeat, completedAt: early, now: early);
        Assert.Equal(new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void DaysOfWeek_to_first_next_repeat_skips_missed_occurrences_when_overdue()
    {
        // "Last Sunday" was due back in January but only completed now (Jun 21) — it should skip the
        // missed occurrences to the first future slot, Jun 28.
        var rule = new RecurrenceRule(RepeatType.Custom, CustomRecurrenceMode.DaysOfWeek,
            Weekdays: new[] { DayOfWeek.Sunday }, WeeksOfMonth: new[] { -1 });
        var due = new DateTimeOffset(2026, 1, 25, 9, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 6, 21, 9, 0, 0, TimeSpan.Zero);
        var next = Next(rule, due, SchedulingPreference.ToFirstNextRepeat, completedAt: now, now: now);
        Assert.Equal(new DateTimeOffset(2026, 6, 28, 9, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void DaysOfWeek_empty_weeks_still_means_every_week()
    {
        // No occurrence restriction → behaves like the plain weekday schedule (next Monday Jun 22).
        var rule = new RecurrenceRule(RepeatType.Custom, CustomRecurrenceMode.DaysOfWeek,
            Weekdays: new[] { DayOfWeek.Monday });
        Assert.Equal(new DateTimeOffset(2026, 6, 22, 9, 0, 0, TimeSpan.Zero), Next(rule, Wed));
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

    // ── SmartScheduling: max(FromScheduledDate, FromCompletionDate), with optional grace ──────

    [Fact]
    public void Smart_late_completion_rests_a_full_interval_from_completion()
    {
        // Weekly chore due Wed, completed 5 days late. FromScheduled would land it only 2 days out;
        // Smart instead keeps a full week of rest from the actual completion.
        var rule = new RecurrenceRule(RepeatType.Weekly);
        var completed = Wed.AddDays(5);
        var next = Next(rule, Wed, SchedulingPreference.SmartScheduling, completedAt: completed, now: completed);
        Assert.Equal(completed.AddDays(7), next);
    }

    [Fact]
    public void Smart_early_completion_holds_the_grid()
    {
        // Weekly chore due Wed, completed 2 days early. Smart keeps the planned Wed cadence rather
        // than drifting earlier (which pure FromCompletionDate would do).
        var rule = new RecurrenceRule(RepeatType.Weekly);
        var completed = Wed.AddDays(-2);
        var next = Next(rule, Wed, SchedulingPreference.SmartScheduling, completedAt: completed, now: completed);
        Assert.Equal(Wed.AddDays(7), next);
    }

    [Fact]
    public void Smart_on_time_completion_advances_one_interval()
    {
        var rule = new RecurrenceRule(RepeatType.Weekly);
        Assert.Equal(Wed.AddDays(7), Next(rule, Wed, SchedulingPreference.SmartScheduling));
    }

    [Fact]
    public void Smart_within_grace_holds_the_grid()
    {
        // Completed 1 day early with a 2-day grace → still treated as on-schedule, keep the grid.
        var rule = new RecurrenceRule(RepeatType.Weekly);
        var completed = Wed.AddDays(-1);
        var next = Next(rule, Wed, SchedulingPreference.SmartScheduling,
            completedAt: completed, now: completed, grace: TimeSpan.FromDays(2));
        Assert.Equal(Wed.AddDays(7), next);
    }

    [Fact]
    public void Smart_beyond_grace_resets_from_completion()
    {
        // Completed 6 days early with a 2-day grace → genuine early completion, reset the cadence
        // from completion (avoids the ~13-day gap holding the grid would create).
        var rule = new RecurrenceRule(RepeatType.Weekly);
        var completed = Wed.AddDays(-6);
        var next = Next(rule, Wed, SchedulingPreference.SmartScheduling,
            completedAt: completed, now: completed, grace: TimeSpan.FromDays(2));
        Assert.Equal(completed.AddDays(7), next);
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

    // ── Time-of-day is fixed by the schedule and never follows the completion time ────────────

    [Fact]
    public void FromCompletionDate_keeps_the_scheduled_time_of_day()
    {
        // Chore due at 18:00; completed at 09:15 the same day. The next due must stay at 18:00,
        // not drift to 09:15 (which plain interval-stepping off the completion instant would do).
        var due = new DateTimeOffset(2026, 6, 17, 18, 0, 0, TimeSpan.Zero);
        var completed = new DateTimeOffset(2026, 6, 17, 9, 15, 0, TimeSpan.Zero);
        var next = Next(new RecurrenceRule(RepeatType.Daily), due,
            SchedulingPreference.FromCompletionDate, completedAt: completed, now: completed);
        Assert.Equal(new DateTimeOffset(2026, 6, 18, 18, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Smart_late_completion_keeps_the_scheduled_time_of_day()
    {
        // Weekly chore due 18:00, completed 5 days late at 09:15: Smart rests a full week from the
        // completion *date*, but the time-of-day stays pinned to 18:00.
        var due = new DateTimeOffset(2026, 6, 17, 18, 0, 0, TimeSpan.Zero);
        var completed = new DateTimeOffset(2026, 6, 22, 9, 15, 0, TimeSpan.Zero);
        var next = Next(new RecurrenceRule(RepeatType.Weekly), due,
            SchedulingPreference.SmartScheduling, completedAt: completed, now: completed);
        Assert.Equal(new DateTimeOffset(2026, 6, 29, 18, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void FromCompletionDate_keeps_scheduled_time_across_utc_offset()
    {
        // Chore due 18:00 at +02:00, completed late evening UTC (early-morning local next day). The
        // next due must land on the correct local date at 18:00 +02:00, not at the UTC completion time.
        var due = new DateTimeOffset(2026, 6, 17, 18, 0, 0, TimeSpan.FromHours(2));
        var completed = new DateTimeOffset(2026, 6, 17, 23, 0, 0, TimeSpan.Zero); // 2026-06-18 01:00 +02:00
        var next = Next(new RecurrenceRule(RepeatType.Daily), due,
            SchedulingPreference.FromCompletionDate, completedAt: completed, now: completed);
        Assert.Equal(new DateTimeOffset(2026, 6, 19, 18, 0, 0, TimeSpan.FromHours(2)), next);
    }

    // ── The 5-timing comparison matrix ────────────────────────────────────────────────────────
    // Daily chore, the scheduled occurrence (D0) due at end-of-day (23:00); completions land at
    // mid-day (12:00) so each completion is always before that day's slot. Each InlineData asserts
    // one cell of the comparison table: (completionDayOffset → expected next-due day offset from D0).

    private static readonly DateTimeOffset DueEod = new(2026, 6, 17, 23, 0, 0, TimeSpan.Zero); // D0 @ end of day

    private static DateTimeOffset MidDay(int dayOffset) => new(2026, 6, 17 + dayOffset, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset NextForTiming(SchedulingPreference pref, int completionDayOffset, TimeSpan? grace = null)
    {
        var completed = MidDay(completionDayOffset); // now == completedAt (completion is the current moment)
        return Next(new RecurrenceRule(RepeatType.Daily), DueEod, pref,
            completedAt: completed, now: completed, grace: grace)!.Value;
    }

    [Theory]
    [InlineData(-3, 1)] // early 3d
    [InlineData(-1, 1)] // early 1d
    [InlineData(0, 1)]  // on time
    [InlineData(1, 1)]  // late 1d
    [InlineData(3, 1)]  // late 3d
    public void Matrix_FromScheduledDate_always_advances_one_day_off_the_grid(int completed, int expected)
    {
        // Rigid grid: completion timing is ignored, next due is always D+1.
        Assert.Equal(DueEod.AddDays(expected), NextForTiming(SchedulingPreference.FromScheduledDate, completed));
    }

    [Theory]
    [InlineData(-3, -2)] // early 3d → completion+1
    [InlineData(-1, 0)]  // early 1d → next due is the original D0 slot tonight
    [InlineData(0, 1)]   // on time
    [InlineData(1, 2)]   // late 1d
    [InlineData(3, 4)]   // late 3d
    public void Matrix_FromCompletionDate_is_always_one_day_after_completion(int completed, int expected)
    {
        // Pure drift: next due tracks the actual completion, one interval out.
        Assert.Equal(DueEod.AddDays(expected), NextForTiming(SchedulingPreference.FromCompletionDate, completed));
    }

    [Theory]
    [InlineData(-3, 1)] // early 3d → next grid slot, never earlier than D+1
    [InlineData(-1, 1)] // early 1d
    [InlineData(0, 1)]  // on time
    [InlineData(1, 1)]  // late 1d → the very next upcoming slot (still ahead today)
    [InlineData(3, 3)]  // late 3d → catches up to today's slot, skipping the missed ones
    public void Matrix_ToFirstNextRepeat_is_the_next_slot_strictly_after_completion(int completed, int expected)
    {
        // First scheduled slot strictly after `now`; mid-day completion is before that day's
        // end-of-day slot, so a late finish lands on the same day's upcoming occurrence.
        Assert.Equal(DueEod.AddDays(expected), NextForTiming(SchedulingPreference.ToFirstNextRepeat, completed));
    }

    [Theory]
    [InlineData(-3, 1)] // early 3d → holds the grid
    [InlineData(-1, 1)] // early 1d → holds the grid
    [InlineData(0, 1)]  // on time
    [InlineData(1, 2)]  // late 1d → a full interval past completion
    [InlineData(3, 4)]  // late 3d → a full interval past completion
    public void Matrix_SmartScheduling_holds_grid_when_early_and_pushes_out_when_late(int completed, int expected)
    {
        // max(FromScheduledDate, FromCompletionDate): grid for on-time-or-early, completion+interval
        // when late. This is where it diverges from ToFirstNextRepeat (D+2/D+4 vs D+1/D+3).
        Assert.Equal(DueEod.AddDays(expected), NextForTiming(SchedulingPreference.SmartScheduling, completed));
    }

    [Theory]
    [InlineData(-3, -2)] // early 3d: gap ~3.5d > 2d grace → reset from completion
    [InlineData(-1, 1)]  // early 1d: gap ~1.5d < 2d grace → still holds the grid
    [InlineData(1, 2)]   // late 1d: grace never applies to late completions → normal Smart
    public void Matrix_SmartScheduling_with_two_day_grace_only_resets_when_genuinely_early(int completed, int expected)
    {
        Assert.Equal(DueEod.AddDays(expected),
            NextForTiming(SchedulingPreference.SmartScheduling, completed, grace: TimeSpan.FromDays(2)));
    }

    [Fact]
    public void Matrix_SmartScheduling_one_day_grace_trips_on_the_one_day_early_completion()
    {
        // Sharp edge: end-of-day schedule vs mid-day completion makes the "1 day early" gap ~1.5d,
        // so a whole-day (1d) grace trips it → resets to completion+1 (D0) instead of the grid (D+1).
        Assert.Equal(DueEod.AddDays(0),
            NextForTiming(SchedulingPreference.SmartScheduling, -1, grace: TimeSpan.FromDays(1)));
    }

    // ── Fixed times-of-day: multiple slots per day (e.g. "feed the dog at 08:00 and 20:00") ──────

    private static readonly TimeOnly[] EightAndEight = { new(8, 0), new(20, 0) };
    private static DateTimeOffset At(int year, int month, int day, int hour) => new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TimesOfDay_daily_advances_to_the_next_time_slot_the_same_day()
    {
        // Due at 08:00; completed on time → the next occurrence is 20:00 that same day, not tomorrow.
        var rule = new RecurrenceRule(RepeatType.Daily, TimesOfDay: EightAndEight);
        Assert.Equal(At(2026, 6, 17, 20), Next(rule, At(2026, 6, 17, 8)));
    }

    [Fact]
    public void TimesOfDay_daily_rolls_to_the_first_slot_next_day_after_the_last_slot()
    {
        // After the day's final slot (20:00) the next is the first slot (08:00) tomorrow.
        var rule = new RecurrenceRule(RepeatType.Daily, TimesOfDay: EightAndEight);
        Assert.Equal(At(2026, 6, 18, 8), Next(rule, At(2026, 6, 17, 20)));
    }

    [Fact]
    public void TimesOfDay_are_sorted_so_input_order_does_not_matter()
    {
        var rule = new RecurrenceRule(RepeatType.Daily, TimesOfDay: new TimeOnly[] { new(20, 0), new(8, 0) });
        Assert.Equal(At(2026, 6, 17, 20), Next(rule, At(2026, 6, 17, 8)));
    }

    [Fact]
    public void TimesOfDay_first_occurrence_lands_on_the_first_slot_on_or_after_start()
    {
        var rule = new RecurrenceRule(RepeatType.Daily, TimesOfDay: EightAndEight);
        Assert.Equal(At(2026, 6, 17, 8), RecurrenceCalculator.FirstOccurrence(rule, At(2026, 6, 17, 6)));
        Assert.Equal(At(2026, 6, 17, 20), RecurrenceCalculator.FirstOccurrence(rule, At(2026, 6, 17, 12)));
        Assert.Equal(At(2026, 6, 18, 8), RecurrenceCalculator.FirstOccurrence(rule, At(2026, 6, 17, 21)));
    }

    [Fact]
    public void TimesOfDay_from_completion_date_picks_the_next_slot_after_completion()
    {
        // Fed the 08:00 slot late at 09:30 → next is the same day's 20:00 slot.
        var rule = new RecurrenceRule(RepeatType.Daily, TimesOfDay: EightAndEight);
        var completed = new DateTimeOffset(2026, 6, 17, 9, 30, 0, TimeSpan.Zero);
        Assert.Equal(At(2026, 6, 17, 20),
            Next(rule, At(2026, 6, 17, 8), SchedulingPreference.FromCompletionDate, completedAt: completed, now: completed));
    }

    [Fact]
    public void TimesOfDay_to_first_next_repeat_skips_missed_slots_to_the_next_future_one()
    {
        // Due 08:00 yesterday, only dealt with now (next day 12:00): skip the missed slots to the
        // next future slot, today's 20:00.
        var rule = new RecurrenceRule(RepeatType.Daily, TimesOfDay: EightAndEight);
        var now = At(2026, 6, 18, 12);
        Assert.Equal(At(2026, 6, 18, 20),
            Next(rule, At(2026, 6, 17, 8), SchedulingPreference.ToFirstNextRepeat, completedAt: now, now: now));
    }

    [Fact]
    public void TimesOfDay_combine_with_days_of_week()
    {
        // "Mondays at 08:00 and 20:00": from Monday 20:00 the next slot is the following Monday 08:00.
        var rule = new RecurrenceRule(RepeatType.Custom, CustomRecurrenceMode.DaysOfWeek,
            Weekdays: new[] { DayOfWeek.Monday }, TimesOfDay: EightAndEight);
        var mondayEvening = At(2026, 6, 22, 20); // Mon Jun 22
        Assert.Equal(At(2026, 6, 29, 8), Next(rule, mondayEvening)); // Mon Jun 29
    }

    [Fact]
    public void TimesOfDay_days_of_week_advances_within_the_same_day_before_rolling_to_next_match()
    {
        var rule = new RecurrenceRule(RepeatType.Custom, CustomRecurrenceMode.DaysOfWeek,
            Weekdays: new[] { DayOfWeek.Monday }, TimesOfDay: EightAndEight);
        Assert.Equal(At(2026, 6, 22, 20), Next(rule, At(2026, 6, 22, 8))); // same Monday, 08:00 → 20:00
    }
}
