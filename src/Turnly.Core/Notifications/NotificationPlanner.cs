using Turnly.Core.Entities;
using Turnly.Core.Enums;

namespace Turnly.Core.Notifications;

/// <summary>Pure timing arithmetic for the notification schedule (unit-tested, no I/O). Given an
/// occurrence's due time, computes when a schedule entry should fire.</summary>
public static class NotificationPlanner
{
    /// <summary>The moment a schedule entry should fire for an occurrence due at
    /// <paramref name="dueAt"/>: before/after the due time by the entry's offset, or exactly at it.</summary>
    public static DateTimeOffset FireTime(ChoreNotification entry, DateTimeOffset dueAt) =>
        entry.Timing switch
        {
            NotificationTiming.Before => dueAt - Offset(entry),
            NotificationTiming.After => dueAt + Offset(entry),
            _ => dueAt
        };

    private static TimeSpan Offset(ChoreNotification entry)
    {
        if (entry.Timing == NotificationTiming.AtDue)
            return TimeSpan.Zero;

        var value = Math.Max(0, entry.OffsetValue);
        return entry.OffsetUnit switch
        {
            NotificationOffsetUnit.Minutes => TimeSpan.FromMinutes(value),
            NotificationOffsetUnit.Hours => TimeSpan.FromHours(value),
            NotificationOffsetUnit.Days => TimeSpan.FromDays(value),
            _ => TimeSpan.Zero
        };
    }
}
