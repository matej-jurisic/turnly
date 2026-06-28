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
    private readonly AchievementService _achievements;
    private readonly NotificationService _notifications;

    public ChoreService(TurnlyDbContext db, TagService tags, AchievementService achievements, NotificationService notifications)
    {
        _db = db;
        _tags = tags;
        _achievements = achievements;
        _notifications = notifications;
    }

    /// <summary><paramref name="viewerId"/> personalises track-mode chores to the logged-in user:
    /// the chore's top-level <c>DueAt</c>/progress reflect that user's own track (so the card buckets
    /// where it matters to them), falling back to the earliest track when they aren't an assignee.</summary>
    public async Task<List<ChoreDto>> ListAsync(Guid? viewerId = null, CancellationToken ct = default)
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
        var assignmentsByChore = await AssignmentsByChoreAsync(ids, ct);
        var pendingByChore = await PendingReassignmentsByChoreAsync(ids, ct);
        var now = DateTimeOffset.UtcNow;

        return chores
            .Select(c => ToDto(c, completionsByChore.GetValueOrDefault(c.Id),
                assignmentsByChore.GetValueOrDefault(c.Id, []), now, viewerId,
                pendingByChore.GetValueOrDefault(c.Id)))
            .ToList();
    }

    public async Task<Result<ChoreDto>> GetAsync(Guid id, Guid? viewerId = null, CancellationToken ct = default)
    {
        var chore = await Query().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (chore is null)
            return Result.Fail<ChoreDto>(Error.NotFound("Chore not found."));

        var completionsByChore = await CompletionsByChoreAsync([id], ct);
        var assignmentsByChore = await AssignmentsByChoreAsync([id], ct);
        var pendingByChore = await PendingReassignmentsByChoreAsync([id], ct);
        return Result.Success(ToDto(chore, completionsByChore.GetValueOrDefault(id),
            assignmentsByChore.GetValueOrDefault(id, []), DateTimeOffset.UtcNow, viewerId,
            pendingByChore.GetValueOrDefault(id)));
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
        if (IsIndependent(chore))
        {
            // Track-mode chore: give every assignee their own schedule; no single current assignee
            // means no initial assignment / rotation history.
            SyncTracks(chore, req, scheduleChanged: true);
        }
        else
        {
            // Record the initial assignment so Least-Assigned counts and undo work from the start.
            _db.ChoreAssignments.Add(new ChoreAssignment { ChoreId = chore.Id, UserId = req.CurrentAssigneeId!.Value });
        }
        await _db.SaveChangesAsync(ct);

        return await GetAsync(chore.Id, ct: ct);
    }

    /// <summary>Duplicates an existing chore under a new name, carrying over all settings
    /// (recurrence, assignees, tags, notifications, per-assignee tracks) but no completion history —
    /// the copy is created through the normal create path, so its schedule starts fresh.</summary>
    public async Task<Result<ChoreDto>> CopyAsync(Guid id, string newName, CancellationToken ct = default)
    {
        var src = await Query().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (src is null)
            return Result.Fail<ChoreDto>(Error.NotFound("Chore not found."));

        var req = new CreateChoreRequest(
            newName, src.Description, src.Emoji, src.Points, src.RepeatType, src.CustomMode,
            src.IntervalCount, src.IntervalUnit, src.Weekdays.ToArray(), src.WeeksOfMonth.ToArray(),
            src.DaysOfMonth.ToArray(), src.Months.ToArray(), src.CompletionsRequired,
            src.RotateOnEachCompletion, src.AssignmentStrategy, src.SchedulingPreference,
            src.GraceMinutes, src.AutoAdvanceIncomplete, src.CompletionWindowMinutes, src.StartDate,
            src.Assignees.Select(a => a.Id).ToArray(), src.CurrentAssigneeId,
            src.Tags.Select(t => t.Name).ToArray(),
            src.Notifications.Select(n => new ChoreNotificationInput(n.Type, n.Timing, n.OffsetValue, n.OffsetUnit, n.Recipients)).ToArray(),
            src.DueTime?.ToString("HH\\:mm"),
            src.AssigneeTracks.Select(t => new TrackInput(t.UserId, t.CompletionsRequired)).ToArray(),
            src.TimesOfDay.Select(t => t.ToString("HH\\:mm")).ToArray());

        return await CreateAsync(req, ct);
    }

    public async Task<Result<ChoreDto>> UpdateAsync(Guid id, UpdateChoreRequest req, CancellationToken ct = default)
    {
        var validation = await ValidateAsync(req, ct);
        if (!validation.Succeeded)
            return Result.Fail<ChoreDto>(validation.Error!);

        var chore = await _db.Chores
            .Include(c => c.Assignees)
            .Include(c => c.Tags)
            .Include(c => c.Notifications)
            .Include(c => c.AssigneeTracks)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (chore is null)
            return Result.Fail<ChoreDto>(Error.NotFound("Chore not found."));

        var previousAssignee = chore.CurrentAssigneeId;
        var oldStartDate = chore.StartDate;
        var oldRule = RecurrenceRule.FromChore(chore);

        Apply(chore, req, validation.Value!);

        var scheduleChanged = chore.StartDate != oldStartDate
            || ScheduleChanged(oldRule, RecurrenceRule.FromChore(chore));

        if (IsIndependent(chore))
        {
            // Reconcile per-assignee tracks (preserving each track's advanced DueAt unless the
            // schedule changed) and re-derive the mirror DueAt from them.
            SyncTracks(chore, req, scheduleChanged);
        }
        else
        {
            // Switching away from track mode drops any leftover tracks.
            SyncTracks(chore, req, scheduleChanged);

            // Only recompute DueAt when the schedule itself changed; editing name/points/assignees/tags
            // must not silently reset a due date that has already been advanced by completions.
            if (scheduleChanged)
                chore.DueAt = RecurrenceCalculator.FirstOccurrence(RecurrenceRule.FromChore(chore), req.StartDate);

            // Reassigning via edit is itself an assignment event (keeps Least-Assigned honest).
            if (chore.CurrentAssigneeId != previousAssignee)
                _db.ChoreAssignments.Add(new ChoreAssignment { ChoreId = chore.Id, UserId = req.CurrentAssigneeId!.Value });
        }

        var resolvedTags = await _tags.ResolveAsync(req.TagNames, ct);
        chore.Tags.Clear();
        foreach (var tag in resolvedTags) chore.Tags.Add(tag);

        await _db.SaveChangesAsync(ct);
        return await GetAsync(chore.Id, ct: ct);
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

    /// <summary>Marks a chore complete. <paramref name="actingUserId"/> is the caller; the completion
    /// is normally credited to them, but an admin may credit it to another user via
    /// <see cref="CompleteChoreRequest.CompletedByUserId"/> (completing on someone's behalf).</summary>
    public async Task<Result<ChoreDto>> CompleteAsync(Guid id, Guid actingUserId, CompleteChoreRequest req, CancellationToken ct = default)
    {
        var chore = await Query().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (chore is null)
            return Result.Fail<ChoreDto>(Error.NotFound("Chore not found."));
        if (chore.IsFrozen)
            return Result.Fail<ChoreDto>(Error.Validation("This chore is paused and cannot be completed."));

        // Resolve who gets credited. Completing on behalf of someone else is admin-only.
        var userId = req.CompletedByUserId ?? actingUserId;
        if (userId != actingUserId)
        {
            var actor = await _db.Users.FindAsync([actingUserId], ct);
            if (actor is null)
                return Result.Fail<ChoreDto>(Error.NotFound("User not found."));
            if (actor.Role != UserRole.Admin)
                return Result.Fail<ChoreDto>(Error.Forbidden("Only admins can complete a chore on behalf of another user."));
        }

        var user = await _db.Users.FindAsync([userId], ct);
        if (user is null)
            return Result.Fail<ChoreDto>(Error.NotFound("User not found."));

        // In track mode the completer must own one of the chore's per-assignee schedules.
        var track = IsIndependent(chore) ? chore.AssigneeTracks.FirstOrDefault(t => t.UserId == userId) : null;
        if (IsIndependent(chore) && track is null)
            return Result.Fail<ChoreDto>(Error.Validation("Only an assignee of this chore can complete their share."));

        var now = DateTimeOffset.UtcNow;
        var completion = new ChoreCompletion
        {
            ChoreId = chore.Id,
            CompletedByUserId = userId,
            CompletedAt = now,
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            PointsAwarded = chore.Points,
            OccurrenceDueAt = track?.DueAt ?? chore.DueAt,
            PreviousAssigneeId = IsIndependent(chore) ? null : chore.CurrentAssigneeId
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

        if (track is not null)
        {
            // Track mode: advance only this assignee's schedule, never rotate. Other tracks are
            // untouched, so a slow assignee never blocks a fast one.
            await AdvanceTrackAsync(chore, track, completion, userId, now, ct);
            chore.DueAt = EarliestTrackDue(chore.AssigneeTracks);
        }
        else
        {
            // Advance the schedule and decide whether this completion opened a new occurrence (which
            // is what triggers an assignee rotation). With RotateOnEachCompletion, a multi-completion
            // chore also rotates on the in-between completions — but not when the occurrence just closed
            // for good (DueAt null, e.g. a finished one-time chore), where a rotation would be pointless.
            var advancedToNewOccurrence = await AdvanceScheduleAsync(chore, completion, now, ct);
            if (advancedToNewOccurrence || (chore.RotateOnEachCompletion && chore.DueAt is not null))
                await RotateAssigneeAsync(chore, now, completion.CompletedByUserId!.Value, completion.Id, ct);
        }

        await _db.SaveChangesAsync(ct);

        // A completion is the main thing that can unlock an achievement — evaluate the credited user.
        var unlocked = await _achievements.EvaluateForUserAsync(userId, now, ct);

        var result = await GetAsync(chore.Id, ct: ct);
        // Hand any freshly-unlocked badges back to the client to celebrate, but only on the earner's own
        // session — an admin completing on someone else's behalf shouldn't get the other user's popup.
        if (result.Succeeded && unlocked.Count > 0 && userId == actingUserId)
            return Result.Success(result.Value! with { UnlockedAchievements = [.. unlocked] });
        return result;
    }

    /// <summary>Skips the current occurrence of a recurring chore: advances the schedule to the
    /// next due date without awarding points or rotating the assignee. Logged as a points-less
    /// <see cref="ChoreCompletion"/> (<c>IsSkip</c>) so it shares the undo path. One-time chores,
    /// and chores with nothing scheduled, cannot be skipped.</summary>
    public async Task<Result<ChoreDto>> SkipAsync(Guid id, Guid userId, SkipChoreRequest req, CancellationToken ct = default)
    {
        var chore = await Query().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (chore is null)
            return Result.Fail<ChoreDto>(Error.NotFound("Chore not found."));
        if (chore.IsFrozen)
            return Result.Fail<ChoreDto>(Error.Validation("This chore is paused and cannot be skipped."));

        if (chore.RepeatType == RepeatType.OneTime)
            return Result.Fail<ChoreDto>(Error.Validation("One-time chores cannot be skipped."));

        // Track mode: skip a specific assignee's schedule (defaults to the caller's own).
        var track = IsIndependent(chore)
            ? chore.AssigneeTracks.FirstOrDefault(t => t.UserId == (req.UserId ?? userId))
            : null;
        if (IsIndependent(chore) && track is null)
            return Result.Fail<ChoreDto>(Error.Validation("That person isn't assigned to this chore."));
        if ((track?.DueAt ?? chore.DueAt) is null)
            return Result.Fail<ChoreDto>(Error.Validation("This chore has nothing scheduled to skip."));

        var skippedUserId = track?.UserId ?? userId;
        var user = await _db.Users.FindAsync([skippedUserId], ct);
        if (user is null)
            return Result.Fail<ChoreDto>(Error.NotFound("User not found."));

        var now = DateTimeOffset.UtcNow;
        var skip = new ChoreCompletion
        {
            ChoreId = chore.Id,
            CompletedByUserId = skippedUserId,
            CompletedAt = now,
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            PointsAwarded = 0,
            IsSkip = true,
            OccurrenceDueAt = track?.DueAt ?? chore.DueAt,
            PreviousAssigneeId = IsIndependent(chore) ? null : chore.CurrentAssigneeId
        };
        _db.ChoreCompletions.Add(skip);

        // Advance the schedule, but keep the same assignee (a skip is not a completion, so it neither
        // awards points nor rotates). It still counts toward a multi-completion occurrence, so the
        // due date only moves once the occurrence is fully closed.
        if (track is not null)
        {
            await AdvanceTrackAsync(chore, track, skip, skippedUserId, now, ct);
            chore.DueAt = EarliestTrackDue(chore.AssigneeTracks);
        }
        else
        {
            await AdvanceScheduleAsync(chore, skip, now, ct);
        }

        await _db.SaveChangesAsync(ct);
        return await GetAsync(chore.Id, ct: ct);
    }

    /// <summary>Reassigns the current occurrence of a chore to another eligible assignee. Admins move
    /// the chore immediately (the strategy still drives future rotations; this just sets the current
    /// occupant and logs the assignment, keeping <see cref="AssignmentStrategy.LeastAssigned"/>
    /// honest). A non-admin may only reassign a chore they currently hold, and the move does not take
    /// effect until the target accepts: their request is recorded as a
    /// <see cref="ReassignmentStatus.Pending"/> <see cref="ChoreReassignmentRequest"/> and the target
    /// is notified (in-app + push). See <see cref="RespondToReassignmentAsync"/>.</summary>
    public async Task<Result<ChoreDto>> ReassignAsync(Guid id, Guid userId, ReassignChoreRequest req, CancellationToken ct = default)
    {
        var chore = await _db.Chores
            .Include(c => c.Assignees)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (chore is null)
            return Result.Fail<ChoreDto>(Error.NotFound("Chore not found."));

        if (IsIndependent(chore))
            return Result.Fail<ChoreDto>(Error.Validation("This chore is shared per person, so there's no single assignee to reassign."));

        if (chore.Assignees.All(a => a.Id != req.AssigneeId))
            return Result.Fail<ChoreDto>(Error.Validation("The new assignee must be one of the chore's assignees."));

        var actor = await _db.Users.FindAsync([userId], ct);
        if (actor is null)
            return Result.Fail<ChoreDto>(Error.NotFound("User not found."));

        // Members may only reassign a chore they currently hold; admins may reassign any.
        if (actor.Role != UserRole.Admin && chore.CurrentAssigneeId != userId)
            return Result.Fail<ChoreDto>(Error.Forbidden("You can only reassign chores that are assigned to you."));

        if (chore.CurrentAssigneeId == req.AssigneeId)
            return await GetAsync(chore.Id, ct: ct); // already theirs — nothing to do

        if (actor.Role == UserRole.Admin)
        {
            // Immediate move. Void any pending request — the chore is being placed by an admin.
            await CancelPendingReassignmentsAsync(chore.Id, ct);
            var previousAssigneeId = chore.CurrentAssigneeId;
            chore.CurrentAssigneeId = req.AssigneeId;
            _db.ChoreAssignments.Add(new ChoreAssignment
            {
                ChoreId = chore.Id,
                UserId = req.AssigneeId,
                PreviousAssigneeId = previousAssigneeId,
                AssignedByUserId = userId,
            });
            await _db.SaveChangesAsync(ct);
            return await GetAsync(chore.Id, ct: ct);
        }

        // Member request: don't move the chore — supersede any prior pending request, record a new
        // one, and notify the target so they can accept or decline.
        await CancelPendingReassignmentsAsync(chore.Id, ct);
        _db.ChoreReassignmentRequests.Add(new ChoreReassignmentRequest
        {
            ChoreId = chore.Id,
            FromUserId = userId,
            ToUserId = req.AssigneeId,
        });

        var title = string.IsNullOrWhiteSpace(chore.Emoji) ? chore.Name : $"{chore.Emoji} {chore.Name}";
        await _notifications.NotifyUserAsync(req.AssigneeId, title,
            $"{actor.DisplayName} wants to reassign this chore to you. Accept or decline it.", chore.Id, ct);

        await _db.SaveChangesAsync(ct);
        return await GetAsync(chore.Id, ct: ct);
    }

    /// <summary>The target of a pending member reassignment accepts (the chore moves to them and the
    /// assignment is logged) or declines (the chore stays with the requester). Either way the
    /// requester is notified of the outcome. Only the request's target may respond.</summary>
    public async Task<Result<ChoreDto>> RespondToReassignmentAsync(Guid choreId, Guid userId, bool accept, CancellationToken ct = default)
    {
        var request = await _db.ChoreReassignmentRequests
            .Include(r => r.Chore)
            .Where(r => r.ChoreId == choreId && r.ToUserId == userId && r.Status == ReassignmentStatus.Pending)
            .FirstOrDefaultAsync(ct);
        if (request is null)
            return Result.Fail<ChoreDto>(Error.NotFound("No pending reassignment for you on this chore."));

        var chore = request.Chore;
        if (chore is null)
            return Result.Fail<ChoreDto>(Error.NotFound("Chore not found."));

        var now = DateTimeOffset.UtcNow;
        request.RespondedAt = now;

        var responder = await _db.Users.FindAsync([userId], ct);
        var responderName = responder?.DisplayName ?? "Someone";
        var title = string.IsNullOrWhiteSpace(chore.Emoji) ? chore.Name : $"{chore.Emoji} {chore.Name}";

        if (accept)
        {
            request.Status = ReassignmentStatus.Accepted;
            var previousAssigneeId = chore.CurrentAssigneeId;
            chore.CurrentAssigneeId = request.ToUserId;
            _db.ChoreAssignments.Add(new ChoreAssignment
            {
                ChoreId = chore.Id,
                UserId = request.ToUserId,
                PreviousAssigneeId = previousAssigneeId,
                AssignedByUserId = userId,
            });
            await _notifications.NotifyUserAsync(request.FromUserId, title,
                $"{responderName} accepted the reassignment. It's now their chore.", chore.Id, ct);
        }
        else
        {
            request.Status = ReassignmentStatus.Declined;
            await _notifications.NotifyUserAsync(request.FromUserId, title,
                $"{responderName} declined the reassignment. It's still your chore.", chore.Id, ct);
        }

        await _db.SaveChangesAsync(ct);
        return await GetAsync(chore.Id, ct: ct);
    }

    /// <summary>Cancels (supersedes/voids) every outstanding <see cref="ReassignmentStatus.Pending"/>
    /// request on a chore — called when a newer request is made or the chore is otherwise moved
    /// (admin reassign, freeze rotation) so a stale acceptance can't resurrect an old owner. Does not
    /// save; the caller commits.</summary>
    private async Task CancelPendingReassignmentsAsync(Guid choreId, CancellationToken ct)
    {
        var pending = await _db.ChoreReassignmentRequests
            .Where(r => r.ChoreId == choreId && r.Status == ReassignmentStatus.Pending)
            .ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var r in pending)
        {
            r.Status = ReassignmentStatus.Cancelled;
            r.RespondedAt = now;
        }
    }

    /// <summary>Admin-only manual reschedule of the current occurrence: sets a new due instant (and
    /// matching wall-clock time) without completing, skipping, or rotating. The chore's recurrence
    /// still drives future occurrences from this new date onward.</summary>
    public async Task<Result<ChoreDto>> RescheduleAsync(Guid id, RescheduleChoreRequest req, CancellationToken ct = default)
    {
        if (Validators.DueTime(req.DueTime, out var dueTime) is { } dueTimeError)
            return Result.Fail<ChoreDto>(dueTimeError);

        var chore = await _db.Chores
            .Include(c => c.AssigneeTracks)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (chore is null)
            return Result.Fail<ChoreDto>(Error.NotFound("Chore not found."));
        if (chore.DueAt is null)
            return Result.Fail<ChoreDto>(Error.Validation("This chore has nothing scheduled to reschedule."));

        if (IsIndependent(chore))
        {
            // Track mode: reschedule one assignee's own occurrence; the mirror follows.
            if (req.UserId is not { } targetId)
                return Result.Fail<ChoreDto>(Error.Validation("Choose whose schedule to reschedule."));
            var track = chore.AssigneeTracks.FirstOrDefault(t => t.UserId == targetId);
            if (track is null)
                return Result.Fail<ChoreDto>(Error.Validation("That person isn't assigned to this chore."));
            track.DueAt = req.DueAt;
            chore.DueAt = EarliestTrackDue(chore.AssigneeTracks);
        }
        else
        {
            chore.DueAt = req.DueAt;
        }
        chore.DueTime = dueTime;
        await _db.SaveChangesAsync(ct);

        return await GetAsync(chore.Id, ct: ct);
    }

    public async Task<Result> UndoCompletionAsync(Guid completionId, Guid actingUserId, CancellationToken ct = default)
    {
        var completion = await _db.ChoreCompletions
            .Include(c => c.Chore)
            .FirstOrDefaultAsync(c => c.Id == completionId, ct);
        if (completion is null)
            return Result.Fail(Error.NotFound("Completion not found."));
        if (completion.IsExpired)
            return Result.Fail(Error.Forbidden("Auto-expired entries cannot be undone."));

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
            if (IsIndependent(chore))
            {
                // Track mode: rewind only the completer's own schedule, then re-derive the mirror.
                var tracks = await _db.ChoreAssigneeTracks.Where(t => t.ChoreId == chore.Id).ToListAsync(ct);
                var track = tracks.FirstOrDefault(t => t.UserId == completion.CompletedByUserId);
                if (track is not null) track.DueAt = completion.OccurrenceDueAt;
                chore.DueAt = EarliestTrackDue(tracks);
            }
            else
            {
                chore.DueAt = completion.OccurrenceDueAt;
                chore.CurrentAssigneeId = completion.PreviousAssigneeId;
            }
        }
        var rotationLog = await _db.ChoreAssignments.Where(a => a.ChoreCompletionId == completionId).ToListAsync(ct);
        _db.ChoreAssignments.RemoveRange(rotationLog);

        _db.ChoreCompletions.Remove(completion);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Admin-only deletion of a historical activity entry (completion, skip, or auto-expired
    /// slot) — e.g. to fix up the log. Unlike <see cref="UndoCompletionAsync"/>, this is pure history
    /// cleanup: it
    /// reverses the entry's points but does <b>not</b> rewind the chore's current schedule or
    /// assignee, since the entry being removed is generally not the current occurrence.</summary>
    public async Task<Result> DeleteActivityAsync(Guid completionId, Guid actingUserId, CancellationToken ct = default)
    {
        var actor = await _db.Users.FindAsync([actingUserId], ct);
        if (actor is null)
            return Result.Fail(Error.NotFound("User not found."));
        if (actor.Role != UserRole.Admin)
            return Result.Fail(Error.Forbidden("Only admins can delete activity entries."));

        var completion = await _db.ChoreCompletions.FirstOrDefaultAsync(c => c.Id == completionId, ct);
        if (completion is null)
            return Result.Fail(Error.NotFound("Activity entry not found."));

        // Reverse points (a no-op for skips and expired rows, which award none) and drop the rotation
        // log, but leave
        // chore.DueAt / CurrentAssigneeId untouched.
        var logEntry = await _db.PointsLog.FirstOrDefaultAsync(e => e.ChoreCompletionId == completionId, ct);
        if (logEntry is not null)
        {
            var completer = await _db.Users.FindAsync([completion.CompletedByUserId], ct);
            if (completer is not null)
                completer.Points -= logEntry.Delta;
            _db.PointsLog.Remove(logEntry);
        }

        var rotationLog = await _db.ChoreAssignments.Where(a => a.ChoreCompletionId == completionId).ToListAsync(ct);
        _db.ChoreAssignments.RemoveRange(rotationLog);

        _db.ChoreCompletions.Remove(completion);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<List<ChoreHistoryEntryDto>> GetHistoryAsync(
        string? tag, Guid? userId, Guid? choreId, bool includeReassignments = false,
        CancellationToken ct = default)
    {
        var completionQuery = _db.ChoreCompletions
            .Include(c => c.Chore)
            .Include(c => c.CompletedBy)
            .AsQueryable();

        if (choreId.HasValue)
            completionQuery = completionQuery.Where(c => c.ChoreId == choreId);
        if (userId.HasValue)
            // Include expired rows where this user was the responsible assignee at expiry time.
            completionQuery = completionQuery.Where(c =>
                c.CompletedByUserId == userId ||
                (c.IsExpired && c.PreviousAssigneeId == userId));

        List<Guid>? tagChoreIds = null;
        if (tag is not null)
        {
            tagChoreIds = await _db.Chores
                .Where(c => c.Tags.Any(t => t.Name == tag))
                .Select(c => c.Id)
                .ToListAsync(ct);
            completionQuery = completionQuery.Where(c => tagChoreIds.Contains(c.ChoreId));
        }

        var completions = await completionQuery.ToListAsync(ct);

        // PreviousAssigneeId on expired rows holds the assignee at expiry time — no nav property,
        // so load them in one batch and map by id.
        var expiredAssigneeIds = completions
            .Where(c => c.IsExpired && c.PreviousAssigneeId.HasValue)
            .Select(c => c.PreviousAssigneeId!.Value)
            .Distinct()
            .ToList();
        var expiredAssignees = expiredAssigneeIds.Count > 0
            ? (await _db.Users.Where(u => expiredAssigneeIds.Contains(u.Id)).ToListAsync(ct))
                .ToDictionary(u => u.Id)
            : new Dictionary<Guid, User>();

        var entries = completions.Select(c =>
            ChoreHistoryEntryDto.FromCompletion(c,
                c.IsExpired && c.PreviousAssigneeId is { } pid && expiredAssignees.TryGetValue(pid, out var u) ? u : null));

        // Reassignments are opt-in (the History page); the per-chore/per-user views want completions
        // only. Manual reassignments only (AssignedByUserId is set) — rotations/initial assignments
        // have no acting user and aren't user-facing history.
        if (includeReassignments)
        {
            var reassignQuery = _db.ChoreAssignments
                .Where(a => a.AssignedByUserId != null)
                .Include(a => a.Chore)
                .Include(a => a.AssignedBy)
                .Include(a => a.User)
                .Include(a => a.PreviousAssignee)
                .AsQueryable();

            if (choreId.HasValue)
                reassignQuery = reassignQuery.Where(a => a.ChoreId == choreId);
            if (userId.HasValue)
                // Relevant to a user if they performed it, or are the old/new assignee.
                reassignQuery = reassignQuery.Where(a =>
                    a.AssignedByUserId == userId || a.PreviousAssigneeId == userId || a.UserId == userId);
            if (tagChoreIds is not null)
                reassignQuery = reassignQuery.Where(a => tagChoreIds.Contains(a.ChoreId));

            entries = entries.Concat((await reassignQuery.ToListAsync(ct))
                .Select(ChoreHistoryEntryDto.FromReassignment));
        }

        // Order client-side: SQLite can't ORDER BY DateTimeOffset.
        return entries.OrderByDescending(e => e.At).ToList();
    }

    private IQueryable<Chore> Query() => _db.Chores
        .Include(c => c.Assignees)
        .Include(c => c.CurrentAssignee)
        .Include(c => c.Tags)
        .Include(c => c.Notifications)
        .Include(c => c.AssigneeTracks)
        .AsSplitQuery();

    private static bool IsIndependent(Chore chore) => chore.AssignmentStrategy == AssignmentStrategy.Independent;

    /// <summary>The earliest scheduled track due — used to keep <c>chore.DueAt</c> as a mirror so all
    /// the existing single-due checks (listing order, "nothing scheduled" guards, the notification
    /// chore-load filter) keep working for track-mode chores.</summary>
    internal static DateTimeOffset? EarliestTrackDue(IEnumerable<ChoreAssigneeTrack> tracks)
    {
        DateTimeOffset? min = null;
        foreach (var t in tracks)
            if (t.DueAt is { } d && (min is null || d < min)) min = d;
        return min;
    }

    /// <summary>Advances <c>chore.DueAt</c> to the next occurrence and returns whether a brand-new
    /// occurrence was opened (true → rotate the assignee). A chore that needs N completions per
    /// occurrence stays put (returns false) until the Nth completion/skip closes it; only then does
    /// it advance via the same <see cref="RecurrenceCalculator.NextDue"/> path as any other chore.
    /// The just-added <paramref name="completion"/> is included in the count.</summary>
    private async Task<bool> AdvanceScheduleAsync(Chore chore, ChoreCompletion completion, DateTimeOffset now, CancellationToken ct)
    {
        if (chore.CompletionsRequired > 1)
        {
            // Completions (skips included) sharing this occurrence's due date count toward closing it.
            // SQLite can't compare DateTimeOffset in SQL, so match client-side; +1 for the row we just
            // added to the context but haven't saved yet.
            var occurrenceDues = await _db.ChoreCompletions
                .Where(x => x.ChoreId == chore.Id)
                .Select(x => x.OccurrenceDueAt)
                .ToListAsync(ct);
            var doneThisOccurrence = occurrenceDues.Count(d => d == completion.OccurrenceDueAt) + 1;

            if (doneThisOccurrence < chore.CompletionsRequired)
                return false; // occurrence still open — same due date, same assignee
        }

        var rule = RecurrenceRule.FromChore(chore);
        var scheduledDue = completion.OccurrenceDueAt ?? now;
        var grace = chore.GraceMinutes is { } m ? TimeSpan.FromMinutes(m) : (TimeSpan?)null;
        chore.DueAt = RecurrenceCalculator.NextDue(rule, chore.SchedulingPreference, scheduledDue, now, now, grace);
        return chore.DueAt is not null;
    }

    /// <summary>Picks the next current assignee per the chore's strategy and records the assignment.
    /// <paramref name="completedBy"/> is folded in as a virtual completion so strategies that count
    /// completions (LeastCompleted) treat the just-closed occurrence as that user's turn.
    /// <paramref name="completionId"/> links the new <see cref="ChoreAssignment"/> to its trigger row
    /// (so undo can reverse the rotation), or null for auto-advance expiry rows.</summary>
    private async Task RotateAssigneeAsync(Chore chore, DateTimeOffset now, Guid completedBy, Guid? completionId, CancellationToken ct)
    {
        var ordered = chore.Assignees
            .Where(u => !u.IsFrozen)
            .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
            .Select(u => u.Id)
            .ToList();
        if (ordered.Count == 0) return;

        // Pull rows to memory and aggregate client-side: SQLite stores DateTimeOffset as TEXT, so a
        // DB-side Max() can't be trusted across varying offsets (same reason we don't ORDER BY it).
        var assignments = (await _db.ChoreAssignments
            .Where(a => a.ChoreId == chore.Id)
            .Select(a => new { a.UserId, a.AssignedAt })
            .ToListAsync(ct))
            .Select(a => (a.UserId, a.AssignedAt))
            .ToList();
        var completions = (await _db.ChoreCompletions
            .Where(x => x.ChoreId == chore.Id && !x.IsSkip && !x.IsExpired)
            .Select(x => new { x.CompletedByUserId, x.CompletedAt })
            .ToListAsync(ct))
            .Where(x => x.CompletedByUserId.HasValue)
            .Select(x => (x.CompletedByUserId!.Value, x.CompletedAt))
            .ToList();

        var next = PickNext(chore, ordered, assignments, completions, completedBy, now);

        chore.CurrentAssigneeId = next;
        _db.ChoreAssignments.Add(new ChoreAssignment
        {
            ChoreId = chore.Id,
            UserId = next,
            AssignedAt = now,
            ChoreCompletionId = completionId
        });
    }

    /// <summary>Runs the chore's assignment strategy against in-memory history to pick the assignee
    /// the next occurrence would rotate to, assuming <paramref name="completedBy"/> completes the
    /// current occurrence at <paramref name="completedAt"/>. Shared by the live rotation
    /// (<see cref="RotateAssigneeAsync"/>) and the DTO's "next assignee" preview, so both agree.</summary>
    private static Guid PickNext(
        Chore chore,
        IReadOnlyList<Guid> ordered,
        IReadOnlyList<(Guid UserId, DateTimeOffset AssignedAt)> assignments,
        IReadOnlyList<(Guid UserId, DateTimeOffset CompletedAt)> completions,
        Guid completedBy,
        DateTimeOffset completedAt)
    {
        var assignedCounts = assignments.GroupBy(a => a.UserId).ToDictionary(g => g.Key, g => g.Count());
        var lastAssignedAt = assignments.GroupBy(a => a.UserId).ToDictionary(g => g.Key, g => g.Max(a => a.AssignedAt));
        var completedCounts = completions.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.Count());
        var lastCompletedAt = completions.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.Max(x => x.CompletedAt));
        completedCounts[completedBy] = completedCounts.GetValueOrDefault(completedBy) + 1;
        if (completedAt > lastCompletedAt.GetValueOrDefault(completedBy, DateTimeOffset.MinValue))
            lastCompletedAt[completedBy] = completedAt;

        return AssignmentPicker.Pick(
            chore.AssignmentStrategy, ordered, chore.CurrentAssigneeId,
            assignedCounts, completedCounts, lastAssignedAt, lastCompletedAt, Random.Shared);
    }

    /// <summary>The assignee the chore would rotate to on its next completion — shown as a preview.
    /// Only meaningful for strategies whose outcome is fixed by current state; the random strategies
    /// (and chores that can't rotate: one-time, nothing scheduled, single assignee) return null.
    /// Assumes the current assignee is the one who completes it (the normal path).</summary>
    private static User? PredictNextAssignee(
        Chore chore, List<ChoreCompletion>? completions,
        IReadOnlyList<(Guid UserId, DateTimeOffset AssignedAt)> assignments, DateTimeOffset now)
    {
        if (chore.RepeatType == RepeatType.OneTime || chore.DueAt is null) return null;
        if (IsIndependent(chore)) return null; // track-mode chores don't rotate
        if (chore.AssignmentStrategy is AssignmentStrategy.Random or AssignmentStrategy.RandomExceptLastAssigned) return null;
        if (chore.CurrentAssigneeId is not { } current) return null;

        var orderedUsers = chore.Assignees.OrderBy(u => u.CreatedAt).ThenBy(u => u.Id).ToList();
        if (orderedUsers.Count < 2) return null;

        var completionPairs = (completions ?? [])
            .Where(x => !x.IsSkip && !x.IsExpired && x.CompletedByUserId.HasValue)
            .Select(x => (x.CompletedByUserId!.Value, x.CompletedAt))
            .ToList();

        var nextId = PickNext(chore, orderedUsers.Select(u => u.Id).ToList(), assignments, completionPairs, current, now);
        return orderedUsers.FirstOrDefault(u => u.Id == nextId);
    }

    /// <summary>All completions per chore id (newest-first within each chore), for both the
    /// last-completion affordance and per-occurrence progress counting. SQLite can't ORDER BY a
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

    private static ChoreDto ToDto(Chore chore, List<ChoreCompletion>? completions,
        IReadOnlyList<(Guid UserId, DateTimeOffset AssignedAt)> assignments, DateTimeOffset now, Guid? viewerId = null,
        ChoreReassignmentRequest? pending = null)
    {
        var latest = completions?.FirstOrDefault(c => !c.IsExpired);

        if (IsIndependent(chore))
        {
            // One DTO per chore, but with each assignee's own track and a top-level DueAt/progress
            // personalised to the viewer (so the card buckets where it matters to them).
            var usersById = chore.Assignees.ToDictionary(u => u.Id);
            var trackDtos = chore.AssigneeTracks
                .Where(t => usersById.ContainsKey(t.UserId))
                .Select(t => new ChoreAssigneeTrackDto(
                    UserDto.FromEntity(usersById[t.UserId]),
                    t.DueAt,
                    t.CompletionsRequired,
                    completions?.Count(x => x.CompletedByUserId == t.UserId && x.OccurrenceDueAt == t.DueAt) ?? 0,
                    // A future DueAt means "done" only once they've actually logged activity; otherwise
                    // it's just a not-yet-reached first occurrence (start date in the future).
                    completions?.Any(x => x.CompletedByUserId == t.UserId) ?? false,
                    // Each assignee's own on-time streak, off their own completion history.
                    StreakCalculator.CurrentStreak(completions?.Where(x => x.CompletedByUserId == t.UserId) ?? [])))
                .OrderBy(d => d.User.DisplayName)
                .ToArray();

            var viewerTrack = viewerId is { } vid ? chore.AssigneeTracks.FirstOrDefault(t => t.UserId == vid) : null;
            var personalDue = viewerTrack?.DueAt ?? EarliestTrackDue(chore.AssigneeTracks);
            int? personalProgress = viewerTrack is { CompletionsRequired: > 1 }
                ? completions?.Count(x => x.CompletedByUserId == viewerTrack.UserId && x.OccurrenceDueAt == viewerTrack.DueAt) ?? 0
                : null;
            // Personalise the top-level streak to the viewer's own track (0 if they aren't an assignee).
            var personalStreak = trackDtos.FirstOrDefault(d => d.User.Id == viewerId)?.Streak ?? 0;

            return ChoreDto.FromEntity(chore, latest, tracks: trackDtos, currentStreak: personalStreak) with
            {
                DueAt = personalDue,
                OccurrenceProgress = personalProgress
            };
        }

        int? progress = null;
        if (chore.CompletionsRequired > 1)
            // Completions + skips logged against the current occurrence (they share its due date).
            progress = completions?.Count(x => x.OccurrenceDueAt == chore.DueAt) ?? 0;
        var next = PredictNextAssignee(chore, completions, assignments, now);
        var streak = StreakCalculator.CurrentStreak(completions ?? []);
        return ChoreDto.FromEntity(chore, latest, progress, next, currentStreak: streak, pendingReassignment: pending);
    }

    /// <summary>Assignment history per chore id, as lightweight (user, when) pairs — for the
    /// "next assignee" preview's Least-Assigned counting. SQLite stores DateTimeOffset as TEXT, so
    /// aggregation happens client-side (same reason we don't ORDER BY it).</summary>
    private async Task<Dictionary<Guid, List<(Guid UserId, DateTimeOffset AssignedAt)>>> AssignmentsByChoreAsync(
        List<Guid> choreIds, CancellationToken ct)
    {
        if (choreIds.Count == 0)
            return new Dictionary<Guid, List<(Guid, DateTimeOffset)>>();

        var assignments = await _db.ChoreAssignments
            .Where(a => choreIds.Contains(a.ChoreId))
            .Select(a => new { a.ChoreId, a.UserId, a.AssignedAt })
            .ToListAsync(ct);

        return assignments
            .GroupBy(a => a.ChoreId)
            .ToDictionary(g => g.Key, g => g.Select(a => (a.UserId, a.AssignedAt)).ToList());
    }

    /// <summary>The single outstanding <see cref="ReassignmentStatus.Pending"/> request per chore (if
    /// any), with both participants loaded for the DTO. At most one pending request exists per chore
    /// (a newer request supersedes the older), so a first-match per chore is safe.</summary>
    private async Task<Dictionary<Guid, ChoreReassignmentRequest>> PendingReassignmentsByChoreAsync(
        List<Guid> choreIds, CancellationToken ct)
    {
        if (choreIds.Count == 0)
            return new Dictionary<Guid, ChoreReassignmentRequest>();

        var requests = await _db.ChoreReassignmentRequests
            .Include(r => r.FromUser)
            .Include(r => r.ToUser)
            .Where(r => choreIds.Contains(r.ChoreId) && r.Status == ReassignmentStatus.Pending)
            .AsNoTracking()
            .ToListAsync(ct);

        return requests
            .GroupBy(r => r.ChoreId)
            .ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>Validates the request and returns the resolved assignee entities.</summary>
    private async Task<Result<List<User>>> ValidateAsync(IChoreInput req, CancellationToken ct)
    {
        if (Validators.ChoreName(req.Name) is { } nameError)
            return Result.Fail<List<User>>(nameError);
        if (Validators.Points(req.Points) is { } pointsError)
            return Result.Fail<List<User>>(pointsError);
        if (Validators.Recurrence(req.RepeatType, req.CustomMode, req.IntervalCount, req.IntervalUnit,
                req.Weekdays, req.DaysOfMonth, req.Months, req.CompletionsRequired, req.WeeksOfMonth) is { } recurrenceError)
            return Result.Fail<List<User>>(recurrenceError);
        if (Validators.DueTime(req.DueTime, out _) is { } dueTimeError)
            return Result.Fail<List<User>>(dueTimeError);
        if (Validators.TimesOfDay(req.TimesOfDay, req.RepeatType, req.CustomMode, out _) is { } timesError)
            return Result.Fail<List<User>>(timesError);

        if (req.Notifications is { } notifications)
        {
            if (notifications.Length > Validators.MaxNotificationsPerChore)
                return Result.Fail<List<User>>(Error.Validation($"A chore can have at most {Validators.MaxNotificationsPerChore} notifications."));
            foreach (var n in notifications)
                if (Validators.NotificationOffset(n.Timing, n.OffsetValue) is { } notificationError)
                    return Result.Fail<List<User>>(notificationError);
        }

        var ids = (req.AssigneeIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
            return Result.Fail<List<User>>(Error.Validation("A chore must have at least one assignee."));

        var assignees = await _db.Users.Where(u => ids.Contains(u.Id)).ToListAsync(ct);
        if (assignees.Count != ids.Count)
            return Result.Fail<List<User>>(Error.Validation("One or more assignees do not exist."));

        if (req.AssignmentStrategy == AssignmentStrategy.Independent)
        {
            // Track mode: no single current assignee; instead every assignee carries a per-person quota.
            if (req.RepeatType == RepeatType.OneTime)
                return Result.Fail<List<User>>(Error.Validation("A one-time chore can't use per-person scheduling."));

            var tracks = req.Tracks ?? [];
            var trackIds = tracks.Select(t => t.UserId).Distinct().ToList();
            if (trackIds.Count != ids.Count || !ids.All(trackIds.Contains))
                return Result.Fail<List<User>>(Error.Validation("Each assignee must have exactly one per-person schedule."));
            if (tracks.Any(t => t.CompletionsRequired < 1 || t.CompletionsRequired > Validators.MaxCompletionsRequired))
                return Result.Fail<List<User>>(Error.Validation($"Each person's number of times must be between 1 and {Validators.MaxCompletionsRequired}."));
        }
        else if (req.CurrentAssigneeId is not { } current || !ids.Contains(current))
        {
            return Result.Fail<List<User>>(Error.Validation("The current assignee must be one of the assignees."));
        }

        return Result.Success(assignees);
    }

    private static bool ScheduleChanged(RecurrenceRule a, RecurrenceRule b) =>
        a.Type != b.Type ||
        a.CustomMode != b.CustomMode ||
        a.IntervalCount != b.IntervalCount ||
        a.IntervalUnit != b.IntervalUnit ||
        !a.Weekdays.SequenceEqual(b.Weekdays) ||
        !a.WeeksOfMonth.SequenceEqual(b.WeeksOfMonth) ||
        !a.DaysOfMonth.SequenceEqual(b.DaysOfMonth) ||
        !a.Months.SequenceEqual(b.Months) ||
        !a.TimesOfDay.SequenceEqual(b.TimesOfDay);

    private void Apply(Chore chore, IChoreInput req, List<User> assignees)
    {
        chore.Name = req.Name.Trim();
        chore.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        chore.Emoji = string.IsNullOrWhiteSpace(req.Emoji) ? null : req.Emoji.Trim();
        chore.Points = req.Points;
        chore.RepeatType = req.RepeatType;
        chore.StartDate = req.StartDate;
        chore.AssignmentStrategy = req.AssignmentStrategy;
        chore.SchedulingPreference = req.SchedulingPreference;
        // Grace only rides on Smart scheduling, and only when positive (0/null = pure max).
        chore.GraceMinutes = req.SchedulingPreference == SchedulingPreference.SmartScheduling && req.GraceMinutes is > 0
            ? req.GraceMinutes
            : null;
        // Auto-advance applies to all recurring non-independent chores (custom included — they always
        // close on a single completion, but can still be advanced/rotated when overdue).
        var supportsAutoAdvance = req.RepeatType != RepeatType.OneTime
            && req.AssignmentStrategy != AssignmentStrategy.Independent;
        chore.AutoAdvanceIncomplete = supportsAutoAdvance && req.AutoAdvanceIncomplete;
        chore.CompletionWindowMinutes = supportsAutoAdvance && req.AutoAdvanceIncomplete && req.CompletionWindowMinutes is > 0
            ? req.CompletionWindowMinutes
            : null;
        // Track mode has no single current assignee (per-assignee tracks instead); rotating chores
        // keep the chosen one.
        chore.CurrentAssigneeId = req.AssignmentStrategy == AssignmentStrategy.Independent ? null : req.CurrentAssigneeId;

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
        // Empty = every week; -1 (last) sorts after the 1st–4th for a stable order.
        chore.WeeksOfMonth = mode == CustomRecurrenceMode.DaysOfWeek
            ? (req.WeeksOfMonth ?? []).Distinct().OrderBy(w => w == -1 ? int.MaxValue : w).ToList()
            : new List<int>();
        chore.DaysOfMonth = mode == CustomRecurrenceMode.DaysOfMonth
            ? (req.DaysOfMonth ?? []).Distinct().OrderBy(d => d).ToList()
            : new List<int>();
        chore.Months = mode == CustomRecurrenceMode.DaysOfMonth
            ? (req.Months ?? []).Distinct().OrderBy(m => m).ToList()
            : new List<int>();

        // "Complete N times per occurrence" rides only on the non-custom repeat types; custom
        // recurrences always close on a single completion. Track mode keeps the scalar at 1 and uses
        // the per-assignee quota on each ChoreAssigneeTrack instead.
        var isIndependent = req.AssignmentStrategy == AssignmentStrategy.Independent;
        chore.CompletionsRequired = isCustom || isIndependent ? 1 : Math.Max(1, req.CompletionsRequired);
        // Per-completion rotation only means anything when more than one completion is required, and
        // never applies in track mode (no rotation at all).
        chore.RotateOnEachCompletion = !isIndependent && chore.CompletionsRequired > 1 && req.RotateOnEachCompletion;

        Validators.DueTime(req.DueTime, out var dueTime); // format already checked in ValidateAsync
        // Fixed times only ride on day-resolution schedules (Daily / DaysOfWeek / DaysOfMonth); clear
        // them otherwise so a stale list can't leak into the recurrence math. When set, DueTime mirrors
        // the earliest slot so single-time display/round-trip stays coherent.
        var supportsTimes = req.RepeatType == RepeatType.Daily ||
            (isCustom && mode is CustomRecurrenceMode.DaysOfWeek or CustomRecurrenceMode.DaysOfMonth);
        Validators.TimesOfDay(req.TimesOfDay, req.RepeatType, req.CustomMode, out var times); // already checked in ValidateAsync
        chore.TimesOfDay = supportsTimes ? times : new List<TimeOnly>();
        chore.DueTime = chore.TimesOfDay.Count > 0 ? chore.TimesOfDay[0] : dueTime;

        chore.Assignees.Clear();
        foreach (var a in assignees) chore.Assignees.Add(a);

        // Rebuild the notification schedule from the request (orphans cascade-delete). AtDue entries
        // carry no offset. New entries are inserted via the DbSet rather than only through the
        // navigation: a ChoreNotification carries a client-set Guid key, so on the update path (where
        // the chore is already tracked) EF would classify a navigation-added child as Modified and
        // emit an UPDATE against a row that doesn't exist yet — a DbUpdateConcurrencyException.
        _db.ChoreNotifications.RemoveRange(chore.Notifications);
        foreach (var n in req.Notifications ?? [])
        {
            _db.ChoreNotifications.Add(new ChoreNotification
            {
                ChoreId = chore.Id,
                Type = n.Type,
                Timing = n.Timing,
                OffsetValue = n.Timing == NotificationTiming.AtDue ? 0 : Math.Max(0, n.OffsetValue),
                OffsetUnit = n.OffsetUnit,
                Recipients = n.Recipients
            });
        }
    }

    /// <summary>Reconciles a chore's per-assignee tracks with the request. Non-track-mode chores have
    /// their tracks dropped. In track mode, existing tracks keep their advanced <c>DueAt</c> (only
    /// reset when <paramref name="scheduleChanged"/>); newly added assignees start at the chore's
    /// current schedule position; dropped assignees lose their track. Always re-derives the mirror
    /// <c>chore.DueAt</c>. New tracks are inserted via the DbSet (not the navigation) for the same
    /// client-set-Guid reason as the notification rebuild above.</summary>
    private void SyncTracks(Chore chore, IChoreInput req, bool scheduleChanged)
    {
        if (req.AssignmentStrategy != AssignmentStrategy.Independent)
        {
            if (chore.AssigneeTracks.Count > 0)
            {
                _db.ChoreAssigneeTracks.RemoveRange(chore.AssigneeTracks);
                chore.AssigneeTracks.Clear();
            }
            return;
        }

        var rule = RecurrenceRule.FromChore(chore);
        var first = RecurrenceCalculator.FirstOccurrence(rule, chore.StartDate);
        // The position a newly added assignee joins at: the chore's current cadence, or the first
        // occurrence on a brand-new chore.
        var baseline = chore.DueAt ?? first;

        var desired = (req.Tracks ?? []).ToDictionary(t => t.UserId, t => Math.Max(1, t.CompletionsRequired));
        var existing = chore.AssigneeTracks.ToList();

        foreach (var track in existing)
        {
            if (desired.TryGetValue(track.UserId, out var quota))
            {
                track.CompletionsRequired = quota;
                if (scheduleChanged) track.DueAt = first; // recurrence grid moved — realign
            }
            else
            {
                _db.ChoreAssigneeTracks.Remove(track); // assignee removed
                chore.AssigneeTracks.Remove(track);
            }
        }

        var existingIds = existing.Select(t => t.UserId).ToHashSet();
        foreach (var (userId, quota) in desired)
        {
            if (existingIds.Contains(userId)) continue;
            _db.ChoreAssigneeTracks.Add(new ChoreAssigneeTrack
            {
                ChoreId = chore.Id,
                UserId = userId,
                CompletionsRequired = quota,
                DueAt = scheduleChanged ? first : baseline
            });
        }

        // Mirror reflects every track, including the ones just added via the DbSet (not yet in the nav).
        var dues = existing
            .Where(t => desired.ContainsKey(t.UserId))
            .Select(t => t.DueAt)
            .Concat(desired.Keys.Where(id => !existingIds.Contains(id))
                .Select(_ => (DateTimeOffset?)(scheduleChanged ? first : baseline)))
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .ToList();
        chore.DueAt = dues.Count == 0 ? null : dues.Min();
    }

    /// <summary>Advances one assignee's track to their next occurrence, gated by their personal quota:
    /// the track only moves once <see cref="ChoreAssigneeTrack.CompletionsRequired"/> completions/skips
    /// (by that user, sharing the track's current <c>DueAt</c>) have been logged — the just-added
    /// <paramref name="entry"/> included. Other tracks are never touched.</summary>
    private async Task AdvanceTrackAsync(Chore chore, ChoreAssigneeTrack track, ChoreCompletion entry,
        Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        if (track.CompletionsRequired > 1)
        {
            // SQLite can't compare DateTimeOffset in SQL, so match client-side; +1 for the row we just
            // added to the context but haven't saved yet.
            var dues = await _db.ChoreCompletions
                .Where(x => x.ChoreId == chore.Id && x.CompletedByUserId == userId)
                .Select(x => x.OccurrenceDueAt)
                .ToListAsync(ct);
            var doneThisOccurrence = dues.Count(d => d == entry.OccurrenceDueAt) + 1;
            if (doneThisOccurrence < track.CompletionsRequired)
                return; // this assignee's occurrence still open
        }

        var rule = RecurrenceRule.FromChore(chore);
        var scheduledDue = entry.OccurrenceDueAt ?? now;
        var grace = chore.GraceMinutes is { } m ? TimeSpan.FromMinutes(m) : (TimeSpan?)null;
        track.DueAt = RecurrenceCalculator.NextDue(rule, chore.SchedulingPreference, scheduledDue, now, now, grace);
    }

    /// <summary>Called by the background service every minute. For every recurring chore with
    /// <see cref="Chore.AutoAdvanceIncomplete"/> set, checks whether the completion window has closed
    /// (now ≥ DueAt + <see cref="Chore.CompletionWindowMinutes"/>) and the occurrence is still short
    /// of its required completions. If so, writes <see cref="ChoreCompletion.IsExpired"/> rows for the
    /// missing slots (snapshotting the responsible assignee in <see cref="ChoreCompletion.PreviousAssigneeId"/>),
    /// advances the schedule using <see cref="SchedulingPreference.FromScheduledDate"/>, and rotates
    /// the assignee so the person who missed the occurrence doesn't stay on duty.</summary>
    public async Task<int> AutoAdvanceAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var advanced = 0;

        // ── Rotating chores ──────────────────────────────────────────────────────────────────────
        var chores = await Query()
            .Where(c => c.AutoAdvanceIncomplete
                     && !c.IsFrozen
                     && c.DueAt != null
                     && c.RepeatType != RepeatType.OneTime
                     && c.AssignmentStrategy != AssignmentStrategy.Independent)
            .ToListAsync(ct);

        foreach (var chore in chores)
        {
            if (chore.DueAt is not { } dueAt) continue;

            var windowEnd = dueAt.AddMinutes(chore.CompletionWindowMinutes ?? 0);
            if (now < windowEnd) continue;

            // Count non-expired completions that close this occurrence (share its DueAt).
            var allDues = await _db.ChoreCompletions
                .Where(x => x.ChoreId == chore.Id && !x.IsExpired)
                .Select(x => x.OccurrenceDueAt)
                .ToListAsync(ct);
            var doneCount = allDues.Count(d => d == dueAt);
            if (doneCount >= chore.CompletionsRequired) continue;

            // Write IsExpired rows for the missing slots, snapshotting who was responsible.
            ChoreCompletion? firstExpired = null;
            for (var i = doneCount; i < chore.CompletionsRequired; i++)
            {
                var row = new ChoreCompletion
                {
                    ChoreId = chore.Id,
                    IsExpired = true,
                    OccurrenceDueAt = dueAt,
                    PointsAwarded = 0,
                    CompletedAt = now,
                    PreviousAssigneeId = chore.CurrentAssigneeId,
                };
                _db.ChoreCompletions.Add(row);
                firstExpired ??= row;
            }

            // Advance and rotate — treat the current assignee's missed occurrence as their "turn used"
            // so the same person doesn't stay on duty after an expired occurrence.
            var rule = RecurrenceRule.FromChore(chore);
            chore.DueAt = RecurrenceCalculator.NextDue(rule, SchedulingPreference.FromScheduledDate, dueAt, now, now);
            if (chore.CurrentAssigneeId.HasValue && chore.DueAt is not null)
                await RotateAssigneeAsync(chore, now, chore.CurrentAssigneeId.Value, firstExpired?.Id, ct);
            advanced++;
        }

        // ── Independent (per-assignee track) chores ─────────────────────────────────────────────
        var trackChores = await Query()
            .Where(c => c.AutoAdvanceIncomplete
                     && !c.IsFrozen
                     && c.DueAt != null
                     && c.RepeatType != RepeatType.OneTime
                     && c.AssignmentStrategy == AssignmentStrategy.Independent)
            .ToListAsync(ct);

        foreach (var chore in trackChores)
        {
            var advancedAny = false;
            var rule = RecurrenceRule.FromChore(chore);

            foreach (var track in chore.AssigneeTracks)
            {
                if (track.DueAt is not { } trackDue) continue;
                var windowEnd = trackDue.AddMinutes(chore.CompletionWindowMinutes ?? 0);
                if (now < windowEnd) continue;

                var allDues = await _db.ChoreCompletions
                    .Where(x => x.ChoreId == chore.Id && x.CompletedByUserId == track.UserId && !x.IsExpired)
                    .Select(x => x.OccurrenceDueAt)
                    .ToListAsync(ct);
                var doneCount = allDues.Count(d => d == trackDue);
                if (doneCount >= track.CompletionsRequired) continue;

                for (var i = doneCount; i < track.CompletionsRequired; i++)
                {
                    _db.ChoreCompletions.Add(new ChoreCompletion
                    {
                        ChoreId = chore.Id,
                        IsExpired = true,
                        OccurrenceDueAt = trackDue,
                        PointsAwarded = 0,
                        CompletedAt = now,
                        PreviousAssigneeId = track.UserId,
                    });
                }

                track.DueAt = RecurrenceCalculator.NextDue(rule, SchedulingPreference.FromScheduledDate, trackDue, now, now);
                advancedAny = true;
            }

            if (advancedAny)
            {
                chore.DueAt = EarliestTrackDue(chore.AssigneeTracks);
                advanced++;
            }
        }

        if (advanced > 0)
            await _db.SaveChangesAsync(ct);

        return advanced;
    }

    public async Task<Result<ChoreDto>> FreezeAsync(Guid id, CancellationToken ct = default)
    {
        var chore = await _db.Chores
            .Include(c => c.AssigneeTracks)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (chore is null)
            return Result.Fail<ChoreDto>(Error.NotFound("Chore not found."));

        chore.IsFrozen = true;
        await _db.SaveChangesAsync(ct);
        return await GetAsync(chore.Id, ct: ct);
    }

    public async Task<Result<ChoreDto>> UnfreezeAsync(Guid id, CancellationToken ct = default)
    {
        var chore = await Query().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (chore is null)
            return Result.Fail<ChoreDto>(Error.NotFound("Chore not found."));

        chore.IsFrozen = false;
        var now = DateTimeOffset.UtcNow;

        if (IsIndependent(chore))
        {
            foreach (var track in chore.AssigneeTracks)
                track.DueAt = StepForwardIfOverdue(chore, track.DueAt, now);
            chore.DueAt = EarliestTrackDue(chore.AssigneeTracks);
        }
        else if (chore.RepeatType != RepeatType.OneTime)
        {
            chore.DueAt = StepForwardIfOverdue(chore, chore.DueAt, now);
        }

        await _db.SaveChangesAsync(ct);
        return await GetAsync(chore.Id, ct: ct);
    }

    /// <summary>Advances <paramref name="dueAt"/> to the first future occurrence when it is in the
    /// past — used on unfreeze. Uses <see cref="SchedulingPreference.FromScheduledDate"/> so the
    /// cadence is preserved.</summary>
    internal static DateTimeOffset? StepForwardIfOverdue(Chore chore, DateTimeOffset? dueAt, DateTimeOffset now)
    {
        if (dueAt is not { } due || due >= now) return dueAt;
        var rule = RecurrenceRule.FromChore(chore);
        while (due < now)
        {
            var next = RecurrenceCalculator.NextDue(rule, SchedulingPreference.FromScheduledDate, due, now, now);
            if (next is null) break;
            due = next.Value;
        }
        return due;
    }

    /// <summary>Computes which rotating chores would be reassigned (and to whom) and which would be
    /// left without an eligible assignee when <paramref name="freezingUserId"/> is frozen. Pure read —
    /// does not save anything. Called by <see cref="Services.UserService"/> for the preview and freeze flow.</summary>
    internal async Task<(List<(Chore Chore, User NewAssignee)> Reassignments, List<Chore> Unassignable)>
        GetChoreReassignmentsForFreezeAsync(Guid freezingUserId, CancellationToken ct)
    {
        var affected = await Query()
            .Where(c => c.CurrentAssigneeId == freezingUserId
                     && c.AssignmentStrategy != AssignmentStrategy.Independent)
            .ToListAsync(ct);

        var reassignments = new List<(Chore, User)>();
        var unassignable = new List<Chore>();
        var now = DateTimeOffset.UtcNow;

        foreach (var chore in affected)
        {
            var eligible = chore.Assignees
                .Where(u => u.Id != freezingUserId && !u.IsFrozen)
                .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
                .ToList();

            if (eligible.Count == 0)
            {
                unassignable.Add(chore);
                continue;
            }

            var assignments = (await _db.ChoreAssignments
                .Where(a => a.ChoreId == chore.Id)
                .Select(a => new { a.UserId, a.AssignedAt })
                .ToListAsync(ct))
                .Select(a => (a.UserId, a.AssignedAt))
                .ToList();

            var completions = (await _db.ChoreCompletions
                .Where(x => x.ChoreId == chore.Id && !x.IsSkip && !x.IsExpired)
                .Select(x => new { x.CompletedByUserId, x.CompletedAt })
                .ToListAsync(ct))
                .Where(x => x.CompletedByUserId.HasValue)
                .Select(x => (x.CompletedByUserId!.Value, x.CompletedAt))
                .ToList();

            var nextId = PickNext(chore, eligible.Select(u => u.Id).ToList(), assignments, completions, freezingUserId, now);
            var newAssignee = eligible.FirstOrDefault(u => u.Id == nextId) ?? eligible[0];
            reassignments.Add((chore, newAssignee));
        }

        return (reassignments, unassignable);
    }

    /// <summary>Executes the chore reassignments computed by <see cref="GetChoreReassignmentsForFreezeAsync"/>:
    /// sets <c>CurrentAssigneeId</c> and logs a <see cref="ChoreAssignment"/> for each affected chore.
    /// Does <b>not</b> save — the caller batches this with the user freeze in one <c>SaveChanges</c>.</summary>
    internal async Task ExecuteChoreReassignmentsForFreezeAsync(Guid freezingUserId, CancellationToken ct)
    {
        var (reassignments, unassignable) = await GetChoreReassignmentsForFreezeAsync(freezingUserId, ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var (chore, newAssignee) in reassignments)
        {
            var previousAssigneeId = chore.CurrentAssigneeId;
            chore.CurrentAssigneeId = newAssignee.Id;
            _db.ChoreAssignments.Add(new ChoreAssignment
            {
                ChoreId = chore.Id,
                UserId = newAssignee.Id,
                AssignedAt = now,
                PreviousAssigneeId = previousAssigneeId,
            });
            // The chore just moved owners; void any pending acceptance so it can't resurrect a stale one.
            await CancelPendingReassignmentsAsync(chore.Id, ct);
        }

        foreach (var chore in unassignable)
        {
            chore.CurrentAssigneeId = null;
            await CancelPendingReassignmentsAsync(chore.Id, ct);
        }
    }
}
