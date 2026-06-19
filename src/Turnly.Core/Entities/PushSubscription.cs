namespace Turnly.Core.Entities;

/// <summary>A Web Push subscription for one of a user's browsers/devices. A user may have several
/// (one per device). Dead subscriptions (the push service returns 404/410) are pruned when a send
/// fails.</summary>
public class PushSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>The push service endpoint URL — unique per subscription.</summary>
    public required string Endpoint { get; set; }

    /// <summary>The client's public key (base64url) for payload encryption.</summary>
    public required string P256dh { get; set; }

    /// <summary>The client's auth secret (base64url) for payload encryption.</summary>
    public required string Auth { get; set; }

    /// <summary>A friendly "Browser · OS" label derived from the User-Agent at subscribe time, so
    /// the user can recognise which device a subscription belongs to.</summary>
    public string? DeviceLabel { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
