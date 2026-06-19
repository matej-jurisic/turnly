using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LibWebPush = WebPush;
using Entities = Turnly.Core.Entities;

namespace Turnly.Core.Notifications;

/// <summary>Real <see cref="IPushSender"/> backed by the WebPush library + configured VAPID keys.</summary>
public class WebPushSender : IPushSender
{
    private readonly VapidOptions _vapid;
    private readonly ILogger<WebPushSender> _logger;
    private readonly LibWebPush.WebPushClient _client = new();

    public WebPushSender(IOptions<VapidOptions> vapid, ILogger<WebPushSender> logger)
    {
        _vapid = vapid.Value;
        _logger = logger;
    }

    public async Task<PushSendResult> SendAsync(Entities.PushSubscription subscription, string payloadJson, CancellationToken ct = default)
    {
        if (!_vapid.IsConfigured)
        {
            _logger.LogWarning("Push not sent: VAPID keys are not configured.");
            return PushSendResult.Failed;
        }

        var sub = new LibWebPush.PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth);
        var details = new LibWebPush.VapidDetails(_vapid.Subject, _vapid.PublicKey, _vapid.PrivateKey);

        try
        {
            await _client.SendNotificationAsync(sub, payloadJson, details, ct);
            _logger.LogInformation("Push accepted by the push service for {Endpoint}", subscription.Endpoint);
            return PushSendResult.Ok;
        }
        catch (LibWebPush.WebPushException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            _logger.LogInformation("Push subscription gone ({Status}); pruning {Endpoint}", ex.StatusCode, subscription.Endpoint);
            return PushSendResult.Gone;
        }
        catch (LibWebPush.WebPushException ex)
        {
            _logger.LogWarning(ex, "Push send rejected ({Status}) for {Endpoint}", ex.StatusCode, subscription.Endpoint);
            return PushSendResult.Failed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending push to {Endpoint}", subscription.Endpoint);
            return PushSendResult.Failed;
        }
    }
}
