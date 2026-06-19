namespace Turnly.Core.Enums;

/// <summary>Who a chore notification is sent to: just the user currently holding the occurrence,
/// or every eligible assignee.</summary>
public enum NotificationRecipients
{
    CurrentAssignee = 0,
    AllAssignees = 1
}
