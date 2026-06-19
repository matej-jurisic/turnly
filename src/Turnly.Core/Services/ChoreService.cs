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
        var chores = (await Query().ToListAsync(ct))
            .OrderBy(c => c.DueAt == null)
            .ThenBy(c => c.DueAt)
            .ThenBy(c => c.Name)
            .ToList();

        var ids = chores.Select(c => c.Id).ToList();
        var lastByChore = await LatestCompletionsAsync(ids, ct);

        return chores
            .Select(c => ChoreDto.FromEntity(c, lastByChore.GetValueOrDefault(c.Id)))
            .ToList();
    }

    public async Task<Result<ChoreDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var chore = await Query().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (chore is null)
            return Result.Fail<ChoreDto>(Error.NotFound("Chore not found."));

        var lastByChore = await LatestCompletionsAsync([id], ct);
        return Result.Success(ChoreDto.FromEntity(chore, lastByChore.GetValueOrDefault(id)));
    }

    public async Task<Result<ChoreDto>> CreateAsync(CreateChoreRequest req, CancellationToken ct = default)
    {
        var validation = await ValidateAsync(req.Name, req.Points, req.RepeatType, req.Weekdays,
            req.AssigneeIds, req.CurrentAssigneeId, ct);
        if (!validation.Succeeded)
            return Result.Fail<ChoreDto>(validation.Error!);

        var chore = new Chore { Name = req.Name.Trim(), StartDate = req.StartDate };
        Apply(chore, req.Name, req.Description, req.Emoji, req.Points, req.RepeatType, req.Weekdays,
            req.StartDate, req.CurrentAssigneeId, validation.Value!);
        chore.Tags = await _tags.ResolveAsync(req.TagNames, ct);
        chore.DueAt = req.StartDate; // first occurrence

        _db.Chores.Add(chore);
        await _db.SaveChangesAsync(ct);

        return await GetAsync(chore.Id, ct);
    }

    public async Task<Result<ChoreDto>> UpdateAsync(Guid id, UpdateChoreRequest req, CancellationToken ct = default)
    {
        var validation = await ValidateAsync(req.Name, req.Points, req.RepeatType, req.Weekdays,
            req.AssigneeIds, req.CurrentAssigneeId, ct);
        if (!validation.Succeeded)
            return Result.Fail<ChoreDto>(validation.Error!);

        var chore = await _db.Chores
            .Include(c => c.Assignees)
            .Include(c => c.Tags)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (chore is null)
            return Result.Fail<ChoreDto>(Error.NotFound("Chore not found."));

        Apply(chore, req.Name, req.Description, req.Emoji, req.Points, req.RepeatType, req.Weekdays,
            req.StartDate, req.CurrentAssigneeId, validation.Value!);

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

        _db.Chores.Remove(chore); // completions cascade-delete
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

        var completion = new ChoreCompletion
        {
            ChoreId = chore.Id,
            CompletedByUserId = userId,
            CompletedAt = DateTimeOffset.UtcNow,
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            PointsAwarded = chore.Points,
            OccurrenceDueAt = chore.DueAt
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

        // Advance to the next occurrence (null for one-time → nothing scheduled).
        chore.DueAt = chore.DueAt is { } due
            ? RecurrenceCalculator.Next(chore.RepeatType, chore.Weekdays, due)
            : null;

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

        // Restore the occurrence that was completed.
        if (completion.Chore is { } chore)
            chore.DueAt = completion.OccurrenceDueAt;

        _db.ChoreCompletions.Remove(completion);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private IQueryable<Chore> Query() => _db.Chores
        .Include(c => c.Assignees)
        .Include(c => c.CurrentAssignee)
        .Include(c => c.Tags)
        .AsSplitQuery();

    /// <summary>The most recent completion (with completer + chore name) per chore id, for undo
    /// affordances. Phase 6 (history) can optimize this; for now it loads the relevant
    /// completions and picks the latest per chore in memory.</summary>
    private async Task<Dictionary<Guid, ChoreCompletion>> LatestCompletionsAsync(
        List<Guid> choreIds, CancellationToken ct)
    {
        if (choreIds.Count == 0)
            return new Dictionary<Guid, ChoreCompletion>();

        var completions = await _db.ChoreCompletions
            .Include(x => x.CompletedBy)
            .Include(x => x.Chore)
            .Where(x => choreIds.Contains(x.ChoreId))
            .ToListAsync(ct);

        // Order client-side: SQLite can't ORDER BY DateTimeOffset.
        return completions
            .OrderByDescending(x => x.CompletedAt)
            .GroupBy(x => x.ChoreId)
            .ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>Validates the request and returns the resolved assignee entities.</summary>
    private async Task<Result<List<User>>> ValidateAsync(string name, int points, RepeatType repeatType,
        DayOfWeek[]? weekdays, Guid[] assigneeIds, Guid currentAssigneeId, CancellationToken ct)
    {
        if (Validators.ChoreName(name) is { } nameError)
            return Result.Fail<List<User>>(nameError);
        if (Validators.Points(points) is { } pointsError)
            return Result.Fail<List<User>>(pointsError);

        var ids = (assigneeIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
            return Result.Fail<List<User>>(Error.Validation("A chore must have at least one assignee."));

        var assignees = await _db.Users.Where(u => ids.Contains(u.Id)).ToListAsync(ct);
        if (assignees.Count != ids.Count)
            return Result.Fail<List<User>>(Error.Validation("One or more assignees do not exist."));

        if (!ids.Contains(currentAssigneeId))
            return Result.Fail<List<User>>(Error.Validation("The current assignee must be one of the assignees."));

        return Result.Success(assignees);
    }

    private static void Apply(Chore chore, string name, string? description, string? emoji, int points,
        RepeatType repeatType, DayOfWeek[]? weekdays, DateTimeOffset startDate, Guid currentAssigneeId,
        List<User> assignees)
    {
        chore.Name = name.Trim();
        chore.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        chore.Emoji = string.IsNullOrWhiteSpace(emoji) ? null : emoji.Trim();
        chore.Points = points;
        chore.RepeatType = repeatType;
        chore.Weekdays = repeatType == RepeatType.Weekly
            ? (weekdays ?? []).Distinct().OrderBy(d => d).ToList()
            : new List<DayOfWeek>();
        chore.StartDate = startDate;
        chore.DueAt = startDate;
        chore.CurrentAssigneeId = currentAssigneeId;
        chore.Assignees.Clear();
        foreach (var a in assignees) chore.Assignees.Add(a);
    }
}
