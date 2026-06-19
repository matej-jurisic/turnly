namespace Turnly.Core.Notifications;

/// <summary>Self-hosted VAPID keys for Web Push. Generated once (e.g. via
/// <c>WebPush.VapidHelper.GenerateVapidKeys()</c> or <c>npx web-push generate-vapid-keys</c>) and
/// supplied through configuration. When unset, the scheduler stays idle and no pushes are sent.</summary>
public class VapidOptions
{
    public const string SectionName = "Vapid";

    /// <summary>The VAPID public key (base64url). Also handed to the browser when subscribing.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>The VAPID private key (base64url). Secret.</summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>A contact URI for the push service, e.g. <c>mailto:admin@example.com</c>.</summary>
    public string Subject { get; set; } = "mailto:admin@example.com";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}
