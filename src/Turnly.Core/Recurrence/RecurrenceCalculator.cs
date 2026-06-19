using Turnly.Core.Enums;

namespace Turnly.Core.Recurrence;

/// <summary>
/// Pure recurrence math for both the basic repeat types (Phase 2) and the custom modes plus
/// scheduling preferences (Phase 3). Given the occurrence that was just completed it returns the
/// next occurrence's due date — or null when nothing follows (one-time chores).
///
/// <para><see cref="CustomRecurrenceMode.Frequency"/> is intentionally NOT handled here: it
/// depends on how many completions exist in the current period, which is database state. The
/// period helpers (<see cref="PeriodStart"/>/<see cref="PeriodEnd"/>) support that logic, which
/// lives in <c>ChoreService</c>.</para>
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
        if (IsFrequency(rule))
            return PeriodEnd(rule.FrequencyPeriod!.Value, start);
        // One-time and all interval-style schedules start exactly at the start date.
        return start;
    }

    /// <summary>The next due date after completing the <paramref name="scheduledDue"/> occurrence,
    /// honouring the scheduling preference. Returns null for one-time chores.</summary>
    public static DateTimeOffset? NextDue(
        RecurrenceRule rule,
        SchedulingPreference pref,
        DateTimeOffset scheduledDue,
        DateTimeOffset completedAt,
        DateTimeOffset now)
    {
        if (rule.Type == RepeatType.OneTime)
            return null;

        if (IsFixedSlot(rule))
        {
            // All three preferences reduce to "the next slot strictly after a base instant".
            var baseInstant = pref switch
            {
                SchedulingPreference.FromCompletionDate => completedAt,
                SchedulingPreference.ToFirstNextRepeat => now,
                _ => scheduledDue,
            };
            return FixedSlotAfter(rule, scheduledDue, baseInstant);
        }

        // Interval-style (Daily / Weekly / Monthly / Yearly / Custom-Interval).
        switch (pref)
        {
            case SchedulingPreference.FromCompletionDate:
                return AddInterval(rule, completedAt);
            case SchedulingPreference.ToFirstNextRepeat:
                var next = AddInterval(rule, scheduledDue);
                for (var i = 0; next <= now && i < MaxIntervalSteps; i++)
                    next = AddInterval(rule, next);
                return next;
            default: // FromScheduledDate
                return AddInterval(rule, scheduledDue);
        }
    }

    // ── Classification ────────────────────────────────────────────────────────────────────

    private static bool IsFixedSlot(RecurrenceRule rule) =>
        rule.Type == RepeatType.Custom &&
        rule.CustomMode is CustomRecurrenceMode.DaysOfWeek or CustomRecurrenceMode.DaysOfMonth;

    private static bool IsFrequency(RecurrenceRule rule) =>
        rule.Type == RepeatType.Custom && rule.CustomMode == CustomRecurrenceMode.Frequency;

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

    // ── Frequency period boundaries ───────────────────────────────────────────────────────

    /// <summary>Start of the period (containing <paramref name="instant"/>) for a frequency chore.
    /// Day/Month/Year are calendar-aligned; Week is a 7-day window aligned to the instant's date.</summary>
    public static DateTimeOffset PeriodStart(FrequencyPeriod period, DateTimeOffset instant) => period switch
    {
        FrequencyPeriod.Day => new DateTimeOffset(instant.Date, instant.Offset),
        FrequencyPeriod.Week => new DateTimeOffset(StartOfWeek(instant.Date), instant.Offset),
        FrequencyPeriod.Month => new DateTimeOffset(new DateTime(instant.Year, instant.Month, 1), instant.Offset),
        FrequencyPeriod.Year => new DateTimeOffset(new DateTime(instant.Year, 1, 1), instant.Offset),
        _ => new DateTimeOffset(instant.Date, instant.Offset),
    };

    /// <summary>End (exclusive) of the period containing <paramref name="instant"/>.</summary>
    public static DateTimeOffset PeriodEnd(FrequencyPeriod period, DateTimeOffset instant)
    {
        var start = PeriodStart(period, instant);
        return period switch
        {
            FrequencyPeriod.Day => start.AddDays(1),
            FrequencyPeriod.Week => start.AddDays(7),
            FrequencyPeriod.Month => start.AddMonths(1),
            FrequencyPeriod.Year => start.AddYears(1),
            _ => start.AddDays(1),
        };
    }

    // Week starts on Monday (ISO), so "3× per week" is predictable regardless of locale.
    private static DateTime StartOfWeek(DateTime date)
    {
        var diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-diff);
    }
}
