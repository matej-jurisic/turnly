using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Dtos;
using Turnly.Core.Enums;

namespace Turnly.Tests.Unit;

public class ChoreServiceTests
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
        RepeatType repeat = RepeatType.Daily,
        int points = 10,
        DayOfWeek[]? weekdays = null,
        string[]? tags = null,
        CustomRecurrenceMode? customMode = null,
        int? intervalCount = null,
        RecurrenceUnit? intervalUnit = null,
        int[]? daysOfMonth = null,
        int[]? months = null,
        int? frequencyCount = null,
        FrequencyPeriod? frequencyPeriod = null,
        AssignmentStrategy strategy = AssignmentStrategy.KeepLastAssigned,
        SchedulingPreference scheduling = SchedulingPreference.FromScheduledDate,
        DateTimeOffset? start = null) =>
        new("Dishes", null, "🍽️", points, repeat, customMode, intervalCount, intervalUnit,
            weekdays, daysOfMonth, months, frequencyCount, frequencyPeriod,
            strategy, scheduling, start ?? Start, assignees, currentAssignee, tags);

    [Fact]
    public async Task CreateAsync_rejects_blank_name()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        var result = await ctx.Chores.CreateAsync(
            NewChore(member, [member]) with { Name = "  " });

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task CreateAsync_requires_at_least_one_assignee()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        var result = await ctx.Chores.CreateAsync(NewChore(member, []));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task CreateAsync_requires_current_assignee_in_assignees()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);

        var result = await ctx.Chores.CreateAsync(NewChore(admin, [member]));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task CreateAsync_weekly_does_not_require_weekdays()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        var result = await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.Weekly, weekdays: []));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task CreateAsync_sets_initial_due_to_start_date()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        var result = await ctx.Chores.CreateAsync(NewChore(member, [member]));

        Assert.True(result.Succeeded);
        Assert.Equal(Start, result.Value!.DueAt);
    }

    [Fact]
    public async Task CreateAsync_stores_and_round_trips_due_time()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        var result = await ctx.Chores.CreateAsync(NewChore(member, [member]) with { DueTime = "09:30" });

        Assert.True(result.Succeeded);
        Assert.Equal("09:30", result.Value!.DueTime);
        var stored = await ctx.Db.Chores.SingleAsync();
        Assert.Equal(new TimeOnly(9, 30), stored.DueTime);
    }

    [Fact]
    public async Task CreateAsync_rejects_malformed_due_time()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        var result = await ctx.Chores.CreateAsync(NewChore(member, [member]) with { DueTime = "9am" });

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task CreateAsync_ignores_due_time_for_frequency_chores()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        var result = await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.Custom,
                customMode: CustomRecurrenceMode.Frequency, frequencyCount: 3,
                frequencyPeriod: FrequencyPeriod.Week) with { DueTime = "09:30" });

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.DueTime);
    }

    [Fact]
    public async Task CreateAsync_reuses_existing_tags()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        await ctx.Chores.CreateAsync(NewChore(member, [member], tags: ["kitchen"]));
        await ctx.Chores.CreateAsync(NewChore(member, [member], tags: ["Kitchen", "outdoor"]));

        var tagNames = await ctx.Db.Tags.Select(t => t.Name).ToListAsync();
        Assert.Equal(2, tagNames.Count); // "kitchen" reused case-insensitively, "outdoor" new
    }

    [Fact]
    public async Task CompleteAsync_awards_points_advances_due_and_logs()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], points: 10))).Value!;

        var result = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest("done"));

        Assert.True(result.Succeeded);
        Assert.Equal(Start.AddDays(1), result.Value!.DueAt); // daily advanced
        Assert.Equal(10, (await ctx.Db.Users.FindAsync(member))!.Points);
        Assert.Equal(10, await ctx.Db.PointsLog.Where(e => e.UserId == member).SumAsync(e => e.Delta));
        Assert.Equal(1, await ctx.Db.ChoreCompletions.CountAsync());
    }

    [Fact]
    public async Task CompleteAsync_one_time_clears_due()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.OneTime))).Value!;

        var result = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.DueAt);
    }

    [Fact]
    public async Task UndoCompletion_reverses_points_and_restores_due()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], points: 10))).Value!;
        await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));
        var completionId = await ctx.Db.ChoreCompletions.Select(c => c.Id).SingleAsync();

        var result = await ctx.Chores.UndoCompletionAsync(completionId, member);

        Assert.True(result.Succeeded);
        Assert.Equal(0, (await ctx.Db.Users.FindAsync(member))!.Points);
        Assert.Empty(ctx.Db.PointsLog);
        Assert.Empty(ctx.Db.ChoreCompletions);
        Assert.Equal(Start, (await ctx.Db.Chores.FindAsync(chore.Id))!.DueAt); // restored
    }

    [Fact]
    public async Task UndoCompletion_forbids_other_non_admin_members()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var other = (await ctx.Users.CreateAsync(
            new CreateUserRequest("kid2", "Kid2", "kidpass2", UserRole.Member, null))).Value!.Id;
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member]))).Value!;
        await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));
        var completionId = await ctx.Db.ChoreCompletions.Select(c => c.Id).SingleAsync();

        var result = await ctx.Chores.UndoCompletionAsync(completionId, other);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Forbidden, result.Error!.Type);
    }

    [Fact]
    public async Task CreateAsync_rejects_custom_interval_without_count()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        var result = await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Custom,
            customMode: CustomRecurrenceMode.Interval, intervalUnit: RecurrenceUnit.Week));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task CreateAsync_rejects_day_that_never_occurs_in_selected_months()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        // Day 31 in February only — can never happen.
        var result = await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Custom,
            customMode: CustomRecurrenceMode.DaysOfMonth, daysOfMonth: [31], months: [2]));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task CreateAsync_allows_day_31_when_a_selected_month_supports_it()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        // Jan has 31 days, so day 31 + {Jan, Feb} is valid (February is simply skipped).
        var result = await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Custom,
            customMode: CustomRecurrenceMode.DaysOfMonth, daysOfMonth: [31], months: [1, 2]));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task CompleteAsync_keep_last_assigned_does_not_rotate()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(member, [admin, member], strategy: AssignmentStrategy.KeepLastAssigned))).Value!;

        var result = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));

        Assert.Equal(member, result.Value!.CurrentAssignee!.Id);
    }

    [Fact]
    public async Task CompleteAsync_round_robin_rotates_to_next_assignee()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        // admin was created first, so the stable order is [admin, member].
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(admin, [admin, member], strategy: AssignmentStrategy.RoundRobin))).Value!;

        var afterFirst = await ctx.Chores.CompleteAsync(chore.Id, admin, new CompleteChoreRequest(null));
        Assert.Equal(member, afterFirst.Value!.CurrentAssignee!.Id);

        var afterSecond = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));
        Assert.Equal(admin, afterSecond.Value!.CurrentAssignee!.Id); // wraps around
    }

    [Fact]
    public async Task CompleteAsync_from_completion_date_schedules_from_now()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Daily,
            scheduling: SchedulingPreference.FromCompletionDate))).Value!;

        var before = DateTimeOffset.UtcNow;
        var result = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));

        // Next due is ~1 day after the actual completion time, not after the scheduled Start.
        Assert.True(result.Value!.DueAt! >= before.AddDays(1).AddSeconds(-5));
        Assert.True(result.Value!.DueAt! <= DateTimeOffset.UtcNow.AddDays(1).AddSeconds(5));
    }

    [Fact]
    public async Task CompleteAsync_frequency_tracks_progress_then_rolls_over()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Custom,
            customMode: CustomRecurrenceMode.Frequency, frequencyCount: 2,
            frequencyPeriod: FrequencyPeriod.Week, start: DateTimeOffset.UtcNow))).Value!;

        var periodEnd = chore.DueAt;

        var first = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));
        Assert.Equal(1, first.Value!.FrequencyProgress);
        Assert.Equal(periodEnd, first.Value!.DueAt); // still due this period

        var second = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));
        Assert.True(second.Value!.DueAt > periodEnd); // rolled into the next period
        Assert.Equal(2, second.Value!.FrequencyProgress); // quota met for the current period
    }

    [Fact]
    public async Task SkipAsync_advances_due_without_points_and_keeps_assignee()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(member, [admin, member], points: 10, strategy: AssignmentStrategy.RoundRobin))).Value!;

        var result = await ctx.Chores.SkipAsync(chore.Id, member, new SkipChoreRequest("away"));

        Assert.True(result.Succeeded);
        Assert.Equal(Start.AddDays(1), result.Value!.DueAt); // daily advanced
        Assert.Equal(member, result.Value!.CurrentAssignee!.Id); // no rotation
        Assert.Equal(0, (await ctx.Db.Users.FindAsync(member))!.Points); // no points
        Assert.Empty(ctx.Db.PointsLog);
        var skip = await ctx.Db.ChoreCompletions.SingleAsync();
        Assert.True(skip.IsSkip);
        Assert.Equal(0, skip.PointsAwarded);
    }

    [Fact]
    public async Task SkipAsync_rejects_one_time_chore()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.OneTime))).Value!;

        var result = await ctx.Chores.SkipAsync(chore.Id, member, new SkipChoreRequest(null));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task SkipAsync_is_undoable_and_restores_due()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member]))).Value!;
        await ctx.Chores.SkipAsync(chore.Id, member, new SkipChoreRequest(null));
        var skipId = await ctx.Db.ChoreCompletions.Select(c => c.Id).SingleAsync();

        var result = await ctx.Chores.UndoCompletionAsync(skipId, member);

        Assert.True(result.Succeeded);
        Assert.Empty(ctx.Db.ChoreCompletions);
        Assert.Equal(Start, (await ctx.Db.Chores.FindAsync(chore.Id))!.DueAt); // restored
    }

    [Fact]
    public async Task SkipAsync_does_not_count_toward_frequency_progress()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Custom,
            customMode: CustomRecurrenceMode.Frequency, frequencyCount: 2,
            frequencyPeriod: FrequencyPeriod.Week, start: DateTimeOffset.UtcNow))).Value!;

        var skipped = await ctx.Chores.SkipAsync(chore.Id, member, new SkipChoreRequest(null));

        Assert.True(skipped.Succeeded);
        Assert.Equal(0, skipped.Value!.FrequencyProgress); // skip is not a completion
    }

    [Fact]
    public async Task ReassignAsync_sets_assignee_and_logs_assignment()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [admin, member]))).Value!;

        var result = await ctx.Chores.ReassignAsync(chore.Id, member, new ReassignChoreRequest(admin));

        Assert.True(result.Succeeded);
        Assert.Equal(admin, result.Value!.CurrentAssignee!.Id);
        // Initial assignment + the reassignment.
        Assert.Equal(2, await ctx.Db.ChoreAssignments.CountAsync(a => a.ChoreId == chore.Id));

        // The reassignment row records who did it and the previous assignee.
        var reassignment = await ctx.Db.ChoreAssignments
            .SingleAsync(a => a.ChoreId == chore.Id && a.AssignedByUserId != null);
        Assert.Equal(member, reassignment.AssignedByUserId);
        Assert.Equal(member, reassignment.PreviousAssigneeId);
        Assert.Equal(admin, reassignment.UserId);
    }

    [Fact]
    public async Task GetHistoryAsync_includes_reassignments_when_requested()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [admin, member]))).Value!;

        await ctx.Chores.ReassignAsync(chore.Id, member, new ReassignChoreRequest(admin));

        // Completions-only view (default) excludes reassignments and the initial assignment.
        var completionsOnly = await ctx.Chores.GetHistoryAsync(null, null, null);
        Assert.Empty(completionsOnly);

        var withReassignments = await ctx.Chores.GetHistoryAsync(null, null, null, includeReassignments: true);
        var entry = Assert.Single(withReassignments);
        Assert.Equal("reassignment", entry.Kind);
        Assert.Equal(member, entry.Actor!.Id);
        Assert.Equal(member, entry.FromAssignee!.Id);
        Assert.Equal(admin, entry.ToAssignee!.Id);
    }

    [Fact]
    public async Task ReassignAsync_rejects_non_assignee()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member]))).Value!;

        var result = await ctx.Chores.ReassignAsync(chore.Id, member, new ReassignChoreRequest(admin));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task UndoCompletion_restores_previous_assignee()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(admin, [admin, member], strategy: AssignmentStrategy.RoundRobin))).Value!;
        await ctx.Chores.CompleteAsync(chore.Id, admin, new CompleteChoreRequest(null));
        var completionId = await ctx.Db.ChoreCompletions.Select(c => c.Id).SingleAsync();

        var result = await ctx.Chores.UndoCompletionAsync(completionId, admin);

        Assert.True(result.Succeeded);
        var reloaded = await ctx.Db.Chores.FindAsync(chore.Id);
        Assert.Equal(admin, reloaded!.CurrentAssigneeId); // rotation reversed
        // Only the initial assignment row remains.
        Assert.Equal(1, await ctx.Db.ChoreAssignments.CountAsync(a => a.ChoreId == chore.Id));
    }

    [Fact]
    public async Task CompleteAsync_admin_can_complete_on_behalf_of_another_user()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], points: 10))).Value!;

        var result = await ctx.Chores.CompleteAsync(chore.Id, admin, new CompleteChoreRequest(null, member));

        Assert.True(result.Succeeded);
        Assert.Equal(10, (await ctx.Db.Users.FindAsync(member))!.Points); // credited to the member
        Assert.Equal(0, (await ctx.Db.Users.FindAsync(admin))!.Points);
        var completion = await ctx.Db.ChoreCompletions.SingleAsync();
        Assert.Equal(member, completion.CompletedByUserId);
    }

    [Fact]
    public async Task CompleteAsync_member_cannot_complete_on_behalf_of_another_user()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], points: 10))).Value!;

        var result = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null, admin));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Forbidden, result.Error!.Type);
        Assert.Equal(0, await ctx.Db.ChoreCompletions.CountAsync());
    }

    [Fact]
    public async Task CompleteAsync_credits_caller_when_no_on_behalf_user_given()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], points: 10))).Value!;

        var result = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));

        Assert.True(result.Succeeded);
        Assert.Equal(member, (await ctx.Db.ChoreCompletions.SingleAsync()).CompletedByUserId);
    }

    [Fact]
    public async Task DeleteActivity_reverses_points_without_rescheduling()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], points: 10))).Value!;
        await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));
        var completionId = await ctx.Db.ChoreCompletions.Select(c => c.Id).SingleAsync();
        var advancedDue = (await ctx.Db.Chores.FindAsync(chore.Id))!.DueAt;

        var result = await ctx.Chores.DeleteActivityAsync(completionId, admin);

        Assert.True(result.Succeeded);
        Assert.Equal(0, (await ctx.Db.Users.FindAsync(member))!.Points); // points reversed
        Assert.Empty(ctx.Db.PointsLog);
        Assert.Empty(ctx.Db.ChoreCompletions);
        // Schedule is NOT rewound (unlike undo): DueAt stays advanced.
        Assert.Equal(advancedDue, (await ctx.Db.Chores.FindAsync(chore.Id))!.DueAt);
        Assert.Equal(Start.AddDays(1), advancedDue);
    }

    [Fact]
    public async Task DeleteActivity_forbids_non_admins()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], points: 10))).Value!;
        await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));
        var completionId = await ctx.Db.ChoreCompletions.Select(c => c.Id).SingleAsync();

        var result = await ctx.Chores.DeleteActivityAsync(completionId, member);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Forbidden, result.Error!.Type);
        Assert.Equal(1, await ctx.Db.ChoreCompletions.CountAsync()); // untouched
    }

    [Fact]
    public async Task DeleteActivity_removes_a_skip_entry()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], points: 10))).Value!;
        await ctx.Chores.SkipAsync(chore.Id, admin, new SkipChoreRequest(null));
        var skipId = await ctx.Db.ChoreCompletions.Where(c => c.IsSkip).Select(c => c.Id).SingleAsync();

        var result = await ctx.Chores.DeleteActivityAsync(skipId, admin);

        Assert.True(result.Succeeded);
        Assert.Empty(ctx.Db.ChoreCompletions);
        Assert.Equal(0, (await ctx.Db.Users.FindAsync(member))!.Points); // skips award nothing
    }

    private static UpdateChoreRequest ToUpdate(CreateChoreRequest c) =>
        new(c.Name, c.Description, c.Emoji, c.Points, c.RepeatType, c.CustomMode, c.IntervalCount,
            c.IntervalUnit, c.Weekdays, c.DaysOfMonth, c.Months, c.FrequencyCount, c.FrequencyPeriod,
            c.AssignmentStrategy, c.SchedulingPreference, c.StartDate, c.AssigneeIds,
            c.CurrentAssigneeId, c.TagNames, c.Notifications, c.DueTime);

    [Fact]
    public async Task UpdateAsync_rebuilds_notifications_after_a_delivery_was_recorded()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var notif = new ChoreNotificationInput(
            NotificationType.Reminder, NotificationTiming.Before, 30,
            NotificationOffsetUnit.Minutes, NotificationRecipients.CurrentAssignee);
        var create = NewChore(member, [member]) with { Notifications = [notif] };
        var chore = (await ctx.Chores.CreateAsync(create)).Value!;

        // Simulate the scheduler having fired the notification at least once: a NotificationDelivery
        // row now references the existing ChoreNotification.
        var notificationId = await ctx.Db.ChoreNotifications.Select(n => n.Id).SingleAsync();
        ctx.Db.NotificationDeliveries.Add(new Core.Entities.NotificationDelivery
        {
            ChoreNotificationId = notificationId,
            OccurrenceDueAt = chore.DueAt!.Value,
        });
        await ctx.Db.SaveChangesAsync();

        // Editing the chore rebuilds (clears + re-adds) its notifications.
        var result = await ctx.Chores.UpdateAsync(chore.Id, ToUpdate(create));

        Assert.True(result.Succeeded);
        Assert.Single(result.Value!.Notifications);
    }

    [Fact]
    public async Task UpdateAsync_can_change_notifications_on_a_chore_that_has_them()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var reminder = new ChoreNotificationInput(
            NotificationType.Reminder, NotificationTiming.Before, 30,
            NotificationOffsetUnit.Minutes, NotificationRecipients.CurrentAssignee);
        var create = NewChore(member, [member]) with { Notifications = [reminder] };
        var chore = (await ctx.Chores.CreateAsync(create)).Value!;

        // Replace the schedule with a different entry.
        var dueNotice = new ChoreNotificationInput(
            NotificationType.Due, NotificationTiming.AtDue, 0,
            NotificationOffsetUnit.Minutes, NotificationRecipients.AllAssignees);
        var result = await ctx.Chores.UpdateAsync(chore.Id, ToUpdate(create) with { Notifications = [dueNotice] });

        Assert.True(result.Succeeded);
        var saved = Assert.Single(result.Value!.Notifications);
        Assert.Equal(NotificationType.Due, saved.Type);
        Assert.Equal(1, await ctx.Db.ChoreNotifications.CountAsync(n => n.ChoreId == chore.Id));
    }
}
