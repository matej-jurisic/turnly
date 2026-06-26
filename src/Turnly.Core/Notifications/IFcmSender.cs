namespace Turnly.Core.Notifications;

/// <summary>Sends a notification to a single FCM registration token (native Android push). Abstracted
/// like <see cref="IPushSender"/> so the notification service can be unit-tested without Firebase.
/// Reuses <see cref="PushSendResult"/>: <see cref="PushSendResult.Gone"/> means the token is dead
/// (FCM reports it unregistered) and the caller should prune it.</summary>
public interface IFcmSender
{
    Task<PushSendResult> SendAsync(string token, string title, string body, string url, Guid? choreId, CancellationToken ct = default);
}
