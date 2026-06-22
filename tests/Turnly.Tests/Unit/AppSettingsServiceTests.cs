using Turnly.Core.Common;

namespace Turnly.Tests.Unit;

public class AppSettingsServiceTests
{
    [Fact]
    public async Task GetAsync_defaults_to_unset_with_the_server_zone()
    {
        using var ctx = new TestContext();

        var dto = await ctx.Settings.GetAsync();

        Assert.Null(dto.TimeZone);
        Assert.False(string.IsNullOrWhiteSpace(dto.ServerTimeZone));
    }

    [Fact]
    public async Task SetTimeZoneAsync_stores_a_valid_zone()
    {
        using var ctx = new TestContext();

        var result = await ctx.Settings.SetTimeZoneAsync("Europe/Zagreb");

        Assert.True(result.Succeeded);
        Assert.Equal("Europe/Zagreb", result.Value!.TimeZone);
        Assert.Equal("Europe/Zagreb", await ctx.Settings.GetTimeZoneIdAsync());
    }

    [Fact]
    public async Task SetTimeZoneAsync_rejects_an_unknown_zone()
    {
        using var ctx = new TestContext();

        var result = await ctx.Settings.SetTimeZoneAsync("Mars/Phobos");

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Null(await ctx.Settings.GetTimeZoneIdAsync());
    }

    [Fact]
    public async Task SetTimeZoneAsync_clears_with_an_empty_value()
    {
        using var ctx = new TestContext();
        await ctx.Settings.SetTimeZoneAsync("Europe/Zagreb");

        var result = await ctx.Settings.SetTimeZoneAsync("");

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.TimeZone);
        Assert.Null(await ctx.Settings.GetTimeZoneIdAsync());
    }
}
