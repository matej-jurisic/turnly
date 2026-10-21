using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Dtos;
using Turnly.Core.Enums;

namespace Turnly.Tests.Unit;

public class OnDemandChoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 17, 9, 0, 0, TimeSpan.Zero);

    private static async Task<(Guid AdminId, Guid MemberId)> SeedUsersAsync(TestContext ctx)
    {
        var admin = await ctx.Setup.CreateFirstAdminAsync(new SetupRequest("admin", "Admin", "password123", null));
        var member = await ctx.Users.CreateAsync(
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        return (admin.Value!.User.Id, member.Value!.Id);
    }

    private static CreateChoreRequest NewChore(
        Guid currentAssignee,
        Guid[] assignees,
        RepeatType repeat = RepeatType.OnDemand,
        AssignmentStrategy strategy = AssignmentStrategy.RoundRobin) =>
        new("Water plants", null, null, 5, repeat, null, null, null, null, null, null, null, 1, false,
            strategy, SchedulingPreference.FromScheduledDate, null, false, null, Start, assignees, currentAssignee, null);

    private static UpdateChoreRequest ToUpdate(CreateChoreRequest c) =>
        new(c.Name, c.Description, c.Emoji, c.Points, c.RepeatType, c.CustomMode, c.IntervalCount,
            c.IntervalUnit, c.Weekdays, c.WeeksOfMonth, c.DaysOfMonth, c.Months, c.CompletionsRequired,
            c.RotateOnEachCompletion, c.AssignmentStrategy, c.SchedulingPreference, c.GraceMinutes,
            c.AutoAdvanceIncomplete, c.CompletionWindowMinutes,
            c.StartDate, c.AssigneeIds, c.CurrentAssigneeId, c.TagNames, c.Notifications, c.DueTime,
            RotationPeriod: c.RotationPeriod);

    [Fact]
    public async Task CreateAsync_on_demand_chore_has_no_due_date()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member]))).Value!;

        Assert.Equal(RepeatType.OnDemand, chore.RepeatType);
        Assert.Null(chore.DueAt);
    }

    [Fact]
    public async Task CreateAsync_on_demand_drops_scheduling_settings()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var notice = new ChoreNotificationInput(NotificationType.Due, NotificationTiming.AtDue, 0,
            NotificationOffsetUnit.Minutes, NotificationRecipients.AllAssignees);

        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member]) with
        {
            CompletionsRequired = 3,
            RotateOnEachCompletion = true,
            AutoAdvanceIncomplete = true,
            CompletionWindowMinutes = 60,
            SchedulingPreference = SchedulingPreference.SmartScheduling,
            GraceMinutes = 30,
            DueTime = "18:00",
            Notifications = [notice],
        })).Value!;

        Assert.Equal(1, chore.CompletionsRequired);
        Assert.False(chore.RotateOnEachCompletion);
        Assert.False(chore.AutoAdvanceIncomplete);
        Assert.Null(chore.CompletionWindowMinutes);
        Assert.Equal(SchedulingPreference.FromScheduledDate, chore.SchedulingPreference);
        Assert.Null(chore.GraceMinutes);
        Assert.Null(chore.DueTime);
        Assert.Empty(chore.Notifications);
    }

    [Fact]
    public async Task CompleteAsync_can_be_repeated_awards_points_and_stays_unscheduled()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member]))).Value!;

        var first = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));
        var second = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Null(second.Value!.DueAt);
        Assert.Equal(2, await ctx.Db.ChoreCompletions.CountAsync(c => c.ChoreId == chore.Id));
        Assert.Equal(10, (await ctx.Db.Users.FindAsync(member))!.Points);
    }

    [Fact]
    public async Task CompleteAsync_rotates_on_every_completion_and_predicts_next()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(admin, [admin, member]))).Value!;
        Assert.Equal(member, chore.NextAssignee!.Id);

        var afterAdmin = (await ctx.Chores.CompleteAsync(chore.Id, admin, new CompleteChoreRequest(null))).Value!;
        Assert.Equal(member, afterAdmin.CurrentAssignee!.Id);
        Assert.Equal(admin, afterAdmin.NextAssignee!.Id);

        var afterMember = (await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null))).Value!;
        Assert.Equal(admin, afterMember.CurrentAssignee!.Id);
    }

    [Fact]
    public async Task UndoCompletion_restores_assignee_and_keeps_it_unscheduled()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(admin, [admin, member]))).Value!;
        await ctx.Chores.CompleteAsync(chore.Id, admin, new CompleteChoreRequest(null));
        var completionId = await ctx.Db.ChoreCompletions.Select(c => c.Id).SingleAsync();

        var result = await ctx.Chores.UndoCompletionAsync(completionId, admin);

        Assert.True(result.Succeeded);
        var after = await ctx.Db.Chores.SingleAsync(c => c.Id == chore.Id);
        Assert.Equal(admin, after.CurrentAssigneeId);
        Assert.Null(after.DueAt);
        Assert.Equal(0, (await ctx.Db.Users.FindAsync(admin))!.Points);
    }

    [Fact]
    public async Task SkipAsync_rejects_on_demand_chore()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member]))).Value!;

        var result = await ctx.Chores.SkipAsync(chore.Id, member, new SkipChoreRequest(null));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task CreateAsync_rejects_on_demand_with_per_person_scheduling()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);

        var result = await ctx.Chores.CreateAsync(NewChore(admin, [admin, member], strategy: AssignmentStrategy.Independent)
            with { Tracks = [new(admin, 1), new(member, 1)] });

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task UpdateAsync_switching_between_daily_and_on_demand_clears_and_restores_due()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var daily = NewChore(member, [member], repeat: RepeatType.Daily);
        var chore = (await ctx.Chores.CreateAsync(daily)).Value!;
        Assert.NotNull(chore.DueAt);

        var onDemand = (await ctx.Chores.UpdateAsync(chore.Id, ToUpdate(daily) with { RepeatType = RepeatType.OnDemand })).Value!;
        Assert.Null(onDemand.DueAt);

        var back = (await ctx.Chores.UpdateAsync(chore.Id, ToUpdate(daily))).Value!;
        Assert.Equal(Start, back.DueAt);
    }

    [Fact]
    public async Task AutoAdvance_and_unfreeze_leave_on_demand_chore_unscheduled()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member]))).Value!;

        await ctx.Chores.FreezeAsync(chore.Id);
        var unfrozen = (await ctx.Chores.UnfreezeAsync(chore.Id)).Value!;
        await ctx.Chores.AutoAdvanceAsync(Start.AddYears(1));

        Assert.Null(unfrozen.DueAt);
        Assert.Null((await ctx.Db.Chores.FindAsync(chore.Id))!.DueAt);
        Assert.Empty(await ctx.Db.ChoreCompletions.ToListAsync());
    }
}
