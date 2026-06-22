namespace Turnly.Core.Notifications;

/// <summary>Pure helper for the per-user quiet-hours window. A window where <c>start &gt; end</c>
/// spans midnight (e.g. 22:00–07:00).</summary>
public static class QuietHours
{
    /// <summary>Whether <paramref name="now"/> (a time-of-day) falls inside the window
    /// [<paramref name="start"/>, <paramref name="end"/>). Either end null = quiet hours off.
    /// The window is half-open so a notification due exactly at the end time still fires.</summary>
    public static bool Contains(TimeOnly? start, TimeOnly? end, TimeOnly now)
    {
        if (start is not { } s || end is not { } e || s == e)
            return false;
        return s < e
            ? now >= s && now < e          // same-day window
            : now >= s || now < e;         // spans midnight
    }
}
