using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;

namespace Turnly.Tests.Unit;

public class AwardServiceTests
{
    private static CreateAwardRequest NewAward(string name = "Ice cream", int cost = 50) =>
        new(name, "A tasty treat", "🍦", cost);

    [Fact]
    public async Task CreateAsync_rejects_blank_name()
    {
        using var ctx = new TestContext();

        var result = await ctx.Awards.CreateAsync(NewAward() with { Name = "  " });

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task CreateAsync_rejects_non_positive_cost()
    {
        using var ctx = new TestContext();

        var result = await ctx.Awards.CreateAsync(NewAward(cost: 0));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task CreateAsync_trims_and_persists()
    {
        using var ctx = new TestContext();

        var result = await ctx.Awards.CreateAsync(NewAward(name: "  Movie night  ", cost: 100));

        Assert.True(result.Succeeded);
        Assert.Equal("Movie night", result.Value!.Name);
        Assert.Equal(100, result.Value.Cost);
        Assert.Single(await ctx.Db.Awards.ToListAsync());
    }

    [Fact]
    public async Task UpdateAsync_changes_fields()
    {
        using var ctx = new TestContext();
        var created = (await ctx.Awards.CreateAsync(NewAward())).Value!;

        var result = await ctx.Awards.UpdateAsync(created.Id,
            new UpdateAwardRequest("Double scoop", null, "🍨", 75));

        Assert.True(result.Succeeded);
        Assert.Equal("Double scoop", result.Value!.Name);
        Assert.Null(result.Value.Description);
        Assert.Equal(75, result.Value.Cost);
    }

    [Fact]
    public async Task DeleteAsync_keeps_existing_redemptions()
    {
        using var ctx = new TestContext();
        var user = new User { Username = "kid", DisplayName = "Kid", Points = 100 };
        ctx.Db.Users.Add(user);
        await ctx.Db.SaveChangesAsync();
        var award = (await ctx.Awards.CreateAsync(NewAward(cost: 50))).Value!;
        var redeemed = await ctx.Redemptions.RedeemAsync(user.Id, award.Id);
        Assert.True(redeemed.Succeeded);

        var deleted = await ctx.Awards.DeleteAsync(award.Id);

        Assert.True(deleted.Succeeded);
        Assert.Empty(await ctx.Db.Awards.ToListAsync());
        var redemption = Assert.Single(await ctx.Db.Redemptions.ToListAsync());
        Assert.Null(redemption.AwardId);          // FK nulled
        Assert.Equal("Ice cream", redemption.AwardName); // snapshot survives
    }
}
