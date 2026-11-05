using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Dtos;
using Turnly.Core.Enums;
using Turnly.Core.Recurrence;

namespace Turnly.Tests.Unit;

/// <summary>A turn per time-of-day slot: each slot of a twice-a-day chore has its own holder, due date
/// and rotation, while the history stays one chore's.</summary>
public class SlotTurnTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Noon = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Night = new(2026, 6, 17, 23, 0, 0, TimeSpan.Zero);

    private record People(Guid Admin, Guid Ana, Guid Ben, Guid Cy);

    private static async Task<People> SeedAsync(TestContext ctx)
    {
        var admin = await ctx.Setup.CreateFirstAdminAsync(new SetupRequest("admin", "Admin", "password123", null));
        async Task<Guid> Member(string name) =>
            (await ctx.Users.CreateAsync(new CreateUserRequest($"{name.ToLowerInvariant()}-user", name, "password1", UserRole.Member, null))).Value!.Id;
        return new People(admin.Value!.User.Id, await Member("Ana"), await Member("Ben"), await Member("Cy"));
    }

    /// <summary>"Feed the dog" at 12:00 (Morning) and 23:00 (Evening), a turn per slot.</summary>
    private static CreateChoreRequest Feed(Guid[] assignees, Guid morning, Guid evening,
        AssignmentStrategy strategy = AssignmentStrategy.RoundRobin) =>
        new("Feed the dog", null, null, 5, RepeatType.Daily, null, null, null, null, null, null, null, 1, false,
            strategy, SchedulingPreference.FromScheduledDate, null, false, null, Start, assignees, morning, null,
            TimesOfDay: ["12:00", "23:00"],
            TimeLabels: new() { ["12:00"] = "Morning", ["23:00"] = "Evening" },
            TurnPerSlot: true,
            SlotTurns: [new SlotTurnInput("12:00", morning), new SlotTurnInput("23:00", evening)]);

    private static UpdateChoreRequest ToUpdate(CreateChoreRequest c) =>
        new(c.Name, c.Description, c.Emoji, c.Points, c.RepeatType, c.CustomMode, c.IntervalCount,
            c.IntervalUnit, c.Weekdays, c.WeeksOfMonth, c.DaysOfMonth, c.Months, c.CompletionsRequired,
            c.RotateOnEachCompletion, c.AssignmentStrategy, c.SchedulingPreference, c.GraceMinutes,
            c.AutoAdvanceIncomplete, c.CompletionWindowMinutes,
            c.StartDate, c.AssigneeIds, c.CurrentAssigneeId, c.TagNames, c.Notifications, c.DueTime,
            c.Tracks, c.TimesOfDay, c.RotationPeriod, c.TimeLabels, c.Pairs, c.CurrentPartnerId,
            c.TurnPerSlot, c.SlotTurns);

    private static ChoreSlotTurnDto Slot(ChoreDto chore, string time) => chore.SlotTurns.Single(s => s.Time == time);

    [Fact]
    public async Task CreateAsync_gives_each_time_its_own_holder_and_mirrors_the_earliest()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);

        var chore = (await ctx.Chores.CreateAsync(Feed([p.Ana, p.Ben], p.Ana, p.Ben))).Value!;

        Assert.True(chore.TurnPerSlot);
        Assert.Equal(["12:00", "23:00"], chore.SlotTurns.Select(s => s.Time));
        Assert.Equal((p.Ana, Noon, "Morning"), (Slot(chore, "12:00").CurrentAssignee!.Id, Slot(chore, "12:00").DueAt!.Value, Slot(chore, "12:00").Label));
        Assert.Equal((p.Ben, Night), (Slot(chore, "23:00").CurrentAssignee!.Id, Slot(chore, "23:00").DueAt!.Value));
        Assert.Equal((p.Ana, Noon), (chore.CurrentAssignee!.Id, chore.DueAt!.Value));
        // Each slot logged its own initial assignment.
        Assert.Equal(2, await ctx.Db.ChoreAssignments.CountAsync(a => a.SlotTime != null));
    }

    [Fact]
    public async Task CreateAsync_turns_the_setting_off_where_it_cannot_apply()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);

        var oneTime = (await ctx.Chores.CreateAsync(Feed([p.Ana, p.Ben], p.Ana, p.Ben) with { TimesOfDay = ["12:00"] })).Value!;
        var twice = (await ctx.Chores.CreateAsync(Feed([p.Ana, p.Ben], p.Ana, p.Ben) with { CompletionsRequired = 2 })).Value!;
        var anyone = (await ctx.Chores.CreateAsync(Feed([p.Ana, p.Ben], p.Ana, p.Ben, AssignmentStrategy.Anyone))).Value!;

        Assert.All([oneTime, twice, anyone], c => Assert.False(c.TurnPerSlot));
        Assert.All([oneTime, twice, anyone], c => Assert.Empty(c.SlotTurns));
        Assert.Equal(0, await ctx.Db.ChoreSlotTurns.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_rejects_a_slot_holder_who_isnt_an_assignee()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);

        var result = await ctx.Chores.CreateAsync(Feed([p.Ana, p.Ben], p.Ana, p.Cy));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task CompleteAsync_can_do_the_evening_while_the_morning_is_still_open()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Feed([p.Ana, p.Ben], p.Ana, p.Ben))).Value!;

        var done = (await ctx.Chores.CompleteAsync(chore.Id, p.Ben, new CompleteChoreRequest(null, SlotTime: "23:00"))).Value!;

        // The evening moved on to tomorrow; the morning is untouched and still Ana's.
        Assert.Equal(Night.AddDays(1), Slot(done, "23:00").DueAt);
        Assert.Equal((p.Ana, Noon), (Slot(done, "12:00").CurrentAssignee!.Id, Slot(done, "12:00").DueAt!.Value));
        Assert.Equal(Noon, done.DueAt);

        var row = await ctx.Db.ChoreCompletions.SingleAsync();
        Assert.Equal((Night, p.Ben, p.Ben), (row.OccurrenceDueAt!.Value, row.CompletedByUserId!.Value, row.PreviousAssigneeId!.Value));
    }

    [Fact]
    public async Task CompleteAsync_without_a_slot_does_the_completers_own_turn_that_day()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Feed([p.Ana, p.Ben], p.Ana, p.Ben))).Value!;

        await ctx.Chores.CompleteAsync(chore.Id, p.Ben, new CompleteChoreRequest(null));
        Assert.Equal(Night, (await ctx.Db.ChoreCompletions.SingleAsync()).OccurrenceDueAt);

        // Cy holds nothing, so the earliest open slot (Ana's morning) is the one covered.
        await ctx.Chores.CompleteAsync(chore.Id, p.Admin, new CompleteChoreRequest(null, CompletedByUserId: p.Cy));
        Assert.Contains(await ctx.Db.ChoreCompletions.ToListAsync(), c => c.OccurrenceDueAt == Noon && c.CompletedByUserId == p.Cy);
    }

    [Fact]
    public async Task Each_slot_rotates_on_its_own_and_staggered_holders_stay_staggered()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Feed([p.Ana, p.Ben], p.Ana, p.Ben))).Value!;

        await ctx.Chores.CompleteAsync(chore.Id, p.Ana, new CompleteChoreRequest(null, SlotTime: "12:00"));
        var after = (await ctx.Chores.CompleteAsync(chore.Id, p.Ben, new CompleteChoreRequest(null, SlotTime: "23:00"))).Value!;

        // Tomorrow: Ben has the morning, Ana the evening.
        Assert.Equal(p.Ben, Slot(after, "12:00").CurrentAssignee!.Id);
        Assert.Equal(p.Ana, Slot(after, "23:00").CurrentAssignee!.Id);
    }

    [Fact]
    public async Task Rotation_passes_over_whoever_already_has_another_slot_that_day()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        // Both slots start with Ana.
        var chore = (await ctx.Chores.CreateAsync(Feed([p.Ana, p.Ben], p.Ana, p.Ana))).Value!;

        var morning = (await ctx.Chores.CompleteAsync(chore.Id, p.Ana, new CompleteChoreRequest(null, SlotTime: "12:00"))).Value!;
        Assert.Equal(p.Ben, Slot(morning, "12:00").CurrentAssignee!.Id);
        // Round-robin would hand tomorrow's evening to Ben too, but he already has tomorrow's morning.
        Assert.Equal(p.Ana, Slot(morning, "23:00").NextAssignee!.Id);

        var evening = (await ctx.Chores.CompleteAsync(chore.Id, p.Ana, new CompleteChoreRequest(null, SlotTime: "23:00"))).Value!;
        Assert.Equal(p.Ana, Slot(evening, "23:00").CurrentAssignee!.Id);
    }

    [Fact]
    public void Picker_avoids_busy_units_while_someone_else_is_free()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        Guid[] all = [a, b, c];
        var none = new Dictionary<Guid, int>();
        var never = new Dictionary<Guid, DateTimeOffset>();

        // Round-robin keeps its order, skipping the busy one.
        Assert.Equal(c, AssignmentPicker.Pick(AssignmentStrategy.RoundRobin, all, a, none, none, never, never, new Random(0), new HashSet<Guid> { b }));
        // Least-completed chooses among the rest, even though the busy one has done least.
        var completed = new Dictionary<Guid, int> { [a] = 3, [b] = 0, [c] = 5 };
        Assert.Equal(a, AssignmentPicker.Pick(AssignmentStrategy.LeastCompleted, all, null, none, completed, never, never, new Random(0), new HashSet<Guid> { b }));
        // Everyone busy: no constraint.
        Assert.Equal(b, AssignmentPicker.Pick(AssignmentStrategy.RoundRobin, all, a, none, none, never, never, new Random(0), new HashSet<Guid> { a, b, c }));
    }

    [Fact]
    public async Task UndoCompletionAsync_rewinds_only_that_slot()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Feed([p.Ana, p.Ben], p.Ana, p.Ben))).Value!;
        await ctx.Chores.CompleteAsync(chore.Id, p.Ana, new CompleteChoreRequest(null, SlotTime: "12:00"));
        await ctx.Chores.CompleteAsync(chore.Id, p.Ben, new CompleteChoreRequest(null, SlotTime: "23:00"));
        var evening = await ctx.Db.ChoreCompletions.SingleAsync(c => c.CompletedByUserId == p.Ben);

        Assert.True((await ctx.Chores.UndoCompletionAsync(evening.Id, p.Ben)).Succeeded);

        var after = (await ctx.Chores.GetAsync(chore.Id)).Value!;
        Assert.Equal((p.Ben, Night), (Slot(after, "23:00").CurrentAssignee!.Id, Slot(after, "23:00").DueAt!.Value));
        Assert.Equal((p.Ben, Noon.AddDays(1)), (Slot(after, "12:00").CurrentAssignee!.Id, Slot(after, "12:00").DueAt!.Value));
        Assert.Equal(Night, after.DueAt); // tonight's evening is open again, and earliest
    }

    [Fact]
    public async Task AutoAdvanceAsync_expires_each_slot_on_its_own_window()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Feed([p.Ana, p.Ben], p.Ana, p.Ben) with { AutoAdvanceIncomplete = true })).Value!;

        await ctx.Chores.AutoAdvanceAsync(Noon.AddHours(1));

        var expired = await ctx.Db.ChoreCompletions.SingleAsync();
        Assert.True(expired.IsExpired);
        Assert.Equal((Noon, p.Ana), (expired.OccurrenceDueAt!.Value, expired.PreviousAssigneeId!.Value));
        var after = (await ctx.Chores.GetAsync(chore.Id)).Value!;
        Assert.Equal(Noon.AddDays(1), Slot(after, "12:00").DueAt);
        Assert.Equal((p.Ben, Night), (Slot(after, "23:00").CurrentAssignee!.Id, Slot(after, "23:00").DueAt!.Value));
    }

    [Fact]
    public async Task UpdateAsync_turning_it_on_keeps_the_open_occurrence_and_off_keeps_the_earliest()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var plain = Feed([p.Ana, p.Ben], p.Ana, p.Ben) with { TurnPerSlot = false, SlotTurns = null };
        var chore = (await ctx.Chores.CreateAsync(plain)).Value!;
        await ctx.Chores.CompleteAsync(chore.Id, p.Ana, new CompleteChoreRequest(null)); // today's noon done

        var on = (await ctx.Chores.UpdateAsync(chore.Id, ToUpdate(plain with
        {
            TurnPerSlot = true,
            SlotTurns = [new SlotTurnInput("23:00", p.Ben)],
        }))).Value!;

        // The open evening stays where it was; the morning picks up at tomorrow's.
        Assert.Equal((p.Ben, Night), (Slot(on, "23:00").CurrentAssignee!.Id, Slot(on, "23:00").DueAt!.Value));
        Assert.Equal(Noon.AddDays(1), Slot(on, "12:00").DueAt);
        Assert.Equal(1, await ctx.Db.ChoreCompletions.CountAsync()); // history untouched

        var off = (await ctx.Chores.UpdateAsync(chore.Id, ToUpdate(plain with { CurrentAssigneeId = p.Ben }))).Value!;
        Assert.False(off.TurnPerSlot);
        Assert.Equal((p.Ben, Night), (off.CurrentAssignee!.Id, off.DueAt!.Value));
        Assert.Equal(0, await ctx.Db.ChoreSlotTurns.CountAsync());
    }

    [Fact]
    public async Task Offers_and_reassignment_move_one_slot()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Feed([p.Ana, p.Ben, p.Cy], p.Ana, p.Ana))).Value!;

        var offered = (await ctx.Chores.OfferAsync(chore.Id, p.Ana, new OfferChoreRequest(0, "23:00"))).Value!;
        Assert.Equal("23:00", Slot(offered, "23:00").OpenOffer!.SlotTime);
        Assert.Null(Slot(offered, "12:00").OpenOffer);

        var claimed = (await ctx.Chores.ClaimOfferAsync(chore.Id, p.Cy, "23:00")).Value!;
        Assert.Equal(p.Cy, Slot(claimed, "23:00").CurrentAssignee!.Id);
        Assert.Equal(p.Ana, Slot(claimed, "12:00").CurrentAssignee!.Id);

        var moved = (await ctx.Chores.ReassignAsync(chore.Id, p.Admin, new ReassignChoreRequest(p.Ben, SlotTime: "12:00"))).Value!;
        Assert.Equal((p.Ben, p.Cy), (Slot(moved, "12:00").CurrentAssignee!.Id, Slot(moved, "23:00").CurrentAssignee!.Id));
        Assert.Equal(p.Ben, moved.CurrentAssignee!.Id); // the mirror follows the earliest slot
    }

    [Fact]
    public async Task Freezing_a_user_hands_each_of_their_slots_on()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(Feed([p.Ana, p.Ben, p.Cy], p.Ana, p.Ben))).Value!;

        await ctx.Users.FreezeAsync(p.Ben);

        var after = (await ctx.Chores.GetAsync(chore.Id)).Value!;
        Assert.Equal(p.Ana, Slot(after, "12:00").CurrentAssignee!.Id);
        // Round-robin after Ben is Cy.
        Assert.Equal(p.Cy, Slot(after, "23:00").CurrentAssignee!.Id);
    }

    [Fact]
    public async Task ProcessDueAsync_reminds_each_slots_own_holder()
    {
        using var ctx = new TestContext();
        var p = await SeedAsync(ctx);
        var atDue = new ChoreNotificationInput(NotificationType.Due, NotificationTiming.AtDue, 0,
            NotificationOffsetUnit.Minutes, NotificationRecipients.CurrentAssignee);
        await ctx.Chores.CreateAsync(Feed([p.Ana, p.Ben], p.Ana, p.Ben) with { Notifications = [atDue] });

        await ctx.Notifications.ProcessDueAsync(Noon);
        await ctx.Notifications.ProcessDueAsync(Night);

        var inbox = await ctx.Db.UserNotifications.ToListAsync();
        Assert.Equal(2, inbox.Count);
        Assert.Contains(inbox, n => n.UserId == p.Ana && n.Title.Contains("Morning"));
        Assert.Contains(inbox, n => n.UserId == p.Ben && n.Title.Contains("Evening"));
    }
}
