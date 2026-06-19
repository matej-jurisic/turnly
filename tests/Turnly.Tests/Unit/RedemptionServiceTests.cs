using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;
using Turnly.Core.Enums;

namespace Turnly.Tests.Unit;

public class RedemptionServiceTests
{
    private static async Task<User> SeedUserAsync(TestContext ctx, int points)
    {
        var user = new User { Username = "kid", DisplayName = "Kid", Points = points };
        ctx.Db.Users.Add(user);
        await ctx.Db.SaveChangesAsync();
        return user;
    }

    private static async Task<AwardDto> SeedAwardAsync(TestContext ctx, int cost)
        => (await ctx.Awards.CreateAsync(new CreateAwardRequest("Ice cream", null, "🍦", cost))).Value!;

    [Fact]
    public async Task RedeemAsync_deducts_points_and_logs_a_negative_entry()
    {
        using var ctx = new TestContext();
        var user = await SeedUserAsync(ctx, 100);
        var award = await SeedAwardAsync(ctx, 30);

        var result = await ctx.Redemptions.RedeemAsync(user.Id, award.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(RedemptionStatus.Pending, result.Value!.Status);
        Assert.Equal(30, result.Value.PointsSpent);
        Assert.Equal(70, (await ctx.Db.Users.FindAsync(user.Id))!.Points);

        var log = Assert.Single(await ctx.Db.PointsLog.ToListAsync());
        Assert.Equal(-30, log.Delta);
        Assert.Equal(PointsLogType.Redemption, log.Type);
        Assert.Equal(result.Value.Id, log.RedemptionId);
    }

    [Fact]
    public async Task RedeemAsync_fails_when_balance_too_low()
    {
        using var ctx = new TestContext();
        var user = await SeedUserAsync(ctx, 20);
        var award = await SeedAwardAsync(ctx, 50);

        var result = await ctx.Redemptions.RedeemAsync(user.Id, award.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal(20, (await ctx.Db.Users.FindAsync(user.Id))!.Points);
        Assert.Empty(await ctx.Db.Redemptions.ToListAsync());
    }

    [Fact]
    public async Task FulfillAsync_sets_status_and_timestamp()
    {
        using var ctx = new TestContext();
        var user = await SeedUserAsync(ctx, 100);
        var award = await SeedAwardAsync(ctx, 30);
        var redemption = (await ctx.Redemptions.RedeemAsync(user.Id, award.Id)).Value!;

        var result = await ctx.Redemptions.FulfillAsync(redemption.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(RedemptionStatus.Fulfilled, result.Value!.Status);
        Assert.NotNull(result.Value.FulfilledAt);
    }

    [Fact]
    public async Task FulfillAsync_rejects_already_fulfilled()
    {
        using var ctx = new TestContext();
        var user = await SeedUserAsync(ctx, 100);
        var award = await SeedAwardAsync(ctx, 30);
        var redemption = (await ctx.Redemptions.RedeemAsync(user.Id, award.Id)).Value!;
        await ctx.Redemptions.FulfillAsync(redemption.Id);

        var result = await ctx.Redemptions.FulfillAsync(redemption.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
    }

    [Fact]
    public async Task CancelAsync_refunds_points_and_removes_log_entry()
    {
        using var ctx = new TestContext();
        var user = await SeedUserAsync(ctx, 100);
        var award = await SeedAwardAsync(ctx, 30);
        var redemption = (await ctx.Redemptions.RedeemAsync(user.Id, award.Id)).Value!;

        var result = await ctx.Redemptions.CancelAsync(redemption.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(100, (await ctx.Db.Users.FindAsync(user.Id))!.Points);
        Assert.Empty(await ctx.Db.Redemptions.ToListAsync());
        Assert.Empty(await ctx.Db.PointsLog.ToListAsync());
    }

    [Fact]
    public async Task CancelAsync_rejected_once_fulfilled()
    {
        using var ctx = new TestContext();
        var user = await SeedUserAsync(ctx, 100);
        var award = await SeedAwardAsync(ctx, 30);
        var redemption = (await ctx.Redemptions.RedeemAsync(user.Id, award.Id)).Value!;
        await ctx.Redemptions.FulfillAsync(redemption.Id);

        var result = await ctx.Redemptions.CancelAsync(redemption.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal(70, (await ctx.Db.Users.FindAsync(user.Id))!.Points);
    }

    [Fact]
    public async Task ListAsync_scopes_to_user_unless_admin()
    {
        using var ctx = new TestContext();
        var a = await SeedUserAsync(ctx, 100);
        var b = new User { Username = "sib", DisplayName = "Sib", Points = 100 };
        ctx.Db.Users.Add(b);
        await ctx.Db.SaveChangesAsync();
        var award = await SeedAwardAsync(ctx, 10);
        await ctx.Redemptions.RedeemAsync(a.Id, award.Id);
        await ctx.Redemptions.RedeemAsync(b.Id, award.Id);

        Assert.Single(await ctx.Redemptions.ListAsync(a.Id, includeAll: false));
        Assert.Equal(2, (await ctx.Redemptions.ListAsync(a.Id, includeAll: true)).Count);
    }
}
