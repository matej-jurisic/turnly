namespace Turnly.Core.Entities;

/// <summary>A marker that a single <see cref="ChoreNotification"/> entry has fired for a specific
/// occurrence (identified by the chore's <c>DueAt</c> at fire time). The unique
/// <c>(ChoreNotificationId, OccurrenceDueAt)</c> index makes each entry fire at most once per
/// occurrence; because completing a chore advances its <c>DueAt</c> to a new occurrence, pending
/// notifications for the old occurrence are never reached — which is how notifications "stop on
/// completion".</summary>
public class NotificationDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ChoreId { get; set; }

    public Guid ChoreNotificationId { get; set; }
    public ChoreNotification? ChoreNotification { get; set; }

    /// <summary>The occurrence this delivery belongs to (the chore's <c>DueAt</c> when it fired).</summary>
    public DateTimeOffset OccurrenceDueAt { get; set; }

    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
}
