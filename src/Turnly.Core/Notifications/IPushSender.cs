using Turnly.Core.Entities;

namespace Turnly.Core.Notifications;

/// <summary>Sends an encrypted Web Push message to a single subscription. Abstracted so the
/// notification service can be unit-tested without hitting a real push service.</summary>
public interface IPushSender
{
    Task<PushSendResult> SendAsync(PushSubscription subscription, string payloadJson, CancellationToken ct = default);
}

/// <summary>Outcome of a push send. <see cref="Gone"/> means the subscription is permanently dead
/// (HTTP 404/410) and the caller should prune it.</summary>
public enum PushSendResult
{
    Ok,
    Gone,
    Failed
}
