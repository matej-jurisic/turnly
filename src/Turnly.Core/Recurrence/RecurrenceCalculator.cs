using Turnly.Core.Enums;

namespace Turnly.Core.Recurrence;

/// <summary>
/// Pure recurrence math for both the basic repeat types (Phase 2) and the custom modes plus
/// scheduling preferences (Phase 3). Given the occurrence that was just completed it returns the
/// next occurrence's due date — or null when nothing follows (one-time chores).
///
/// <para>A chore's "complete N times per occurrence" count is NOT handled here: it depends on how
/// many completions exist for the current occurrence, which is database state. That gate lives in
/// <c>ChoreService.AdvanceScheduleAsync</c>, which only calls into this calculator once the
/// occurrence has actually closed.</para>
/// </summary>
public static class RecurrenceCalculator
{
    // Guards against runaway loops if a rule is mis-configured (e.g. empty weekday set slips past
    // validation). ~10 years so the rarest reachable slot — day 29 in February, which can sit an
    // 8-year gap apart across a non-leap century year — always resolves.
    private const int MaxDaySteps = 3700;
    private const int MaxIntervalSteps = 10_000;

    /// <summary>The first due date on or after <paramref name="start"/> for the given rule.</summary>
    public static DateTimeOffset FirstOccurrence(RecurrenceRule rule, DateTimeOffset start)
    {
        if (IsFixedSlot(rule))
            return FixedSlotOnOrAfter(rule, start);
        // One-time and all interval-style schedules start exactly at the start date.
        return start;
    }

    /// <summary>The next due date after completing the <paramref name="scheduledDue"/> occurrence,
    /// honouring the scheduling preference. Returns null for one-time chores. <paramref name="grace"/>
    /// only applies to <see cref="SchedulingPreference.SmartScheduling"/>.</summary>
    public static DateTimeOffset? NextDue(
        RecurrenceRule rule,
        SchedulingPreference pref,
        DateTimeOffset scheduledDue,
        DateTimeOffset completedAt,
        DateTimeOffset now,
        TimeSpan? grace = null)
    {
        if (rule.Type == RepeatType.OneTime)
            return null;

        if (pref == SchedulingPreference.SmartScheduling)
        {
            // Hold the planned cadence, but never sooner than one interval after the actual
            // completion: the later of the grid-anchored and completion-anchored next dates.
            var fromCompletion = NextDueFor(rule, SchedulingPreference.FromCompletionDate, scheduledDue, completedAt, now);

            // Completed more than the grace window early → treat it as a genuine early completion and
            // reset from completion, rather than holding the grid (which would leave an over-long gap).
            if (grace is { } g && scheduledDue - completedAt > g)
                return fromCompletion;

            var fromScheduled = NextDueFor(rule, SchedulingPreference.FromScheduledDate, scheduledDue, completedAt, now);
            return fromScheduled >= fromCompletion ? fromScheduled : fromCompletion;
        }

        return NextDueFor(rule, pref, scheduledDue, completedAt, now);
    }

    /// <summary>The next due date for one of the three base preferences (everything except the
    /// composite <see cref="SchedulingPreference.SmartScheduling"/>). Never null — the one-time guard
    /// lives in <see cref="NextDue"/>.</summary>
    private static DateTimeOffset NextDueFor(
        RecurrenceRule rule,
        SchedulingPreference pref,
        DateTimeOffset scheduledDue,
        DateTimeOffset completedAt,
        DateTimeOffset now)
    {
        DateTimeOffset result;
        if (IsFixedSlot(rule))
        {
            // All base preferences reduce to "the next slot strictly after a base instant".
            var baseInstant = pref switch
            {
                SchedulingPreference.FromCompletionDate => completedAt,
                SchedulingPreference.ToFirstNextRepeat => now,
                _ => scheduledDue,
            };
            result = FixedSlotAfter(rule, scheduledDue, baseInstant);
        }
        else
        {
            // Interval-style (Daily / Weekly / Monthly / Yearly / Custom-Interval).
            switch (pref)
            {
                case SchedulingPreference.FromCompletionDate:
                    result = AddInterval(rule, completedAt);
                    break;
                case SchedulingPreference.ToFirstNextRepeat:
                    var next = AddInterval(rule, scheduledDue);
                    for (var i = 0; next <= now && i < MaxIntervalSteps; i++)
                        next = AddInterval(rule, next);
                    result = next;
                    break;
                default: // FromScheduledDate
                    result = AddInterval(rule, scheduledDue);
                    break;
            }
        }

        // The time-of-day is part of the schedule (a chore due at 18:00 stays due at 18:00) and must
        // never drift to the completion instant — which FromCompletionDate would otherwise do, since
        // it steps the interval off `completedAt` (= the moment "complete" was tapped, in UTC). Re-anchor
        // every computed date to the occurrence's set local time-of-day and offset.
        return WithScheduledTimeOfDay(result, scheduledDue);
    }

    /// <summary>Forces <paramref name="result"/> onto <paramref name="canonical"/>'s local time-of-day
    /// and UTC offset, keeping the calendar date as it falls in that offset. Fixed-slot results already
    /// carry the right time, so this is a no-op for them; it only corrects the interval-style paths that
    /// step off the completion instant.</summary>
    private static DateTimeOffset WithScheduledTimeOfDay(DateTimeOffset result, DateTimeOffset canonical)
    {
        var localDate = result.ToOffset(canonical.Offset).Date;
        return new DateTimeOffset(localDate.Add(canonical.TimeOfDay), canonical.Offset);
    }

    // ── Classification ────────────────────────────────────────────────────────────────────

    private static bool IsFixedSlot(RecurrenceRule rule) =>
        rule.Type == RepeatType.Custom &&
        rule.CustomMode is CustomRecurrenceMode.DaysOfWeek or CustomRecurrenceMode.DaysOfMonth;

    // ── Interval stepping ─────────────────────────────────────────────────────────────────

    /// <summary>Advances one interval. Monthly/Yearly clamp to the month end (Jan 31 → Feb 28).</summary>
    private static DateTimeOffset AddInterval(RecurrenceRule rule, DateTimeOffset d) => rule.Type switch
    {
        RepeatType.Daily => d.AddDays(1),
        RepeatType.Weekly => d.AddDays(7),
        RepeatType.Monthly => d.AddMonths(1),
        RepeatType.Yearly => d.AddYears(1),
        RepeatType.Custom => rule.IntervalUnit switch
        {
            RecurrenceUnit.Day => d.AddDays(rule.IntervalCount ?? 1),
            RecurrenceUnit.Week => d.AddDays(7 * (rule.IntervalCount ?? 1)),
            RecurrenceUnit.Month => d.AddMonths(rule.IntervalCount ?? 1),
            RecurrenceUnit.Year => d.AddYears(rule.IntervalCount ?? 1),
            _ => d.AddDays(rule.IntervalCount ?? 1),
        },
        _ => d.AddDays(1),
    };

    // ── Fixed-slot scanning ───────────────────────────────────────────────────────────────

    private static DateTimeOffset FixedSlotOnOrAfter(RecurrenceRule rule, DateTimeOffset anchor) =>
        ScanSlot(rule, anchor, anchor, inclusive: true);

    private static DateTimeOffset FixedSlotAfter(RecurrenceRule rule, DateTimeOffset anchor, DateTimeOffset baseInstant) =>
        ScanSlot(rule, anchor, baseInstant, inclusive: false);

    /// <summary>Scans forward day by day from <paramref name="baseInstant"/> for the next date that
    /// matches the rule's slot predicate. The result keeps <paramref name="anchor"/>'s time-of-day
    /// and offset so the chore stays due at a consistent time.</summary>
    private static DateTimeOffset ScanSlot(RecurrenceRule rule, DateTimeOffset anchor, DateTimeOffset baseInstant, bool inclusive)
    {
        var time = anchor.TimeOfDay;
        var offset = anchor.Offset;
        var date = baseInstant.Date;
        for (var i = 0; i < MaxDaySteps; i++, date = date.AddDays(1))
        {
            if (!MatchesSlot(rule, date)) continue;
            var candidate = new DateTimeOffset(date.Add(time), offset);
            if (candidate > baseInstant || (inclusive && candidate == baseInstant))
                return candidate;
        }
        // Unreachable for validated rules; fall back to the anchor so callers always get a value.
        return anchor;
    }

    private static bool MatchesSlot(RecurrenceRule rule, DateTime date) => rule.CustomMode switch
    {
        CustomRecurrenceMode.DaysOfWeek => rule.Weekdays.Contains(date.DayOfWeek),
        CustomRecurrenceMode.DaysOfMonth => rule.DaysOfMonth.Contains(date.Day) && rule.Months.Contains(date.Month),
        _ => false,
    };
}
