using Turnly.Core.Enums;

namespace Turnly.Core.Entities;

/// <summary>One user's chosen <see cref="DeliveryChannel"/> for one <see cref="NotificationCategory"/>.
/// Storage is <b>sparse</b>: a missing row means the category is at its default
/// (<see cref="DeliveryChannel.PushAndInbox"/>), so existing users need no backfill and rows are only
/// written when someone actually changes something.</summary>
public class UserNotificationPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public NotificationCategory Category { get; set; }
    public DeliveryChannel Channel { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
