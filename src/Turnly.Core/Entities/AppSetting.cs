namespace Turnly.Core.Entities;

/// <summary>A single app-wide configuration value, stored as a key/value pair. Currently holds the
/// family timezone (an IANA or Windows zone id) used to evaluate quiet hours server-side. An empty
/// value means "unset" (fall back to the server's local zone).</summary>
public class AppSetting
{
    public required string Key { get; set; }
    public string Value { get; set; } = string.Empty;
}
