using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;
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
        int[]? weeksOfMonth = null,
        int[]? daysOfMonth = null,
        int[]? months = null,
        int completionsRequired = 1,
        bool rotateOnEachCompletion = false,
        AssignmentStrategy strategy = AssignmentStrategy.KeepLastAssigned,
        SchedulingPreference scheduling = SchedulingPreference.FromScheduledDate,
        int? graceMinutes = null,
        DateTimeOffset? start = null) =>
        new("Dishes", null, "🍽️", points, repeat, customMode, intervalCount, intervalUnit,
            weekdays, weeksOfMonth, daysOfMonth, months, completionsRequired, rotateOnEachCompletion,
            strategy, scheduling, graceMinutes, false, null, start ?? Start, assignees, currentAssignee, tags);

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
    public async Task CreateAsync_keeps_due_time_for_multi_completion_chores()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        var result = await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.Weekly, completionsRequired: 3) with { DueTime = "09:30" });

        Assert.True(result.Succeeded);
        Assert.Equal("09:30", result.Value!.DueTime);
        Assert.Equal(3, result.Value!.CompletionsRequired);
    }

    [Fact]
    public async Task CreateAsync_rejects_completion_count_on_custom_recurrence()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        var result = await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.Custom,
                customMode: CustomRecurrenceMode.Interval, intervalCount: 2,
                intervalUnit: RecurrenceUnit.Week, completionsRequired: 3));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
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
    public async Task CreateAsync_persists_days_of_week_occurrence_restriction()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        var result = await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Custom,
            customMode: CustomRecurrenceMode.DaysOfWeek, weekdays: [DayOfWeek.Monday],
            weeksOfMonth: [3, 1, -1]));

        Assert.True(result.Succeeded);
        // -1 (last) is normalised to sort after the numbered occurrences.
        Assert.Equal(new[] { 1, 3, -1 }, result.Value!.WeeksOfMonth);
    }

    [Fact]
    public async Task CreateAsync_rejects_invalid_week_of_month_occurrence()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        // 5 isn't an offered occurrence (only 1–4 and -1/last).
        var result = await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Custom,
            customMode: CustomRecurrenceMode.DaysOfWeek, weekdays: [DayOfWeek.Monday],
            weeksOfMonth: [5]));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
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
    public async Task NextAssignee_predicts_round_robin_rotation()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        // admin created first → stable order [admin, member]; current is admin, so next is member.
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(admin, [admin, member], strategy: AssignmentStrategy.RoundRobin))).Value!;

        Assert.Equal(member, chore.NextAssignee!.Id);

        // The prediction matches who the rotation actually picks.
        var afterFirst = await ctx.Chores.CompleteAsync(chore.Id, admin, new CompleteChoreRequest(null));
        Assert.Equal(member, afterFirst.Value!.CurrentAssignee!.Id);
        Assert.Equal(admin, afterFirst.Value!.NextAssignee!.Id); // wraps back
    }

    [Fact]
    public async Task NextAssignee_is_null_for_random_strategy()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(admin, [admin, member], strategy: AssignmentStrategy.Random))).Value!;

        Assert.Null(chore.NextAssignee);
    }

    [Fact]
    public async Task NextAssignee_is_null_for_one_time_and_single_assignee()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);

        var oneTime = (await ctx.Chores.CreateAsync(
            NewChore(admin, [admin, member], repeat: RepeatType.OneTime, strategy: AssignmentStrategy.RoundRobin))).Value!;
        Assert.Null(oneTime.NextAssignee);

        var solo = (await ctx.Chores.CreateAsync(
            NewChore(member, [member], strategy: AssignmentStrategy.RoundRobin))).Value!;
        Assert.Null(solo.NextAssignee);
    }

    [Fact]
    public async Task NextAssignee_predicts_least_completed_after_a_completion()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        // current is admin; once admin completes, admin leads completions, so the least-completed
        // pick (and prediction) is member.
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(admin, [admin, member], strategy: AssignmentStrategy.LeastCompleted))).Value!;

        var after = await ctx.Chores.CompleteAsync(chore.Id, admin, new CompleteChoreRequest(null));
        Assert.Equal(member, after.Value!.CurrentAssignee!.Id);
        // member is now current; the preview anticipates member completing next, handing it to admin.
        Assert.Equal(admin, after.Value!.NextAssignee!.Id);
    }

    [Fact]
    public async Task CompleteAsync_from_completion_date_schedules_from_completion_date_at_scheduled_time()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        // Start is 2026-06-17 09:00; completing this overdue daily reschedules off the completion date.
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Daily,
            scheduling: SchedulingPreference.FromCompletionDate))).Value!;

        var completedDate = DateTimeOffset.UtcNow.Date;
        var result = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));

        // Next due is one day after the completion *date* — but the time-of-day stays pinned to the
        // chore's scheduled 09:00, never drifting to whatever time "complete" was tapped.
        Assert.Equal(new TimeSpan(9, 0, 0), result.Value!.DueAt!.Value.TimeOfDay);
        Assert.Equal(completedDate.AddDays(1), result.Value!.DueAt!.Value.Date);
    }

    [Fact]
    public async Task CompleteAsync_multi_completion_tracks_progress_then_advances()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Weekly,
            completionsRequired: 2, start: DateTimeOffset.UtcNow))).Value!;

        var originalDue = chore.DueAt;

        var first = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));
        Assert.Equal(1, first.Value!.OccurrenceProgress);
        Assert.Equal(originalDue, first.Value!.DueAt); // occurrence still open

        var second = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));
        Assert.True(second.Value!.DueAt > originalDue); // occurrence closed, advanced a week
        Assert.Equal(0, second.Value!.OccurrenceProgress); // fresh occurrence starts empty
    }

    [Fact]
    public async Task CreateAsync_with_times_of_day_starts_on_the_first_slot_and_round_trips()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        // "Twice a day" at 08:00 and 20:00; Start is 2026-06-17 09:00, so the first slot on/after is 20:00.
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Daily)
            with { TimesOfDay = ["20:00", "08:00"] })).Value!;

        Assert.Equal(new[] { "08:00", "20:00" }, chore.TimesOfDay); // de-duped + sorted
        Assert.Equal(new DateTimeOffset(2026, 6, 17, 20, 0, 0, TimeSpan.Zero), chore.DueAt);
    }

    [Fact]
    public async Task CompleteAsync_with_times_of_day_advances_to_the_next_slot_then_next_day()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Daily)
            with { TimesOfDay = ["08:00", "20:00"] })).Value!; // first due 2026-06-17 20:00

        var afterEvening = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));
        // After the day's last slot, the next occurrence is the first slot the following morning.
        Assert.Equal(new DateTimeOffset(2026, 6, 18, 8, 0, 0, TimeSpan.Zero), afterEvening.Value!.DueAt);

        var afterMorning = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));
        // From the morning slot, the next is the same day's evening slot.
        Assert.Equal(new DateTimeOffset(2026, 6, 18, 20, 0, 0, TimeSpan.Zero), afterMorning.Value!.DueAt);
    }

    [Fact]
    public async Task CreateAsync_rejects_times_of_day_on_unsupported_repeat_type()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);

        var result = await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Weekly)
            with { TimesOfDay = ["08:00", "20:00"] });

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task CompleteAsync_rotates_on_each_completion_when_enabled()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(admin, [admin, member], RepeatType.Weekly,
            completionsRequired: 2, rotateOnEachCompletion: true,
            strategy: AssignmentStrategy.RoundRobin, start: DateTimeOffset.UtcNow))).Value!;

        var originalDue = chore.DueAt;
        Assert.Equal(admin, chore.CurrentAssignee!.Id);

        // First (1/2) completion keeps the occurrence open but still rotates to the next assignee.
        var first = await ctx.Chores.CompleteAsync(chore.Id, admin, new CompleteChoreRequest(null));
        Assert.Equal(originalDue, first.Value!.DueAt); // occurrence still open
        Assert.Equal(member, first.Value!.CurrentAssignee!.Id); // but rotated mid-occurrence

        // Second completion closes the occurrence and rotates again.
        var second = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));
        Assert.True(second.Value!.DueAt > originalDue);
        Assert.Equal(admin, second.Value!.CurrentAssignee!.Id);
    }

    [Fact]
    public async Task CompleteAsync_keeps_assignee_mid_occurrence_by_default()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(admin, [admin, member], RepeatType.Weekly,
            completionsRequired: 2, strategy: AssignmentStrategy.RoundRobin,
            start: DateTimeOffset.UtcNow))).Value!;

        // Without rotate-on-each, the 1/2 completion leaves the assignee untouched.
        var first = await ctx.Chores.CompleteAsync(chore.Id, admin, new CompleteChoreRequest(null));
        Assert.Equal(admin, first.Value!.CurrentAssignee!.Id);
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
    public async Task SkipAsync_counts_toward_occurrence_without_advancing_early()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Weekly,
            completionsRequired: 2, start: DateTimeOffset.UtcNow))).Value!;

        var originalDue = chore.DueAt;
        var skipped = await ctx.Chores.SkipAsync(chore.Id, member, new SkipChoreRequest(null));

        Assert.True(skipped.Succeeded);
        Assert.Equal(1, skipped.Value!.OccurrenceProgress); // a skip counts as one of the N
        Assert.Equal(originalDue, skipped.Value!.DueAt); // but the occurrence isn't closed yet

        // A completion closes the 2nd slot → occurrence advances.
        var done = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));
        Assert.True(done.Value!.DueAt > originalDue);
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
    public async Task RescheduleAsync_sets_new_due_and_time_without_rotating()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(member, [admin, member], strategy: AssignmentStrategy.RoundRobin))).Value!;

        var newDue = Start.AddDays(5);
        var result = await ctx.Chores.RescheduleAsync(chore.Id, new RescheduleChoreRequest(newDue, "09:30"));

        Assert.True(result.Succeeded);
        Assert.Equal(newDue, result.Value!.DueAt);
        Assert.Equal("09:30", result.Value!.DueTime);
        Assert.Equal(member, result.Value!.CurrentAssignee!.Id); // no rotation
        Assert.Empty(ctx.Db.ChoreCompletions); // not a completion or skip
    }

    [Fact]
    public async Task RescheduleAsync_rejects_bad_time_format()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member]))).Value!;

        var result = await ctx.Chores.RescheduleAsync(chore.Id, new RescheduleChoreRequest(Start.AddDays(1), "9am"));

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
            c.IntervalUnit, c.Weekdays, c.WeeksOfMonth, c.DaysOfMonth, c.Months, c.CompletionsRequired,
            c.RotateOnEachCompletion, c.AssignmentStrategy, c.SchedulingPreference, c.GraceMinutes,
            c.AutoAdvanceIncomplete, c.CompletionWindowMinutes,
            c.StartDate, c.AssigneeIds, c.CurrentAssigneeId, c.TagNames, c.Notifications, c.DueTime);

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

    // ── Scheduling preferences end-to-end ────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_smart_scheduling_late_completion_pushes_beyond_scheduled_grid()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        // Weekly chore 5 days overdue: Smart should anchor off the completion date (~now + 7 days)
        // rather than the scheduled date (5daysAgo + 7 = 2 days out).
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Weekly,
            scheduling: SchedulingPreference.SmartScheduling,
            start: DateTimeOffset.UtcNow.AddDays(-5)))).Value!;
        var originalDue = chore.DueAt!.Value;

        var result = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));

        Assert.True(result.Succeeded);
        // FromScheduledDate gives originalDue + 7 (≈ 2 days out); Smart picks the later date.
        Assert.True(result.Value!.DueAt > originalDue.AddDays(7));
    }

    [Fact]
    public async Task CompleteAsync_smart_scheduling_early_completion_holds_scheduled_grid()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        // Weekly chore due 5 days from now; completing early should hold the planned cadence,
        // not drift to now + 7 days (which FromCompletionDate would do).
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Weekly,
            scheduling: SchedulingPreference.SmartScheduling,
            start: DateTimeOffset.UtcNow.AddDays(5)))).Value!;
        var scheduledDue = chore.DueAt!.Value;

        var result = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));

        Assert.True(result.Succeeded);
        Assert.Equal(scheduledDue.AddDays(7), result.Value!.DueAt);
    }

    [Fact]
    public async Task CompleteAsync_to_first_next_repeat_skips_missed_occurrences_to_the_future()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        // Daily chore 3 days overdue: FromScheduledDate would land in the past; ToFirstNextRepeat
        // must jump to the first daily slot strictly after now.
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Daily,
            scheduling: SchedulingPreference.ToFirstNextRepeat,
            start: DateTimeOffset.UtcNow.AddDays(-3)))).Value!;

        var result = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.DueAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CompleteAsync_smart_scheduling_within_grace_holds_grid()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        // Weekly chore due in 30 minutes with a 1-hour grace: completing 30 min early is inside
        // the grace window, so Smart treats it as on-schedule and holds the cadence.
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Weekly,
            scheduling: SchedulingPreference.SmartScheduling, graceMinutes: 60,
            start: DateTimeOffset.UtcNow.AddMinutes(30)))).Value!;
        var scheduledDue = chore.DueAt!.Value;

        var result = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));

        Assert.True(result.Succeeded);
        Assert.Equal(scheduledDue.AddDays(7), result.Value!.DueAt);
    }

    [Fact]
    public async Task CompleteAsync_smart_scheduling_beyond_grace_resets_from_completion()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        // Weekly chore due in 5 days with a 1-hour grace: completing 5 days early far exceeds the
        // grace, so Smart resets from completion (~now + 7) instead of holding the grid (~now + 12).
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Weekly,
            scheduling: SchedulingPreference.SmartScheduling, graceMinutes: 60,
            start: DateTimeOffset.UtcNow.AddDays(5)))).Value!;
        var scheduledDue = chore.DueAt!.Value;

        var result = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));

        Assert.True(result.Succeeded);
        // Grid would be scheduledDue + 7 ≈ now + 12 days; grace-exceeded resets to ≈ now + 7.
        Assert.True(result.Value!.DueAt < scheduledDue.AddDays(7));
        Assert.True(result.Value!.DueAt > DateTimeOffset.UtcNow);
    }

    // ── Assignment strategies end-to-end ─────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_least_assigned_rotates_to_fewest_assignments()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        // Admin is the initial assignee (gets 1 assignment record on create). LeastAssigned
        // picks member (0 assignments) as the next current assignee after the first completion.
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(admin, [admin, member], strategy: AssignmentStrategy.LeastAssigned))).Value!;

        var result = await ctx.Chores.CompleteAsync(chore.Id, admin, new CompleteChoreRequest(null));

        Assert.True(result.Succeeded);
        Assert.Equal(member, result.Value!.CurrentAssignee!.Id);
    }

    [Fact]
    public async Task CompleteAsync_least_completed_rotates_to_fewest_completions()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(admin, [admin, member], strategy: AssignmentStrategy.LeastCompleted))).Value!;

        // Admin completes once; admin now leads the completion count, so LeastCompleted rotates to member.
        var result = await ctx.Chores.CompleteAsync(chore.Id, admin, new CompleteChoreRequest(null));

        Assert.True(result.Succeeded);
        Assert.Equal(member, result.Value!.CurrentAssignee!.Id);
    }

    [Fact]
    public async Task CompleteAsync_random_except_last_assigned_never_keeps_current_assignee()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(admin, [admin, member], strategy: AssignmentStrategy.RandomExceptLastAssigned))).Value!;

        // With two assignees, "not admin" can only be member.
        var result = await ctx.Chores.CompleteAsync(chore.Id, admin, new CompleteChoreRequest(null));

        Assert.True(result.Succeeded);
        Assert.NotEqual(admin, result.Value!.CurrentAssignee!.Id);
    }

    [Fact]
    public async Task CompleteAsync_random_picks_from_within_the_assignee_set()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(admin, [admin, member], strategy: AssignmentStrategy.Random))).Value!;

        var result = await ctx.Chores.CompleteAsync(chore.Id, admin, new CompleteChoreRequest(null));

        Assert.True(result.Succeeded);
        Assert.Contains(result.Value!.CurrentAssignee!.Id, new[] { admin, member });
    }

    // ── UpdateAsync: schedule change vs non-schedule edit ────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_schedule_change_resets_due_to_first_occurrence()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var create = NewChore(member, [member], RepeatType.Daily);
        var chore = (await ctx.Chores.CreateAsync(create)).Value!;
        var afterComplete = (await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null))).Value!;
        Assert.Equal(Start.AddDays(1), afterComplete.DueAt); // confirmed it advanced

        // Switching from Daily to Weekly changes the schedule, so DueAt must recompute from StartDate.
        var result = await ctx.Chores.UpdateAsync(chore.Id, ToUpdate(create) with { RepeatType = RepeatType.Weekly });

        Assert.True(result.Succeeded);
        Assert.Equal(Start, result.Value!.DueAt); // first weekly occurrence is the StartDate itself
    }

    [Fact]
    public async Task UpdateAsync_non_schedule_edit_preserves_advanced_due_date()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var create = NewChore(member, [member]);
        var chore = (await ctx.Chores.CreateAsync(create)).Value!;
        var afterComplete = (await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null))).Value!;
        var advancedDue = afterComplete.DueAt;

        // Rename only — no schedule parameters change.
        var result = await ctx.Chores.UpdateAsync(chore.Id, ToUpdate(create) with { Name = "Cleaned Dishes" });

        Assert.True(result.Succeeded);
        Assert.Equal(advancedDue, result.Value!.DueAt); // DueAt must not be silently reset
    }

    [Fact]
    public async Task UpdateAsync_strategy_change_takes_effect_on_next_completion()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var create = NewChore(admin, [admin, member], strategy: AssignmentStrategy.KeepLastAssigned);
        var chore = (await ctx.Chores.CreateAsync(create)).Value!;

        // Switch from KeepLastAssigned to RoundRobin while admin is current.
        await ctx.Chores.UpdateAsync(chore.Id,
            ToUpdate(create) with { AssignmentStrategy = AssignmentStrategy.RoundRobin });

        // Next completion should rotate admin → member per RoundRobin order.
        var result = await ctx.Chores.CompleteAsync(chore.Id, admin, new CompleteChoreRequest(null));

        Assert.True(result.Succeeded);
        Assert.Equal(member, result.Value!.CurrentAssignee!.Id);
    }

    // ── TimesOfDay + DaysOfWeek at service level ─────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_times_of_day_with_days_of_week_lands_on_the_first_slot()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        // "Mondays at 08:00 and 20:00", starting early Mon Jun 22 (before the first slot).
        var mondayStart = new DateTimeOffset(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.Custom,
                customMode: CustomRecurrenceMode.DaysOfWeek,
                weekdays: [DayOfWeek.Monday], start: mondayStart)
            with { TimesOfDay = ["08:00", "20:00"] })).Value!;

        Assert.Equal(new DateTimeOffset(2026, 6, 22, 8, 0, 0, TimeSpan.Zero), chore.DueAt);
        Assert.Equal(new[] { "08:00", "20:00" }, chore.TimesOfDay);
    }

    [Fact]
    public async Task CompleteAsync_times_of_day_with_days_of_week_advances_within_day_then_to_next_week()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var mondayStart = new DateTimeOffset(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.Custom,
                customMode: CustomRecurrenceMode.DaysOfWeek,
                weekdays: [DayOfWeek.Monday], start: mondayStart)
            with { TimesOfDay = ["08:00", "20:00"] })).Value!;

        // Complete Mon 08:00 → next same-day slot is 20:00.
        var afterMorning = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));
        Assert.Equal(new DateTimeOffset(2026, 6, 22, 20, 0, 0, TimeSpan.Zero), afterMorning.Value!.DueAt);

        // Complete Mon 20:00 → roll to next Monday 08:00.
        var afterEvening = await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));
        Assert.Equal(new DateTimeOffset(2026, 6, 29, 8, 0, 0, TimeSpan.Zero), afterEvening.Value!.DueAt);
    }

    // ── AutoAdvanceAsync ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AutoAdvanceAsync_advances_and_fills_expired_slots_when_window_closed()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.Weekly, completionsRequired: 3, start: Start)
            with { AutoAdvanceIncomplete = true })).Value!;
        var originalDue = chore.DueAt!.Value; // Start

        // 1 real completion — occurrence stays open (1/3 done).
        await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));

        // Window = null → expires immediately at DueAt; now > DueAt triggers auto-advance.
        var advanced = await ctx.Chores.AutoAdvanceAsync(Start.AddHours(1));

        Assert.Equal(1, advanced);
        var completions = await ctx.Db.ChoreCompletions.ToListAsync();
        Assert.Equal(3, completions.Count);
        Assert.Equal(2, completions.Count(c => c.IsExpired));
        // Expired rows: correct OccurrenceDueAt, 0 points, no actor.
        Assert.All(completions.Where(c => c.IsExpired), c =>
        {
            Assert.Equal(originalDue, c.OccurrenceDueAt);
            Assert.Equal(0, c.PointsAwarded);
            Assert.Null(c.CompletedByUserId);
        });
        // DueAt advanced to next weekly occurrence.
        var stored = await ctx.Db.Chores.FindAsync(chore.Id);
        Assert.Equal(originalDue.AddDays(7), stored!.DueAt);
    }

    [Fact]
    public async Task AutoAdvanceAsync_fills_all_slots_when_no_completions_exist()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.Weekly, completionsRequired: 2, start: Start)
            with { AutoAdvanceIncomplete = true })).Value!;
        var originalDue = chore.DueAt!.Value;

        var advanced = await ctx.Chores.AutoAdvanceAsync(Start.AddHours(1));

        Assert.Equal(1, advanced);
        var completions = await ctx.Db.ChoreCompletions.ToListAsync();
        Assert.Equal(2, completions.Count);
        Assert.All(completions, c => Assert.True(c.IsExpired));
        var stored = await ctx.Db.Chores.FindAsync(chore.Id);
        Assert.Equal(originalDue.AddDays(7), stored!.DueAt);
    }

    [Fact]
    public async Task AutoAdvanceAsync_does_not_advance_before_window_closes()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.Weekly, completionsRequired: 2, start: Start)
            with { AutoAdvanceIncomplete = true, CompletionWindowMinutes = 120 })).Value!;

        // now = Start + 60 min — inside the 120-minute window.
        var advanced = await ctx.Chores.AutoAdvanceAsync(Start.AddMinutes(60));

        Assert.Equal(0, advanced);
        Assert.Empty(ctx.Db.ChoreCompletions);
        Assert.Equal(Start, (await ctx.Db.Chores.FindAsync(chore.Id))!.DueAt);
    }

    [Fact]
    public async Task AutoAdvanceAsync_does_not_advance_future_chores()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var futureStart = DateTimeOffset.UtcNow.AddDays(7);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.Weekly, completionsRequired: 2, start: futureStart)
            with { AutoAdvanceIncomplete = true })).Value!;

        var advanced = await ctx.Chores.AutoAdvanceAsync(DateTimeOffset.UtcNow);

        Assert.Equal(0, advanced);
        Assert.Empty(ctx.Db.ChoreCompletions);
    }

    [Fact]
    public async Task AutoAdvanceAsync_does_not_advance_when_occurrence_fully_completed()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.Weekly, completionsRequired: 2, start: Start)
            with { AutoAdvanceIncomplete = true })).Value!;

        // Complete both slots — occurrence closes and DueAt advances to next week.
        await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));
        await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));
        var advancedDue = (await ctx.Db.Chores.FindAsync(chore.Id))!.DueAt;

        // now is before the new DueAt — nothing to expire yet.
        var advanced = await ctx.Chores.AutoAdvanceAsync(Start.AddHours(1));

        Assert.Equal(0, advanced);
        Assert.Empty(await ctx.Db.ChoreCompletions.Where(c => c.IsExpired).ToListAsync());
        Assert.Equal(advancedDue, (await ctx.Db.Chores.FindAsync(chore.Id))!.DueAt);
    }

    [Fact]
    public async Task AutoAdvanceAsync_advances_using_from_scheduled_date_ignoring_chore_preference()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        // Chore prefers FromCompletionDate — auto-advance must use FromScheduledDate instead.
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.Weekly, completionsRequired: 2,
                scheduling: SchedulingPreference.FromCompletionDate, start: Start)
            with { AutoAdvanceIncomplete = true })).Value!;
        var originalDue = chore.DueAt!.Value; // Start

        // Trigger auto-advance 10 days late; FromCompletionDate would give now + 7d,
        // but FromScheduledDate should give originalDue + 7d.
        await ctx.Chores.AutoAdvanceAsync(Start.AddDays(10));

        var stored = await ctx.Db.Chores.FindAsync(chore.Id);
        Assert.Equal(originalDue.AddDays(7), stored!.DueAt);
    }

    [Fact]
    public async Task AutoAdvanceAsync_skips_chores_without_auto_advance_enabled()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var autoChore = (await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.Weekly, completionsRequired: 2, start: Start)
            with { AutoAdvanceIncomplete = true })).Value!;
        var normalChore = (await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.Weekly, completionsRequired: 2, start: Start))).Value!;

        var advanced = await ctx.Chores.AutoAdvanceAsync(Start.AddHours(1));

        Assert.Equal(1, advanced);
        Assert.Equal(2, await ctx.Db.ChoreCompletions.CountAsync(c => c.IsExpired && c.ChoreId == autoChore.Id));
        Assert.Equal(0, await ctx.Db.ChoreCompletions.CountAsync(c => c.IsExpired && c.ChoreId == normalChore.Id));
        Assert.Equal(Start, (await ctx.Db.Chores.FindAsync(normalChore.Id))!.DueAt);
    }

    [Fact]
    public async Task AutoAdvanceAsync_respects_completion_window_minutes()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.Daily, completionsRequired: 2, start: Start)
            with { AutoAdvanceIncomplete = true, CompletionWindowMinutes = 30 })).Value!;

        // now = Start + 29 min — inside window.
        Assert.Equal(0, await ctx.Chores.AutoAdvanceAsync(Start.AddMinutes(29)));
        Assert.Empty(ctx.Db.ChoreCompletions);

        // now = Start + 31 min — window closed.
        Assert.Equal(1, await ctx.Chores.AutoAdvanceAsync(Start.AddMinutes(31)));
        Assert.Equal(2, await ctx.Db.ChoreCompletions.CountAsync(c => c.IsExpired));
    }

    [Fact]
    public async Task AutoAdvanceAsync_rotates_assignee_after_expiry()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(admin, [admin, member], RepeatType.Weekly, completionsRequired: 2,
                strategy: AssignmentStrategy.RoundRobin, start: Start)
            with { AutoAdvanceIncomplete = true })).Value!;

        await ctx.Chores.AutoAdvanceAsync(Start.AddHours(1));

        // After auto-advance the occurrence expires on admin's watch → rotates to member.
        var stored = await ctx.Db.Chores.FindAsync(chore.Id);
        Assert.Equal(member, stored!.CurrentAssigneeId);
    }

    [Fact]
    public async Task CreateAsync_clears_auto_advance_for_custom_recurrence()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.Custom,
                customMode: CustomRecurrenceMode.Interval, intervalCount: 2, intervalUnit: RecurrenceUnit.Week)
            with { AutoAdvanceIncomplete = true })).Value!;

        Assert.False(chore.AutoAdvanceIncomplete);
    }

    [Fact]
    public async Task UndoCompletion_blocks_undo_of_expired_entries()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.Weekly, completionsRequired: 2, start: Start)
            with { AutoAdvanceIncomplete = true })).Value!;

        await ctx.Chores.AutoAdvanceAsync(Start.AddHours(1));
        var expiredId = await ctx.Db.ChoreCompletions.Where(c => c.IsExpired).Select(c => c.Id).FirstAsync();

        var result = await ctx.Chores.UndoCompletionAsync(expiredId, member);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Forbidden, result.Error!.Type);
    }

    [Fact]
    public async Task DeleteActivity_blocks_deletion_of_expired_entries()
    {
        using var ctx = new TestContext();
        var (admin, _) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(admin, [admin], RepeatType.Weekly, completionsRequired: 2, start: Start)
            with { AutoAdvanceIncomplete = true })).Value!;

        await ctx.Chores.AutoAdvanceAsync(Start.AddHours(1));
        var expiredId = await ctx.Db.ChoreCompletions.Where(c => c.IsExpired).Select(c => c.Id).FirstAsync();

        var result = await ctx.Chores.DeleteActivityAsync(expiredId, admin);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.Forbidden, result.Error!.Type);
    }

    [Fact]
    public async Task AutoAdvanceAsync_expired_entries_excluded_from_last_completion_in_dto()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.Weekly, completionsRequired: 2, start: Start)
            with { AutoAdvanceIncomplete = true })).Value!;

        // Auto-advance with no real completions → only expired rows exist.
        await ctx.Chores.AutoAdvanceAsync(Start.AddHours(1));

        // The DTO's LastCompletion (used for the undo affordance) must be null.
        var dto = (await ctx.Chores.GetAsync(chore.Id)).Value!;
        Assert.Null(dto.LastCompletion);
    }

    [Fact]
    public async Task AutoAdvanceAsync_expired_entries_excluded_from_occurrence_progress_count()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(
            NewChore(member, [member], RepeatType.Weekly, completionsRequired: 3, start: Start)
            with { AutoAdvanceIncomplete = true })).Value!;

        // Complete 1/3 then auto-advance; the new occurrence should start at 0, not count the
        // 2 expired rows from the prior occurrence.
        await ctx.Chores.CompleteAsync(chore.Id, member, new CompleteChoreRequest(null));
        await ctx.Chores.AutoAdvanceAsync(Start.AddHours(1));

        var dto = (await ctx.Chores.GetAsync(chore.Id)).Value!;
        Assert.Equal(0, dto.OccurrenceProgress); // fresh occurrence
    }

    // ── Streaks ──────────────────────────────────────────────────────────────

    /// <summary>Logs a completed occurrence directly so the on-time/late relationship is
    /// deterministic regardless of wall-clock time.</summary>
    private static async Task LogCompletionAsync(TestContext ctx, Guid choreId, Guid userId,
        DateTimeOffset due, double lateHours = 0, bool isSkip = false)
    {
        ctx.Db.ChoreCompletions.Add(new ChoreCompletion
        {
            ChoreId = choreId,
            CompletedByUserId = userId,
            OccurrenceDueAt = due,
            CompletedAt = due.AddHours(lateHours),
            IsSkip = isSkip,
        });
        await ctx.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAsync_surfaces_on_time_streak()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Weekly))).Value!;

        await LogCompletionAsync(ctx, chore.Id, member, Start, lateHours: -1);
        await LogCompletionAsync(ctx, chore.Id, member, Start.AddDays(7), lateHours: -2);

        var dto = (await ctx.Chores.GetAsync(chore.Id)).Value!;
        Assert.Equal(2, dto.CurrentStreak);
    }

    [Fact]
    public async Task GetAsync_late_completion_breaks_streak()
    {
        using var ctx = new TestContext();
        var (_, member) = await SeedUsersAsync(ctx);
        var chore = (await ctx.Chores.CreateAsync(NewChore(member, [member], RepeatType.Weekly))).Value!;

        await LogCompletionAsync(ctx, chore.Id, member, Start, lateHours: -1);
        await LogCompletionAsync(ctx, chore.Id, member, Start.AddDays(7), lateHours: 3); // newest is late

        var dto = (await ctx.Chores.GetAsync(chore.Id)).Value!;
        Assert.Equal(0, dto.CurrentStreak);
    }

    // ── Copy ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CopyAsync_duplicates_settings_under_new_name_with_no_history()
    {
        using var ctx = new TestContext();
        var (admin, member) = await SeedUsersAsync(ctx);
        var source = (await ctx.Chores.CreateAsync(NewChore(member, [admin, member], RepeatType.Weekly,
            points: 25, strategy: AssignmentStrategy.RoundRobin, tags: ["kitchen"]))).Value!;
        await ctx.Chores.CompleteAsync(source.Id, member, new CompleteChoreRequest("done"));

        var copy = await ctx.Chores.CopyAsync(source.Id, "Dishes (copy)");

        Assert.True(copy.Succeeded);
        Assert.NotEqual(source.Id, copy.Value!.Id);
        Assert.Equal("Dishes (copy)", copy.Value.Name);
        Assert.Equal(25, copy.Value.Points);
        Assert.Equal(RepeatType.Weekly, copy.Value.RepeatType);
        Assert.Equal(AssignmentStrategy.RoundRobin, copy.Value.AssignmentStrategy);
        Assert.Equal(2, copy.Value.Assignees.Length);
        Assert.Equal(["kitchen"], copy.Value.Tags);
        // The copy starts fresh — no completion history carried over.
        Assert.Equal(0, await ctx.Db.ChoreCompletions.CountAsync(c => c.ChoreId == copy.Value.Id));
        Assert.Null(copy.Value.LastCompletion);
    }

    [Fact]
    public async Task CopyAsync_returns_not_found_for_unknown_chore()
    {
        using var ctx = new TestContext();
        await SeedUsersAsync(ctx);

        var result = await ctx.Chores.CopyAsync(Guid.NewGuid(), "Whatever");

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }
}
