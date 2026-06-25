using Turnly.Core.Enums;

namespace Turnly.Core.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Username { get; set; }
    public required string DisplayName { get; set; }
    public string AvatarColor { get; set; } = "#6366f1";
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Member;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Denormalized point balance, kept in sync with <see cref="PointsLog"/>.</summary>
    public int Points { get; set; }

    /// <summary>Start of the user's quiet-hours window (server-local time). During quiet hours push
    /// notifications are suppressed (the in-app inbox row is still written). Null when quiet hours are
    /// off; <see cref="QuietHoursStart"/> and <see cref="QuietHoursEnd"/> are set together. A window
    /// where start &gt; end spans midnight (e.g. 22:00–07:00).</summary>
    public TimeOnly? QuietHoursStart { get; set; }
    public TimeOnly? QuietHoursEnd { get; set; }

    /// <summary>When true the user is suspended: excluded from assignment rotation, their Independent
    /// tracks get no auto-advance or notifications, and rotating chores they own are reassigned on
    /// freeze. Admin-only toggle. On unfreeze, stale Independent tracks are stepped forward.</summary>
    public bool IsFrozen { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<PointsLogEntry> PointsLog { get; set; } = new List<PointsLogEntry>();
}
