namespace Turnly.Core.Entities;

/// <summary>A Firebase Cloud Messaging registration token for one of a user's native (Android) app
/// installs. The native shell can't do Web Push, so it registers an FCM token here instead; a user
/// may have several (one per device). Dead tokens (FCM reports them unregistered) are pruned when a
/// send fails, mirroring <see cref="PushSubscription"/>.</summary>
public class FcmDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>The FCM registration token for this install - unique.</summary>
    public required string Token { get; set; }

    /// <summary>A friendly label derived from the User-Agent at register time.</summary>
    public string? DeviceLabel { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
