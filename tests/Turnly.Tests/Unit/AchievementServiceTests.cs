using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Dtos;
using Turnly.Core.Enums;

namespace Turnly.Tests.Unit;

public class AchievementServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 9, 0, 0, TimeSpan.Zero);

    private static async Task<(Guid AdminId, Guid MemberId)> SeedUsersAsync(TestContext ctx)
    {
        var admin = await ctx.Setup.CreateFirstAdminAsync(new SetupRequest("admin", "Admin", "password123", null));
        var member = await ctx.Users.CreateAsync(
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        return (admin.Value!.User.Id, member.Value!.Id);
    }

    private static CreateChoreRequest DailyChore(Guid assignee, int points = 10, string[]? tags = null) =>
        new("Dishes", null, "🍽️", points, RepeatType.Daily, null, null, null,
            null, null, null, null, 1, false,
            AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate, null, false, null,
            Now, [assignee], assignee, tags);

    [Fact]
    public async Task Completing_a_chore_grants_first_completion_inline()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(DailyChore(member))).Value!;

        // No separate evaluate call — the completion itself grants the achievement.
        await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));

        var first = (await ctx.Achievements.ListForUserAsync(member)).Single(a => a.Key == "first-completion");
        Assert.True(first.Earned);
        Assert.NotNull(first.EarnedAt);
    }

    [Fact]
    public async Task Unlocking_writes_an_inbox_notification()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(DailyChore(member))).Value!;

        await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));

        var inbox = await ctx.Notifications.ListInboxAsync(member);
        Assert.Contains(inbox, n => n.Title.Contains("Achievement unlocked") && n.Body.Contains("Getting Started"));
    }

    [Fact]
    public async Task An_achievement_is_granted_and_notified_only_once()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(DailyChore(member))).Value!;

        // Complete two occurrences; "first-completion" should still only exist once.
        await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));
        await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));

        var rows = await ctx.Db.UserAchievements
            .CountAsync(a => a.UserId == member && a.AchievementKey == "first-completion");
        Assert.Equal(1, rows);

        var inbox = await ctx.Notifications.ListInboxAsync(member);
        Assert.Equal(1, inbox.Count(n => n.Body.Contains("Getting Started")));
    }

    [Fact]
    public async Task Redeeming_grants_the_first_redemption_achievement()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        await ctx.Users.AdjustPointsAsync(member, new AdjustPointsRequest(100, "seed"));
        var award = (await ctx.Awards.CreateAsync(new CreateAwardRequest("Ice cream", null, "🍦", 50))).Value!;

        await ctx.Redemptions.RedeemAsync(member, award.Id);

        var redeem = (await ctx.Achievements.ListForUserAsync(member)).Single(a => a.Key == "first-redemption");
        Assert.True(redeem.Earned);
    }

    [Fact]
    public async Task Admin_point_grant_unlocks_a_points_milestone()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        await ctx.Users.AdjustPointsAsync(member, new AdjustPointsRequest(150, "bonus"));

        var hundred = (await ctx.Achievements.ListForUserAsync(member)).Single(a => a.Key == "points-100");
        Assert.True(hundred.Earned);
        Assert.Equal(100, hundred.Progress); // clamped to the threshold
    }

    [Fact]
    public async Task Undo_does_not_revoke_an_earned_badge_permanent_model()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(DailyChore(member))).Value!;

        var dto = (await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null))).Value!;
        await ctx.Chores.UndoCompletionAsync(dto.LastCompletion!.Id, member);

        // The completion (and its points) are reversed, but the badge stays earned.
        var first = (await ctx.Achievements.ListForUserAsync(member)).Single(a => a.Key == "first-completion");
        Assert.True(first.Earned);
        Assert.Equal(0, first.Progress); // live progress reflects the reversal
    }

    [Fact]
    public async Task RevokeAsync_removes_an_earned_badge()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(DailyChore(member))).Value!;
        await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));

        var result = await ctx.Achievements.RevokeAsync(member, "first-completion");

        Assert.True(result.Succeeded);
        var first = (await ctx.Achievements.ListForUserAsync(member)).Single(a => a.Key == "first-completion");
        Assert.False(first.Earned);
    }

    [Fact]
    public async Task RevokeAsync_returns_not_found_when_not_earned()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        var result = await ctx.Achievements.RevokeAsync(member, "first-completion");

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task ListForUserAsync_reports_progress_for_unearned_milestones()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(DailyChore(member))).Value!;

        await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));

        var ten = (await ctx.Achievements.ListForUserAsync(member)).Single(a => a.Key == "completions-10");
        Assert.False(ten.Earned);
        Assert.Equal(1, ten.Progress);
        Assert.Equal(10, ten.Threshold);
    }

    [Fact]
    public async Task PointsEarned_counts_grants_not_spends()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        await ctx.Users.AdjustPointsAsync(member, new AdjustPointsRequest(150, "grant"));
        await ctx.Users.AdjustPointsAsync(member, new AdjustPointsRequest(-50, "spend"));

        var stats = await ctx.Achievements.ComputeStatsAsync(member);
        Assert.Equal(150, stats.PointsEarned); // negative deltas excluded
    }

    [Fact]
    public async Task ComputeStats_counts_distinct_chores_and_tags()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var a = (await ctx.Chores.CreateAsync(DailyChore(member, tags: ["kitchen"]))).Value!;
        var b = (await ctx.Chores.CreateAsync(DailyChore(member, tags: ["outdoor"]) with { Name = "Trash" })).Value!;

        await ctx.Chores.CompleteAsync(a.Id, member, new CompleteChoreRequest(null));
        await ctx.Chores.CompleteAsync(b.Id, member, new CompleteChoreRequest(null));

        var stats = await ctx.Achievements.ComputeStatsAsync(member);
        Assert.Equal(2, stats.DistinctChores);
        Assert.Equal(2, stats.DistinctTags);
    }
}
