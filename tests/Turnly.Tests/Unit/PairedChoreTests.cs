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
    public async Task CreateAsync_rejects_the_same_pair_twice()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);

        var result = await ctx.Chores.CreateAsync(
            Paired(p.Ana, [p.Ana, p.Ben, p.Cy], [[p.Ana, p.Ben], [p.Ben, p.Ana]]));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task Overlapping_pairs_each_take_their_own_turn()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            Paired(p.Ana, [p.Ana, p.Ben, p.Cy], [[p.Ana, p.Ben], [p.Ana, p.Cy], [p.Ben, p.Cy]]))).Value!;

        // With no pair named, the holder starts on their first one.
        Assert.Equal(p.Ben, chore.CurrentPartner!.Id);
        Assert.Equal((p.Ana, p.Cy), (chore.NextAssignee!.Id, chore.NextPartner!.Id));

        var first = (await ctx.Chores.CompleteAsync(chore.Id, p.Ana, new CompleteChoreRequest(null))).Value!;
        Assert.Equal((p.Ana, p.Cy), (first.CurrentAssignee!.Id, first.CurrentPartner!.Id));

        // Cy closes it: the credit goes to the pair holding the turn, not Cy's other pair.
        var second = (await ctx.Chores.CompleteAsync(chore.Id, p.Cy, new CompleteChoreRequest(null))).Value!;
        Assert.Equal((p.Ben, p.Cy), (second.CurrentAssignee!.Id, second.CurrentPartner!.Id));
        Assert.Equal(20, await PointsAsync(ctx, p.Ana));
        Assert.Equal(10, await PointsAsync(ctx, p.Ben));
        Assert.Equal(10, await PointsAsync(ctx, p.Cy));

        var third = (await ctx.Chores.CompleteAsync(chore.Id, p.Ben, new CompleteChoreRequest(null))).Value!;
        Assert.Equal((p.Ana, p.Ben), (third.CurrentAssignee!.Id, third.CurrentPartner!.Id));
    }

    [Fact]
    public async Task CompleteAsync_credits_the_pair_an_admin_names()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            Paired(p.Ana, [p.Ana, p.Ben, p.Cy], [[p.Ana, p.Ben], [p.Ana, p.Cy], [p.Ben, p.Cy]]))).Value!;

        // Cy is in two pairs and not holding it, so by default they'd be credited alone.
        var done = await ctx.Chores.CompleteAsync(chore.Id, p.Admin, new CompleteChoreRequest(null, CompletedByUserIds: [p.Cy, p.Ben]));

        Assert.True(done.Succeeded);
        Assert.Equal(0, await PointsAsync(ctx, p.Ana));
        Assert.Equal(10, await PointsAsync(ctx, p.Ben));
        Assert.Equal(10, await PointsAsync(ctx, p.Cy));
        var entry = Assert.Single(await ctx.Chores.GetHistoryAsync(null, null, chore.Id));
        Assert.Equal((p.Ben, p.Cy), (entry.Actor!.Id, entry.Partner!.Id));
        Assert.Empty(entry.Helpers);
    }

    [Fact]
    public async Task CompleteAsync_credits_everyone_an_admin_names_assignees_or_not()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Paired(p.Ana, [p.Ana, p.Ben, p.Cy], [[p.Ana, p.Ben]]))).Value!;

        // Dee and the admin aren't assignees; Ana holds it, so Ana leads and the turn stays hers.
        var done = await ctx.Chores.CompleteAsync(chore.Id, p.Admin,
            new CompleteChoreRequest(null, CompletedByUserIds: [p.Dee, p.Ana, p.Admin]));

        Assert.True(done.Succeeded);
        foreach (var credited in new[] { p.Ana, p.Dee, p.Admin })
            Assert.Equal(10, await PointsAsync(ctx, credited));
        Assert.Equal(0, await PointsAsync(ctx, p.Ben)); // named nobody beyond the list, partner included
        Assert.Equal(p.Cy, done.Value!.CurrentAssignee!.Id);

        var entry = Assert.Single(await ctx.Chores.GetHistoryAsync(null, null, chore.Id));
        Assert.Equal(p.Ana, entry.Actor!.Id);
        Assert.Null(entry.Partner);
        Assert.Equal(new[] { p.Admin, p.Dee }.Order(), entry.Helpers.Select(h => h.Id).Order());
        Assert.Single(await ctx.Chores.GetHistoryAsync(null, p.Dee, chore.Id));
        var stats = await ctx.Users.GetStatsAsync();
        Assert.Equal(1, stats.UserStats.Single(u => u.UserId == p.Dee).AllTimeCount);

        Assert.True((await ctx.Chores.UndoCompletionAsync(entry.Id, p.Admin)).Succeeded);
        foreach (var credited in new[] { p.Ana, p.Dee, p.Admin })
            Assert.Equal(0, await PointsAsync(ctx, credited));
    }

    [Fact]
    public async Task CompleteAsync_lets_a_member_name_fellow_assignees_on_an_anyone_chore()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Paired(p.Ana, [p.Ana, p.Ben, p.Cy], [], AssignmentStrategy.Anyone))).Value!;

        // Not one of the doers, or naming a non-assignee: still admin-only.
        var withoutMe = await ctx.Chores.CompleteAsync(chore.Id, p.Ana, new CompleteChoreRequest(null, CompletedByUserIds: [p.Ben, p.Cy]));
        Assert.Equal(ErrorType.Forbidden, withoutMe.Error!.Type);
        var outsider = await ctx.Chores.CompleteAsync(chore.Id, p.Ana, new CompleteChoreRequest(null, CompletedByUserIds: [p.Ana, p.Dee]));
        Assert.Equal(ErrorType.Forbidden, outsider.Error!.Type);

        var done = await ctx.Chores.CompleteAsync(chore.Id, p.Ana, new CompleteChoreRequest(null, CompletedByUserIds: [p.Ana, p.Ben]));

        Assert.True(done.Succeeded);
        Assert.Equal(10, await PointsAsync(ctx, p.Ana));
        Assert.Equal(10, await PointsAsync(ctx, p.Ben));
        Assert.Equal(0, await PointsAsync(ctx, p.Cy));
        var entry = Assert.Single(await ctx.Chores.GetHistoryAsync(null, null, chore.Id));
        Assert.Equal(p.Ana, entry.Actor!.Id);
    }

    [Fact]
    public async Task CompleteAsync_rejects_named_people_from_a_member_or_several_on_a_per_person_chore()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            Paired(p.Ana, [p.Ana, p.Ben, p.Cy], [[p.Ana, p.Ben], [p.Ana, p.Cy]]))).Value!;

        var member = await ctx.Chores.CompleteAsync(chore.Id, p.Ana, new CompleteChoreRequest(null, CompletedByUserIds: [p.Ana, p.Cy]));
        Assert.Equal(ErrorType.Forbidden, member.Error!.Type);

        var tracks = (await ctx.Chores.CreateAsync(Paired(p.Ana, [p.Ana, p.Ben], [], AssignmentStrategy.Independent) with
        {
            Tracks = [new TrackInput(p.Ana, 1), new TrackInput(p.Ben, 1)]
        })).Value!;
        var several = await ctx.Chores.CompleteAsync(tracks.Id, p.Admin, new CompleteChoreRequest(null, CompletedByUserIds: [p.Ana, p.Ben]));
        Assert.Equal(ErrorType.Validation, several.Error!.Type);
    }

    [Fact]
    public async Task LeastCompleted_tells_overlapping_pairs_apart()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Paired(p.Ana, [p.Ana, p.Ben, p.Cy],
            [[p.Ana, p.Ben], [p.Ana, p.Cy]], AssignmentStrategy.LeastCompleted))).Value!;

        // Ana has now done it once, but only as half of Ana & Ben: Ana & Cy is still owed a turn.
        var first = (await ctx.Chores.CompleteAsync(chore.Id, p.Ana, new CompleteChoreRequest(null))).Value!;
        Assert.Equal((p.Ana, p.Cy), (first.CurrentAssignee!.Id, first.CurrentPartner!.Id));

        var second = (await ctx.Chores.CompleteAsync(chore.Id, p.Ana, new CompleteChoreRequest(null))).Value!;
        Assert.Equal((p.Ana, p.Ben), (second.CurrentAssignee!.Id, second.CurrentPartner!.Id));
    }

    [Fact]
    public async Task CreateAsync_starts_on_the_named_pair_and_rejects_a_partner_outside_the_pairs()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        Guid[][] pairs = [[p.Ana, p.Ben], [p.Ana, p.Cy]];

        var chore = (await ctx.Chores.CreateAsync(
            Paired(p.Ana, [p.Ana, p.Ben, p.Cy, p.Dee], pairs) with { CurrentPartnerId = p.Cy })).Value!;
        Assert.Equal(p.Cy, chore.CurrentPartner!.Id);

        var bad = await ctx.Chores.CreateAsync(
            Paired(p.Ana, [p.Ana, p.Ben, p.Cy, p.Dee], pairs) with { CurrentPartnerId = p.Dee });
        Assert.Equal(ErrorType.Validation, bad.Error!.Type);
    }

    [Fact]
    public async Task ReassignAsync_places_the_named_pair()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            Paired(p.Ana, [p.Ana, p.Ben, p.Cy], [[p.Ana, p.Ben], [p.Ana, p.Cy]]))).Value!;

        // Same person, another of their pairs: a real move, not the "already theirs" no-op.
        var moved = (await ctx.Chores.ReassignAsync(chore.Id, p.Admin, new ReassignChoreRequest(p.Ana, p.Cy))).Value!;
        Assert.Equal((p.Ana, p.Cy), (moved.CurrentAssignee!.Id, moved.CurrentPartner!.Id));

        var bad = await ctx.Chores.ReassignAsync(chore.Id, p.Admin, new ReassignChoreRequest(p.Ben, p.Cy));
        Assert.Equal(ErrorType.Validation, bad.Error!.Type);
    }

    [Fact]
    public async Task UndoCompletionAsync_gives_the_turn_back_to_the_same_pair()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            Paired(p.Ana, [p.Ana, p.Ben, p.Cy], [[p.Ana, p.Ben], [p.Ana, p.Cy], [p.Ben, p.Cy]]) with { CurrentPartnerId = p.Cy })).Value!;
        await ctx.Chores.CompleteAsync(chore.Id, p.Ana, new CompleteChoreRequest(null));
        var completionId = (await ctx.Db.ChoreCompletions.SingleAsync()).Id;

        await ctx.Chores.UndoCompletionAsync(completionId, p.Ana);

        var after = (await ctx.Chores.GetAsync(chore.Id)).Value!;
        Assert.Equal((p.Ana, p.Cy), (after.CurrentAssignee!.Id, after.CurrentPartner!.Id));
    }

    [Fact]
    public async Task A_frozen_member_takes_only_their_own_pairs_out_of_the_rotation()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            Paired(p.Ana, [p.Ana, p.Ben, p.Cy], [[p.Ana, p.Ben], [p.Ana, p.Cy], [p.Ben, p.Cy]]))).Value!;
        await ctx.Users.FreezeAsync(p.Cy);

        // Ana & Ben is the only pair left, so there is nobody to rotate to.
        var done = (await ctx.Chores.CompleteAsync(chore.Id, p.Ben, new CompleteChoreRequest(null))).Value!;

        Assert.Equal((p.Ana, p.Ben), (done.CurrentAssignee!.Id, done.CurrentPartner!.Id));
        Assert.Null(done.NextAssignee);
        Assert.Equal(0, await PointsAsync(ctx, p.Cy));
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
    public async Task UpdateActivity_credits_both_partners_when_an_expired_entry_is_marked_done()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var req = Paired(p.Ana, [p.Ana, p.Ben, p.Cy, p.Dee], [[p.Ana, p.Ben], [p.Cy, p.Dee]]) with
        {
            AutoAdvanceIncomplete = true
        };
        var chore = (await ctx.Chores.CreateAsync(req)).Value!;
        await ctx.Chores.AutoAdvanceAsync(chore.DueAt!.Value.AddMinutes(1));
        var expiredId = await ctx.Db.ChoreCompletions.Select(c => c.Id).SingleAsync();

        // Credited to the other pair: the partner follows whoever is named, not who missed it.
        var result = await ctx.Chores.UpdateActivityAsync(
            expiredId, p.Admin, new UpdateActivityRequest(chore.DueAt!.Value, p.Dee));

        Assert.True(result.Succeeded);
        var row = await ctx.Db.ChoreCompletions.SingleAsync();
        Assert.False(row.IsExpired);
        Assert.Equal(p.Dee, row.CompletedByUserId);
        Assert.Equal(p.Cy, row.PartnerUserId);
        Assert.Equal(10, await PointsAsync(ctx, p.Dee));
        Assert.Equal(10, await PointsAsync(ctx, p.Cy));
        Assert.Equal(0, await PointsAsync(ctx, p.Ben));
        Assert.Equal(2, await ctx.Db.PointsLog.CountAsync(e => e.ChoreCompletionId == expiredId));
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
