using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Dtos;
using Turnly.Core.Enums;
using Turnly.Core.Notifications;

namespace Turnly.Tests.Unit;

/// <summary>Personal chore notifications and the daily digest.</summary>
public class NotificationScheduleTests
{
    private static readonly DateTimeOffset Due = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(Guid AdminId, Guid MemberId)> SeedUsersAsync(TestContext ctx)
    {
        var admin = await ctx.Setup.CreateFirstAdminAsync(new SetupRequest("admin", "Admin", "password123", null));
        var member = await ctx.Users.CreateAsync(
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        // Evaluate local times in UTC so the test doesn't depend on the host's zone.
        await ctx.Settings.SetTimeZoneAsync("UTC");
        return (admin.Value!.User.Id, member.Value!.Id);
    }

    private static async Task<Guid> SeedChoreAsync(
        TestContext ctx, Guid currentAssignee, Guid[] assignees, ChoreNotificationInput[] notifications,
        DateTimeOffset dueAt, string name = "Dishes")
    {
        var req = new CreateChoreRequest(
            name, null, "🍽️", 10, RepeatType.Daily, null, null, null,
            null, null, null, null, 1, false,
            AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate, null, false, null,
            dueAt.AddDays(-1), assignees, currentAssignee, null, notifications);

        var result = await ctx.Chores.CreateAsync(req);
        Assert.True(result.Succeeded);

        var chore = await ctx.Db.Chores.FirstAsync(c => c.Id == result.Value!.Id);
        chore.DueAt = dueAt;
        await ctx.Db.SaveChangesAsync();
        return chore.Id;
    }

    private static Task SubscribeAsync(TestContext ctx, Guid userId, string endpoint) =>
        ctx.Notifications.SubscribeAsync(userId, new PushSubscribeRequest(endpoint, "p256", "auth"));

    private static ChoreNotificationInput HoursBefore(int hours, NotificationRecipients recipients = NotificationRecipients.CurrentAssignee) =>
        new(NotificationType.Reminder, NotificationTiming.Before, hours, NotificationOffsetUnit.Hours, recipients);

    private static async Task SetScheduleAsync(TestContext ctx, Guid userId, string? digest) =>
        Assert.True((await ctx.Users.UpdateNotificationScheduleAsync(userId, new UpdateNotificationScheduleRequest(digest))).Succeeded);

    private static async Task SetPersonalAsync(TestContext ctx, Guid userId, Guid choreId, params ChoreNotificationInput[] entries) =>
        Assert.True((await ctx.Notifications.SetPersonalNotificationsAsync(userId, choreId, entries)).Succeeded);

    // ---- Personal chore notifications ----

    [Fact]
    public async Task ProcessDueAsync_personal_entries_replace_the_chores_for_their_owner()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = await SeedChoreAsync(ctx, member, [member], [HoursBefore(1), HoursBefore(3)], Due);
        await SubscribeAsync(ctx, member, "https://push.example/member");
        await SetPersonalAsync(ctx, member, chore, HoursBefore(2));

        await ctx.Notifications.ProcessDueAsync(Due.AddHours(-3));
        Assert.Empty(ctx.Push.Sent);

        await ctx.Notifications.ProcessDueAsync(Due.AddHours(-2));
        await ctx.Notifications.ProcessDueAsync(Due.AddHours(-1));
        Assert.Single(ctx.Push.Sent);
    }

    [Fact]
    public async Task ProcessDueAsync_personal_entries_only_affect_their_owner()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = await SeedChoreAsync(ctx, member, [member, admin], [HoursBefore(1, NotificationRecipients.AllAssignees)], Due);
        await SubscribeAsync(ctx, member, "https://push.example/member");
        await SubscribeAsync(ctx, admin, "https://push.example/admin");
        await SetPersonalAsync(ctx, admin, chore, HoursBefore(2, NotificationRecipients.AllAssignees));

        await ctx.Notifications.ProcessDueAsync(Due.AddHours(-2));
        Assert.Equal(["https://push.example/admin"], ctx.Push.Sent.Select(s => s.Endpoint));

        await ctx.Notifications.ProcessDueAsync(Due.AddHours(-1));
        Assert.Equal(["https://push.example/admin", "https://push.example/member"], ctx.Push.Sent.Select(s => s.Endpoint));
    }

    [Fact]
    public async Task ProcessDueAsync_personal_entries_work_on_a_chore_without_notifications()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = await SeedChoreAsync(ctx, member, [member], [], Due);
        await SubscribeAsync(ctx, member, "https://push.example/member");
        await SetPersonalAsync(ctx, member, chore, HoursBefore(1));

        await ctx.Notifications.ProcessDueAsync(Due.AddHours(-1));
        Assert.Single(ctx.Push.Sent);
    }

    [Fact]
    public async Task ProcessDueAsync_personal_turn_entry_waits_for_the_owners_turn()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = await SeedChoreAsync(ctx, member, [member, admin], [], Due);
        await SubscribeAsync(ctx, admin, "https://push.example/admin");
        await SetPersonalAsync(ctx, admin, chore, HoursBefore(1));

        // CurrentAssignee recipients: the admin set it up, but it isn't their turn.
        await ctx.Notifications.ProcessDueAsync(Due.AddHours(-1));
        Assert.Empty(ctx.Push.Sent);
    }

    [Fact]
    public async Task SetPersonalNotificationsAsync_empty_list_restores_the_chores_schedule()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = await SeedChoreAsync(ctx, member, [member], [HoursBefore(1)], Due);
        await SubscribeAsync(ctx, member, "https://push.example/member");
        await SetPersonalAsync(ctx, member, chore, HoursBefore(3));
        await SetPersonalAsync(ctx, member, chore);

        await ctx.Notifications.ProcessDueAsync(Due.AddHours(-3));
        Assert.Empty(ctx.Push.Sent);
        await ctx.Notifications.ProcessDueAsync(Due.AddHours(-1));
        Assert.Single(ctx.Push.Sent);
        Assert.Equal(1, await ctx.Db.ChoreNotifications.CountAsync(n => n.ChoreId == chore));
    }

    [Fact]
    public async Task SetPersonalNotificationsAsync_validates()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = await SeedChoreAsync(ctx, member, [member], [], Due);

        var badOffset = await ctx.Notifications.SetPersonalNotificationsAsync(member, chore, [HoursBefore(0)]);
        var tooMany = await ctx.Notifications.SetPersonalNotificationsAsync(member, chore,
            Enumerable.Repeat(HoursBefore(1), Validators.MaxNotificationsPerChore + 1).ToArray());
        var missing = await ctx.Notifications.SetPersonalNotificationsAsync(member, Guid.NewGuid(), [HoursBefore(1)]);

        Assert.Equal(ErrorType.Validation, badOffset.Error!.Type);
        Assert.Equal(ErrorType.Validation, tooMany.Error!.Type);
        Assert.Equal(ErrorType.NotFound, missing.Error!.Type);
    }

    [Fact]
    public async Task ChoreDto_shows_personal_entries_only_to_their_owner()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = await SeedChoreAsync(ctx, member, [member, admin], [HoursBefore(1)], Due);
        await SetPersonalAsync(ctx, member, chore, HoursBefore(2), HoursBefore(5));

        var mine = (await ctx.Chores.GetAsync(chore, member)).Value!;
        var theirs = (await ctx.Chores.GetAsync(chore, admin)).Value!;

        Assert.Single(mine.Notifications);
        Assert.Equal(2, mine.MyNotifications.Length);
        Assert.Single(theirs.Notifications);
        Assert.Empty(theirs.MyNotifications);
    }

    [Fact]
    public async Task UpdateNotificationScheduleAsync_validates()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        var badTime = await ctx.Users.UpdateNotificationScheduleAsync(member, new UpdateNotificationScheduleRequest("25:00"));
        var ok = await ctx.Users.UpdateNotificationScheduleAsync(member, new UpdateNotificationScheduleRequest("07:30"));

        Assert.Equal(ErrorType.Validation, badTime.Error!.Type);
        Assert.Equal("07:30", ok.Value!.DigestTime);
    }

    // ---- Daily digest ----

    [Fact]
    public async Task ProcessDigestsAsync_sends_once_per_day_listing_todays_and_overdue_chores()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var morning = new DateTimeOffset(2026, 6, 20, 7, 0, 0, TimeSpan.Zero);
        await SeedChoreAsync(ctx, member, [member], [], Due, "Dishes");
        await SeedChoreAsync(ctx, member, [member], [], Due.AddDays(-2), "Laundry");
        await SeedChoreAsync(ctx, member, [member], [], Due.AddDays(3), "Windows");
        await SubscribeAsync(ctx, member, "https://push.example/member");
        await SetScheduleAsync(ctx, member, "07:00");

        Assert.Equal(0, await ctx.Notifications.ProcessDigestsAsync(morning.AddMinutes(-1)));
        Assert.Equal(1, await ctx.Notifications.ProcessDigestsAsync(morning));
        Assert.Equal(0, await ctx.Notifications.ProcessDigestsAsync(morning.AddMinutes(1)));

        var body = Assert.Single(await ctx.Notifications.ListInboxAsync(member)).Body;
        Assert.Equal("Overdue: Laundry. Today: Dishes.", body);
        Assert.Single(ctx.Push.Sent);

        // The next day it goes out again.
        Assert.Equal(1, await ctx.Notifications.ProcessDigestsAsync(morning.AddDays(1)));
    }

    [Fact]
    public async Task SendTestDigestAsync_sends_now_without_using_up_the_days_digest()
    {
        using var ctx = new TestContext();
        var (admin, _) = await SeedUsersAsync(ctx);
        var morning = new DateTimeOffset(2026, 6, 20, 7, 0, 0, TimeSpan.Zero);
        await SeedChoreAsync(ctx, admin, [admin], [], Due, "Dishes");
        await SubscribeAsync(ctx, admin, "https://push.example/admin");
        await SetScheduleAsync(ctx, admin, "07:00");

        var result = await ctx.Notifications.SendTestDigestAsync(admin, morning.AddHours(-2));

        Assert.True(result.Succeeded);
        Assert.Equal("Today: Dishes.", Assert.Single(await ctx.Notifications.ListInboxAsync(admin)).Body);
        Assert.Equal(1, await ctx.Notifications.ProcessDigestsAsync(morning));
    }

    [Fact]
    public async Task SendTestDigestAsync_sends_on_an_empty_day_and_needs_a_device()
    {
        using var ctx = new TestContext();
        var (admin, _) = await SeedUsersAsync(ctx);

        var noDevice = await ctx.Notifications.SendTestDigestAsync(admin, Due);
        await SubscribeAsync(ctx, admin, "https://push.example/admin");
        var sent = await ctx.Notifications.SendTestDigestAsync(admin, Due);

        Assert.Equal(ErrorType.Validation, noDevice.Error!.Type);
        Assert.True(sent.Succeeded);
        Assert.Equal("Nothing due today.", Assert.Single(await ctx.Notifications.ListInboxAsync(admin)).Body);
    }

    [Fact]
    public async Task ProcessDigestsAsync_skips_when_there_is_nothing_to_say()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        // Held by someone else.
        await SeedChoreAsync(ctx, admin, [member, admin], [], Due);
        await SetScheduleAsync(ctx, member, "07:00");

        Assert.Equal(0, await ctx.Notifications.ProcessDigestsAsync(new DateTimeOffset(2026, 6, 20, 7, 0, 0, TimeSpan.Zero)));
        Assert.Empty(await ctx.Notifications.ListInboxAsync(member));
    }

    [Fact]
    public async Task ProcessDigestsAsync_waits_out_quiet_hours()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        await SeedChoreAsync(ctx, member, [member], [], Due);
        await SetScheduleAsync(ctx, member, "06:30");
        var user = await ctx.Db.Users.FirstAsync(u => u.Id == member);
        user.QuietHoursStart = new TimeOnly(22, 0);
        user.QuietHoursEnd = new TimeOnly(7, 0);
        await ctx.Db.SaveChangesAsync();

        Assert.Equal(0, await ctx.Notifications.ProcessDigestsAsync(new DateTimeOffset(2026, 6, 20, 6, 30, 0, TimeSpan.Zero)));
        Assert.Equal(1, await ctx.Notifications.ProcessDigestsAsync(new DateTimeOffset(2026, 6, 20, 7, 0, 0, TimeSpan.Zero)));
    }

    // ---- Pure helpers ----

    [Theory]
    [InlineData("07:00", "06:59", null, null, false)]
    [InlineData("07:00", "07:00", null, null, true)]
    [InlineData("07:00", "12:59", null, null, true)]
    [InlineData("07:00", "13:00", null, null, false)]   // stale
    [InlineData("06:30", "06:45", "22:00", "07:00", false)]
    [InlineData("06:30", "07:00", "22:00", "07:00", true)]
    public void DailyDigest_IsDue(string digest, string now, string? quietStart, string? quietEnd, bool expected) =>
        Assert.Equal(expected, DailyDigest.IsDue(TimeOnly.Parse(digest), TimeOnly.Parse(now),
            quietStart is null ? null : TimeOnly.Parse(quietStart), quietEnd is null ? null : TimeOnly.Parse(quietEnd)));

    [Fact]
    public void DailyDigest_Classify_follows_the_household_day()
    {
        var dayStart = TimeSpan.FromHours(5);
        var endOfMonday = new DateTime(2026, 6, 22, 23, 59, 59);
        // 01:00 Tuesday is still Monday: Monday's all-day chore is due today, not overdue.
        Assert.Equal(DailyDigest.Bucket.Today, DailyDigest.Classify(endOfMonday, false, new DateTime(2026, 6, 23, 1, 0, 0), dayStart));
        Assert.Equal(DailyDigest.Bucket.Overdue, DailyDigest.Classify(endOfMonday, false, new DateTime(2026, 6, 23, 6, 0, 0), dayStart));
        // A timed chore goes overdue at its instant.
        Assert.Equal(DailyDigest.Bucket.Overdue, DailyDigest.Classify(new DateTime(2026, 6, 23, 8, 0, 0), true, new DateTime(2026, 6, 23, 9, 0, 0), dayStart));
        Assert.Equal(DailyDigest.Bucket.Today, DailyDigest.Classify(new DateTime(2026, 6, 23, 18, 0, 0), true, new DateTime(2026, 6, 23, 9, 0, 0), dayStart));
        Assert.Equal(DailyDigest.Bucket.Later, DailyDigest.Classify(new DateTime(2026, 6, 24, 18, 0, 0), true, new DateTime(2026, 6, 23, 9, 0, 0), dayStart));
    }
}
