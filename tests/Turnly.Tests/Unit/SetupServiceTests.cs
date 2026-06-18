using Turnly.Core.Common;
using Turnly.Core.Dtos;
using Turnly.Core.Enums;

namespace Turnly.Tests.Unit;

public class SetupServiceTests
{
    private static SetupRequest Request => new("admin", "Admin", "password123", "#ef4444");

    [Fact]
    public async Task NeedsSetup_is_true_when_no_users_exist()
    {
        using var ctx = new TestContext();
        Assert.True(await ctx.Setup.NeedsSetupAsync());
    }

    [Fact]
    public async Task CreateFirstAdmin_creates_an_admin_and_issues_tokens()
    {
        using var ctx = new TestContext();

        var result = await ctx.Setup.CreateFirstAdminAsync(Request);

        Assert.True(result.Succeeded);
        Assert.Equal(UserRole.Admin, result.Value!.User.Role);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.Value.RefreshToken));
        Assert.False(await ctx.Setup.NeedsSetupAsync());
    }

    [Fact]
    public async Task CreateFirstAdmin_is_rejected_once_a_user_exists()
    {
        using var ctx = new TestContext();
        await ctx.Setup.CreateFirstAdminAsync(Request);

        var second = await ctx.Setup.CreateFirstAdminAsync(new SetupRequest("other", "Other", "password123", null));

        Assert.False(second.Succeeded);
        Assert.Equal(ErrorType.Conflict, second.Error!.Type);
    }

    [Fact]
    public async Task CreateFirstAdmin_validates_password_length()
    {
        using var ctx = new TestContext();

        var result = await ctx.Setup.CreateFirstAdminAsync(new SetupRequest("admin", "Admin", "12", null));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }
}
