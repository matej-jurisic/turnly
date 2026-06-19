using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Data;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;
using Turnly.Core.Enums;
using Turnly.Core.Recurrence;

namespace Turnly.Core.Services;

public class ChoreService
{
    private readonly TurnlyDbContext _db;
    private readonly TagService _tags;

    public ChoreService(TurnlyDbContext db, TagService tags)
    {
        _db = db;
        _tags = tags;
    }

    public async Task<List<ChoreDto>> ListAsync(CancellationToken ct = default)
    {
        // Order client-side: SQLite can't ORDER BY DateTimeOffset. Scheduled chores first
        // (earliest due), then unscheduled, then by name.
        var chores = (await Query().AsNoTracking().ToListAsync(ct))
            .OrderBy(c => c.DueAt == null)
            .ThenBy(c => c.DueAt)
            .ThenBy(c => c.Name)
            .ToList();

        var ids = chores.Select(c => c.Id).ToList();
        var completionsByChore = await CompletionsByChoreAsync(ids, ct);
        var now = DateTimeOffset.UtcNow;

        return chores
            .Select(c => ToDto(c, completionsByChore.GetValueOrDefault(c.Id), now))
            .ToList();
    }

    public async Task<Result<ChoreDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var chore = await Query().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (chore is null)
            return Result.Fail<ChoreDto>(Error.NotFound("Chore not found."));

        var completionsByChore = await CompletionsByChoreAsync([id], ct);
        return Result.Success(ToDto(chore, completionsByChore.GetValueOrDefault(id), DateTimeOffset.UtcNow));
    }

    public async Task<Result<ChoreDto>> CreateAsync(CreateChoreRequest req, CancellationToken ct = default)
    {
        var validation = await ValidateAsync(req, ct);
        if (!validation.Succeeded)
            return Result.Fail<ChoreDto>(validation.Error!);

        var chore = new Chore { Name = req.Name.Trim(), StartDate = req.StartDate };
        Apply(chore, req, validation.Value!);
        chore.Tags = await _tags.ResolveAsync(req.TagNames, ct);
        chore.DueAt = RecurrenceCalculator.FirstOccurrence(RecurrenceRule.FromChore(chore), req.StartDate);

        _db.Chores.Add(chore);
        // Record the initial assignment so Least-Assigned counts and undo work from the start.
        _db.ChoreAssignments.Add(new ChoreAssignment { ChoreId = chore.Id, UserId = req.CurrentAssigneeId });
        await _db.SaveChangesAsync(ct);

        return await GetAsync(chore.Id, ct);
    }

    public async Task<Result<ChoreDto>> UpdateAsync(Guid id, UpdateChoreRequest req, CancellationToken ct = default)
    {
        var validation = await ValidateAsync(req, ct);
        if (!validation.Succeeded)
            return Result.Fail<ChoreDto>(validation.Error!);

        var chore = await _db.Chores
            .Include(c => c.Assignees)
            .Include(c => c.Tags)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (chore is null)
            return Result.Fail<ChoreDto>(Error.NotFound("Chore not found."));

        var previousAssignee = chore.CurrentAssigneeId;
        var oldStartDate = chore.StartDate;
        var oldRule = RecurrenceRule.FromChore(chore);

        Apply(chore, req, validation.Value!);

        // Only recompute DueAt when the schedule itself changed; editing name/points/assignees/tags
        // must not silently reset a due date that has already been advanced by completions.
        if (chore.StartDate != oldStartDate || ScheduleChanged(oldRule, RecurrenceRule.FromChore(chore)))
            chore.DueAt = RecurrenceCalculator.FirstOccurrence(RecurrenceRule.FromChore(chore), req.StartDate);

        // Reassigning via edit is itself an assignment event (keeps Least-Assigned honest).
        if (chore.CurrentAssigneeId != previousAssignee)
            _db.ChoreAssignments.Add(new ChoreAssignment { ChoreId = chore.Id, UserId = req.CurrentAssigneeId });

        var resolvedTags = await _tags.ResolveAsync(req.TagNames, ct);
        chore.Tags.Clear();
        foreach (var tag in resolvedTags) chore.Tags.Add(tag);

        await _db.SaveChangesAsync(ct);
        return await GetAsync(chore.Id, ct);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var chore = await _db.Chores.FindAsync([id], ct);
        if (chore is null)
            return Result.Fail(Error.NotFound("Chore not found."));

        _db.Chores.Remove(chore); // completions + assignments cascade-delete
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<ChoreDto>> CompleteAsync(Guid id, Guid userId, CompleteChoreRequest req, CancellationToken ct = default)
    {
        var chore = await Query().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (chore is null)
            return Result.Fail<ChoreDto>(Error.NotFound("Chore not found."));

        var user = await _db.Users.FindAsync([userId], ct);
        if (user is null)
            return Result.Fail<ChoreDto>(Error.NotFound("User not found."));

        var now = DateTimeOffset.UtcNow;
        var completion = new ChoreCompletion
        {
            ChoreId = chore.Id,
            CompletedByUserId = userId,
            CompletedAt = now,
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            PointsAwarded = chore.Points,
            OccurrenceDueAt = chore.DueAt,
            PreviousAssigneeId = chore.CurrentAssigneeId
        };
        _db.ChoreCompletions.Add(completion);

        if (chore.Points != 0)
        {
            _db.PointsLog.Add(new PointsLogEntry
            {
                UserId = userId,
                Delta = chore.Points,
                Type = PointsLogType.Completion,
                ChoreCompletionId = completion.Id,
                Description = chore.Name
            });
            user.Points += chore.Points;
        }

        // Advance the schedule and decide whether this completion opened a new occurrence (which
        // is what triggers an assignee rotation).
        var advancedToNewOccurrence = await AdvanceScheduleAsync(chore, completion, now, ct);
        if (advancedToNewOccurrence)
            await RotateAssigneeAsync(chore, completion, now, ct);

        await _db.SaveChangesAsync(ct);
        return await GetAsync(chore.Id, ct);
    }

    public async Task<Result> UndoCompletionAsync(Guid completionId, Guid actingUserId, CancellationToken ct = default)
    {
        var completion = await _db.ChoreCompletions
            .Include(c => c.Chore)
            .FirstOrDefaultAsync(c => c.Id == completionId, ct);
        if (completion is null)
            return Result.Fail(Error.NotFound("Completion not found."));

        var actor = await _db.Users.FindAsync([actingUserId], ct);
        if (actor is null)
            return Result.Fail(Error.NotFound("User not found."));

        // Only the person who completed it, or an admin, may undo.
        if (completion.CompletedByUserId != actingUserId && actor.Role != UserRole.Admin)
            return Result.Fail(Error.Forbidden("You can only undo your own completions."));

        // Reverse points: remove the linked log entry and decrement the completer's balance.
        var logEntry = await _db.PointsLog.FirstOrDefaultAsync(e => e.ChoreCompletionId == completionId, ct);
        if (logEntry is not null)
        {
            var completer = await _db.Users.FindAsync([completion.CompletedByUserId], ct);
            if (completer is not null)
                completer.Points -= logEntry.Delta;
            _db.PointsLog.Remove(logEntry);
        }

        // Restore the occurrence that was completed, including the assignee any rotation moved away.
        if (completion.Chore is { } chore)
        {
            chore.DueAt = completion.OccurrenceDueAt;
            chore.CurrentAssigneeId = completion.PreviousAssigneeId;
        }
        var rotationLog = await _db.ChoreAssignments.Where(a => a.ChoreCompletionId == completionId).ToListAsync(ct);
        _db.ChoreAssignments.RemoveRange(rotationLog);

        _db.ChoreCompletions.Remove(completion);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<List<ChoreCompletionDto>> GetHistoryAsync(
        string? tag, Guid? completedByUserId, Guid? choreId, CancellationToken ct = default)
    {
        var query = _db.ChoreCompletions
            .Include(c => c.Chore)
            .Include(c => c.CompletedBy)
            .AsQueryable();

        if (choreId.HasValue)
            query = query.Where(c => c.ChoreId == choreId);

        if (completedByUserId.HasValue)
            query = query.Where(c => c.CompletedByUserId == completedByUserId);

        if (tag is not null)
        {
            var choreIds = await _db.Chores
                .Where(c => c.Tags.Any(t => t.Name == tag))
                .Select(c => c.Id)
                .ToListAsync(ct);
            query = query.Where(c => choreIds.Contains(c.ChoreId));
        }

        // Order client-side: SQLite can't ORDER BY DateTimeOffset.
        var completions = (await query.ToListAsync(ct))
            .OrderByDescending(c => c.CompletedAt)
            .ToList();

        return completions.Select(ChoreCompletionDto.FromEntity).ToList();
    }

    private IQueryable<Chore> Query() => _db.Chores
        .Include(c => c.Assignees)
        .Include(c => c.CurrentAssignee)
        .Include(c => c.Tags)
        .AsSplitQuery();

    /// <summary>Advances <c>chore.DueAt</c> to the next occurrence and returns whether a brand-new
    /// occurrence was opened (true → rotate the assignee). For frequency chores the period only
    /// rolls over — and thus rotates — once the required count is met.</summary>
    private async Task<bool> AdvanceScheduleAsync(Chore chore, ChoreCompletion completion, DateTimeOffset now, CancellationToken ct)
    {
        if (chore is { RepeatType: RepeatType.Custom, CustomMode: CustomRecurrenceMode.Frequency, FrequencyPeriod: { } period })
        {
            var start = RecurrenceCalculator.PeriodStart(period, now);
            var end = RecurrenceCalculator.PeriodEnd(period, now);
            // SQLite can't compare DateTimeOffset in SQL, so count client-side. +1 for the
            // completion we just added but haven't saved yet.
            var completedAts = await _db.ChoreCompletions
                .Where(x => x.ChoreId == chore.Id)
                .Select(x => x.CompletedAt)
                .ToListAsync(ct);
            var doneThisPeriod = completedAts.Count(t => t >= start && t < end) + 1;

            if (doneThisPeriod >= (chore.FrequencyCount ?? 1))
            {
                chore.DueAt = RecurrenceCalculator.PeriodEnd(period, end); // roll into the next period
                return true;
            }

            chore.DueAt = end; // still due this period; same occupant keeps it
            return false;
        }

        var rule = RecurrenceRule.FromChore(chore);
        var scheduledDue = completion.OccurrenceDueAt ?? now;
        chore.DueAt = RecurrenceCalculator.NextDue(rule, chore.SchedulingPreference, scheduledDue, now, now);
        return chore.DueAt is not null;
    }

    /// <summary>Picks the next current assignee per the chore's strategy and records the assignment
    /// (linked to <paramref name="completion"/> so undo can reverse it).</summary>
    private async Task RotateAssigneeAsync(Chore chore, ChoreCompletion completion, DateTimeOffset now, CancellationToken ct)
    {
        var ordered = chore.Assignees
            .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
            .Select(u => u.Id)
            .ToList();
        if (ordered.Count == 0) return;

        var assignedCounts = await _db.ChoreAssignments
            .Where(a => a.ChoreId == chore.Id)
            .GroupBy(a => a.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        var completedCounts = await _db.ChoreCompletions
            .Where(x => x.ChoreId == chore.Id)
            .GroupBy(x => x.CompletedByUserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);
        // Count the just-added (unsaved) completion too.
        completedCounts[completion.CompletedByUserId] = completedCounts.GetValueOrDefault(completion.CompletedByUserId) + 1;

        var next = AssignmentPicker.Pick(
            chore.AssignmentStrategy, ordered, chore.CurrentAssigneeId,
            assignedCounts, completedCounts, Random.Shared);

        chore.CurrentAssigneeId = next;
        _db.ChoreAssignments.Add(new ChoreAssignment
        {
            ChoreId = chore.Id,
            UserId = next,
            AssignedAt = now,
            ChoreCompletionId = completion.Id
        });
    }

    /// <summary>All completions per chore id (newest-first within each chore), for both the
    /// last-completion affordance and frequency-progress counting. SQLite can't ORDER BY a
    /// DateTimeOffset, so ordering happens client-side.</summary>
    private async Task<Dictionary<Guid, List<ChoreCompletion>>> CompletionsByChoreAsync(
        List<Guid> choreIds, CancellationToken ct)
    {
        if (choreIds.Count == 0)
            return new Dictionary<Guid, List<ChoreCompletion>>();

        var completions = await _db.ChoreCompletions
            .Include(x => x.CompletedBy)
            .Where(x => choreIds.Contains(x.ChoreId))
            .ToListAsync(ct);

        return completions
            .GroupBy(x => x.ChoreId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CompletedAt).ToList());
    }

    private static ChoreDto ToDto(Chore chore, List<ChoreCompletion>? completions, DateTimeOffset now)
    {
        var latest = completions?.FirstOrDefault();
        int? progress = null;
        if (chore is { RepeatType: RepeatType.Custom, CustomMode: CustomRecurrenceMode.Frequency, FrequencyPeriod: { } period })
        {
            var start = RecurrenceCalculator.PeriodStart(period, now);
            var end = RecurrenceCalculator.PeriodEnd(period, now);
            progress = completions?.Count(x => x.CompletedAt >= start && x.CompletedAt < end) ?? 0;
        }
        return ChoreDto.FromEntity(chore, latest, progress);
    }

    /// <summary>Validates the request and returns the resolved assignee entities.</summary>
    private async Task<Result<List<User>>> ValidateAsync(IChoreInput req, CancellationToken ct)
    {
        if (Validators.ChoreName(req.Name) is { } nameError)
            return Result.Fail<List<User>>(nameError);
        if (Validators.Points(req.Points) is { } pointsError)
            return Result.Fail<List<User>>(pointsError);
        if (Validators.Recurrence(req.RepeatType, req.CustomMode, req.IntervalCount, req.IntervalUnit,
                req.Weekdays, req.DaysOfMonth, req.Months, req.FrequencyCount, req.FrequencyPeriod) is { } recurrenceError)
            return Result.Fail<List<User>>(recurrenceError);

        var ids = (req.AssigneeIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
            return Result.Fail<List<User>>(Error.Validation("A chore must have at least one assignee."));

        var assignees = await _db.Users.Where(u => ids.Contains(u.Id)).ToListAsync(ct);
        if (assignees.Count != ids.Count)
            return Result.Fail<List<User>>(Error.Validation("One or more assignees do not exist."));

        if (!ids.Contains(req.CurrentAssigneeId))
            return Result.Fail<List<User>>(Error.Validation("The current assignee must be one of the assignees."));

        return Result.Success(assignees);
    }

    private static bool ScheduleChanged(RecurrenceRule a, RecurrenceRule b) =>
        a.Type != b.Type ||
        a.CustomMode != b.CustomMode ||
        a.IntervalCount != b.IntervalCount ||
        a.IntervalUnit != b.IntervalUnit ||
        !a.Weekdays.SequenceEqual(b.Weekdays) ||
        !a.DaysOfMonth.SequenceEqual(b.DaysOfMonth) ||
        !a.Months.SequenceEqual(b.Months) ||
        a.FrequencyCount != b.FrequencyCount ||
        a.FrequencyPeriod != b.FrequencyPeriod;

    private static void Apply(Chore chore, IChoreInput req, List<User> assignees)
    {
        chore.Name = req.Name.Trim();
        chore.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        chore.Emoji = string.IsNullOrWhiteSpace(req.Emoji) ? null : req.Emoji.Trim();
        chore.Points = req.Points;
        chore.RepeatType = req.RepeatType;
        chore.StartDate = req.StartDate;
        chore.AssignmentStrategy = req.AssignmentStrategy;
        chore.SchedulingPreference = req.SchedulingPreference;
        chore.CurrentAssigneeId = req.CurrentAssigneeId;

        // Keep only the recurrence parameters relevant to the chosen mode; null/clear the rest so
        // stale values can't leak into the recurrence math.
        var isCustom = req.RepeatType == RepeatType.Custom;
        chore.CustomMode = isCustom ? req.CustomMode : null;

        var mode = isCustom ? req.CustomMode : null;
        chore.IntervalCount = mode == CustomRecurrenceMode.Interval ? req.IntervalCount : null;
        chore.IntervalUnit = mode == CustomRecurrenceMode.Interval ? req.IntervalUnit : null;
        chore.Weekdays = mode == CustomRecurrenceMode.DaysOfWeek
            ? (req.Weekdays ?? []).Distinct().OrderBy(d => d).ToList()
            : new List<DayOfWeek>();
        chore.DaysOfMonth = mode == CustomRecurrenceMode.DaysOfMonth
            ? (req.DaysOfMonth ?? []).Distinct().OrderBy(d => d).ToList()
            : new List<int>();
        chore.Months = mode == CustomRecurrenceMode.DaysOfMonth
            ? (req.Months ?? []).Distinct().OrderBy(m => m).ToList()
            : new List<int>();
        chore.FrequencyCount = mode == CustomRecurrenceMode.Frequency ? req.FrequencyCount : null;
        chore.FrequencyPeriod = mode == CustomRecurrenceMode.Frequency ? req.FrequencyPeriod : null;

        chore.Assignees.Clear();
        foreach (var a in assignees) chore.Assignees.Add(a);
    }
}
