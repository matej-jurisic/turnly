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
        if (IsSlotScanned(rule))
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
        if (IsSlotScanned(rule))
        {
            // All base preferences reduce to "the next slot strictly after a base instant".
            var baseInstant = pref switch
            {
                SchedulingPreference.FromCompletionDate => completedAt,
                // Anchor off the scheduled date like the interval path so an early completion still
                // advances to the next slot, but never scan from before `now` — that keeps an overdue
                // chore skipping the occurrences it already missed to the first future one.
                SchedulingPreference.ToFirstNextRepeat => scheduledDue > now ? scheduledDue : now,
                _ => scheduledDue,
            };
            // The scanner already stamps each result with its own slot time-of-day (which, with
            // multiple TimesOfDay, is deliberately *not* the scheduled occurrence's time), so the
            // interval path's re-anchoring below must not run here.
            return FixedSlotAfter(rule, scheduledDue, baseInstant);
        }

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

    /// <summary>Whether the schedule resolves by scanning forward day-by-day for matching slots rather
    /// than by interval stepping. True for the calendar custom modes, and for any schedule carrying
    /// explicit <see cref="RecurrenceRule.TimesOfDay"/> — multiple fixed times turn even a plain Daily
    /// chore into a slot scan (each day × each time is a distinct occurrence).</summary>
    private static bool IsSlotScanned(RecurrenceRule rule) => IsFixedSlot(rule) || rule.TimesOfDay.Count > 0;

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

    /// <summary>Scans forward day by day from <paramref name="baseInstant"/> for the next slot that
    /// matches the rule's day predicate, trying each of the day's times-of-day in order. The result
    /// keeps <paramref name="anchor"/>'s offset; the time-of-day comes from the matched slot (the
    /// rule's <see cref="RecurrenceRule.TimesOfDay"/>, or the anchor's own time when none are set).</summary>
    private static DateTimeOffset ScanSlot(RecurrenceRule rule, DateTimeOffset anchor, DateTimeOffset baseInstant, bool inclusive)
    {
        var offset = anchor.Offset;
        var times = SlotTimes(rule, anchor);
        var date = baseInstant.ToOffset(offset).Date;
        for (var i = 0; i < MaxDaySteps; i++, date = date.AddDays(1))
        {
            if (!MatchesDay(rule, date)) continue;
            foreach (var time in times)
            {
                var candidate = new DateTimeOffset(date.Add(time), offset);
                if (candidate > baseInstant || (inclusive && candidate == baseInstant))
                    return candidate;
            }
        }
        // Unreachable for validated rules; fall back to the anchor so callers always get a value.
        return anchor;
    }

    /// <summary>The sorted times-of-day a slot can fall on: the rule's explicit
    /// <see cref="RecurrenceRule.TimesOfDay"/>, or a single slot at the anchor's own time when none
    /// are configured (the plain calendar-mode case).</summary>
    private static IReadOnlyList<TimeSpan> SlotTimes(RecurrenceRule rule, DateTimeOffset anchor) =>
        rule.TimesOfDay.Count == 0
            ? new[] { anchor.TimeOfDay }
            : rule.TimesOfDay.Select(t => t.ToTimeSpan()).OrderBy(t => t).ToArray();

    /// <summary>Whether <paramref name="date"/> is an eligible day for the rule (ignoring time-of-day).
    /// Daily matches every day; the custom calendar modes apply their weekday/day-of-month predicate.</summary>
    private static bool MatchesDay(RecurrenceRule rule, DateTime date) => rule.CustomMode switch
    {
        CustomRecurrenceMode.DaysOfWeek =>
            rule.Weekdays.Contains(date.DayOfWeek) && MatchesWeekOfMonth(rule.WeeksOfMonth, date),
        CustomRecurrenceMode.DaysOfMonth => rule.DaysOfMonth.Contains(date.Day) && rule.Months.Contains(date.Month),
        // Non-calendar schedules only reach the scanner when they carry explicit TimesOfDay, which the
        // validator restricts to Daily — so every day is an eligible slot day.
        _ => true,
    };

    /// <summary>Whether <paramref name="date"/> falls on one of the selected occurrences of its weekday
    /// within the month. An empty set means every week. Values 1–4 are the nth occurrence; -1 is the last
    /// (the final time that weekday occurs in the month).</summary>
    private static bool MatchesWeekOfMonth(IReadOnlyCollection<int> weeks, DateTime date)
    {
        if (weeks.Count == 0) return true;
        var ordinal = (date.Day - 1) / 7 + 1;
        if (weeks.Contains(ordinal)) return true;
        var isLast = date.Day + 7 > DateTime.DaysInMonth(date.Year, date.Month);
        return isLast && weeks.Contains(-1);
    }
}
