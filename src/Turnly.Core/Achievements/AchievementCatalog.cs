namespace Turnly.Core.Achievements;

/// <summary>Per-user metrics an achievement's criterion is evaluated against. Computed once
/// (<see cref="Turnly.Core.Services.AchievementService.ComputeStatsAsync"/>) and shared across the
/// whole catalog so a single user evaluation runs a fixed, small number of queries.</summary>
public record AchievementStats(
    int Completions,
    int MaxStreak,
    int PointsEarned,
    int Redemptions,
    int DistinctChores,
    int DistinctTags);

/// <summary>A code-defined achievement: stable <see cref="Key"/>, presentation, and a pure
/// <see cref="Progress"/> selector returning the user's current value toward <see cref="Threshold"/>.
/// Earned once <c>Progress(stats) &gt;= Threshold</c>. Cosmetic only — no points are awarded.</summary>
public record AchievementDefinition(
    string Key,
    string Name,
    string Description,
    string Emoji,
    string Category,
    int Threshold,
    Func<AchievementStats, int> Progress);

/// <summary>The fixed catalog of achievements. Add entries here to introduce new ones — earning is
/// driven entirely off the <see cref="AchievementDefinition.Progress"/> selectors against
/// <see cref="AchievementStats"/>, so no schema or migration is needed for a new achievement.</summary>
public static class AchievementCatalog
{
    public const string Completions = "Completions";
    public const string Streaks = "Streaks";
    public const string Points = "Points";
    public const string Rewards = "Rewards";
    public const string Variety = "Variety";

    public static readonly IReadOnlyList<AchievementDefinition> All =
    [
        // Completion milestones.
        new("first-completion", "Getting Started", "Complete your first chore.", "🎯", Completions, 1, s => s.Completions),
        new("completions-10", "On a Roll", "Complete 10 chores.", "✅", Completions, 10, s => s.Completions),
        new("completions-50", "Committed", "Complete 50 chores.", "💪", Completions, 50, s => s.Completions),
        new("completions-100", "Centurion", "Complete 100 chores.", "🏆", Completions, 100, s => s.Completions),
        new("completions-500", "Chore Champion", "Complete 500 chores.", "👑", Completions, 500, s => s.Completions),

        // On-time streak milestones (longest current streak across the user's chores).
        new("streak-7", "Warming Up", "Reach a 7-occurrence on-time streak.", "🔥", Streaks, 7, s => s.MaxStreak),
        new("streak-30", "Unstoppable", "Reach a 30-occurrence on-time streak.", "⚡", Streaks, 30, s => s.MaxStreak),
        new("streak-100", "Legendary Streak", "Reach a 100-occurrence on-time streak.", "🌟", Streaks, 100, s => s.MaxStreak),

        // Lifetime points earned.
        new("points-100", "Pocket Change", "Earn 100 points.", "🪙", Points, 100, s => s.PointsEarned),
        new("points-1000", "Big Earner", "Earn 1,000 points.", "💰", Points, 1000, s => s.PointsEarned),
        new("points-10000", "Point Tycoon", "Earn 10,000 points.", "💎", Points, 10000, s => s.PointsEarned),

        // Award redemptions.
        new("first-redemption", "Treat Yourself", "Redeem your first award.", "🎁", Rewards, 1, s => s.Redemptions),
        new("redemptions-10", "Big Spender", "Redeem 10 awards.", "🛍️", Rewards, 10, s => s.Redemptions),

        // Variety.
        new("distinct-chores-10", "Jack of All Chores", "Complete 10 different chores.", "🧹", Variety, 10, s => s.DistinctChores),
        new("distinct-tags-5", "Well-Rounded", "Complete chores across 5 different tags.", "🏷️", Variety, 5, s => s.DistinctTags),
    ];
}
