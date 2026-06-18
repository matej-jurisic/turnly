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

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<PointsLogEntry> PointsLog { get; set; } = new List<PointsLogEntry>();
}
