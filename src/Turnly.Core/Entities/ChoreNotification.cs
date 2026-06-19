using Turnly.Core.Enums;

namespace Turnly.Core.Entities;

/// <summary>One entry in a chore's notification schedule: fire a <see cref="Type"/> message
/// <see cref="Timing"/> the occurrence due time (offset by <see cref="OffsetValue"/> +
/// <see cref="OffsetUnit"/>, ignored for <see cref="NotificationTiming.AtDue"/>) to
/// <see cref="Recipients"/>. Notifications stop once the occurrence is completed because completion
/// advances the chore's <c>DueAt</c> (see <see cref="NotificationDelivery"/>).</summary>
public class ChoreNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ChoreId { get; set; }
    public Chore? Chore { get; set; }

    public NotificationType Type { get; set; } = NotificationType.Reminder;
    public NotificationTiming Timing { get; set; } = NotificationTiming.Before;

    /// <summary>Offset magnitude for <see cref="NotificationTiming.Before"/>/<c>After</c>; 0 for
    /// <see cref="NotificationTiming.AtDue"/>.</summary>
    public int OffsetValue { get; set; }
    public NotificationOffsetUnit OffsetUnit { get; set; } = NotificationOffsetUnit.Minutes;

    public NotificationRecipients Recipients { get; set; } = NotificationRecipients.CurrentAssignee;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
