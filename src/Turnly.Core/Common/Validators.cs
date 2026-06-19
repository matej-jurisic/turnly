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
    public const int MaxFrequency = 100;

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
        int? frequencyCount,
        FrequencyPeriod? frequencyPeriod)
    {
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

            case CustomRecurrenceMode.Frequency:
                if (frequencyPeriod is null)
                    return Error.Validation("A frequency recurrence must specify a period.");
                if (frequencyCount is not { } f || f < 1 || f > MaxFrequency)
                    return Error.Validation($"The frequency must be between 1 and {MaxFrequency}.");
                break;
        }

        return null;
    }

    // Highest day number a month can hold. February counts 29 so leap-year-only chores
    // (day 29 in Feb) stay valid.
    private static int MaxDayInMonth(int month) =>
        new[] { 31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 }[month - 1];
}
