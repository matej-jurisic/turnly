namespace Turnly.Core.Entities;

/// <summary>A marker that a single <see cref="ChoreNotification"/> entry has fired for a specific
/// occurrence (identified by the chore's <c>DueAt</c> at fire time). The unique
/// <c>(ChoreNotificationId, OccurrenceDueAt, UserId)</c> index makes each entry fire at most once per
/// occurrence; because completing a chore advances its <c>DueAt</c> to a new occurrence, pending
/// notifications for the old occurrence are never reached — which is how notifications "stop on
/// completion". For <see cref="Enums.AssignmentStrategy.Independent"/> chores the same entry fires
/// once per assignee track, so <see cref="UserId"/> (the track owner) is part of the dedup key;
/// rotating chores leave it null (one row per occurrence regardless of recipients).</summary>
public class NotificationDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ChoreId { get; set; }

    public Guid ChoreNotificationId { get; set; }
    public ChoreNotification? ChoreNotification { get; set; }

    /// <summary>The occurrence this delivery belongs to (the chore's — or in track mode, the track's
    /// — <c>DueAt</c> when it fired).</summary>
    public DateTimeOffset OccurrenceDueAt { get; set; }

    /// <summary>The track owner this delivery is for, in <see cref="Enums.AssignmentStrategy.Independent"/>
    /// mode; null for rotating chores. Part of the dedup index so an entry fires once per assignee.</summary>
    public Guid? UserId { get; set; }

    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
}
