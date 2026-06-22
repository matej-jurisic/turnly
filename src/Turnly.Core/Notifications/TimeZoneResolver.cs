namespace Turnly.Core.Notifications;

/// <summary>Resolves a configured timezone id to a <see cref="TimeZoneInfo"/>. The stored id can be an
/// IANA name (e.g. "Europe/Zagreb") or a Windows id — .NET accepts both cross-platform — so we always
/// re-resolve from the raw stored string rather than a normalised one, keeping the DB portable.</summary>
public static class TimeZoneResolver
{
    /// <summary>The configured zone, or the server's local zone when unset or unrecognised on this host.</summary>
    public static TimeZoneInfo Resolve(string? id)
        => !string.IsNullOrWhiteSpace(id) && TryFind(id, out var zone) ? zone : TimeZoneInfo.Local;

    /// <summary>Whether <paramref name="id"/> is a timezone this host recognises.</summary>
    public static bool IsValid(string id) => TryFind(id, out _);

    private static bool TryFind(string id, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
            return true;
        }
        catch (Exception) // TimeZoneNotFoundException / InvalidTimeZoneException
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
