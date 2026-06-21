using Turnly.Core.Enums;

namespace Turnly.Core.Common;

/// <summary>Shared business-rule validation reused across services (and unit-tested).</summary>
public static class Validators
{
    public const int MinUsernameLength = 3;
    public const int MaxUsernameLength = 64;
    public const int MinPasswordLength = 6;
    public const int MaxChoreNameLength = 128;
    public const int MaxAwardNameLength = 128;
    public const int MaxPoints = 100_000;
    public const int MaxInterval = 365;
    public const int MaxCompletionsRequired = 100;
    public const int MaxNotificationsPerChore = 20;
    public const int MaxNotificationOffset = 365;
    public const int MaxTimesOfDay = 24;

    public static Error? Username(string? username)
    {
        var value = username?.Trim() ?? string.Empty;
        if (value.Length < MinUsernameLength)
            return Error.Validation($"Username must be at least {MinUsernameLength} characters.");
        if (value.Length > MaxUsernameLength)
            return Error.Validation($"Username must be at most {MaxUsernameLength} characters.");
        return null;
    }

    public static Error? DisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return Error.Validation("Display name is required.");
        return null;
    }

    public static Error? Password(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            return Error.Validation($"Password must be at least {MinPasswordLength} characters.");
        return null;
    }

    public static Error? ChoreName(string? name)
    {
        var value = name?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return Error.Validation("Chore name is required.");
        if (value.Length > MaxChoreNameLength)
            return Error.Validation($"Chore name must be at most {MaxChoreNameLength} characters.");
        return null;
    }

    public static Error? Points(int points)
    {
        if (points < 0)
            return Error.Validation("Points cannot be negative.");
        if (points > MaxPoints)
            return Error.Validation($"Points must be at most {MaxPoints}.");
        return null;
    }

    public static Error? AwardName(string? name)
    {
        var value = name?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return Error.Validation("Award name is required.");
        if (value.Length > MaxAwardNameLength)
            return Error.Validation($"Award name must be at most {MaxAwardNameLength} characters.");
        return null;
    }

    public static Error? AwardCost(int cost)
    {
        if (cost < 1)
            return Error.Validation("Award cost must be at least 1 point.");
        if (cost > MaxPoints)
            return Error.Validation($"Award cost must be at most {MaxPoints}.");
        return null;
    }

    /// <summary>Validates the custom-recurrence parameters. Non-custom repeat types carry no extra
    /// parameters, so they always pass.</summary>
    public static Error? Recurrence(
        RepeatType type,
        CustomRecurrenceMode? mode,
        int? intervalCount,
        RecurrenceUnit? intervalUnit,
        IReadOnlyCollection<DayOfWeek>? weekdays,
        IReadOnlyCollection<int>? daysOfMonth,
        IReadOnlyCollection<int>? months,
        int completionsRequired,
        IReadOnlyCollection<int>? weeksOfMonth = null)
    {
        // "Complete N times per occurrence" is only offered on the non-custom repeat types.
        if (completionsRequired < 1 || completionsRequired > MaxCompletionsRequired)
            return Error.Validation($"The number of times must be between 1 and {MaxCompletionsRequired}.");
        if (type == RepeatType.Custom && completionsRequired != 1)
            return Error.Validation("A custom recurrence can't also set a per-occurrence completion count.");

        if (type != RepeatType.Custom)
            return null;

        if (mode is null)
            return Error.Validation("A custom chore must specify a recurrence mode.");

        switch (mode)
        {
            case CustomRecurrenceMode.Interval:
                if (intervalUnit is null)
                    return Error.Validation("An interval recurrence must specify a unit.");
                if (intervalCount is not { } n || n < 1 || n > MaxInterval)
                    return Error.Validation($"The interval must be between 1 and {MaxInterval}.");
                break;

            case CustomRecurrenceMode.DaysOfWeek:
                if (weekdays is null || weekdays.Count == 0)
                    return Error.Validation("Select at least one weekday.");
                // Empty = every week; otherwise each value is an nth occurrence (1–4) or last (-1).
                if (weeksOfMonth is not null && weeksOfMonth.Any(w => w is not (1 or 2 or 3 or 4 or -1)))
                    return Error.Validation("Week-of-month occurrences must be the 1st–4th or last.");
                break;

            case CustomRecurrenceMode.DaysOfMonth:
                if (daysOfMonth is null || daysOfMonth.Count == 0)
                    return Error.Validation("Select at least one day of the month.");
                if (daysOfMonth.Any(d => d < 1 || d > 31))
                    return Error.Validation("Days of the month must be between 1 and 31.");
                if (months is null || months.Count == 0)
                    return Error.Validation("Select at least one month.");
                if (months.Any(m => m < 1 || m > 12))
                    return Error.Validation("Months must be between 1 and 12.");
                // Reject combos that can never occur (e.g. day 31 in February only). The smallest
                // selected day must fit the most generous selected month.
                if (daysOfMonth.Min() > months.Max(MaxDayInMonth))
                    return Error.Validation("The selected day never occurs in any of the selected months.");
                break;
        }

        return null;
    }

    /// <summary>Validates a single notification schedule entry's offset. <c>AtDue</c> carries no
    /// offset; before/after entries need a positive, bounded offset.</summary>
    public static Error? NotificationOffset(NotificationTiming timing, int offsetValue)
    {
        if (timing == NotificationTiming.AtDue)
            return null;
        if (offsetValue < 1)
            return Error.Validation("A before/after notification needs an offset of at least 1.");
        if (offsetValue > MaxNotificationOffset)
            return Error.Validation($"A notification offset must be at most {MaxNotificationOffset}.");
        return null;
    }

    /// <summary>Validates an optional due-time string. Null/empty is valid (= end of day); otherwise
    /// it must be "HH:mm". Returns the parsed value via <paramref name="parsed"/>.</summary>
    public static Error? DueTime(string? value, out TimeOnly? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!TimeOnly.TryParseExact(value, "HH:mm", out var time))
            return Error.Validation("A due time must be in HH:mm format.");
        parsed = time;
        return null;
    }

    /// <summary>Validates the optional "N times a day" fixed time-of-day list and returns the parsed,
    /// de-duplicated, sorted set via <paramref name="parsed"/>. Empty/null is valid (single daily
    /// slot). Times are only allowed on day-resolution schedules: Daily and the custom DaysOfWeek /
    /// DaysOfMonth calendar modes — never on interval/weekly/monthly/yearly/one-time schedules, whose
    /// recurrence math can't host multiple within-day slots coherently.</summary>
    public static Error? TimesOfDay(string[]? values, RepeatType type, CustomRecurrenceMode? mode, out List<TimeOnly> parsed)
    {
        parsed = new List<TimeOnly>();
        var raw = values?.Where(v => !string.IsNullOrWhiteSpace(v)).ToList() ?? new List<string>();
        if (raw.Count == 0)
            return null;

        var supportsTimes = type == RepeatType.Daily ||
            (type == RepeatType.Custom && mode is CustomRecurrenceMode.DaysOfWeek or CustomRecurrenceMode.DaysOfMonth);
        if (!supportsTimes)
            return Error.Validation("Multiple times a day are only available for daily, days-of-week, or days-of-month schedules.");
        if (raw.Count > MaxTimesOfDay)
            return Error.Validation($"A chore can have at most {MaxTimesOfDay} times a day.");

        var set = new HashSet<TimeOnly>();
        foreach (var value in raw)
        {
            if (!TimeOnly.TryParseExact(value.Trim(), "HH:mm", out var time))
                return Error.Validation("Each time must be in HH:mm format.");
            if (!set.Add(time))
                return Error.Validation("Times of day must be distinct.");
        }
        parsed = set.OrderBy(t => t).ToList();
        return null;
    }

    // Highest day number a month can hold. February counts 29 so leap-year-only chores
    // (day 29 in Feb) stay valid.
    private static int MaxDayInMonth(int month) =>
        new[] { 31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 }[month - 1];
}
