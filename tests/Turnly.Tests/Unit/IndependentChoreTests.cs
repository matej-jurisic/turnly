using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;
using Turnly.Core.Enums;

namespace Turnly.Tests.Unit;

/// <summary>"Everyone independently" (track-mode) chores: each assignee has their own schedule and
/// quota, so one person never blocks another, and reminders fan out per track.</summary>
public class IndependentChoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Week2 = Start.AddDays(7);

    private static async Task<(Guid Admin, Guid A, Guid B)> SeedAsync(TestContext ctx)
    {
        var admin = await ctx.Setup.CreateFirstAdminAsync(new SetupRequest("admin", "Admin", "password123", null));
        var a = await ctx.Users.CreateAsync(new CreateUserRequest("alice", "Alice", "alicepw1", UserRole.Member, null));
        var b = await ctx.Users.CreateAsync(new CreateUserRequest("bob", "Bob", "bobpw123", UserRole.Member, null));
        return (admin.Value!.User.Id, a.Value!.Id, b.Value!.Id);
    }

    private static CreateChoreRequest Independent(
        Guid[] assignees, (Guid UserId, int Quota)[] tracks, RepeatType repeat = RepeatType.Weekly) =>
        new("Dishes", null, "🍽️", 10, repeat, null, null, null,
            null, null, null, null, 1, false,
            AssignmentStrategy.Independent, SchedulingPreference.FromScheduledDate, null,
            Start, assignees, assignees[0], null, null, null,
            tracks.Select(t => new TrackInput(t.UserId, t.Quota)).ToArray());

    private static Task<List<ChoreAssigneeTrack>> TracksAsync(TestContext ctx, Guid choreId) =>
        ctx.Db.ChoreAssigneeTracks.Where(t => t.ChoreId == choreId).ToListAsync();

    private static ChoreNotificationInput AtDue() =>
        new(NotificationType.Due, NotificationTiming.AtDue, 0, NotificationOffsetUnit.Minutes,
            NotificationRecipients.CurrentAssignee);

    [Fact]
    public async Task Create_gives_each_assignee_a_track_and_no_current_assignee()
    {
        using var ctx = new TestContext();
        var (_, a, b) = await SeedAsync(ctx);

        var res = await ctx.Chores.CreateAsync(Independent([a, b], [(a, 1), (b, 1)]));

        Assert.True(res.Succeeded);
        Assert.Null(res.Value!.CurrentAssignee);
        Assert.Equal(2, res.Value.Tracks.Length);
        Assert.Equal(Start, res.Value.DueAt);
        var tracks = await TracksAsync(ctx, res.Value.Id);
        Assert.All(tracks, t => Assert.Equal(Start, t.DueAt));
    }

    [Fact]
    public async Task Complete_advances_only_the_completing_users_track()
    {
        using var ctx = new TestContext();
        var (_, a, b) = await SeedAsync(ctx);
        var id = (await ctx.Chores.CreateAsync(Independent([a, b], [(a, 1), (b, 1)]))).Value!.Id;

        var res = await ctx.Chores.CompleteAsync(id, a, new CompleteChoreRequest(null));

        Assert.True(res.Succeeded);
        var tracks = await TracksAsync(ctx, id);
        Assert.Equal(Week2, tracks.Single(t => t.UserId == a).DueAt); // Alice rolled forward
        Assert.Equal(Start, tracks.Single(t => t.UserId == b).DueAt); // Bob is untouched, not blocked
    }

    [Fact]
    public async Task Complete_keeps_occurrence_open_until_personal_quota_is_met()
    {
        using var ctx = new TestContext();
        var (_, a, b) = await SeedAsync(ctx);
        var id = (await ctx.Chores.CreateAsync(Independent([a, b], [(a, 3), (b, 1)]))).Value!.Id;

        await ctx.Chores.CompleteAsync(id, a, new CompleteChoreRequest(null));
        await ctx.Chores.CompleteAsync(id, a, new CompleteChoreRequest(null));
        Assert.Equal(Start, (await TracksAsync(ctx, id)).Single(t => t.UserId == a).DueAt); // 2/3 — still open

        await ctx.Chores.CompleteAsync(id, a, new CompleteChoreRequest(null));
        Assert.Equal(Week2, (await TracksAsync(ctx, id)).Single(t => t.UserId == a).DueAt); // 3/3 — advances
    }

    [Fact]
    public async Task Uneven_quotas_advance_each_person_on_their_own_count()
    {
        using var ctx = new TestContext();
        var (_, a, b) = await SeedAsync(ctx);
        var id = (await ctx.Chores.CreateAsync(Independent([a, b], [(a, 3), (b, 2)]))).Value!.Id;

        await ctx.Chores.CompleteAsync(id, b, new CompleteChoreRequest(null));
        await ctx.Chores.CompleteAsync(id, b, new CompleteChoreRequest(null));

        var tracks = await TracksAsync(ctx, id);
        Assert.Equal(Week2, tracks.Single(t => t.UserId == b).DueAt); // Bob hit 2/2
        Assert.Equal(Start, tracks.Single(t => t.UserId == a).DueAt); // Alice still at 0/3
    }

    [Fact]
    public async Task Undo_restores_only_the_completing_users_track()
    {
        using var ctx = new TestContext();
        var (_, a, b) = await SeedAsync(ctx);
        var id = (await ctx.Chores.CreateAsync(Independent([a, b], [(a, 1), (b, 1)]))).Value!.Id;
        var completion = (await ctx.Chores.CompleteAsync(id, a, new CompleteChoreRequest(null))).Value!.LastCompletion!;

        var res = await ctx.Chores.UndoCompletionAsync(completion.Id, a);

        Assert.True(res.Succeeded);
        var tracks = await TracksAsync(ctx, id);
        Assert.Equal(Start, tracks.Single(t => t.UserId == a).DueAt); // rewound
        Assert.Equal(Start, tracks.Single(t => t.UserId == b).DueAt);
    }

    [Fact]
    public async Task Skip_targets_the_named_track_only()
    {
        using var ctx = new TestContext();
        var (admin, a, b) = await SeedAsync(ctx);
        var id = (await ctx.Chores.CreateAsync(Independent([a, b], [(a, 1), (b, 1)]))).Value!.Id;

        var res = await ctx.Chores.SkipAsync(id, admin, new SkipChoreRequest(null, b));

        Assert.True(res.Succeeded);
        var tracks = await TracksAsync(ctx, id);
        Assert.Equal(Week2, tracks.Single(t => t.UserId == b).DueAt); // Bob's skipped occurrence advances
        Assert.Equal(Start, tracks.Single(t => t.UserId == a).DueAt);
        Assert.Equal(0, (await ctx.Db.Users.FindAsync(b))!.Points); // a skip awards nothing
    }

    [Fact]
    public async Task Reassign_is_rejected_for_track_mode()
    {
        using var ctx = new TestContext();
        var (admin, a, b) = await SeedAsync(ctx);
        var id = (await ctx.Chores.CreateAsync(Independent([a, b], [(a, 1), (b, 1)]))).Value!.Id;

        var res = await ctx.Chores.ReassignAsync(id, admin, new ReassignChoreRequest(b));

        Assert.False(res.Succeeded);
        Assert.Equal(ErrorType.Validation, res.Error!.Type);
    }

    [Fact]
    public async Task Create_requires_a_track_for_every_assignee()
    {
        using var ctx = new TestContext();
        var (_, a, b) = await SeedAsync(ctx);

        var res = await ctx.Chores.CreateAsync(Independent([a, b], [(a, 1)])); // missing Bob

        Assert.False(res.Succeeded);
        Assert.Equal(ErrorType.Validation, res.Error!.Type);
    }

    [Fact]
    public async Task Create_rejects_one_time_track_mode()
    {
        using var ctx = new TestContext();
        var (_, a, b) = await SeedAsync(ctx);

        var res = await ctx.Chores.CreateAsync(Independent([a, b], [(a, 1), (b, 1)], RepeatType.OneTime));

        Assert.False(res.Succeeded);
        Assert.Equal(ErrorType.Validation, res.Error!.Type);
    }

    [Fact]
    public async Task Track_started_flag_distinguishes_a_not_yet_started_occurrence_from_a_completed_one()
    {
        using var ctx = new TestContext();
        var (_, a, b) = await SeedAsync(ctx);
        // Future start date: every track's first occurrence is in the future, but nobody has done
        // anything yet — so they must read as "not started", not "done".
        var future = Start.AddYears(1);
        var req = Independent([a, b], [(a, 1), (b, 1)]) with { StartDate = future };
        var id = (await ctx.Chores.CreateAsync(req)).Value!.Id;

        var created = await ctx.Chores.GetAsync(id, a);
        Assert.All(created.Value!.Tracks, t => Assert.False(t.Started)); // future first occurrence, untouched

        await ctx.Chores.CompleteAsync(id, a, new CompleteChoreRequest(null)); // Alice does hers early

        var after = await ctx.Chores.GetAsync(id, a);
        Assert.True(after.Value!.Tracks.Single(t => t.User.Id == a).Started); // now genuinely done/advanced
        Assert.False(after.Value.Tracks.Single(t => t.User.Id == b).Started); // Bob still hasn't started
    }

    [Fact]
    public async Task Get_personalises_due_date_to_the_viewers_track()
    {
        using var ctx = new TestContext();
        var (_, a, b) = await SeedAsync(ctx);
        var id = (await ctx.Chores.CreateAsync(Independent([a, b], [(a, 1), (b, 1)]))).Value!.Id;
        await ctx.Chores.CompleteAsync(id, a, new CompleteChoreRequest(null)); // Alice rolls to Week2

        var asAlice = await ctx.Chores.GetAsync(id, a);
        var asBob = await ctx.Chores.GetAsync(id, b);

        Assert.Equal(Week2, asAlice.Value!.DueAt);
        Assert.Equal(Start, asBob.Value!.DueAt);
    }

    [Fact]
    public async Task Notifications_fire_once_per_track()
    {
        using var ctx = new TestContext();
        var (_, a, b) = await SeedAsync(ctx);
        var req = Independent([a, b], [(a, 1), (b, 1)]) with { Notifications = [AtDue()] };
        var id = (await ctx.Chores.CreateAsync(req)).Value!.Id;
        await ctx.Notifications.SubscribeAsync(a, new PushSubscribeRequest("https://push/a", "p", "s"));
        await ctx.Notifications.SubscribeAsync(b, new PushSubscribeRequest("https://push/b", "p", "s"));

        var fired = await ctx.Notifications.ProcessDueAsync(Start);

        Assert.Equal(2, fired); // one per assignee track
        Assert.Equal(2, ctx.Push.Sent.Count);
        var deliveries = await ctx.Db.NotificationDeliveries.Where(d => d.ChoreId == id).ToListAsync();
        Assert.Equal(new[] { a, b }.OrderBy(x => x), deliveries.Select(d => d.UserId!.Value).OrderBy(x => x));
        Assert.Equal(0, await ctx.Notifications.ProcessDueAsync(Start)); // dedup — no re-fire
    }

    [Fact]
    public async Task Notifications_stop_for_a_completed_track_but_keep_firing_for_others()
    {
        using var ctx = new TestContext();
        var (_, a, b) = await SeedAsync(ctx);
        var req = Independent([a, b], [(a, 1), (b, 1)]) with { Notifications = [AtDue()] };
        var id = (await ctx.Chores.CreateAsync(req)).Value!.Id;
        await ctx.Notifications.SubscribeAsync(a, new PushSubscribeRequest("https://push/a", "p", "s"));
        await ctx.Notifications.SubscribeAsync(b, new PushSubscribeRequest("https://push/b", "p", "s"));

        // Alice completes first, advancing her track past this occurrence.
        await ctx.Chores.CompleteAsync(id, a, new CompleteChoreRequest(null));
        var fired = await ctx.Notifications.ProcessDueAsync(Start);

        Assert.Equal(1, fired); // only Bob's track is still due now
        Assert.Single(ctx.Push.Sent);
        Assert.Equal("https://push/b", ctx.Push.Sent[0].Endpoint);
    }
}
