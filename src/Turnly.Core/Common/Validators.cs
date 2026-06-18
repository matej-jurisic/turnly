namespace Turnly.Core.Common;

/// <summary>Shared business-rule validation reused across services (and unit-tested).</summary>
public static class Validators
{
    public const int MinUsernameLength = 3;
    public const int MaxUsernameLength = 64;
    public const int MinPasswordLength = 6;
    public const int MaxChoreNameLength = 128;
    public const int MaxPoints = 100_000;

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
}
