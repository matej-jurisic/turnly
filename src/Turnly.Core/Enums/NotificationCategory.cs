namespace Turnly.Core.Enums;

/// <summary>The kind of thing a notification is about, from the *recipient's* point of view. This is
/// the unit a user tunes in their notification preferences, so the categories are deliberately
/// coarser than <see cref="NotificationType"/> (which only describes scheduled chore entries) and
/// span the ad-hoc offer notifications too.</summary>
public enum NotificationCategory
{
    /// <summary>A scheduled nudge ahead of the due time (<see cref="NotificationType.Reminder"/>).</summary>
    ChoreReminder = 0,

    /// <summary>A scheduled notification at the due time (<see cref="NotificationType.Due"/>).</summary>
    ChoreDue = 1,

    /// <summary>A scheduled chase after the due time passed uncompleted
    /// (<see cref="NotificationType.FollowUp"/>).</summary>
    ChoreOverdue = 2,

    /// <summary>Someone put a chore up for grabs, or offered one to you directly. This is the
    /// highest-volume category: an open offer fans out to every other eligible assignee.</summary>
    OfferReceived = 3,

    /// <summary>Something happened to an offer *you* made: it was claimed or declined.</summary>
    OfferResponse = 4,

    /// <summary>Another member nudged you about a chore you hold, optionally with a note.</summary>
    Nudge = 6,

    /// <summary>App-level messages (test pushes and anything not tied to a chore or offer). Cannot be
    /// turned off - see <see cref="Turnly.Core.Notifications.NotificationRouter"/>.</summary>
    System = 5
}
