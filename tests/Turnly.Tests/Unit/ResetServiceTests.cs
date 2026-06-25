using Microsoft.EntityFrameworkCore;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;
using Turnly.Core.Enums;

namespace Turnly.Tests.Unit;

public class ResetServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 17, 9, 0, 0, TimeSpan.Zero);

    private static async Task<(Guid AdminId, Guid MemberId)> SeedUsersAsync(TestContext ctx)
    {
        var admin = await ctx.Setup.CreateFirstAdminAsync(new SetupRequest("admin", "Admin", "password123", null));
        var member = await ctx.Users.CreateAsync(
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        return (admin.Value!.User.Id, member.Value!.Id);
    }

    private static CreateChoreRequest DailyChore(Guid assignee) =>
        new("Dishes", null, "🍽️", 10, RepeatType.Daily, null, null, null,
            null, null, null, null, 1, false,
            AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate, null, false, null,
            Start, [assignee], assignee, null);

    [Fact]
    public async Task FreshStart_clears_activity_history_and_gacha_but_keeps_chores()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        // Real activity: a completion logs a ChoreCompletion, PointsLogEntry, and ChoreAssignment.
        var chore = (await ctx.Chores.CreateAsync(DailyChore(member))).Value!;
        await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest("done"));

        // Round out the rest of the wiped surfaces + the user fields the reset zeroes.
        var user = (await ctx.Db.Users.FindAsync(member))!;
        user.Points = 123;
        user.Dust = 50;
        user.PullsSinceLegendary = 7;
        user.EquippedFrameKey = "frame-gold-thin";
        user.EquippedThemeKey = "theme-sky";
        user.AvatarColor = "#ef4444";
        ctx.Db.Redemptions.Add(new Redemption
        {
            UserId = member, AwardName = "Ice cream", PointsSpent = 20, Status = RedemptionStatus.Pending,
        });
        ctx.Db.UserAchievements.Add(new UserAchievement { UserId = member, AchievementKey = "test-only-badge" });
        ctx.Db.UserCosmetics.Add(new UserCosmetic { UserId = member, CosmeticKey = "frame-gold-thin", Count = 1 });
        ctx.Db.UserNotifications.Add(new UserNotification { UserId = member, Title = "Reminder", Body = "Do the dishes" });
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Reset.FreshStartAsync();

        Assert.True(result.Succeeded);

        // Every activity / history / gacha-ownership table is empty.
        Assert.Empty(await ctx.Db.ChoreCompletions.ToListAsync());
        Assert.Empty(await ctx.Db.ChoreAssignments.ToListAsync());
        Assert.Empty(await ctx.Db.PointsLog.ToListAsync());
        Assert.Empty(await ctx.Db.Redemptions.ToListAsync());
        Assert.Empty(await ctx.Db.UserAchievements.ToListAsync());
        Assert.Empty(await ctx.Db.UserCosmetics.ToListAsync());
        Assert.Empty(await ctx.Db.UserNotifications.ToListAsync());

        // Balances and equipped cosmetics reset to the free defaults.
        var after = (await ctx.Db.Users.AsNoTracking().FirstAsync(u => u.Id == member));
        Assert.Equal(0, after.Points);
        Assert.Equal(0, after.Dust);
        Assert.Equal(0, after.PullsSinceLegendary);
        Assert.Null(after.EquippedFrameKey);
        Assert.Null(after.EquippedThemeKey);
        Assert.Equal("#6366f1", after.AvatarColor);

        // The chore itself is untouched: still present, same assignee, still scheduled.
        var keptChore = await ctx.Db.Chores.AsNoTracking().FirstAsync(c => c.Id == chore.Id);
        Assert.Equal(member, keptChore.CurrentAssigneeId);
        Assert.NotNull(keptChore.DueAt);
    }

    [Fact]
    public async Task FreshStart_succeeds_on_empty_database()
    {
        using var ctx = new TestContext();
        await SeedUsersAsync(ctx);

        var result = await ctx.Reset.FreshStartAsync();

        Assert.True(result.Succeeded);
    }
}
