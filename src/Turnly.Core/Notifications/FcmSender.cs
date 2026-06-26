using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Turnly.Core.Notifications;

/// <summary>Real <see cref="IFcmSender"/> backed by the Firebase Admin SDK. The <see cref="FirebaseApp"/>
/// is created lazily on first send from the configured service-account credential; when none is set
/// the sender stays inert and reports <see cref="PushSendResult.Failed"/> (so nothing is sent and the
/// app runs fine without Firebase).</summary>
public class FcmSender : IFcmSender
{
    private const string AppName = "turnly-fcm";

    private readonly FcmOptions _options;
    private readonly ILogger<FcmSender> _logger;
    private readonly object _gate = new();
    private FirebaseMessaging? _messaging;
    private bool _initFailed;

    public FcmSender(IOptions<FcmOptions> options, ILogger<FcmSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    private FirebaseMessaging? GetMessaging()
    {
        if (_messaging is not null) return _messaging;
        if (_initFailed || !_options.IsConfigured) return null;

        lock (_gate)
        {
            if (_messaging is not null) return _messaging;
            if (_initFailed) return null;
            try
            {
                var credential = !string.IsNullOrWhiteSpace(_options.CredentialsJson)
                    ? GoogleCredential.FromJson(_options.CredentialsJson)
                    : GoogleCredential.FromFile(_options.CredentialsPath);

                var app = FirebaseApp.GetInstance(AppName)
                    ?? FirebaseApp.Create(new AppOptions { Credential = credential }, AppName);

                _messaging = FirebaseMessaging.GetMessaging(app);
                return _messaging;
            }
            catch (Exception ex)
            {
                _initFailed = true;
                _logger.LogError(ex, "Failed to initialize Firebase Cloud Messaging; native push disabled.");
                return null;
            }
        }
    }

    public async Task<PushSendResult> SendAsync(string token, string title, string body, string url, Guid? choreId, CancellationToken ct = default)
    {
        var messaging = GetMessaging();
        if (messaging is null)
        {
            _logger.LogWarning("FCM not sent: Firebase credentials are not configured.");
            return PushSendResult.Failed;
        }

        // Mirror the Web Push payload so the app's tap handler deep-links the same way.
        var data = new Dictionary<string, string> { ["url"] = url };
        if (choreId is { } id) data["choreId"] = id.ToString();

        var message = new Message
        {
            Token = token,
            Notification = new Notification { Title = title, Body = body },
            Data = data,
            Android = new AndroidConfig { Priority = Priority.High },
        };

        try
        {
            await messaging.SendAsync(message, ct);
            return PushSendResult.Ok;
        }
        catch (FirebaseMessagingException ex)
            when (ex.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.SenderIdMismatch)
        {
            _logger.LogInformation("FCM token gone ({Code}); pruning.", ex.MessagingErrorCode);
            return PushSendResult.Gone;
        }
        catch (FirebaseMessagingException ex)
        {
            _logger.LogWarning(ex, "FCM send rejected ({Code}).", ex.MessagingErrorCode);
            return PushSendResult.Failed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending FCM message.");
            return PushSendResult.Failed;
        }
    }
}
