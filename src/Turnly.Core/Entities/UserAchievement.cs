namespace Turnly.Core.Entities;

/// <summary>Records that a user has earned a catalog achievement. The achievement definitions
/// themselves live in code (<see cref="Turnly.Core.Achievements.AchievementCatalog"/>); this row is
/// just the per-user "unlocked" marker, keyed by the definition's stable <see cref="AchievementKey"/>.
/// One row per (user, achievement) — earned achievements are permanent and never revoked.</summary>
public class UserAchievement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Stable key of the earned achievement (matches an <c>AchievementDefinition.Key</c>).</summary>
    public required string AchievementKey { get; set; }

    public DateTimeOffset EarnedAt { get; set; } = DateTimeOffset.UtcNow;
}
