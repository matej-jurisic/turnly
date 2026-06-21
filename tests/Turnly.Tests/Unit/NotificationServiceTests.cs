using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;
using Turnly.Core.Enums;

namespace Turnly.Tests.Unit;

public class NotificationServiceTests
{
    private static readonly DateTimeOffset Due = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(Guid AdminId, Guid MemberId)> SeedUsersAsync(TestContext ctx)
    {
        var admin = await ctx.Setup.CreateFirstAdminAsync(new SetupRequest("admin", "Admin", "password123", null));
        var member = await ctx.Users.CreateAsync(
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        return (admin.Value!.User.Id, member.Value!.Id);
    }

    /// <summary>Creates a chore with one notification entry and forces its DueAt to a known value
    /// (Create computes DueAt from the start date, which we then override for deterministic timing).</summary>
    private static async Task<Guid> SeedChoreAsync(
        TestContext ctx, Guid currentAssignee, Guid[] assignees,
        ChoreNotificationInput notification, DateTimeOffset dueAt)
    {
        var req = new CreateChoreRequest(
            "Dishes", null, "🍽️", 10, RepeatType.Daily, null, null, null,
            null, null, null, null, 1, false,
            AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate, null, false, null,
            dueAt.AddDays(-1), assignees, currentAssignee, null, [notification]);

        var result = await ctx.Chores.CreateAsync(req);
        Assert.True(result.Succeeded);

        var chore = await ctx.Db.Chores.FirstAsync(c => c.Id == result.Value!.Id);
        chore.DueAt = dueAt;
        await ctx.Db.SaveChangesAsync();
        return chore.Id;
    }

    private static Task SubscribeAsync(TestContext ctx, Guid userId, string endpoint = "https://push.example/sub-1") =>
        ctx.Notifications.SubscribeAsync(userId, new PushSubscribeRequest(endpoint, "p256", "auth"));

    private static ChoreNotificationInput AtDue(NotificationRecipients recipients = NotificationRecipients.CurrentAssignee) =>
        new(NotificationType.Due, NotificationTiming.AtDue, 0, NotificationOffsetUnit.Minutes, recipients);

    [Fact]
    public async Task ProcessDueAsync_fires_when_the_moment_has_arrived()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        await SeedChoreAsync(ctx, member, [member], AtDue(), Due);
        await SubscribeAsync(ctx, member);

        var fired = await ctx.Notifications.ProcessDueAsync(Due);

        Assert.Equal(1, fired);
        Assert.Single(ctx.Push.Sent);
    }

    [Fact]
    public async Task ProcessDueAsync_does_not_fire_before_the_moment()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        await SeedChoreAsync(ctx, member, [member], AtDue(), Due);
        await SubscribeAsync(ctx, member);

        var fired = await ctx.Notifications.ProcessDueAsync(Due.AddMinutes(-1));

        Assert.Equal(0, fired);
        Assert.Empty(ctx.Push.Sent);
    }

    [Fact]
    public async Task ProcessDueAsync_fires_each_entry_only_once_per_occurrence()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        await SeedChoreAsync(ctx, member, [member], AtDue(), Due);
        await SubscribeAsync(ctx, member);

        await ctx.Notifications.ProcessDueAsync(Due);
        var secondRun = await ctx.Notifications.ProcessDueAsync(Due.AddMinutes(5));

        Assert.Equal(0, secondRun);
        Assert.Single(ctx.Push.Sent);
    }

    [Fact]
    public async Task ProcessDueAsync_does_not_fire_stale_notifications()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        await SeedChoreAsync(ctx, member, [member], AtDue(), Due);
        await SubscribeAsync(ctx, member);

        // Two days after the due time is outside the 1-day stale window.
        var fired = await ctx.Notifications.ProcessDueAsync(Due.AddDays(2));

        Assert.Equal(0, fired);
        Assert.Empty(ctx.Push.Sent);
    }

    [Fact]
    public async Task ProcessDueAsync_stops_after_completion_advances_the_occurrence()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        // Daily reminder one hour after due (a follow-up) so it hasn't fired by the due time.
        var followUp = new ChoreNotificationInput(
            NotificationType.FollowUp, NotificationTiming.After, 1, NotificationOffsetUnit.Hours,
            NotificationRecipients.CurrentAssignee);
        var choreId = await SeedChoreAsync(ctx, member, [member], followUp, Due);
        await SubscribeAsync(ctx, member);

        // Complete it before the follow-up window; this advances DueAt to the next day.
        var complete = await ctx.Chores.CompleteAsync(choreId, member, new CompleteChoreRequest(null));
        Assert.True(complete.Succeeded);

        // Scan at the original follow-up time: the old occurrence's follow-up must not fire.
        var fired = await ctx.Notifications.ProcessDueAsync(Due.AddHours(1));

        Assert.Equal(0, fired);
        Assert.Empty(ctx.Push.Sent);
    }

    [Fact]
    public async Task ProcessDueAsync_sends_to_all_assignees_when_configured()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        await SeedChoreAsync(ctx, member, [member, admin], AtDue(NotificationRecipients.AllAssignees), Due);
        await SubscribeAsync(ctx, member, "https://push.example/member");
        await SubscribeAsync(ctx, admin, "https://push.example/admin");

        var fired = await ctx.Notifications.ProcessDueAsync(Due);

        Assert.Equal(1, fired);
        Assert.Equal(2, ctx.Push.Sent.Count);
    }

    [Fact]
    public async Task ProcessDueAsync_targets_only_the_current_assignee_by_default()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        await SeedChoreAsync(ctx, member, [member, admin], AtDue(), Due);
        await SubscribeAsync(ctx, member, "https://push.example/member");
        await SubscribeAsync(ctx, admin, "https://push.example/admin");

        await ctx.Notifications.ProcessDueAsync(Due);

        Assert.Single(ctx.Push.Sent);
        Assert.Equal("https://push.example/member", ctx.Push.Sent[0].Endpoint);
    }

    [Fact]
    public async Task ProcessDueAsync_prunes_dead_subscriptions()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        await SeedChoreAsync(ctx, member, [member], AtDue(), Due);
        await SubscribeAsync(ctx, member, "https://push.example/dead");
        ctx.Push.GoneEndpoints.Add("https://push.example/dead");

        await ctx.Notifications.ProcessDueAsync(Due);

        Assert.Empty(await ctx.Db.PushSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task ProcessDueAsync_writes_an_inbox_item_even_without_a_subscription()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var choreId = await SeedChoreAsync(ctx, member, [member], AtDue(), Due);
        // No push subscription — the in-app inbox should still record it.

        var fired = await ctx.Notifications.ProcessDueAsync(Due);

        Assert.Equal(1, fired);
        var inbox = await ctx.Notifications.ListInboxAsync(member);
        var item = Assert.Single(inbox);
        Assert.Equal(choreId, item.ChoreId);
        Assert.False(item.Read);
    }

    [Fact]
    public async Task ProcessDueAsync_writes_an_inbox_item_per_recipient()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        await SeedChoreAsync(ctx, member, [member, admin], AtDue(NotificationRecipients.AllAssignees), Due);

        await ctx.Notifications.ProcessDueAsync(Due);

        Assert.Single(await ctx.Notifications.ListInboxAsync(member));
        Assert.Single(await ctx.Notifications.ListInboxAsync(admin));
    }

    [Fact]
    public async Task MarkInboxReadAsync_marks_all_unread_as_read()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        await SeedChoreAsync(ctx, member, [member], AtDue(), Due);
        await ctx.Notifications.ProcessDueAsync(Due);

        var marked = await ctx.Notifications.MarkInboxReadAsync(member, Due.AddMinutes(1));

        Assert.Equal(1, marked);
        Assert.All(await ctx.Notifications.ListInboxAsync(member), n => Assert.True(n.Read));
    }

    [Fact]
    public async Task DeleteInboxAsync_removes_own_notification()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        await SeedChoreAsync(ctx, member, [member], AtDue(), Due);
        await ctx.Notifications.ProcessDueAsync(Due);
        var item = Assert.Single(await ctx.Notifications.ListInboxAsync(member));

        var result = await ctx.Notifications.DeleteInboxAsync(member, item.Id);

        Assert.True(result.Succeeded);
        Assert.Empty(await ctx.Notifications.ListInboxAsync(member));
    }

    [Fact]
    public async Task DeleteInboxAsync_rejects_another_users_notification()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        await SeedChoreAsync(ctx, member, [member], AtDue(), Due);
        await ctx.Notifications.ProcessDueAsync(Due);
        var item = Assert.Single(await ctx.Notifications.ListInboxAsync(member));

        var result = await ctx.Notifications.DeleteInboxAsync(admin, item.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Single(await ctx.Notifications.ListInboxAsync(member));
    }

    [Fact]
    public async Task ClearInboxAsync_removes_only_the_callers_notifications()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        await SeedChoreAsync(ctx, member, [member, admin], AtDue(NotificationRecipients.AllAssignees), Due);
        await ctx.Notifications.ProcessDueAsync(Due);

        var cleared = await ctx.Notifications.ClearInboxAsync(member);

        Assert.Equal(1, cleared);
        Assert.Empty(await ctx.Notifications.ListInboxAsync(member));
        Assert.Single(await ctx.Notifications.ListInboxAsync(admin));
    }

    [Fact]
    public async Task SubscribeAsync_upserts_by_endpoint()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);

        await SubscribeAsync(ctx, member, "https://push.example/shared");
        await SubscribeAsync(ctx, admin, "https://push.example/shared");

        var subs = await ctx.Db.PushSubscriptions.ToListAsync();
        Assert.Single(subs);
        Assert.Equal(admin, subs[0].UserId);
    }

    [Fact]
    public async Task ListDevicesAsync_returns_only_the_users_devices_with_a_label()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        await ctx.Notifications.SubscribeAsync(member, new PushSubscribeRequest("https://push.example/m", "p", "a"),
            "Mozilla/5.0 (X11; Linux x86_64; rv:128.0) Gecko/20100101 Firefox/128.0");
        await ctx.Notifications.SubscribeAsync(admin, new PushSubscribeRequest("https://push.example/a", "p", "a"));

        var devices = await ctx.Notifications.ListDevicesAsync(member);

        var device = Assert.Single(devices);
        Assert.Equal("Firefox · Linux", device.Label);
        Assert.Equal("https://push.example/m", device.Endpoint);
    }

    [Fact]
    public async Task RemoveDeviceAsync_removes_own_device()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        await SubscribeAsync(ctx, member, "https://push.example/m");
        var device = (await ctx.Notifications.ListDevicesAsync(member)).Single();

        var result = await ctx.Notifications.RemoveDeviceAsync(member, device.Id);

        Assert.True(result.Succeeded);
        Assert.Empty(await ctx.Db.PushSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task RemoveDeviceAsync_rejects_another_users_device()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        await SubscribeAsync(ctx, member, "https://push.example/m");
        var device = (await ctx.Notifications.ListDevicesAsync(member)).Single();

        var result = await ctx.Notifications.RemoveDeviceAsync(admin, device.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Forbidden, result.Error!.Type);
    }
}
