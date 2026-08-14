using Turnly.Core.Enums;

namespace Turnly.Core.Notifications;

/// <summary>Pure decision point for "how far does this notification reach this recipient?". Every
/// send path funnels through <see cref="Resolve"/> so the user's controls apply uniformly, whether
/// the notification came from the scheduler or an ad-hoc call.
///
/// <para>The rules compose by taking the <b>most restrictive</b> answer: the category preference sets
/// the ceiling, a mute on the chore knocks push out, and quiet hours knocks push out. Nothing can
/// promote a notification back up - suppression only ever travels one way.</para></summary>
public static class NotificationRouter
{
    /// <summary>Categories the user cannot silence. <see cref="NotificationCategory.System"/> covers
    /// the test push, whose whole job is to prove push works - honouring a stale "Off" there would
    /// make the diagnostic lie about the delivery path.</summary>
    public static bool IsConfigurable(NotificationCategory category) =>
        category != NotificationCategory.System;

    /// <summary>Resolves the effective channel for one recipient.</summary>
    /// <param name="category">What the notification is about.</param>
    /// <param name="preference">The recipient's setting for that category (default when they have no row).</param>
    /// <param name="choreMuted">Whether this recipient has muted the chore the notification is about.</param>
    /// <param name="inQuietHours">Whether the recipient is inside their quiet-hours window right now.</param>
    public static DeliveryChannel Resolve(
        NotificationCategory category,
        DeliveryChannel preference,
        bool choreMuted,
        bool inQuietHours)
    {
        if (!IsConfigurable(category))
            return DeliveryChannel.PushAndInbox;

        // Off wins outright - the user asked not to hear about this at all.
        if (preference == DeliveryChannel.Off)
            return DeliveryChannel.Off;

        // A mute or a quiet window demotes push to inbox-only, but never discards the record: the
        // user is saying "don't interrupt me", not "pretend this never happened".
        if (preference == DeliveryChannel.InboxOnly || choreMuted || inQuietHours)
            return DeliveryChannel.InboxOnly;

        return DeliveryChannel.PushAndInbox;
    }

    /// <summary>The category a scheduled chore notification entry belongs to.</summary>
    public static NotificationCategory CategoryOf(NotificationType type) => type switch
    {
        NotificationType.Due => NotificationCategory.ChoreDue,
        NotificationType.FollowUp => NotificationCategory.ChoreOverdue,
        _ => NotificationCategory.ChoreReminder
    };
}
