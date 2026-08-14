namespace Turnly.Core.Entities;

/// <summary>One user silencing one chore. Scoped to the user, so muting a shared chore only quiets
/// it for the person who muted it - everyone else still gets notified normally.</summary>
public class ChoreMute
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid ChoreId { get; set; }
    public Chore? Chore { get; set; }

    /// <summary>When the mute lapses, or <c>null</c> for an indefinite mute. An expired row is treated
    /// as unmuted and swept up lazily the next time the user's mutes are read, so nothing needs to
    /// scan for them on a timer.</summary>
    public DateTimeOffset? Until { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
