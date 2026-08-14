using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;
using Turnly.Core.Enums;

namespace Turnly.Tests.Unit;

/// <summary>Covers the per-user notification controls end to end through the service: category
/// preferences, per-chore mutes, and their interaction with quiet hours on both send paths.</summary>
public class NotificationPreferenceTests
{
    private static readonly DateTimeOffset Due = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(Guid AdminId, Guid MemberId)> SeedUsersAsync(TestContext ctx)
    {
        var admin = await ctx.Setup.CreateFirstAdminAsync(new SetupRequest("admin", "Admin", "password123", null));
        var member = await ctx.Users.CreateAsync(
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        return (admin.Value!.User.Id, member.Value!.Id);
    }

    private static async Task<Guid> SeedChoreAsync(
        TestContext ctx, Guid currentAssignee, Guid[] assignees, NotificationType type = NotificationType.Due)
    {
        var notification = new ChoreNotificationInput(
            type, NotificationTiming.AtDue, 0, NotificationOffsetUnit.Minutes, NotificationRecipients.CurrentAssignee);
        var req = new CreateChoreRequest(
            "Dishes", null, "🍽️", 10, RepeatType.Daily, null, null, null,
            null, null, null, null, 1, false,
            AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate, null, false, null,
            Due.AddDays(-1), assignees, currentAssignee, null, [notification]);

        var result = await ctx.Chores.CreateAsync(req);
        Assert.True(result.Succeeded);

        var chore = await ctx.Db.Chores.FirstAsync(c => c.Id == result.Value!.Id);
        chore.DueAt = Due;
        await ctx.Db.SaveChangesAsync();
        return chore.Id;
    }

    private static Task SubscribeAsync(TestContext ctx, Guid userId, string endpoint = "https://push.example/sub-1") =>
        ctx.Notifications.SubscribeAsync(userId, new PushSubscribeRequest(endpoint, "p256", "auth"));

    // --- Preference matrix ---------------------------------------------------------------------

    [Fact]
    public async Task ListPreferencesAsync_returns_every_category_at_its_default()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        var prefs = await ctx.Notifications.ListPreferencesAsync(member);

        Assert.Equal(Enum.GetValues<NotificationCategory>().Length, prefs.Count);
        Assert.All(prefs, p => Assert.Equal(DeliveryChannel.PushAndInbox, p.Channel));
        Assert.False(prefs.Single(p => p.Category == NotificationCategory.System).Configurable);
    }

    [Fact]
    public async Task UpdatePreferenceAsync_stores_a_non_default_choice()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        var result = await ctx.Notifications.UpdatePreferenceAsync(member,
            new UpdateNotificationPreferenceRequest(NotificationCategory.ChoreOverdue, DeliveryChannel.Off));

        Assert.True(result.Succeeded);
        Assert.Equal(DeliveryChannel.Off,
            result.Value!.Single(p => p.Category == NotificationCategory.ChoreOverdue).Channel);
        Assert.Single(await ctx.Db.UserNotificationPreferences.ToListAsync());
    }

    [Fact]
    public async Task UpdatePreferenceAsync_back_to_default_removes_the_row()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        await ctx.Notifications.UpdatePreferenceAsync(member,
            new UpdateNotificationPreferenceRequest(NotificationCategory.ChoreDue, DeliveryChannel.Off));
        await ctx.Notifications.UpdatePreferenceAsync(member,
            new UpdateNotificationPreferenceRequest(NotificationCategory.ChoreDue, DeliveryChannel.PushAndInbox));

        // Storage stays sparse: "never touched it" and "set it back" converge on no row at all.
        Assert.Empty(await ctx.Db.UserNotificationPreferences.ToListAsync());
    }

    [Fact]
    public async Task UpdatePreferenceAsync_rejects_silencing_the_system_category()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        var result = await ctx.Notifications.UpdatePreferenceAsync(member,
            new UpdateNotificationPreferenceRequest(NotificationCategory.System, DeliveryChannel.Off));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task Preferences_are_per_user()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);

        await ctx.Notifications.UpdatePreferenceAsync(member,
            new UpdateNotificationPreferenceRequest(NotificationCategory.ChoreDue, DeliveryChannel.Off));

        var adminPrefs = await ctx.Notifications.ListPreferencesAsync(admin);
        Assert.Equal(DeliveryChannel.PushAndInbox,
            adminPrefs.Single(p => p.Category == NotificationCategory.ChoreDue).Channel);
    }

    // --- Preferences applied to the scheduled path ----------------------------------------------

    [Fact]
    public async Task ProcessDueAsync_inbox_only_preference_suppresses_the_push()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        await SeedChoreAsync(ctx, member, [member]);
        await SubscribeAsync(ctx, member);
        await ctx.Notifications.UpdatePreferenceAsync(member,
            new UpdateNotificationPreferenceRequest(NotificationCategory.ChoreDue, DeliveryChannel.InboxOnly));

        var fired = await ctx.Notifications.ProcessDueAsync(Due);

        Assert.Equal(1, fired);
        Assert.Empty(ctx.Push.Sent);
        Assert.Single(await ctx.Notifications.ListInboxAsync(member));
    }

    [Fact]
    public async Task ProcessDueAsync_off_preference_drops_the_inbox_row_too()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        await SeedChoreAsync(ctx, member, [member]);
        await SubscribeAsync(ctx, member);
        await ctx.Notifications.UpdatePreferenceAsync(member,
            new UpdateNotificationPreferenceRequest(NotificationCategory.ChoreDue, DeliveryChannel.Off));

        var fired = await ctx.Notifications.ProcessDueAsync(Due);

        // Still "fired" — the dedup row is written so the scan doesn't retry every minute.
        Assert.Equal(1, fired);
        Assert.Empty(ctx.Push.Sent);
        Assert.Empty(await ctx.Notifications.ListInboxAsync(member));
    }

    [Fact]
    public async Task ProcessDueAsync_preference_applies_per_category_not_across_them()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        // A reminder-type entry; the user silenced only the overdue category.
        await SeedChoreAsync(ctx, member, [member], NotificationType.Reminder);
        await SubscribeAsync(ctx, member);
        await ctx.Notifications.UpdatePreferenceAsync(member,
            new UpdateNotificationPreferenceRequest(NotificationCategory.ChoreOverdue, DeliveryChannel.Off));

        await ctx.Notifications.ProcessDueAsync(Due);

        Assert.Single(ctx.Push.Sent);
    }

    // --- Chore mutes ------------------------------------------------------------------------------

    [Fact]
    public async Task MuteChoreAsync_suppresses_the_push_but_keeps_the_inbox_row()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var choreId = await SeedChoreAsync(ctx, member, [member]);
        await SubscribeAsync(ctx, member);
        Assert.True((await ctx.Notifications.MuteChoreAsync(member, choreId, null)).Succeeded);

        var fired = await ctx.Notifications.ProcessDueAsync(Due);

        Assert.Equal(1, fired);
        Assert.Empty(ctx.Push.Sent);
        Assert.Single(await ctx.Notifications.ListInboxAsync(member));
    }

    [Fact]
    public async Task MuteChoreAsync_only_quiets_the_chore_for_the_person_who_muted_it()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var notification = new ChoreNotificationInput(
            NotificationType.Due, NotificationTiming.AtDue, 0, NotificationOffsetUnit.Minutes,
            NotificationRecipients.AllAssignees);
        var req = new CreateChoreRequest(
            "Dishes", null, "🍽️", 10, RepeatType.Daily, null, null, null,
            null, null, null, null, 1, false,
            AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate, null, false, null,
            Due.AddDays(-1), [member, admin], member, null, [notification]);
        var created = await ctx.Chores.CreateAsync(req);
        var chore = await ctx.Db.Chores.FirstAsync(c => c.Id == created.Value!.Id);
        chore.DueAt = Due;
        await ctx.Db.SaveChangesAsync();

        await SubscribeAsync(ctx, member, "https://push.example/member");
        await SubscribeAsync(ctx, admin, "https://push.example/admin");
        await ctx.Notifications.MuteChoreAsync(member, chore.Id, null);

        await ctx.Notifications.ProcessDueAsync(Due);

        // Only the admin's device is buzzed; the member still gets their inbox row.
        var sent = Assert.Single(ctx.Push.Sent);
        Assert.Equal("https://push.example/admin", sent.Endpoint);
        Assert.Single(await ctx.Notifications.ListInboxAsync(member));
    }

    [Fact]
    public async Task An_expired_mute_stops_applying()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var choreId = await SeedChoreAsync(ctx, member, [member]);
        await SubscribeAsync(ctx, member);

        // Write an already-lapsed mute directly — MuteChoreAsync rejects a past expiry by design.
        ctx.Db.ChoreMutes.Add(new ChoreMute { UserId = member, ChoreId = choreId, Until = Due.AddHours(-1) });
        await ctx.Db.SaveChangesAsync();

        await ctx.Notifications.ProcessDueAsync(Due);

        Assert.Single(ctx.Push.Sent);
    }

    [Fact]
    public async Task A_mute_that_has_not_lapsed_still_applies()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var choreId = await SeedChoreAsync(ctx, member, [member]);
        await SubscribeAsync(ctx, member);
        await ctx.Notifications.MuteChoreAsync(member, choreId, DateTimeOffset.UtcNow.AddDays(30));

        await ctx.Notifications.ProcessDueAsync(Due);

        Assert.Empty(ctx.Push.Sent);
    }

    [Fact]
    public async Task MuteChoreAsync_rejects_an_expiry_in_the_past()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var choreId = await SeedChoreAsync(ctx, member, [member]);

        var result = await ctx.Notifications.MuteChoreAsync(member, choreId, DateTimeOffset.UtcNow.AddHours(-1));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task MuteChoreAsync_rejects_an_unknown_chore()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        var result = await ctx.Notifications.MuteChoreAsync(member, Guid.NewGuid(), null);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task Re_muting_updates_the_expiry_rather_than_duplicating()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var choreId = await SeedChoreAsync(ctx, member, [member]);

        await ctx.Notifications.MuteChoreAsync(member, choreId, null);
        var until = DateTimeOffset.UtcNow.AddDays(1);
        await ctx.Notifications.MuteChoreAsync(member, choreId, until);

        var mute = Assert.Single(await ctx.Db.ChoreMutes.ToListAsync());
        Assert.NotNull(mute.Until);
    }

    [Fact]
    public async Task UnmuteChoreAsync_is_idempotent()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var choreId = await SeedChoreAsync(ctx, member, [member]);

        Assert.True((await ctx.Notifications.UnmuteChoreAsync(member, choreId)).Succeeded);
        await ctx.Notifications.MuteChoreAsync(member, choreId, null);
        Assert.True((await ctx.Notifications.UnmuteChoreAsync(member, choreId)).Succeeded);
        Assert.Empty(await ctx.Db.ChoreMutes.ToListAsync());
    }

    [Fact]
    public async Task Chore_dto_reports_the_mute_to_the_viewer_only()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var choreId = await SeedChoreAsync(ctx, member, [member, admin]);
        await ctx.Notifications.MuteChoreAsync(member, choreId, null);

        var asMember = await ctx.Chores.GetAsync(choreId, viewerId: member);
        var asAdmin = await ctx.Chores.GetAsync(choreId, viewerId: admin);

        Assert.True(asMember.Value!.IsMuted);
        Assert.False(asAdmin.Value!.IsMuted);
    }

    // --- The ad-hoc path (offers, test pushes) ---------------------------------------------------

    [Fact]
    public async Task NotifyUserAsync_honours_the_category_preference()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        await SubscribeAsync(ctx, member);
        await ctx.Notifications.UpdatePreferenceAsync(member,
            new UpdateNotificationPreferenceRequest(NotificationCategory.OfferReceived, DeliveryChannel.Off));

        var sent = await ctx.Notifications.NotifyUserAsync(
            member, "Dishes", "Up for grabs", choreId: null, NotificationCategory.OfferReceived);
        await ctx.Db.SaveChangesAsync();

        Assert.Equal(0, sent);
        Assert.Empty(ctx.Push.Sent);
        Assert.Empty(await ctx.Notifications.ListInboxAsync(member));
    }

    [Fact]
    public async Task NotifyUserAsync_respects_quiet_hours()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        await SubscribeAsync(ctx, member);

        // Cover the whole day so the call lands inside the window whenever the test runs.
        var user = await ctx.Db.Users.FirstAsync(u => u.Id == member);
        user.QuietHoursStart = new TimeOnly(0, 0);
        user.QuietHoursEnd = new TimeOnly(23, 59);
        await ctx.Db.SaveChangesAsync();

        var sent = await ctx.Notifications.NotifyUserAsync(
            member, "Dishes", "Up for grabs", choreId: null, NotificationCategory.OfferReceived);
        await ctx.Db.SaveChangesAsync();

        // The push is muted, but the inbox still records it — nothing is lost.
        Assert.Equal(0, sent);
        Assert.Empty(ctx.Push.Sent);
        Assert.Single(await ctx.Notifications.ListInboxAsync(member));
    }

    [Fact]
    public async Task NotifyUserAsync_honours_a_chore_mute()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var choreId = await SeedChoreAsync(ctx, member, [member]);
        await SubscribeAsync(ctx, member);
        await ctx.Notifications.MuteChoreAsync(member, choreId, null);

        var sent = await ctx.Notifications.NotifyUserAsync(
            member, "Dishes", "Up for grabs", choreId, NotificationCategory.OfferReceived);
        await ctx.Db.SaveChangesAsync();

        Assert.Equal(0, sent);
        Assert.Single(await ctx.Notifications.ListInboxAsync(member));
    }

    [Fact]
    public async Task The_test_push_ignores_an_Off_preference_on_the_system_category()
    {
        using var ctx = new TestContext();
        var (admin, _) = await SeedUsersAsync(ctx);
        await SubscribeAsync(ctx, admin);
        // Can't be set through the service, so write the row directly to prove the router still wins.
        ctx.Db.UserNotificationPreferences.Add(new UserNotificationPreference
        {
            UserId = admin,
            Category = NotificationCategory.System,
            Channel = DeliveryChannel.Off
        });
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Notifications.SendTestAsync(admin);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value);
        Assert.Single(ctx.Push.Sent);
    }

    [Fact]
    public async Task NotifyUsersAsync_routes_each_recipient_independently()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        await SubscribeAsync(ctx, member, "https://push.example/member");
        await SubscribeAsync(ctx, admin, "https://push.example/admin");
        await ctx.Notifications.UpdatePreferenceAsync(member,
            new UpdateNotificationPreferenceRequest(NotificationCategory.OfferReceived, DeliveryChannel.Off));

        var sent = await ctx.Notifications.NotifyUsersAsync(
            [member, admin], "Dishes", "Up for grabs", choreId: null, NotificationCategory.OfferReceived);
        await ctx.Db.SaveChangesAsync();

        Assert.Equal(1, sent);
        var push = Assert.Single(ctx.Push.Sent);
        Assert.Equal("https://push.example/admin", push.Endpoint);
    }
}
