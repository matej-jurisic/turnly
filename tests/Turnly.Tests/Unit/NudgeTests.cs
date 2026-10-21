using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;
using Turnly.Core.Enums;
using Turnly.Core.Services;

namespace Turnly.Tests.Unit;

public class NudgeTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 17, 9, 0, 0, TimeSpan.Zero);

    private record People(Guid Admin, Guid Ana, Guid Ben, Guid Cy);

    private static async Task<People> SeedAsync(TestContext ctx)
    {
        var admin = await ctx.Setup.CreateFirstAdminAsync(new SetupRequest("admin", "Admin", "password123", null));
        async Task<Guid> Member(string name) =>
            (await ctx.Users.CreateAsync(new CreateUserRequest($"{name.ToLowerInvariant()}-user", name, "password1", UserRole.Member, null))).Value!.Id;
        return new People(admin.Value!.User.Id, await Member("Ana"), await Member("Ben"), await Member("Cy"));
    }

    private static CreateChoreRequest Rotating(Guid current, Guid[] assignees, Guid[][]? pairs = null,
        RepeatType repeat = RepeatType.Daily) =>
        new("Garage", null, null, 10, repeat, null, null, null, null, null, null, null, 1, false,
            AssignmentStrategy.RoundRobin, SchedulingPreference.FromScheduledDate, null, false, null, Start, assignees, current, null,
            Pairs: pairs);

    private static CreateChoreRequest Independent(Guid[] assignees) =>
        new("Dishes", null, null, 10, RepeatType.Weekly, null, null, null, null, null, null, null, 1, false,
            AssignmentStrategy.Independent, SchedulingPreference.FromScheduledDate, null, false, null,
            Start, assignees, assignees[0], null, null, null,
            assignees.Select(a => new TrackInput(a, 1)).ToArray());

    private static Task<List<UserNotification>> InboxAsync(TestContext ctx, Guid userId) =>
        ctx.Db.UserNotifications.Where(n => n.UserId == userId).ToListAsync();

    [Fact]
    public async Task NudgeAsync_notifies_the_holder_with_the_note()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Rotating(p.Ana, [p.Ana, p.Ben]))).Value!;

        var result = await ctx.Chores.NudgeAsync(chore.Id, p.Admin, new NudgeChoreRequest(Note: "  Bins go out tonight  "));

        Assert.True(result.Succeeded);
        Assert.Equal([p.Ana], result.Value!.Select(u => u.Id));
        var inbox = Assert.Single(await InboxAsync(ctx, p.Ana));
        Assert.Equal(chore.Id, inbox.ChoreId);
        Assert.Equal("Admin nudged you: Bins go out tonight", inbox.Body);
        Assert.Empty(await InboxAsync(ctx, p.Ben));
        Assert.Equal("Bins go out tonight", (await ctx.Db.ChoreNudges.SingleAsync()).Note);
    }

    [Fact]
    public async Task NudgeAsync_rejects_self_and_people_not_holding_it()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Rotating(p.Ana, [p.Ana, p.Ben]))).Value!;

        var self = await ctx.Chores.NudgeAsync(chore.Id, p.Ana, new NudgeChoreRequest(ToUserId: p.Ana));
        var notHolder = await ctx.Chores.NudgeAsync(chore.Id, p.Admin, new NudgeChoreRequest(ToUserId: p.Ben));
        // The holder nudging with no target leaves nobody to nudge.
        var nobody = await ctx.Chores.NudgeAsync(chore.Id, p.Ana, new NudgeChoreRequest());

        Assert.Equal(ErrorType.Validation, self.Error!.Type);
        Assert.Equal(ErrorType.Validation, notHolder.Error!.Type);
        Assert.Equal(ErrorType.Validation, nobody.Error!.Type);
        Assert.Equal(0, await ctx.Db.UserNotifications.CountAsync());
    }

    [Fact]
    public async Task NudgeAsync_rejects_a_long_note_and_a_paused_chore()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Rotating(p.Ana, [p.Ana, p.Ben]))).Value!;

        var tooLong = await ctx.Chores.NudgeAsync(chore.Id, p.Admin,
            new NudgeChoreRequest(Note: new string('x', Validators.MaxNudgeNoteLength + 1)));
        await ctx.Chores.FreezeAsync(chore.Id);
        var paused = await ctx.Chores.NudgeAsync(chore.Id, p.Admin, new NudgeChoreRequest());

        Assert.Equal(ErrorType.Validation, tooLong.Error!.Type);
        Assert.Equal(ErrorType.Validation, paused.Error!.Type);
    }

    [Fact]
    public async Task NudgeAsync_cooldown_is_per_sender_and_lapses()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Rotating(p.Ana, [p.Ana, p.Ben]))).Value!;

        Assert.True((await ctx.Chores.NudgeAsync(chore.Id, p.Admin, new NudgeChoreRequest())).Succeeded);
        var again = await ctx.Chores.NudgeAsync(chore.Id, p.Admin, new NudgeChoreRequest());
        // Someone else's cooldown is their own.
        var otherSender = await ctx.Chores.NudgeAsync(chore.Id, p.Ben, new NudgeChoreRequest());

        Assert.Equal(ErrorType.Validation, again.Error!.Type);
        Assert.True(otherSender.Succeeded);
        Assert.Equal(2, (await InboxAsync(ctx, p.Ana)).Count);

        // Once the window has passed the sender can nudge again, and the old row is pruned.
        var sent = await ctx.Db.ChoreNudges.SingleAsync(n => n.FromUserId == p.Admin);
        sent.CreatedAt -= ChoreService.NudgeCooldown;
        await ctx.Db.SaveChangesAsync();

        Assert.True((await ctx.Chores.NudgeAsync(chore.Id, p.Admin, new NudgeChoreRequest())).Succeeded);
        Assert.Equal(1, await ctx.Db.ChoreNudges.CountAsync(n => n.FromUserId == p.Admin));
    }

    [Fact]
    public async Task NudgeAsync_reaches_both_members_of_a_pair()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            Rotating(p.Ana, [p.Ana, p.Ben, p.Cy], pairs: [[p.Ana, p.Ben]]))).Value!;

        var result = await ctx.Chores.NudgeAsync(chore.Id, p.Cy, new NudgeChoreRequest());

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { p.Ana, p.Ben }.Order(), result.Value!.Select(u => u.Id).Order());
        // Either member can be singled out too.
        var partner = await ctx.Chores.NudgeAsync(chore.Id, p.Admin, new NudgeChoreRequest(ToUserId: p.Ben));
        Assert.True(partner.Succeeded);
    }

    [Fact]
    public async Task NudgeAsync_on_demand_chore_reaches_the_holder()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        // Never due, but still somebody's turn.
        var chore = (await ctx.Chores.CreateAsync(Rotating(p.Ana, [p.Ana, p.Ben], repeat: RepeatType.OnDemand))).Value!;

        var result = await ctx.Chores.NudgeAsync(chore.Id, p.Ben, new NudgeChoreRequest());

        Assert.True(result.Succeeded);
        Assert.Equal([p.Ana], result.Value!.Select(u => u.Id));
    }

    [Fact]
    public async Task NudgeAsync_per_person_chore_reaches_every_other_assignee()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Independent([p.Ana, p.Ben, p.Cy]))).Value!;

        var result = await ctx.Chores.NudgeAsync(chore.Id, p.Ana, new NudgeChoreRequest());

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { p.Ben, p.Cy }.Order(), result.Value!.Select(u => u.Id).Order());
    }

    [Fact]
    public async Task NudgeAsync_respects_the_recipients_preference()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Rotating(p.Ana, [p.Ana, p.Ben]))).Value!;
        await ctx.Notifications.UpdatePreferenceAsync(p.Ana,
            new UpdateNotificationPreferenceRequest(NotificationCategory.Nudge, DeliveryChannel.Off));

        var result = await ctx.Chores.NudgeAsync(chore.Id, p.Admin, new NudgeChoreRequest());

        // The nudge still counts against the sender's cooldown; the recipient just doesn't hear it.
        Assert.True(result.Succeeded);
        Assert.Empty(await InboxAsync(ctx, p.Ana));
    }
}
