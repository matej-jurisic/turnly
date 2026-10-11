using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Dtos;
using Turnly.Core.Enums;

namespace Turnly.Tests.Unit;

/// <summary>Fixed pairs: two assignees hold, rotate and are credited for a chore as one unit.</summary>
public class PairedChoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 17, 9, 0, 0, TimeSpan.Zero);

    private record People(Guid Admin, Guid Ana, Guid Ben, Guid Cy, Guid Dee);

    private static async Task<People> SeedAsync(TestContext ctx)
    {
        var admin = await ctx.Setup.CreateFirstAdminAsync(new SetupRequest("admin", "Admin", "password123", null));
        async Task<Guid> Member(string name) =>
            (await ctx.Users.CreateAsync(new CreateUserRequest($"{name.ToLowerInvariant()}-user", name, "password1", UserRole.Member, null))).Value!.Id;
        return new People(admin.Value!.User.Id, await Member("Ana"), await Member("Ben"), await Member("Cy"), await Member("Dee"));
    }

    private static CreateChoreRequest Paired(Guid current, Guid[] assignees, Guid[][] pairs,
        AssignmentStrategy strategy = AssignmentStrategy.RoundRobin) =>
        new("Garage", null, null, 10, RepeatType.Daily, null, null, null, null, null, null, null, 1, false,
            strategy, SchedulingPreference.FromScheduledDate, null, false, null, Start, assignees, current, null,
            Pairs: pairs);

    private static Task<int> PointsAsync(TestContext ctx, Guid userId) =>
        ctx.Db.Users.Where(u => u.Id == userId).Select(u => u.Points).SingleAsync();

    [Fact]
    public async Task CreateAsync_rejects_a_person_in_two_pairs()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);

        var result = await ctx.Chores.CreateAsync(
            Paired(p.Ana, [p.Ana, p.Ben, p.Cy], [[p.Ana, p.Ben], [p.Ana, p.Cy]]));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_pair_member_who_is_not_an_assignee()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);

        var result = await ctx.Chores.CreateAsync(Paired(p.Ana, [p.Ana, p.Ben], [[p.Ana, p.Cy]]));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_pair_of_one_person()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);

        var result = await ctx.Chores.CreateAsync(Paired(p.Ana, [p.Ana, p.Ben], [[p.Ana, p.Ana]]));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GetAsync_surfaces_pairs_and_the_current_partner()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);

        var chore = (await ctx.Chores.CreateAsync(
            Paired(p.Ben, [p.Ana, p.Ben, p.Cy, p.Dee], [[p.Ana, p.Ben], [p.Cy, p.Dee]]))).Value!;

        Assert.Equal(2, chore.Pairs.Length);
        Assert.Equal(p.Ben, chore.CurrentAssignee!.Id);
        Assert.Equal(p.Ana, chore.CurrentPartner!.Id);
        Assert.Equal(p.Cy, chore.NextAssignee!.Id);
        Assert.Equal(p.Dee, chore.NextPartner!.Id);
    }

    [Fact]
    public async Task CompleteAsync_credits_both_partners_from_one_completion()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Paired(p.Ana, [p.Ana, p.Ben], [[p.Ana, p.Ben]]))).Value!;

        // The partner taps it, not the one nominally holding it.
        var done = await ctx.Chores.CompleteAsync(chore.Id, p.Ben, new CompleteChoreRequest(null));

        Assert.True(done.Succeeded);
        Assert.Equal(10, await PointsAsync(ctx, p.Ana));
        Assert.Equal(10, await PointsAsync(ctx, p.Ben));
        var row = await ctx.Db.ChoreCompletions.SingleAsync();
        Assert.Equal(p.Ben, row.CompletedByUserId);
        Assert.Equal(p.Ana, row.PartnerUserId);
        Assert.Equal(p.Ana, done.Value!.LastCompletion!.Partner!.Id);
        Assert.Equal(2, await ctx.Db.PointsLog.CountAsync(e => e.ChoreCompletionId == row.Id));
    }

    [Fact]
    public async Task CompleteAsync_rotates_between_whole_pairs()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            Paired(p.Ana, [p.Ana, p.Ben, p.Cy, p.Dee], [[p.Ana, p.Ben], [p.Cy, p.Dee]]))).Value!;

        var first = (await ctx.Chores.CompleteAsync(chore.Id, p.Ana, new CompleteChoreRequest(null))).Value!;
        Assert.Equal(p.Cy, first.CurrentAssignee!.Id);
        Assert.Equal(p.Dee, first.CurrentPartner!.Id);

        // Either member closing it hands the turn back to the other pair, never to their own partner.
        var second = (await ctx.Chores.CompleteAsync(chore.Id, p.Dee, new CompleteChoreRequest(null))).Value!;
        Assert.Equal(p.Ana, second.CurrentAssignee!.Id);
        Assert.Equal(p.Ben, second.CurrentPartner!.Id);
    }

    [Fact]
    public async Task CompleteAsync_rotates_a_pair_with_a_solo_assignee()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            Paired(p.Ana, [p.Ana, p.Ben, p.Cy], [[p.Ana, p.Ben]]))).Value!;

        var first = (await ctx.Chores.CompleteAsync(chore.Id, p.Ana, new CompleteChoreRequest(null))).Value!;
        Assert.Equal(p.Cy, first.CurrentAssignee!.Id);
        Assert.Null(first.CurrentPartner);

        var second = (await ctx.Chores.CompleteAsync(chore.Id, p.Cy, new CompleteChoreRequest(null))).Value!;
        Assert.Equal(p.Ana, second.CurrentAssignee!.Id);
        Assert.Equal(p.Ben, second.CurrentPartner!.Id);
        Assert.Equal(10, await PointsAsync(ctx, p.Ben)); // credited once, for the pair's own turn only
    }

    [Fact]
    public async Task LeastCompleted_counts_a_pairs_completions_as_one_unit()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Paired(p.Ana, [p.Ana, p.Ben, p.Cy, p.Dee],
            [[p.Ana, p.Ben], [p.Cy, p.Dee]], AssignmentStrategy.LeastCompleted))).Value!;

        // Ben closes the first pair's turn; the second pair has done nothing yet, so it's theirs.
        var first = (await ctx.Chores.CompleteAsync(chore.Id, p.Ben, new CompleteChoreRequest(null))).Value!;
        Assert.Equal(p.Cy, first.CurrentAssignee!.Id);

        // Now one each: the tie goes to whoever went longest ago, which is the first pair again.
        var second = (await ctx.Chores.CompleteAsync(chore.Id, p.Cy, new CompleteChoreRequest(null))).Value!;
        Assert.Equal(p.Ana, second.CurrentAssignee!.Id);
    }

    [Fact]
    public async Task A_single_pair_has_nobody_to_rotate_to()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Paired(p.Ana, [p.Ana, p.Ben], [[p.Ana, p.Ben]]))).Value!;
        Assert.Null(chore.NextAssignee);

        var done = (await ctx.Chores.CompleteAsync(chore.Id, p.Ana, new CompleteChoreRequest(null))).Value!;

        Assert.Equal(p.Ana, done.CurrentAssignee!.Id);
        Assert.Equal(p.Ben, done.CurrentPartner!.Id);
    }

    [Fact]
    public async Task UndoCompletionAsync_takes_back_both_partners_points_and_the_partner_may_undo()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            Paired(p.Ana, [p.Ana, p.Ben, p.Cy, p.Dee], [[p.Ana, p.Ben], [p.Cy, p.Dee]]))).Value!;
        await ctx.Chores.CompleteAsync(chore.Id, p.Ana, new CompleteChoreRequest(null));
        var completionId = (await ctx.Db.ChoreCompletions.SingleAsync()).Id;

        Assert.Equal(ErrorType.Forbidden, (await ctx.Chores.UndoCompletionAsync(completionId, p.Cy)).Error!.Type);
        var undo = await ctx.Chores.UndoCompletionAsync(completionId, p.Ben);

        Assert.True(undo.Succeeded);
        Assert.Equal(0, await PointsAsync(ctx, p.Ana));
        Assert.Equal(0, await PointsAsync(ctx, p.Ben));
        Assert.Empty(await ctx.Db.PointsLog.ToListAsync());
        Assert.Equal(p.Ana, (await ctx.Chores.GetAsync(chore.Id)).Value!.CurrentAssignee!.Id);
    }

    [Fact]
    public async Task A_frozen_partner_is_neither_shown_nor_credited()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Paired(p.Ana, [p.Ana, p.Ben], [[p.Ana, p.Ben]]))).Value!;
        await ctx.Users.FreezeAsync(p.Ben);

        Assert.Null((await ctx.Chores.GetAsync(chore.Id)).Value!.CurrentPartner);
        await ctx.Chores.CompleteAsync(chore.Id, p.Ana, new CompleteChoreRequest(null));

        Assert.Equal(10, await PointsAsync(ctx, p.Ana));
        Assert.Equal(0, await PointsAsync(ctx, p.Ben));
        Assert.Null((await ctx.Db.ChoreCompletions.SingleAsync()).PartnerUserId);
    }

    [Fact]
    public async Task Freezing_the_holder_leaves_the_chore_with_their_partner()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            Paired(p.Ana, [p.Ana, p.Ben, p.Cy, p.Dee], [[p.Ana, p.Ben], [p.Cy, p.Dee]]))).Value!;

        await ctx.Users.FreezeAsync(p.Ana);

        var after = (await ctx.Chores.GetAsync(chore.Id)).Value!;
        Assert.Equal(p.Ben, after.CurrentAssignee!.Id);
        Assert.Null(after.CurrentPartner);
        // The other pair is still next: Ben alone is what's left of the first unit.
        Assert.Equal(p.Cy, after.NextAssignee!.Id);
    }

    [Fact]
    public async Task OfferAsync_is_rejected_on_a_paired_chore()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            Paired(p.Ana, [p.Ana, p.Ben, p.Cy, p.Dee], [[p.Ana, p.Ben], [p.Cy, p.Dee]]))).Value!;

        var result = await ctx.Chores.OfferAsync(chore.Id, p.Ana, new OfferChoreRequest());

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task ReassignAsync_to_the_holders_partner_is_a_no_op()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            Paired(p.Ana, [p.Ana, p.Ben, p.Cy, p.Dee], [[p.Ana, p.Ben], [p.Cy, p.Dee]]))).Value!;

        var same = (await ctx.Chores.ReassignAsync(chore.Id, p.Admin, new ReassignChoreRequest(p.Ben))).Value!;
        Assert.Equal(p.Ana, same.CurrentAssignee!.Id);

        var moved = (await ctx.Chores.ReassignAsync(chore.Id, p.Admin, new ReassignChoreRequest(p.Dee))).Value!;
        Assert.Equal(p.Dee, moved.CurrentAssignee!.Id);
        Assert.Equal(p.Cy, moved.CurrentPartner!.Id);
    }

    [Fact]
    public async Task Independent_chores_drop_their_pairs()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var req = Paired(p.Ana, [p.Ana, p.Ben], [[p.Ana, p.Ben]], AssignmentStrategy.Independent) with
        {
            Tracks = [new TrackInput(p.Ana, 1), new TrackInput(p.Ben, 1)]
        };

        var chore = (await ctx.Chores.CreateAsync(req)).Value!;

        Assert.Empty(chore.Pairs);
    }

    [Fact]
    public async Task AutoAdvanceAsync_shares_the_miss_with_the_partner()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var req = Paired(p.Ana, [p.Ana, p.Ben, p.Cy, p.Dee], [[p.Ana, p.Ben], [p.Cy, p.Dee]]) with
        {
            AutoAdvanceIncomplete = true
        };
        var chore = (await ctx.Chores.CreateAsync(req)).Value!;

        await ctx.Chores.AutoAdvanceAsync(chore.DueAt!.Value.AddMinutes(1));

        var expired = await ctx.Db.ChoreCompletions.SingleAsync();
        Assert.True(expired.IsExpired);
        Assert.Equal(p.Ana, expired.PreviousAssigneeId);
        Assert.Equal(p.Ben, expired.PartnerUserId);
        var stats = await ctx.Users.GetStatsAsync();
        Assert.Equal(1, stats.UserStats.Single(u => u.UserId == p.Ben).MissedCount);
        Assert.Equal(0, stats.UserStats.Single(u => u.UserId == p.Ben).AllTimeCount);
    }

    [Fact]
    public async Task Stats_and_history_count_a_paired_completion_for_both()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Paired(p.Ana, [p.Ana, p.Ben], [[p.Ana, p.Ben]]))).Value!;
        await ctx.Chores.CompleteAsync(chore.Id, p.Ana, new CompleteChoreRequest(null));

        var stats = await ctx.Users.GetStatsAsync();
        Assert.Equal(1, stats.UserStats.Single(u => u.UserId == p.Ana).AllTimeCount);
        Assert.Equal(1, stats.UserStats.Single(u => u.UserId == p.Ben).AllTimeCount);

        var history = await ctx.Chores.GetHistoryAsync(null, p.Ben, null);
        Assert.Equal(p.Ben, Assert.Single(history).Partner!.Id);
        Assert.Equal(1, (await ctx.Achievements.ComputeStatsAsync(p.Ben)).Completions);
    }
}
