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

    [Fact]
    public async Task GetAsync_defaults_the_day_start_to_midnight()
    {
        using var ctx = new TestContext();

        Assert.Equal("00:00", (await ctx.Settings.GetAsync()).DayStart);
    }

    [Fact]
    public async Task SetDayStartAsync_stores_the_time_and_keeps_the_zone()
    {
        using var ctx = new TestContext();
        await ctx.Settings.SetTimeZoneAsync("Europe/Zagreb");

        var result = await ctx.Settings.SetDayStartAsync("05:00");

        Assert.True(result.Succeeded);
        Assert.Equal("05:00", result.Value!.DayStart);
        Assert.Equal("Europe/Zagreb", result.Value.TimeZone);
        Assert.Equal(TimeSpan.FromHours(5), await Turnly.Core.Services.AppSettingsService.ReadDayStartAsync(ctx.Db));
    }

    [Theory]
    [InlineData("5am")]
    [InlineData("12:01")]
    [InlineData("23:00")]
    public async Task SetDayStartAsync_rejects_a_malformed_or_afternoon_time(string value)
    {
        using var ctx = new TestContext();

        var result = await ctx.Settings.SetDayStartAsync(value);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("00:00", (await ctx.Settings.GetAsync()).DayStart);
    }

    [Fact]
    public async Task SetDayStartAsync_resets_to_midnight_with_an_empty_value()
    {
        using var ctx = new TestContext();
        await ctx.Settings.SetDayStartAsync("05:00");

        var result = await ctx.Settings.SetDayStartAsync(null);

        Assert.True(result.Succeeded);
        Assert.Equal("00:00", result.Value!.DayStart);
    }
}
