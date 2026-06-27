using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Cosmetics;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;
using Turnly.Core.Enums;

namespace Turnly.Tests.Unit;

public class GachaServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 9, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> SeedUserAsync(TestContext ctx, int points = 0, int dust = 0)
    {
        var admin = await ctx.Setup.CreateFirstAdminAsync(new SetupRequest("admin", "Admin", "password123", null));
        var id = admin.Value!.User.Id;
        var u = await ctx.Db.Users.FindAsync(id);
        u!.Points = points;
        u.Dust = dust;
        await ctx.Db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task Pull_deducts_points()
    {
        using var ctx = new TestContext();
        var id = await SeedUserAsync(ctx, points: 100);

        var result = await ctx.Gacha.PullAsync(id, 1, Now);

        Assert.True(result.Succeeded);
        var u = await ctx.Db.Users.FindAsync(id);
        Assert.Equal(100 - CosmeticCatalog.PullCost, u!.Points);
    }

    [Fact]
    public async Task Pull_fails_when_short_on_points()
    {
        using var ctx = new TestContext();
        var id = await SeedUserAsync(ctx, points: 10);

        var result = await ctx.Gacha.PullAsync(id, 1, Now);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
    }

    [Fact]
    public async Task Pity_forces_a_legendary_and_resets()
    {
        using var ctx = new TestContext();
        var id = await SeedUserAsync(ctx, points: 100);
        var u = await ctx.Db.Users.FindAsync(id);
        u!.PullsSinceLegendary = CosmeticCatalog.PityThreshold - 1;
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Gacha.PullAsync(id, 1, Now);

        Assert.True(result.Succeeded);
        Assert.Equal(CosmeticRarity.Legendary, result.Value!.Single().Cosmetic.Rarity);
        var after = await ctx.Db.Users.FindAsync(id);
        Assert.Equal(0, after!.PullsSinceLegendary);
    }

    [Fact]
    public async Task Duplicate_pull_awards_dust_and_no_new_row()
    {
        using var ctx = new TestContext();
        var id = await SeedUserAsync(ctx, points: 100);
        // Pre-own every Legendary cosmetic, then force a Legendary pull via pity, so the roll must dupe.
        foreach (var def in CosmeticCatalog.All.Where(c => c.Rarity == CosmeticRarity.Legendary))
            ctx.Db.UserCosmetics.Add(new UserCosmetic { UserId = id, CosmeticKey = def.Key });
        var u = await ctx.Db.Users.FindAsync(id);
        u!.PullsSinceLegendary = CosmeticCatalog.PityThreshold - 1;
        await ctx.Db.SaveChangesAsync();

        var ownedBefore = await ctx.Db.UserCosmetics.CountAsync(c => c.UserId == id);
        var result = await ctx.Gacha.PullAsync(id, 1, Now);

        Assert.True(result.Succeeded);
        var roll = result.Value!.Single();
        Assert.False(roll.IsNew);
        Assert.Equal(CosmeticCatalog.Config(CosmeticRarity.Legendary).DustAward, roll.DustAwarded);
        var ownedAfter = await ctx.Db.UserCosmetics.CountAsync(c => c.UserId == id);
        Assert.Equal(ownedBefore, ownedAfter);
        var after = await ctx.Db.Users.FindAsync(id);
        Assert.Equal(roll.DustAwarded, after!.Dust);
    }

    [Fact]
    public async Task Craft_spends_dust_and_grants_the_cosmetic()
    {
        using var ctx = new TestContext();
        const string key = "frame-liquid-gold";
        var cost = CosmeticCatalog.Config(CosmeticRarity.Legendary).DustCraftCost;
        var id = await SeedUserAsync(ctx, dust: cost);

        var result = await ctx.Gacha.CraftAsync(id, key);

        Assert.True(result.Succeeded);
        var u = await ctx.Db.Users.FindAsync(id);
        Assert.Equal(0, u!.Dust);
        Assert.True(await ctx.Db.UserCosmetics.AnyAsync(c => c.UserId == id && c.CosmeticKey == key));
    }

    [Fact]
    public async Task Craft_fails_when_short_on_dust()
    {
        using var ctx = new TestContext();
        var id = await SeedUserAsync(ctx, dust: 5);

        var result = await ctx.Gacha.CraftAsync(id, "frame-liquid-gold");

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
    }

    [Fact]
    public async Task Craft_fails_when_already_owned()
    {
        using var ctx = new TestContext();
        const string key = "frame-gold-thin";
        var id = await SeedUserAsync(ctx, dust: 1000);
        ctx.Db.UserCosmetics.Add(new UserCosmetic { UserId = id, CosmeticKey = key });
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Gacha.CraftAsync(id, key);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
    }

    [Fact]
    public async Task Equip_rejects_an_unowned_cosmetic()
    {
        using var ctx = new TestContext();
        var id = await SeedUserAsync(ctx);

        var result = await ctx.Gacha.EquipAsync(id, CosmeticSlot.Frame, "frame-gold-thin");

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Forbidden, result.Error!.Type);
    }

    [Fact]
    public async Task Equip_then_unequip_a_frame()
    {
        using var ctx = new TestContext();
        const string key = "frame-gold-thin";
        var id = await SeedUserAsync(ctx);
        ctx.Db.UserCosmetics.Add(new UserCosmetic { UserId = id, CosmeticKey = key });
        await ctx.Db.SaveChangesAsync();

        Assert.True((await ctx.Gacha.EquipAsync(id, CosmeticSlot.Frame, key)).Succeeded);
        var u = await ctx.Db.Users.FindAsync(id);
        Assert.Equal(key, u!.EquippedFrameKey);

        Assert.True((await ctx.Gacha.EquipAsync(id, CosmeticSlot.Frame, null)).Succeeded);
        Assert.Null(u.EquippedFrameKey);
    }

    [Fact]
    public async Task Default_color_is_owned_without_a_row_and_equippable()
    {
        using var ctx = new TestContext();
        var id = await SeedUserAsync(ctx);

        // The default purple is owned by everyone (no UserCosmetic row needed) and rolls never grant it.
        var state = (await ctx.Gacha.GetStateAsync(id)).Value!;
        var indigo = state.Cosmetics.Single(c => c.Key == "color-indigo");
        Assert.True(indigo.Owned);

        var result = await ctx.Gacha.EquipAsync(id, CosmeticSlot.Color, "color-indigo");
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Equip_color_sets_the_avatar_color()
    {
        using var ctx = new TestContext();
        const string key = "color-amber";
        var id = await SeedUserAsync(ctx);
        ctx.Db.UserCosmetics.Add(new UserCosmetic { UserId = id, CosmeticKey = key });
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Gacha.EquipAsync(id, CosmeticSlot.Color, key);

        Assert.True(result.Succeeded);
        var u = await ctx.Db.Users.FindAsync(id);
        Assert.Equal("#f59e0b", u!.AvatarColor);
    }

    [Fact]
    public async Task Equip_rejects_an_unowned_color()
    {
        using var ctx = new TestContext();
        var id = await SeedUserAsync(ctx);

        var result = await ctx.Gacha.EquipAsync(id, CosmeticSlot.Color, "color-rose");

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Forbidden, result.Error!.Type);
    }

    [Fact]
    public async Task Equip_rejects_a_slot_mismatch()
    {
        using var ctx = new TestContext();
        const string key = "frame-gold-thin"; // a Frame, not a Theme
        var id = await SeedUserAsync(ctx);
        ctx.Db.UserCosmetics.Add(new UserCosmetic { UserId = id, CosmeticKey = key });
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Gacha.EquipAsync(id, CosmeticSlot.Theme, key);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }
}
