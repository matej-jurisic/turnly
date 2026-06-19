namespace Turnly.Core.Enums;

/// <summary>The kind of chore notification, which drives the message wording.
/// <see cref="Reminder"/> nudges ahead of the due time, <see cref="Due"/> fires at the due time,
/// and <see cref="FollowUp"/> chases an occurrence that passed its due time uncompleted.</summary>
public enum NotificationType
{
    Reminder = 0,
    Due = 1,
    FollowUp = 2
}
