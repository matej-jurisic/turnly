namespace Turnly.Core.Common;

/// <summary>Shared business-rule validation reused across services (and unit-tested).</summary>
public static class Validators
{
    public const int MinUsernameLength = 3;
    public const int MaxUsernameLength = 64;
    public const int MinPasswordLength = 6;

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
}
