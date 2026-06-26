namespace Turnly.Core.Notifications;

/// <summary>Firebase Cloud Messaging credentials for native (Android) push. Supply a Firebase
/// service-account credential either as a path to the JSON file (<see cref="CredentialsPath"/>) or
/// inline (<see cref="CredentialsJson"/>). When unset, FCM sends are skipped (the FCM sender stays
/// inert, like Web Push without VAPID keys), so the app builds and runs without Firebase.</summary>
public class FcmOptions
{
    public const string SectionName = "Fcm";

    /// <summary>Path to the Firebase service-account JSON (e.g. /run/secrets/firebase.json).</summary>
    public string CredentialsPath { get; set; } = string.Empty;

    /// <summary>The service-account JSON inline (alternative to <see cref="CredentialsPath"/>).</summary>
    public string CredentialsJson { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CredentialsPath) || !string.IsNullOrWhiteSpace(CredentialsJson);
}
