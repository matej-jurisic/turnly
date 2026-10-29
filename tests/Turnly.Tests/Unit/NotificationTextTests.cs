using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Dtos;
using Turnly.Core.Enums;
using Turnly.Core.Notifications;

namespace Turnly.Tests.Unit;

public class NotificationTextTests
{
    private static readonly NotificationText Hr = NotificationText.Croatian;
    private static readonly NotificationText En = NotificationText.English;

    [Fact]
    public void For_falls_back_to_english()
    {
        Assert.Same(Hr, NotificationText.For("hr"));
        Assert.Same(En, NotificationText.For(null));
        Assert.Same(En, NotificationText.For("de"));
    }

    [Theory]
    [InlineData(1, "za 1 bod")]
    [InlineData(3, "za 3 boda")]
    [InlineData(5, "za 5 bodova")]
    [InlineData(12, "za 12 bodova")]
    [InlineData(21, "za 21 bod")]
    [InlineData(22, "za 22 boda")]
    public void Croatian_offer_declines_the_bounty(int bounty, string expected) =>
        Assert.Contains(expected, Hr.OfferOpen("Ana", bounty));

    [Theory]
    [InlineData(1, "i vaš 1 bod")]
    [InlineData(2, "i vaša 2 boda")]
    [InlineData(11, "i vaših 11 bodova")]
    public void Croatian_claim_declines_the_bounty(int bounty, string expected) =>
        Assert.Contains(expected, Hr.OfferClaimed("Ana", bounty));

    [Theory]
    [InlineData(1, "rok za 1 minutu")]
    [InlineData(3, "rok za 3 minute")]
    [InlineData(15, "rok za 15 minuta")]
    [InlineData(120, "rok za 2 sata")]
    [InlineData(300, "rok za 5 sati")]
    [InlineData(1440, "rok za 1 dan")]
    public void Croatian_durations_take_the_accusative(int minutes, string expected) =>
        Assert.Equal(expected, Hr.DueIn(null, TimeSpan.FromMinutes(minutes), 0));

    [Fact]
    public void Croatian_messages_read_in_full()
    {
        Assert.Equal("Ana i Ben - kasni 2 dana (10 bod.)", Hr.Overdue(Hr.People(["Ana", "Ben"]), TimeSpan.FromDays(2), 10));
        Assert.Equal("Obavljeno umjesto vas: Ana, Ben i Cy.", Hr.Covered(Hr.People(["Ana", "Ben", "Cy"])));
        Assert.Equal("Ana vas podsjeća: Smeće večeras", Hr.Nudge("Ana", "Smeće večeras"));
        Assert.Equal("Ana preuzima ovaj posao. Više nije na vama.", Hr.OfferClaimed("Ana", 0));
    }

    [Fact]
    public async Task NotifyUsersAsync_words_each_recipient_in_their_own_language()
    {
        using var ctx = new TestContext();
        var admin = (await ctx.Setup.CreateFirstAdminAsync(new SetupRequest("admin", "Admin", "password123", null))).Value!.User.Id;
        var member = (await ctx.Users.CreateAsync(new CreateUserRequest("ana", "Ana", "password1", UserRole.Member, null))).Value!.Id;
        Assert.True((await ctx.Users.SetLanguageAsync(member, new SetLanguageRequest("hr"))).Succeeded);

        await ctx.Notifications.NotifyUsersAsync([admin, member], "Dishes",
            text => text.Nudge("Ben", null), choreId: null, NotificationCategory.Nudge);
        await ctx.Db.SaveChangesAsync();

        var bodies = await ctx.Db.UserNotifications.ToDictionaryAsync(n => n.UserId, n => n.Body);
        Assert.Equal("Ben nudged you about this chore.", bodies[admin]);
        Assert.Equal("Ben vas podsjeća na ovaj posao.", bodies[member]);
    }

    [Fact]
    public async Task SetLanguageAsync_rejects_an_unknown_language()
    {
        using var ctx = new TestContext();
        var admin = (await ctx.Setup.CreateFirstAdminAsync(new SetupRequest("admin", "Admin", "password123", null))).Value!.User.Id;

        var result = await ctx.Users.SetLanguageAsync(admin, new SetLanguageRequest("de"));

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }
}
