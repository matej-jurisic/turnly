namespace Turnly.Core.Enums;

/// <summary>When a notification fires relative to the occurrence's due time.
/// <see cref="AtDue"/> carries no offset; <see cref="Before"/>/<see cref="After"/> are offset by
/// the entry's value + unit.</summary>
public enum NotificationTiming
{
    Before = 0,
    AtDue = 1,
    After = 2
}
