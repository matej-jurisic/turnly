namespace Turnly.Core.Enums;

/// <summary>How far a notification in a given <see cref="NotificationCategory"/> is allowed to reach
/// the user. Ordered least-restrictive first, which is also the order the settings UI shows.</summary>
public enum DeliveryChannel
{
    /// <summary>The default: write the in-app inbox row and push to the user's devices.</summary>
    PushAndInbox = 0,

    /// <summary>Write the inbox row but send no push - the notification is there when the user goes
    /// looking, it just doesn't interrupt. This is also what quiet hours does to any category.</summary>
    InboxOnly = 1,

    /// <summary>Drop the notification entirely: no push, no inbox row.</summary>
    Off = 2
}
