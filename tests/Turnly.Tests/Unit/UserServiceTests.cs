using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Dtos;
using Turnly.Core.Enums;

namespace Turnly.Tests.Unit;

public class UserServiceTests
{
    private static CreateUserRequest NewUser(string username, UserRole role = UserRole.Member) =>
        new(username, $"Display {username}", "password123", role, null);

    private static async Task<Guid> SeedAdminAsync(TestContext ctx, string username = "admin")
    {
        var result = await ctx.Setup.CreateFirstAdminAsync(
            new SetupRequest(username, "Admin", "password123", null));
        return result.Value!.User.Id;
    }

    [Fact]
    public async Task CreateAsync_rejects_duplicate_username()
    {
        using var ctx = new TestContext();
        await SeedAdminAsync(ctx);
        await ctx.Users.CreateAsync(NewUser("bob"));

        var result = await ctx.Users.CreateAsync(NewUser("bob"));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
    }

    [Fact]
    public async Task CreateAsync_rejects_short_password()
    {
        using var ctx = new TestContext();
        var result = await ctx.Users.CreateAsync(new CreateUserRequest("bob", "Bob", "123", UserRole.Member, null));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task DeleteAsync_blocks_deleting_self()
    {
        using var ctx = new TestContext();
        var adminId = await SeedAdminAsync(ctx);

        var result = await ctx.Users.DeleteAsync(adminId, actingUserId: adminId);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
    }

    [Fact]
    public async Task DeleteAsync_blocks_deleting_the_last_admin()
    {
        using var ctx = new TestContext();
        var adminId = await SeedAdminAsync(ctx);
        var otherId = (await ctx.Users.CreateAsync(NewUser("member"))).Value!.Id;

        // 'other' (a member) tries to delete the sole admin.
        var result = await ctx.Users.DeleteAsync(adminId, actingUserId: otherId);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
    }

    [Fact]
    public async Task DeleteAsync_removes_member_when_an_admin_remains()
    {
        using var ctx = new TestContext();
        var adminId = await SeedAdminAsync(ctx);
        var memberId = (await ctx.Users.CreateAsync(NewUser("member"))).Value!.Id;

        var result = await ctx.Users.DeleteAsync(memberId, actingUserId: adminId);

        Assert.True(result.Succeeded);
        Assert.Null(await ctx.Db.Users.FindAsync(memberId));
    }

    [Fact]
    public async Task UpdateAsync_blocks_demoting_the_last_admin()
    {
        using var ctx = new TestContext();
        var adminId = await SeedAdminAsync(ctx);

        var result = await ctx.Users.UpdateAsync(adminId, new UpdateUserRequest("Admin", "#000000", UserRole.Member));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
    }

    [Fact]
    public async Task UpdateAsync_allows_demotion_when_another_admin_exists()
    {
        using var ctx = new TestContext();
        var firstAdminId = await SeedAdminAsync(ctx);
        await ctx.Users.CreateAsync(NewUser("admin2", UserRole.Admin));

        var result = await ctx.Users.UpdateAsync(firstAdminId, new UpdateUserRequest("Admin", "#000000", UserRole.Member));

        Assert.True(result.Succeeded);
        Assert.Equal(UserRole.Member, result.Value!.Role);
    }

    [Fact]
    public async Task SetPasswordAsync_revokes_existing_refresh_tokens()
    {
        using var ctx = new TestContext();
        var adminId = await SeedAdminAsync(ctx);
        await ctx.Auth.LoginAsync("admin", "password123"); // creates an active refresh token

        var result = await ctx.Users.SetPasswordAsync(adminId, "brand-new-password");

        Assert.True(result.Succeeded);
        var anyActive = await ctx.Db.RefreshTokens.AnyAsync(t => t.UserId == adminId && t.RevokedAt == null);
        Assert.False(anyActive);
    }

    // ── Per-user freeze ─────────────────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset ChoreStart = new(2026, 6, 17, 9, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> SeedRotatingChoreAsync(TestContext ctx, Guid currentAssignee, Guid[] assignees) =>
        (await ctx.Chores.CreateAsync(new CreateChoreRequest(
            "Dishes", null, "🍽️", 10, RepeatType.Daily, null, null, null,
            null, null, null, null, 1, false,
            AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate, null, false, null,
            ChoreStart, assignees, currentAssignee, null))).Value!.Id;

    [Fact]
    public async Task FreezeAsync_sets_frozen_flag()
    {
        using var ctx = new TestContext();
        var adminId = await SeedAdminAsync(ctx);

        var result = await ctx.Users.FreezeAsync(adminId);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.IsFrozen);
        Assert.True((await ctx.Db.Users.FindAsync(adminId))!.IsFrozen);
    }

    [Fact]
    public async Task FreezeAsync_returns_not_found_for_unknown_user()
    {
        using var ctx = new TestContext();

        var result = await ctx.Users.FreezeAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task FreezeAsync_reassigns_current_rotating_chores()
    {
        using var ctx = new TestContext();
        var adminId = await SeedAdminAsync(ctx);
        var memberId = (await ctx.Users.CreateAsync(NewUser("bob"))).Value!.Id;
        var choreId = await SeedRotatingChoreAsync(ctx, adminId, [adminId, memberId]);

        await ctx.Users.FreezeAsync(adminId);

        var chore = await ctx.Db.Chores.FindAsync(choreId);
        Assert.NotEqual(adminId, chore!.CurrentAssigneeId);
        Assert.Equal(memberId, chore.CurrentAssigneeId);
    }

    [Fact]
    public async Task FreezeAsync_leaves_chore_unassigned_when_no_eligible_assignee_remains()
    {
        using var ctx = new TestContext();
        var adminId = await SeedAdminAsync(ctx);
        // Admin is the only assignee — nobody else to reassign to
        var choreId = await SeedRotatingChoreAsync(ctx, adminId, [adminId]);

        await ctx.Users.FreezeAsync(adminId);

        var chore = await ctx.Db.Chores.FindAsync(choreId);
        Assert.Null(chore!.CurrentAssigneeId);
    }

    [Fact]
    public async Task GetFreezePreviewAsync_returns_reassignments()
    {
        using var ctx = new TestContext();
        var adminId = await SeedAdminAsync(ctx);
        var memberId = (await ctx.Users.CreateAsync(NewUser("bob"))).Value!.Id;
        var choreId = await SeedRotatingChoreAsync(ctx, adminId, [adminId, memberId]);

        var result = await ctx.Users.GetFreezePreviewAsync(adminId);

        Assert.True(result.Succeeded);
        var preview = result.Value!;
        Assert.Single(preview.Reassignments);
        Assert.Equal(choreId, preview.Reassignments[0].ChoreId);
        Assert.Equal(memberId, preview.Reassignments[0].NewAssigneeId);
        Assert.Empty(preview.Unassignable);
    }

    [Fact]
    public async Task GetFreezePreviewAsync_flags_unassignable_chores()
    {
        using var ctx = new TestContext();
        var adminId = await SeedAdminAsync(ctx);
        var choreId = await SeedRotatingChoreAsync(ctx, adminId, [adminId]);

        var result = await ctx.Users.GetFreezePreviewAsync(adminId);

        Assert.True(result.Succeeded);
        var preview = result.Value!;
        Assert.Empty(preview.Reassignments);
        Assert.Single(preview.Unassignable);
        Assert.Equal(choreId, preview.Unassignable[0].ChoreId);
    }

    [Fact]
    public async Task UnfreezeAsync_clears_frozen_flag()
    {
        using var ctx = new TestContext();
        var adminId = await SeedAdminAsync(ctx);
        await ctx.Users.FreezeAsync(adminId);

        var result = await ctx.Users.UnfreezeAsync(adminId);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.IsFrozen);
    }

    [Fact]
    public async Task UnfreezeAsync_advances_stale_independent_tracks()
    {
        using var ctx = new TestContext();
        var adminId = await SeedAdminAsync(ctx);
        var memberId = (await ctx.Users.CreateAsync(NewUser("bob"))).Value!.Id;
        var req = new CreateChoreRequest(
            "Dishes", null, "🍽️", 10, RepeatType.Daily, null, null, null,
            null, null, null, null, 1, false,
            AssignmentStrategy.Independent, SchedulingPreference.FromScheduledDate, null, false, null,
            ChoreStart, [adminId, memberId], adminId, null, null, null,
            [new TrackInput(adminId, 1), new TrackInput(memberId, 1)]);
        await ctx.Chores.CreateAsync(req);
        await ctx.Users.FreezeAsync(adminId);

        var result = await ctx.Users.UnfreezeAsync(adminId);

        Assert.True(result.Succeeded);
        // Admin's track (which was at ChoreStart, in the past) should now be >= now
        var tracks = await ctx.Db.ChoreAssigneeTracks.Where(t => t.UserId == adminId).ToListAsync();
        Assert.All(tracks, t => Assert.True(t.DueAt >= DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task UnfreezeAsync_does_not_affect_rotating_chore_due_at()
    {
        using var ctx = new TestContext();
        var adminId = await SeedAdminAsync(ctx);
        var memberId = (await ctx.Users.CreateAsync(NewUser("bob"))).Value!.Id;
        var choreId = await SeedRotatingChoreAsync(ctx, adminId, [adminId, memberId]);
        var originalDue = (await ctx.Db.Chores.FindAsync(choreId))!.DueAt;
        await ctx.Users.FreezeAsync(adminId);

        await ctx.Users.UnfreezeAsync(adminId);

        var chore = await ctx.Db.Chores.FindAsync(choreId);
        Assert.Equal(originalDue, chore!.DueAt);
    }
}
