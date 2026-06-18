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
}
