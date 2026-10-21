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
        var pendingByChore = await OpenOffersByChoreAsync(ids, ct);
        var now = DateTimeOffset.UtcNow;
        var mutedByChore = await MutesByChoreAsync(ids, viewerId, now, ct);
        var dayStart = await AppSettingsService.ReadDayStartAsync(_db, ct);

        return chores
            .Select(c => ToDto(c, completionsByChore.GetValueOrDefault(c.Id),
                assignmentsByChore.GetValueOrDefault(c.Id, []), now, dayStart, viewerId,
                pendingByChore.GetValueOrDefault(c.Id), mutedByChore.GetValueOrDefault(c.Id)))
            .ToList();
    }

    public async Task<Result<ChoreDto>> GetAsync(Guid id, Guid? viewerId = null, CancellationToken ct = default)
    {
        var chore = await Query().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (chore is null)
            return Result.Fail<ChoreDto>(Error.NotFound("Chore not found."));

        var completionsByChore = await CompletionsByChoreAsync([id], ct);
        var assignmentsByChore = await AssignmentsByChoreAsync([id], ct);
        var pendingByChore = await OpenOffersByChoreAsync([id], ct);
        var now = DateTimeOffset.UtcNow;
        var mutedByChore = await MutesByChoreAsync([id], viewerId, now, ct);
        return Result.Success(ToDto(chore, completionsByChore.GetValueOrDefault(id),
            assignmentsByChore.GetValueOrDefault(id, []), now, await LateGraceAsync(chore, ct), viewerId,
            pendingByChore.GetValueOrDefault(id), mutedByChore.GetValueOrDefault(id)));
    }

    public async Task<Result<ChoreDto>> CreateAsync(CreateChoreRequest req, CancellationToken ct = default)
    {
        var validation = await ValidateAsync(req, ct);
        if (!validation.Succeeded)
            return Result.Fail<ChoreDto>(validation.Error!);

        var chore = new Chore { Name = req.Name.Trim(), StartDate = req.StartDate };
        Apply(chore, req, validation.Value!);
        chore.Tags = await _tags.ResolveAsync(req.TagNames, ct);
        chore.DueAt = InitialDue(chore);

        _db.Chores.Add(chore);
        if (IsIndependent(chore))
        {
            // Track-mode chore: give every assignee their own schedule; no single current assignee
            // means no initial assignment / rotation history.
            SyncTracks(chore, req, scheduleChanged: true);
        }
        else if (req.CurrentAssigneeId is { } initialAssignee)
        {
            // Record the initial assignment so Least-Assigned counts and undo work from the start.
            // (A Manual chore may be created unassigned, in which case there's nothing to record.)
            _db.ChoreAssignments.Add(new ChoreAssignment { ChoreId = chore.Id, UserId = initialAssignee, PartnerUserId = chore.CurrentPartnerId });
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
            src.TimesOfDay.Select(t => t.ToString("HH\\:mm")).ToArray(),
            src.RotationPeriod,
            src.TimeLabels.ToDictionary(p => p.Key.ToString("HH\\:mm"), p => p.Value),
            src.Pairs.Select(p => new[] { p.First, p.Second }).ToArray(),
            src.HeldPartnerId());

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
        var previousPartner = chore.HeldPartnerId();
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
                chore.DueAt = InitialDue(chore);

            // Reassigning via edit is itself an assignment event (keeps Least-Assigned honest).
            // Clearing the assignee (Manual only) isn't an assignment, so it logs nothing.
            // So is handing it to another of the same person's pairs.
            if ((chore.CurrentAssigneeId != previousAssignee || chore.CurrentPartnerId != previousPartner)
                && chore.CurrentAssigneeId is { } editedAssignee)
                _db.ChoreAssignments.Add(new ChoreAssignment { ChoreId = chore.Id, UserId = editedAssignee, PartnerUserId = chore.CurrentPartnerId });

            // Paired chores can't be offered, so an offer made before the pairs were set is refunded
            // rather than left for someone to claim on their partner's behalf.
            if (chore.Pairs.Count > 0)
                await VoidOffersAsync(chore, OfferStatus.Cancelled, ct);
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
    /// <see cref="CompleteChoreRequest.CompletedByUserId"/> (completing on someone's behalf).
    /// <see cref="CompleteChoreRequest.CompletedAt"/> backdates it for a chore done earlier and logged late.</summary>
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

        // A paired chore is done together, so one tap credits the completer's partner too (whoever
        // holds the turn with them): same points, same completion row.
        var partner = chore.CreditedPartner(userId, chore.CurrentAssigneeId, chore.CurrentPartner()?.Id);

        var now = DateTimeOffset.UtcNow;

        // Backdating: the chore was done earlier but only logged now. The given instant becomes the
        // completion's timestamp (so on-time/late, streaks and completion-anchored scheduling see
        // when it really happened), while `now` stays the wall clock for everything else.
        var completedAt = now;
        if (req.CompletedAt is { } backdated)
        {
            // A minute of slack for a client clock that runs slightly ahead of the server.
            if (backdated > now.AddMinutes(1))
                return Result.Fail<ChoreDto>(Error.Validation("A chore can't be completed in the future."));
            completedAt = backdated > now ? now : backdated.ToUniversalTime();

            // The chore's log (for a track chore: that person's own entries and expired slots).
            // SQLite can't compare DateTimeOffset in SQL, so the time checks below run client-side.
            var logged = await _db.ChoreCompletions
                .Where(x => x.ChoreId == chore.Id
                    && (track == null || x.CompletedByUserId == userId || (x.IsExpired && x.PreviousAssigneeId == userId)))
                .ToListAsync(ct);

            // The time may fall inside an occurrence that has since auto-expired: the next expiry
            // after it, with no entry of another occurrence in between (which would mean a different
            // occurrence was the open one back then). That missed slot is what was really done, so
            // fill it in rather than completing the current occurrence.
            var expired = logged
                .Where(x => x.IsExpired && x.CompletedAt >= completedAt)
                .OrderBy(x => x.CompletedAt).ThenBy(x => x.OccurrenceDueAt)
                .FirstOrDefault();
            if (expired is not null && !logged.Any(x => x.OccurrenceDueAt != expired.OccurrenceDueAt
                    && x.CompletedAt > completedAt && x.CompletedAt < expired.CompletedAt))
            {
                FillExpired(chore, expired, user, completedAt);
                if (!string.IsNullOrWhiteSpace(req.Notes))
                    expired.Notes = req.Notes.Trim();
                await _db.SaveChangesAsync(ct);

                var earned = await _achievements.EvaluateForUserAsync(userId, now, ct);
                if (expired.PartnerUserId is { } partnerId)
                    await _achievements.EvaluateForUserAsync(partnerId, now, ct);

                // The schedule, assignee and any live offer belong to the current occurrence, which
                // this didn't touch.
                var filled = await GetAsync(chore.Id, ct: ct);
                if (!filled.Succeeded)
                    return filled;
                return Result.Success(filled.Value! with
                {
                    FilledExpiredDueAt = expired.OccurrenceDueAt ?? expired.CompletedAt,
                    UnlockedAchievements = userId == actingUserId ? [.. earned] : [],
                });
            }

            // Otherwise never before the latest entry already logged, so history stays in order and
            // "last completion" keeps pointing at the newest row.
            if (logged.Any(x => x.CompletedAt > completedAt))
                return Result.Fail<ChoreDto>(Error.Validation("That's earlier than this chore's last logged entry. Pick a later time."));
        }

        var completion = new ChoreCompletion
        {
            ChoreId = chore.Id,
            CompletedByUserId = userId,
            PartnerUserId = partner?.Id,
            CompletedAt = completedAt,
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            PointsAwarded = chore.Points,
            OccurrenceDueAt = track?.DueAt ?? chore.DueAt,
            PreviousAssigneeId = IsIndependent(chore) ? null : chore.CurrentAssigneeId,
            PreviousPartnerId = chore.HeldPartnerId()
        };
        _db.ChoreCompletions.Add(completion);

        AwardPoints(chore, completion, user, partner);

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
            // A new occurrence only hands over once it crosses the chore's rotation period, so a
            // monthly-rotating weekly chore stays with the same person until the month turns.
            // An on-demand chore has no occurrences, so every completion is a turn and hands over.
            var advancedToNewOccurrence = await AdvanceScheduleAsync(chore, completion, now, ct);
            var rotates = advancedToNewOccurrence
                ? CrossedRotationBoundary(chore, completion.OccurrenceDueAt ?? now)
                : chore.RepeatType == RepeatType.OnDemand
                  || (chore.RotateOnEachCompletion && chore.DueAt is not null);
            if (rotates)
                await RotateAssigneeAsync(chore, now, completion.CompletedByUserId!.Value, completion.PartnerUserId, completion.Id, ct);

            // The occurrence someone was being offered is done, so nobody can claim it any more —
            // void the offer and give the bounty back.
            await VoidOffersAsync(chore, OfferStatus.Cancelled, ct);
        }

        await _db.SaveChangesAsync(ct);

        // A completion is the main thing that can unlock an achievement — evaluate the credited user.
        var unlocked = await _achievements.EvaluateForUserAsync(userId, now, ct);
        // The partner earned the same completion; their badges just aren't this session's to celebrate.
        if (partner is not null)
            await _achievements.EvaluateForUserAsync(partner.Id, now, ct);

        var result = await GetAsync(chore.Id, ct: ct);
        // Hand any freshly-unlocked badges back to the client to celebrate, but only on the earner's own
        // session — an admin completing on someone else's behalf shouldn't get the other user's popup.
        if (result.Succeeded && unlocked.Count > 0 && userId == actingUserId)
            return Result.Success(result.Value! with { UnlockedAchievements = [.. unlocked] });
        return result;
    }

    /// <summary>Skips the current occurrence of a recurring chore: advances the schedule to the
    /// next due date without awarding points or rotating the assignee (except across a calendar
    /// <see cref="RotationPeriod"/> boundary, which hands over however the occurrence closed). Logged as a points-less
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
        if (chore.RepeatType == RepeatType.OnDemand)
            return Result.Fail<ChoreDto>(Error.Validation("On-demand chores have nothing scheduled to skip."));

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
            PreviousAssigneeId = IsIndependent(chore) ? null : chore.CurrentAssigneeId,
            PreviousPartnerId = chore.HeldPartnerId()
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
            var advancedToNewOccurrence = await AdvanceScheduleAsync(chore, skip, now, ct);
            // The one exception to "a skip never rotates": a calendar rotation period hands over on
            // its boundary no matter how the last occurrence closed, otherwise skipping the final
            // occurrence of the period would leave the same person holding the whole next one too.
            if (advancedToNewOccurrence
                && chore.RotationPeriod != RotationPeriod.EveryOccurrence
                && CrossedRotationBoundary(chore, skip.OccurrenceDueAt ?? now))
                await RotateAssigneeAsync(chore, now, chore.CurrentAssigneeId ?? skippedUserId, chore.CurrentPartner()?.Id, skip.Id, ct);
            // Same as a completion: the offered occurrence is gone, so refund and void.
            await VoidOffersAsync(chore, OfferStatus.Cancelled, ct);
        }

        await _db.SaveChangesAsync(ct);
        return await GetAsync(chore.Id, ct: ct);
    }

    /// <summary>Admin-only: moves the current occurrence of a chore to another assignee immediately.
    /// The strategy still drives future rotations; this just sets the current occupant and logs the
    /// assignment, keeping <see cref="AssignmentStrategy.LeastAssigned"/> honest. Members don't get a
    /// direct move — they put the chore up for someone to take via <see cref="OfferAsync"/>.</summary>
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

        if (actor.Role != UserRole.Admin)
            return Result.Fail<ChoreDto>(Error.Forbidden("Only admins can reassign a chore directly. Offer it instead and let someone claim it."));

        if (req.PartnerId is { } partnerId && !chore.Pairs.Any(p => p.Is(req.AssigneeId, partnerId)))
            return Result.Fail<ChoreDto>(Error.Validation("Those two aren't a pair on this chore."));

        // Already theirs — nothing to do. Picking the holder's partner is the same pair, so likewise,
        // unless a different pair of theirs was asked for by name.
        var holders = chore.CurrentHolderIds().ToList();
        if (holders.Contains(req.AssigneeId) && (req.PartnerId is not { } wanted || holders.Contains(wanted)))
            return await GetAsync(chore.Id, ct: ct);

        // Void any live offer with a refund — the chore is being placed by an admin, so nobody is
        // going to claim it.
        await VoidOffersAsync(chore, OfferStatus.Cancelled, ct);

        var previousAssigneeId = chore.CurrentAssigneeId;
        chore.CurrentAssigneeId = req.AssigneeId;
        chore.CurrentPartnerId = req.PartnerId;
        chore.CurrentPartnerId = chore.HeldPartnerId(); // no pair named: their first one
        _db.ChoreAssignments.Add(new ChoreAssignment
        {
            ChoreId = chore.Id,
            UserId = req.AssigneeId,
            PartnerUserId = chore.CurrentPartnerId,
            PreviousAssigneeId = previousAssigneeId,
            AssignedByUserId = userId,
        });
        await _db.SaveChangesAsync(ct);
        return await GetAsync(chore.Id, ct: ct);
    }

    /// <summary>Puts the current occurrence of a chore up for someone else to take, optionally with a
    /// points bounty. Offers are always open: every other assignee is notified and the first to claim
    /// it gets it. The chore doesn't move until someone claims it.
    ///
    /// <para>The bounty is deducted from the offerer here and held until the offer resolves, so a claim
    /// can always be paid. A newer offer supersedes any live one (refunded).</para></summary>
    public async Task<Result<ChoreDto>> OfferAsync(Guid id, Guid userId, OfferChoreRequest req, CancellationToken ct = default)
    {
        var chore = await _db.Chores
            .Include(c => c.Assignees)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (chore is null)
            return Result.Fail<ChoreDto>(Error.NotFound("Chore not found."));

        if (IsIndependent(chore))
            return Result.Fail<ChoreDto>(Error.Validation("This chore is shared per person, so there's no single assignee to hand over."));
        // An offer is one person handing over their own turn. A pair's turn isn't one member's to
        // give away, and a claim would commit the claimer's partner without asking them.
        if (chore.Pairs.Count > 0)
            return Result.Fail<ChoreDto>(Error.Validation("This chore is done in pairs, so it can't be offered. Ask an admin to reassign it."));
        if (chore.IsFrozen)
            return Result.Fail<ChoreDto>(Error.Validation("This chore is paused and cannot be offered."));
        if (chore.CurrentAssigneeId != userId)
            return Result.Fail<ChoreDto>(Error.Forbidden("You can only offer a chore that's assigned to you."));

        var actor = await _db.Users.FindAsync([userId], ct);
        if (actor is null)
            return Result.Fail<ChoreDto>(Error.NotFound("User not found."));

        if (req.BountyPoints < 0)
            return Result.Fail<ChoreDto>(Error.Validation("A bounty can't be negative."));
        // The escrow below would otherwise put the offerer in the red.
        if (req.BountyPoints > actor.Points)
            return Result.Fail<ChoreDto>(Error.Validation($"You only have {actor.Points} points to offer."));

        // Frozen users are out of rotation, so they're not eligible to pick anything up either.
        var eligible = chore.Assignees.Where(a => a.Id != userId && !a.IsFrozen).ToList();
        if (eligible.Count == 0)
            return Result.Fail<ChoreDto>(Error.Validation("There's nobody else assigned to this chore to take it."));

        // Supersede any live offer (refunding its bounty) before escrowing the new one.
        await VoidOffersAsync(chore, OfferStatus.Cancelled, ct);

        _db.ChoreOffers.Add(new ChoreOffer
        {
            ChoreId = chore.Id,
            FromUserId = userId,
            BountyPoints = req.BountyPoints,
        });

        if (req.BountyPoints > 0)
        {
            actor.Points -= req.BountyPoints;
            _db.PointsLog.Add(new PointsLogEntry
            {
                UserId = userId,
                Delta = -req.BountyPoints,
                Type = PointsLogType.Bounty,
                Description = $"Bounty offered on {chore.Name}",
            });
        }

        var title = NotificationTitle(chore);
        var reward = req.BountyPoints > 0 ? $" for {req.BountyPoints} points" : "";
        // One fan-out call, so the routing context is loaded once for the whole eligible set
        // rather than once per recipient.
        await _notifications.NotifyUsersAsync(eligible.Select(u => u.Id).ToList(), title,
            $"{actor.DisplayName} put this chore up for grabs{reward}. Claim it before someone else does.",
            chore.Id, NotificationCategory.OfferReceived, ct);

        await _db.SaveChangesAsync(ct);
        return await GetAsync(chore.Id, viewerId: userId, ct: ct);
    }

    /// <summary>Takes an offered chore: the chore moves to the claimer, the assignment is logged, and
    /// the escrowed bounty is paid out to them. Offers are first come, first served — the status
    /// flip is a conditional update inside a transaction, so a simultaneous second claim loses cleanly
    /// with a conflict rather than both claimers being credited.</summary>
    public async Task<Result<ChoreDto>> ClaimOfferAsync(Guid choreId, Guid userId, CancellationToken ct = default)
    {
        var offer = await _db.ChoreOffers
            .FirstOrDefaultAsync(o => o.ChoreId == choreId && o.Status == OfferStatus.Pending, ct);
        if (offer is null)
            return Result.Fail<ChoreDto>(Error.NotFound("This chore isn't up for grabs."));

        var chore = await _db.Chores
            .Include(c => c.Assignees)
            .FirstOrDefaultAsync(c => c.Id == choreId, ct);
        if (chore is null)
            return Result.Fail<ChoreDto>(Error.NotFound("Chore not found."));

        if (offer.FromUserId == userId)
            return Result.Fail<ChoreDto>(Error.Validation("You can't claim your own offer."));
        if (chore.Assignees.All(a => a.Id != userId))
            return Result.Fail<ChoreDto>(Error.Forbidden("Only someone assigned to this chore can claim it."));

        var claimer = await _db.Users.FindAsync([userId], ct);
        if (claimer is null)
            return Result.Fail<ChoreDto>(Error.NotFound("User not found."));

        var now = DateTimeOffset.UtcNow;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Compare-and-set on Pending: whoever's UPDATE lands first owns the claim.
        var claimed = await _db.ChoreOffers
            .Where(o => o.Id == offer.Id && o.Status == OfferStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OfferStatus.Claimed)
                .SetProperty(o => o.ClaimedByUserId, userId)
                .SetProperty(o => o.RespondedAt, now), ct);
        if (claimed == 0)
        {
            await tx.RollbackAsync(ct);
            return Result.Fail<ChoreDto>(Error.Conflict("Someone else just claimed this chore."));
        }

        // ExecuteUpdate writes straight to the database, so the tracked copy still says Pending.
        // Pull the new state back in rather than leaving a stale entity in the context.
        await _db.Entry(offer).ReloadAsync(ct);

        var previousAssigneeId = chore.CurrentAssigneeId;
        chore.CurrentAssigneeId = userId;
        _db.ChoreAssignments.Add(new ChoreAssignment
        {
            ChoreId = chore.Id,
            UserId = userId,
            PreviousAssigneeId = previousAssigneeId,
            AssignedByUserId = userId,
        });

        if (offer.BountyPoints > 0)
        {
            claimer.Points += offer.BountyPoints;
            _db.PointsLog.Add(new PointsLogEntry
            {
                UserId = userId,
                Delta = offer.BountyPoints,
                Type = PointsLogType.Bounty,
                Description = $"Bounty claimed on {chore.Name}",
            });
        }

        var reward = offer.BountyPoints > 0 ? $" and took your {offer.BountyPoints} points" : "";
        await _notifications.NotifyUserAsync(offer.FromUserId, NotificationTitle(chore),
            $"{claimer.DisplayName} claimed this chore{reward}. It's off your plate.", chore.Id,
            NotificationCategory.OfferResponse, ct);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(chore.Id, viewerId: userId, ct: ct);
    }

    /// <summary>Pulls a live offer back. Only the offerer (or an admin) can; the bounty is refunded.
    /// Silent, rather than notifying everyone twice.</summary>
    public async Task<Result<ChoreDto>> WithdrawOfferAsync(Guid choreId, Guid userId, CancellationToken ct = default)
    {
        var offer = await _db.ChoreOffers
            .Include(o => o.Chore)
            .FirstOrDefaultAsync(o => o.ChoreId == choreId && o.Status == OfferStatus.Pending, ct);
        if (offer is null)
            return Result.Fail<ChoreDto>(Error.NotFound("There's no live offer on this chore."));

        var chore = offer.Chore;
        if (chore is null)
            return Result.Fail<ChoreDto>(Error.NotFound("Chore not found."));

        var actor = await _db.Users.FindAsync([userId], ct);
        if (actor is null)
            return Result.Fail<ChoreDto>(Error.NotFound("User not found."));
        if (offer.FromUserId != userId && actor.Role != UserRole.Admin)
            return Result.Fail<ChoreDto>(Error.Forbidden("Only the person who offered this chore can withdraw it."));

        offer.Status = OfferStatus.Withdrawn;
        offer.RespondedAt = DateTimeOffset.UtcNow;
        await RefundOfferAsync(offer, chore.Name, ct);

        await _db.SaveChangesAsync(ct);
        return await GetAsync(chore.Id, viewerId: userId, ct: ct);
    }

    /// <summary>Voids every live offer on a chore (refunding the escrowed bounty) because the chore has
    /// moved on without a claim — superseded by a newer offer, placed by an admin, rotated away on
    /// freeze, completed or skipped by the offerer, or auto-advanced past its occurrence. Does not save;
    /// the caller commits.</summary>
    private async Task VoidOffersAsync(Chore chore, OfferStatus status, CancellationToken ct)
    {
        var live = await _db.ChoreOffers
            .Where(o => o.ChoreId == chore.Id && o.Status == OfferStatus.Pending)
            .ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var offer in live)
        {
            offer.Status = status;
            offer.RespondedAt = now;
            await RefundOfferAsync(offer, chore.Name, ct);
        }
    }

    /// <summary>Returns an unclaimed offer's escrowed bounty to the offerer. No-op for a bounty-less
    /// offer. Does not save; the caller commits.</summary>
    private async Task RefundOfferAsync(ChoreOffer offer, string choreName, CancellationToken ct)
    {
        if (offer.BountyPoints <= 0)
            return;

        var from = await _db.Users.FindAsync([offer.FromUserId], ct);
        if (from is null)
            return;

        from.Points += offer.BountyPoints;
        _db.PointsLog.Add(new PointsLogEntry
        {
            UserId = offer.FromUserId,
            Delta = offer.BountyPoints,
            Type = PointsLogType.Bounty,
            Description = $"Bounty refunded on {choreName}",
        });
    }

    private static string NotificationTitle(Chore chore) =>
        string.IsNullOrWhiteSpace(chore.Emoji) ? chore.Name : $"{chore.Emoji} {chore.Name}";

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

        // Only the people credited with it (the completer, or their partner on a paired chore), or
        // an admin, may undo.
        if (completion.CompletedByUserId != actingUserId && completion.PartnerUserId != actingUserId
            && actor.Role != UserRole.Admin)
            return Result.Fail(Error.Forbidden("You can only undo your own completions."));

        await ReversePointsAsync(completionId, ct);

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
                chore.CurrentPartnerId = completion.PreviousPartnerId;
            }
        }
        var rotationLog = await _db.ChoreAssignments.Where(a => a.ChoreCompletionId == completionId).ToListAsync(ct);
        _db.ChoreAssignments.RemoveRange(rotationLog);

        _db.ChoreCompletions.Remove(completion);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Takes back the points a completion awarded: removes its log entries and decrements
    /// each credited user's balance. A paired completion has one entry per partner. Does not save.</summary>
    private async Task ReversePointsAsync(Guid completionId, CancellationToken ct)
    {
        var entries = await _db.PointsLog.Where(e => e.ChoreCompletionId == completionId).ToListAsync(ct);
        foreach (var entry in entries)
        {
            var credited = await _db.Users.FindAsync([entry.UserId], ct);
            if (credited is not null)
                credited.Points -= entry.Delta;
        }
        _db.PointsLog.RemoveRange(entries);
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
        await ReversePointsAsync(completionId, ct);

        var rotationLog = await _db.ChoreAssignments.Where(a => a.ChoreCompletionId == completionId).ToListAsync(ct);
        _db.ChoreAssignments.RemoveRange(rotationLog);

        _db.ChoreCompletions.Remove(completion);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Admin-only correction of a historical activity entry. A completion gets a new
    /// timestamp (e.g. it was logged late but done on time). An auto-expired entry becomes a real
    /// completion: credited to the given user (and their partner on a paired chore), stamped with the
    /// given time, and awarded the chore's current points. Like <see cref="DeleteActivityAsync"/> this
    /// is history cleanup, so the chore's current schedule and assignee are left alone.</summary>
    public async Task<Result> UpdateActivityAsync(Guid completionId, Guid actingUserId, UpdateActivityRequest req, CancellationToken ct = default)
    {
        var actor = await _db.Users.FindAsync([actingUserId], ct);
        if (actor is null)
            return Result.Fail(Error.NotFound("User not found."));
        if (actor.Role != UserRole.Admin)
            return Result.Fail(Error.Forbidden("Only admins can edit activity entries."));

        var completion = await _db.ChoreCompletions.FirstOrDefaultAsync(c => c.Id == completionId, ct);
        if (completion is null)
            return Result.Fail(Error.NotFound("Activity entry not found."));
        if (completion.IsSkip)
            return Result.Fail(Error.Validation("A skipped entry has no completion time to change."));

        var chore = await _db.Chores
            .Include(c => c.Assignees)
            .FirstOrDefaultAsync(c => c.Id == completion.ChoreId, ct);
        if (chore is null)
            return Result.Fail(Error.NotFound("Chore not found."));

        var now = DateTimeOffset.UtcNow;
        // Same minute of client-clock slack as a backdated completion.
        if (req.CompletedAt > now.AddMinutes(1))
            return Result.Fail(Error.Validation("A chore can't be completed in the future."));
        var completedAt = req.CompletedAt > now ? now : req.CompletedAt.ToUniversalTime();

        // On a track chore an expired slot belongs to one person's schedule (snapshotted in
        // PreviousAssigneeId), so it can only be credited to them.
        var trackOwnerId = IsIndependent(chore) ? completion.CompletedByUserId ?? completion.PreviousAssigneeId : null;

        User? user = null;
        if (completion.IsExpired)
        {
            if ((req.CompletedByUserId ?? trackOwnerId) is not { } userId)
                return Result.Fail(Error.Validation("Choose who completed it."));
            if (trackOwnerId is not null && userId != trackOwnerId)
                return Result.Fail(Error.Validation("This chore is shared per person, so an expired entry can only be credited to the person who missed it."));
            user = await _db.Users.FindAsync([userId], ct);
            if (user is null)
                return Result.Fail(Error.NotFound("User not found."));
        }

        // The entry may move freely within its own occurrence, but not past an entry of another one
        // (for a track chore: that person's own): history stays in order and "last completion" keeps
        // pointing at the newest occurrence. SQLite can't compare DateTimeOffset in SQL, so check
        // client-side.
        var others = await _db.ChoreCompletions
            .Where(x => x.ChoreId == chore.Id && x.Id != completion.Id)
            .Select(x => new { x.CompletedAt, x.OccurrenceDueAt, x.CompletedByUserId, x.PreviousAssigneeId })
            .ToListAsync(ct);
        var crossesAnother = others
            .Where(x => x.OccurrenceDueAt != completion.OccurrenceDueAt)
            .Where(x => trackOwnerId is null || (x.CompletedByUserId ?? x.PreviousAssigneeId) == trackOwnerId)
            .Any(x => x.CompletedAt < completion.CompletedAt ? x.CompletedAt > completedAt
                : x.CompletedAt > completion.CompletedAt && x.CompletedAt < completedAt);
        if (crossesAnother)
            return Result.Fail(Error.Validation("That time would move this entry past another one in this chore's log. Pick a time closer to the original."));

        if (user is not null)
            FillExpired(chore, completion, user, completedAt);
        else
            completion.CompletedAt = completedAt;

        await _db.SaveChangesAsync(ct);

        // A new completion, or one that now counts as on time, can unlock a badge.
        foreach (var credited in new[] { completion.CompletedByUserId, completion.PartnerUserId })
            if (credited is { } creditedId)
                await _achievements.EvaluateForUserAsync(creditedId, now, ct);

        return Result.Success();
    }

    /// <summary>Turns an auto-expired row into a real completion by <paramref name="user"/> (and their
    /// partner on a paired chore) at <paramref name="completedAt"/>, awarding the chore's current
    /// points. The row keeps its occurrence, so nothing about the schedule moves. Does not save.</summary>
    private void FillExpired(Chore chore, ChoreCompletion expired, User user, DateTimeOffset completedAt)
    {
        // One of the two who missed it shares it with the other; anyone else brings their own partner.
        var partner = chore.CreditedPartner(user.Id, expired.PreviousAssigneeId, expired.PartnerUserId);
        expired.IsExpired = false;
        expired.CompletedByUserId = user.Id;
        expired.PartnerUserId = partner?.Id;
        expired.PointsAwarded = chore.Points;
        expired.CompletedAt = completedAt;
        // Completion rows on a track chore carry no assignee snapshot (nothing rotates).
        if (IsIndependent(chore))
            expired.PreviousAssigneeId = null;
        AwardPoints(chore, expired, user, partner);
    }

    /// <summary>Credits a completion's points to the completer and, on a paired chore, their partner:
    /// one <see cref="PointsLogEntry"/> per person, linked to the same completion. Does not save.</summary>
    private void AwardPoints(Chore chore, ChoreCompletion completion, User user, User? partner)
    {
        if (chore.Points == 0)
            return;

        foreach (var credited in partner is null ? new[] { user } : [user, partner])
        {
            _db.PointsLog.Add(new PointsLogEntry
            {
                UserId = credited.Id,
                Delta = chore.Points,
                Type = PointsLogType.Completion,
                ChoreCompletionId = completion.Id,
                Description = chore.Name
            });
            credited.Points += chore.Points;
        }
    }

    public async Task<List<ChoreHistoryEntryDto>> GetHistoryAsync(
        string? tag, Guid? userId, Guid? choreId, bool includeReassignments = false,
        CancellationToken ct = default)
    {
        var completionQuery = _db.ChoreCompletions
            .Include(c => c.Chore)
            .Include(c => c.CompletedBy)
            .Include(c => c.Partner)
            .AsQueryable();

        if (choreId.HasValue)
            completionQuery = completionQuery.Where(c => c.ChoreId == choreId);
        if (userId.HasValue)
            // Include expired rows where this user was the responsible assignee at expiry time, and
            // anything they share as the partner on a paired chore.
            completionQuery = completionQuery.Where(c =>
                c.CompletedByUserId == userId ||
                c.PartnerUserId == userId ||
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

        // An all-day chore is on time until the household's day start, which decides what's a rescue.
        var lateGrace = await AppSettingsService.ReadLateGraceByChoreAsync(_db, ct);

        var entries = completions.Select(c =>
            ChoreHistoryEntryDto.FromCompletion(c,
                c.IsExpired && c.PreviousAssigneeId is { } pid && expiredAssignees.TryGetValue(pid, out var u) ? u : null,
                lateGrace.GetValueOrDefault(c.ChoreId)));

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

    /// <summary>Where a chore's schedule starts: its first occurrence on or after the start date, or
    /// nothing at all for an on-demand chore, which is never due.</summary>
    private static DateTimeOffset? InitialDue(Chore chore) =>
        chore.RepeatType == RepeatType.OnDemand
            ? null
            : RecurrenceCalculator.FirstOccurrence(RecurrenceRule.FromChore(chore), chore.StartDate);

    /// <summary>The chore's <see cref="Chore.LateGrace"/> under the configured day start. Subtracting
    /// it from an instant gives the "household clock" an all-day chore is judged on: 01:00 with a
    /// 05:00 day start still belongs to the previous day. Zero (and no query) for a timed chore.</summary>
    private async Task<TimeSpan> LateGraceAsync(Chore chore, CancellationToken ct) =>
        chore.HasDueTime ? TimeSpan.Zero : await AppSettingsService.ReadDayStartAsync(_db, ct);

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
        // The entry's own timestamp, not `now`: a backdated completion anchors completion-based
        // scheduling on when the chore was really done. Both run on the household clock, so an all-day
        // chore finished after midnight but before the day start still counts as done that day.
        var lateGrace = await LateGraceAsync(chore, ct);
        chore.DueAt = RecurrenceCalculator.NextDue(rule, chore.SchedulingPreference, scheduledDue,
            completion.CompletedAt - lateGrace, now - lateGrace, grace);
        return chore.DueAt is not null;
    }

    /// <summary>Whether the occurrence that just closed (due <paramref name="closedDue"/>) and the one
    /// <c>chore.DueAt</c> now points at sit on opposite sides of the chore's <see cref="RotationPeriod"/>
    /// boundary. Always true for <see cref="RotationPeriod.EveryOccurrence"/>; false when nothing is
    /// scheduled next.</summary>
    private static bool CrossedRotationBoundary(Chore chore, DateTimeOffset closedDue) =>
        chore.DueAt is { } nextDue && RotationBoundary.Crossed(chore.RotationPeriod, closedDue, nextDue);

    /// <summary>Whoever takes a turn together: a fixed pair, or one person alone.</summary>
    private readonly record struct RotationUnit(Guid Lead, Guid? Partner)
    {
        public bool Has(Guid userId) => Lead == userId || Partner == userId;
    }

    /// <summary>One logged turn (an assignment or a completion) and who shared it, for the
    /// rotation counts.</summary>
    private readonly record struct Turn(Guid UserId, Guid? PartnerId, DateTimeOffset At);

    /// <summary>Picks who takes the chore next per its strategy and records the assignment.
    /// <paramref name="completedBy"/> (with <paramref name="completedWith"/>, their partner) is folded
    /// in as a virtual completion so strategies that count completions (LeastCompleted) treat the
    /// just-closed occurrence as their turn.
    /// <paramref name="completionId"/> links the new <see cref="ChoreAssignment"/> to its trigger row
    /// (so undo can reverse the rotation), or null for auto-advance expiry rows.</summary>
    private async Task RotateAssigneeAsync(Chore chore, DateTimeOffset now, Guid completedBy, Guid? completedWith, Guid? completionId, CancellationToken ct)
    {
        // Manual: the handover is an admin's call, so the new occurrence opens with nobody on it.
        // No ChoreAssignment row — undo restores the assignee from the completion's snapshot.
        if (chore.AssignmentStrategy == AssignmentStrategy.Manual)
        {
            chore.CurrentAssigneeId = null;
            chore.CurrentPartnerId = null;
            return;
        }

        var ordered = chore.Assignees
            .Where(u => !u.IsFrozen)
            .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
            .Select(u => u.Id)
            .ToList();
        if (ordered.Count == 0) return;

        var (assignments, completions) = await RotationHistoryAsync(chore.Id, ct);
        var next = PickNext(chore, ordered, assignments, completions, completedBy, completedWith, now);

        chore.CurrentAssigneeId = next.Lead;
        chore.CurrentPartnerId = next.Partner;
        _db.ChoreAssignments.Add(new ChoreAssignment
        {
            ChoreId = chore.Id,
            UserId = next.Lead,
            PartnerUserId = next.Partner,
            AssignedAt = now,
            ChoreCompletionId = completionId
        });
    }

    /// <summary>A chore's past assignments and real completions as <see cref="Turn"/>s. Rows are
    /// pulled to memory and aggregated client-side: SQLite stores DateTimeOffset as TEXT, so a DB-side
    /// Max() can't be trusted across varying offsets (same reason we don't ORDER BY it).</summary>
    private async Task<(List<Turn> Assignments, List<Turn> Completions)> RotationHistoryAsync(Guid choreId, CancellationToken ct)
    {
        var assignments = (await _db.ChoreAssignments
            .Where(a => a.ChoreId == choreId)
            .Select(a => new { a.UserId, a.PartnerUserId, a.AssignedAt })
            .ToListAsync(ct))
            .Select(a => new Turn(a.UserId, a.PartnerUserId, a.AssignedAt))
            .ToList();
        var completions = (await _db.ChoreCompletions
            .Where(x => x.ChoreId == choreId && !x.IsSkip && !x.IsExpired)
            .Select(x => new { x.CompletedByUserId, x.PartnerUserId, x.CompletedAt })
            .ToListAsync(ct))
            .Where(x => x.CompletedByUserId.HasValue)
            .Select(x => new Turn(x.CompletedByUserId!.Value, x.PartnerUserId, x.CompletedAt))
            .ToList();
        return (assignments, completions);
    }

    /// <summary>Runs the chore's assignment strategy against in-memory history to pick who the next
    /// occurrence would rotate to, assuming <paramref name="completedBy"/> (with
    /// <paramref name="completedWith"/>) completes the current occurrence at
    /// <paramref name="completedAt"/>. Shared by the live rotation (<see cref="RotateAssigneeAsync"/>),
    /// the DTO's "next assignee" preview and freeze reassignment, so all three agree.</summary>
    private static RotationUnit PickNext(
        Chore chore,
        IReadOnlyList<Guid> ordered,
        IReadOnlyList<Turn> assignments,
        IReadOnlyList<Turn> completions,
        Guid completedBy,
        Guid? completedWith,
        DateTimeOffset completedAt)
    {
        // A pair rotates as one: the picker chooses between units, so it never has to know pairs exist.
        var units = RotationUnits(chore, ordered);

        // The unit a logged turn counts for: the exact one when it's still in the rotation, otherwise
        // the person's first (rows from before pairs could overlap carry no partner; a pair may have
        // been edited away since). Null when they're not in the pool at all.
        RotationUnit? UnitOf(Guid userId, Guid? partnerId)
        {
            foreach (var u in units)
                if (u.Has(userId) && (partnerId is { } p ? u.Has(p) : u.Partner is null)) return u;
            foreach (var u in units)
                if (u.Has(userId)) return u;
            return null;
        }

        var assigned = assignments
            .Select(a => (Unit: UnitOf(a.UserId, a.PartnerId), a.At))
            .Where(a => a.Unit is not null)
            .GroupBy(a => a.Unit!.Value)
            .ToList();
        var completed = completions
            .Select(x => (Unit: UnitOf(x.UserId, x.PartnerId), x.At))
            .Where(x => x.Unit is not null)
            .GroupBy(x => x.Unit!.Value)
            .ToList();

        var assignedCounts = assigned.ToDictionary(g => g.Key, g => g.Count());
        var lastAssignedAt = assigned.ToDictionary(g => g.Key, g => g.Max(a => a.At));
        var completedCounts = completed.ToDictionary(g => g.Key, g => g.Count());
        var lastCompletedAt = completed.ToDictionary(g => g.Key, g => g.Max(x => x.At));
        if (UnitOf(completedBy, completedWith) is { } done)
        {
            completedCounts[done] = completedCounts.GetValueOrDefault(done) + 1;
            if (completedAt > lastCompletedAt.GetValueOrDefault(done, DateTimeOffset.MinValue))
                lastCompletedAt[done] = completedAt;
        }

        return AssignmentPicker.Pick(
            chore.AssignmentStrategy, units,
            chore.CurrentAssigneeId is { } current ? UnitOf(current, chore.CurrentPartner()?.Id) : null,
            assignedCounts, completedCounts, lastAssignedAt, lastCompletedAt, Random.Shared);
    }

    /// <summary>Everything that takes a turn on the chore, in a stable order: each live pair whose two
    /// members are both in <paramref name="ordered"/> (the eligible pool), plus everyone in the pool
    /// who is in no such pair, alone. So a person in three pairs gets three turns, and a pair with a
    /// frozen member drops out (leaving the other alone only if they have no other pair). Just the
    /// pool itself on an unpaired chore.</summary>
    private static List<RotationUnit> RotationUnits(Chore chore, IReadOnlyList<Guid> ordered)
    {
        var position = ordered.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var units = chore.LivePairs()
            .Where(p => position.ContainsKey(p.First) && position.ContainsKey(p.Second))
            .Select(p => position[p.First] < position[p.Second]
                ? new RotationUnit(p.First, p.Second)
                : new RotationUnit(p.Second, p.First))
            .ToList();
        var alone = ordered.Where(id => !units.Any(u => u.Has(id))).Select(id => new RotationUnit(id, null)).ToList();
        return units.Concat(alone)
            .OrderBy(u => position[u.Lead])
            .ThenBy(u => u.Partner is { } p ? position[p] : -1)
            .ToList();
    }

    /// <summary>Who the chore would rotate to on its next completion — shown as a preview.
    /// Only meaningful for strategies whose outcome is fixed by current state; the random strategies
    /// (and chores that can't rotate: one-time, nothing scheduled, single assignee) return null.
    /// On-demand chores never have anything scheduled but rotate on every completion, so they predict.
    /// Assumes the current holder is the one who completes it (the normal path).</summary>
    private static (User Assignee, User? Partner)? PredictNextAssignee(
        Chore chore, List<ChoreCompletion>? completions, IReadOnlyList<Turn> assignments, DateTimeOffset now)
    {
        if (chore.RepeatType == RepeatType.OneTime) return null;
        if (chore.DueAt is null && chore.RepeatType != RepeatType.OnDemand) return null;
        if (IsIndependent(chore)) return null; // track-mode chores don't rotate
        if (chore.AssignmentStrategy is AssignmentStrategy.Random or AssignmentStrategy.RandomExceptLastAssigned) return null;
        if (chore.AssignmentStrategy == AssignmentStrategy.Manual) return null; // an admin picks, nothing to predict
        if (chore.CurrentAssigneeId is not { } current) return null;

        var orderedUsers = chore.Assignees.Where(u => !u.IsFrozen).OrderBy(u => u.CreatedAt).ThenBy(u => u.Id).ToList();
        var orderedIds = orderedUsers.Select(u => u.Id).ToList();
        // Fewer than two units (a lone assignee, or a single pair) means there's nobody to rotate to.
        if (RotationUnits(chore, orderedIds).Count < 2) return null;

        var turns = (completions ?? [])
            .Where(x => !x.IsSkip && !x.IsExpired && x.CompletedByUserId.HasValue)
            .Select(x => new Turn(x.CompletedByUserId!.Value, x.PartnerUserId, x.CompletedAt))
            .ToList();

        var next = PickNext(chore, orderedIds, assignments, turns, current, chore.CurrentPartner()?.Id, now);
        var assignee = orderedUsers.FirstOrDefault(u => u.Id == next.Lead);
        return assignee is null ? null : (assignee, orderedUsers.FirstOrDefault(u => u.Id == next.Partner));
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
            .Include(x => x.Partner)
            .Where(x => choreIds.Contains(x.ChoreId))
            .ToListAsync(ct);

        return completions
            .GroupBy(x => x.ChoreId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CompletedAt).ToList());
    }

    private static ChoreDto ToDto(Chore chore, List<ChoreCompletion>? completions,
        IReadOnlyList<Turn> assignments, DateTimeOffset now, TimeSpan dayStart,
        Guid? viewerId = null, ChoreOffer? offer = null, ChoreMute? mute = null)
    {
        var latest = completions?.FirstOrDefault(c => !c.IsExpired);
        var lateGrace = chore.LateGrace(dayStart);

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
                    StreakCalculator.CurrentStreak(completions?.Where(x => x.CompletedByUserId == t.UserId) ?? [], lateGrace: lateGrace)))
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
                DueLabel = chore.SlotLabel(personalDue),
                OccurrenceProgress = personalProgress,
                IsMuted = mute is not null,
                MutedUntil = mute?.Until
            };
        }

        int? progress = null;
        if (chore.CompletionsRequired > 1)
            // Completions + skips logged against the current occurrence (they share its due date).
            progress = completions?.Count(x => x.OccurrenceDueAt == chore.DueAt) ?? 0;
        var next = PredictNextAssignee(chore, completions, assignments, now);
        var streak = StreakCalculator.CurrentStreak(completions ?? [], lateGrace: lateGrace);
        return ChoreDto.FromEntity(chore, latest, progress, next?.Assignee, currentStreak: streak, openOffer: offer,
            nextPartner: next?.Partner) with
        {
            IsMuted = mute is not null,
            MutedUntil = mute?.Until
        };
    }

    /// <summary>The viewer's own live mute per chore id (expired mutes are excluded, so they stop
    /// applying without anything sweeping for them). Empty when there's no viewer — a mute is personal,
    /// so an unattributed render shows nothing muted.</summary>
    private async Task<Dictionary<Guid, ChoreMute>> MutesByChoreAsync(
        List<Guid> choreIds, Guid? viewerId, DateTimeOffset now, CancellationToken ct)
    {
        if (choreIds.Count == 0 || viewerId is not { } vid)
            return new Dictionary<Guid, ChoreMute>();

        // SQLite can't compare a DateTimeOffset in SQL, so the expiry filter runs client-side.
        var mutes = await _db.ChoreMutes
            .Where(m => m.UserId == vid && choreIds.Contains(m.ChoreId))
            .AsNoTracking()
            .ToListAsync(ct);

        return mutes
            .Where(m => m.Until == null || m.Until > now)
            .ToDictionary(m => m.ChoreId);
    }

    /// <summary>Assignment history per chore id, as lightweight <see cref="Turn"/>s — for the
    /// "next assignee" preview's Least-Assigned counting. SQLite stores DateTimeOffset as TEXT, so
    /// aggregation happens client-side (same reason we don't ORDER BY it).</summary>
    private async Task<Dictionary<Guid, List<Turn>>> AssignmentsByChoreAsync(
        List<Guid> choreIds, CancellationToken ct)
    {
        if (choreIds.Count == 0)
            return new Dictionary<Guid, List<Turn>>();

        var assignments = await _db.ChoreAssignments
            .Where(a => choreIds.Contains(a.ChoreId))
            .Select(a => new { a.ChoreId, a.UserId, a.PartnerUserId, a.AssignedAt })
            .ToListAsync(ct);

        return assignments
            .GroupBy(a => a.ChoreId)
            .ToDictionary(g => g.Key, g => g.Select(a => new Turn(a.UserId, a.PartnerUserId, a.AssignedAt)).ToList());
    }

    /// <summary>The single live <see cref="OfferStatus.Pending"/> offer per chore (if any), with both
    /// participants loaded for the DTO. At most one live offer exists per chore (a newer offer
    /// supersedes the older), so a first-match per chore is safe.</summary>
    private async Task<Dictionary<Guid, ChoreOffer>> OpenOffersByChoreAsync(
        List<Guid> choreIds, CancellationToken ct)
    {
        if (choreIds.Count == 0)
            return new Dictionary<Guid, ChoreOffer>();

        var offers = await _db.ChoreOffers
            .Include(o => o.FromUser)
            .Where(o => choreIds.Contains(o.ChoreId) && o.Status == OfferStatus.Pending)
            .AsNoTracking()
            .ToListAsync(ct);

        return offers
            .GroupBy(o => o.ChoreId)
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
        if (Validators.TimeLabels(req.TimeLabels, out _) is { } labelsError)
            return Result.Fail<List<User>>(labelsError);
        if (!Enum.IsDefined(req.RotationPeriod))
            return Result.Fail<List<User>>(Error.Validation("Unknown rotation period."));

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
            if (req.RepeatType == RepeatType.OnDemand)
                return Result.Fail<List<User>>(Error.Validation("An on-demand chore can't use per-person scheduling."));

            var tracks = req.Tracks ?? [];
            var trackIds = tracks.Select(t => t.UserId).Distinct().ToList();
            if (trackIds.Count != ids.Count || !ids.All(trackIds.Contains))
                return Result.Fail<List<User>>(Error.Validation("Each assignee must have exactly one per-person schedule."));
            if (tracks.Any(t => t.CompletionsRequired < 1 || t.CompletionsRequired > Validators.MaxCompletionsRequired))
                return Result.Fail<List<User>>(Error.Validation($"Each person's number of times must be between 1 and {Validators.MaxCompletionsRequired}."));
        }
        else if (req.AssignmentStrategy == AssignmentStrategy.Manual && req.CurrentAssigneeId is null)
        {
            // Manual chores may sit unassigned, waiting for an admin to place them.
        }
        else if (req.CurrentAssigneeId is not { } current || !ids.Contains(current))
        {
            return Result.Fail<List<User>>(Error.Validation("The current assignee must be one of the assignees."));
        }

        // Track mode has no shared turn to pair people on, so its pairs are dropped rather than checked.
        if (req.AssignmentStrategy != AssignmentStrategy.Independent)
        {
            if (Validators.Pairs(req.Pairs, ids, out var pairs) is { } pairsError)
                return Result.Fail<List<User>>(pairsError);
            if (req.CurrentPartnerId is { } currentPartner
                && !(req.CurrentAssigneeId is { } holder && pairs.Any(p => p.Is(holder, currentPartner))))
                return Result.Fail<List<User>>(Error.Validation("The current partner must be in a pair with the current assignee."));
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
        // An on-demand chore has no schedule, so none of the scheduling settings can mean anything:
        // they're stored as their defaults so nothing stale lingers if it's switched back.
        var onDemand = req.RepeatType == RepeatType.OnDemand;
        chore.SchedulingPreference = onDemand ? SchedulingPreference.FromScheduledDate : req.SchedulingPreference;
        // Grace only rides on Smart scheduling, and only when positive (0/null = pure max).
        chore.GraceMinutes = chore.SchedulingPreference == SchedulingPreference.SmartScheduling && req.GraceMinutes is > 0
            ? req.GraceMinutes
            : null;
        // Auto-advance applies to all recurring non-independent chores (custom included — they always
        // close on a single completion, but can still be advanced/rotated when overdue).
        var supportsAutoAdvance = req.RepeatType is not (RepeatType.OneTime or RepeatType.OnDemand)
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
        // On-demand has no occurrence to count towards.
        chore.CompletionsRequired = isCustom || isIndependent || onDemand ? 1 : Math.Max(1, req.CompletionsRequired);
        // Per-completion rotation only means anything when more than one completion is required, and
        // never applies in track mode (no rotation at all).
        // A rotation period only means something on a chore that rotates and comes due more often than
        // the period itself; everywhere else it's stored as the default so it can't linger unseen.
        var supportsRotationPeriod = !isIndependent
            && req.AssignmentStrategy != AssignmentStrategy.KeepLastAssigned
            && RotationBoundary.AppliesTo(req.RotationPeriod, req.RepeatType);
        chore.RotationPeriod = supportsRotationPeriod ? req.RotationPeriod : RotationPeriod.EveryOccurrence;
        // A calendar rotation period and per-completion rotation contradict each other; the period wins.
        chore.RotateOnEachCompletion = !isIndependent && chore.CompletionsRequired > 1 && req.RotateOnEachCompletion
            && chore.RotationPeriod == RotationPeriod.EveryOccurrence;

        Validators.DueTime(req.DueTime, out var dueTime); // format already checked in ValidateAsync
        // Fixed times only ride on day-resolution schedules (Daily / DaysOfWeek / DaysOfMonth); clear
        // them otherwise so a stale list can't leak into the recurrence math. When set, DueTime mirrors
        // the earliest slot so single-time display/round-trip stays coherent.
        var supportsTimes = req.RepeatType == RepeatType.Daily ||
            (isCustom && mode is CustomRecurrenceMode.DaysOfWeek or CustomRecurrenceMode.DaysOfMonth);
        Validators.TimesOfDay(req.TimesOfDay, req.RepeatType, req.CustomMode, out var times); // already checked in ValidateAsync
        chore.TimesOfDay = supportsTimes ? times : new List<TimeOnly>();
        chore.DueTime = onDemand ? null : chore.TimesOfDay.Count > 0 ? chore.TimesOfDay[0] : dueTime;
        // Labels name slots, so one whose time is no longer in the schedule goes with it.
        Validators.TimeLabels(req.TimeLabels, out var labels); // already checked in ValidateAsync
        chore.TimeLabels = labels.Where(p => chore.TimesOfDay.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);

        chore.Assignees.Clear();
        foreach (var a in assignees) chore.Assignees.Add(a);

        Validators.Pairs(req.Pairs, assignees.Select(a => a.Id).ToList(), out var pairs); // already checked in ValidateAsync
        chore.Pairs = isIndependent ? new List<ChorePair>() : pairs;
        // Which of the holder's pairs has the turn: the one asked for, else the one that had it while
        // it still exists, else their first.
        if (req.CurrentPartnerId is not null)
            chore.CurrentPartnerId = req.CurrentPartnerId;
        chore.CurrentPartnerId = chore.HeldPartnerId();

        // Rebuild the notification schedule from the request (orphans cascade-delete). AtDue entries
        // carry no offset. New entries are inserted via the DbSet rather than only through the
        // navigation: a ChoreNotification carries a client-set Guid key, so on the update path (where
        // the chore is already tracked) EF would classify a navigation-added child as Modified and
        // emit an UPDATE against a row that doesn't exist yet — a DbUpdateConcurrencyException.
        // Every notification fires relative to a due time, which an on-demand chore never has.
        _db.ChoreNotifications.RemoveRange(chore.Notifications);
        foreach (var n in onDemand ? [] : req.Notifications ?? [])
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
        var lateGrace = await LateGraceAsync(chore, ct);
        track.DueAt = RecurrenceCalculator.NextDue(rule, chore.SchedulingPreference, scheduledDue,
            entry.CompletedAt - lateGrace, now - lateGrace, grace);
    }

    /// <summary>Called by the background service every minute. For every recurring chore with
    /// <see cref="Chore.AutoAdvanceIncomplete"/> set, checks whether the completion window has closed
    /// (now ≥ DueAt + <see cref="Chore.LateGrace"/> + <see cref="Chore.CompletionWindowMinutes"/>) and the occurrence is still short
    /// of its required completions. If so, writes <see cref="ChoreCompletion.IsExpired"/> rows for the
    /// missing slots (snapshotting the responsible assignee in <see cref="ChoreCompletion.PreviousAssigneeId"/>),
    /// advances the schedule using <see cref="SchedulingPreference.FromScheduledDate"/>, and rotates
    /// the assignee so the person who missed the occurrence doesn't stay on duty.</summary>
    public async Task<int> AutoAdvanceAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var advanced = 0;
        var dayStart = await AppSettingsService.ReadDayStartAsync(_db, ct);

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

            var windowEnd = (dueAt + chore.LateGrace(dayStart)).AddMinutes(chore.CompletionWindowMinutes ?? 0);
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
                    PreviousPartnerId = chore.HeldPartnerId(),
                    PartnerUserId = chore.CurrentPartner()?.Id,
                };
                _db.ChoreCompletions.Add(row);
                firstExpired ??= row;
            }

            // Advance and rotate — treat the current assignee's missed occurrence as their "turn used"
            // so the same person doesn't stay on duty after an expired occurrence. A calendar rotation
            // period still holds them until its boundary, same as a completion would.
            var rule = RecurrenceRule.FromChore(chore);
            chore.DueAt = RecurrenceCalculator.NextDue(rule, SchedulingPreference.FromScheduledDate, dueAt, now, now);
            if (chore.CurrentAssigneeId.HasValue && CrossedRotationBoundary(chore, dueAt))
                await RotateAssigneeAsync(chore, now, chore.CurrentAssigneeId.Value, chore.CurrentPartner()?.Id, firstExpired?.Id, ct);
            // Nobody claimed the occurrence before it expired — refund the offerer's bounty.
            await VoidOffersAsync(chore, OfferStatus.Cancelled, ct);
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
                var windowEnd = (trackDue + chore.LateGrace(dayStart)).AddMinutes(chore.CompletionWindowMinutes ?? 0);
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
        // On the household clock, so an all-day occurrence that is still open isn't stepped over.
        var now = DateTimeOffset.UtcNow - await LateGraceAsync(chore, ct);

        if (IsIndependent(chore))
        {
            foreach (var track in chore.AssigneeTracks)
                track.DueAt = StepForwardIfOverdue(chore, track.DueAt, now);
            chore.DueAt = EarliestTrackDue(chore.AssigneeTracks);
        }
        else if (chore.RepeatType is not (RepeatType.OneTime or RepeatType.OnDemand))
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
    internal async Task<(List<(Chore Chore, User NewAssignee, Guid? PartnerId)> Reassignments, List<Chore> Unassignable)>
        GetChoreReassignmentsForFreezeAsync(Guid freezingUserId, CancellationToken ct)
    {
        var affected = await Query()
            .Where(c => c.CurrentAssigneeId == freezingUserId
                     && c.AssignmentStrategy != AssignmentStrategy.Independent)
            .ToListAsync(ct);

        var reassignments = new List<(Chore, User, Guid?)>();
        var unassignable = new List<Chore>();
        var now = DateTimeOffset.UtcNow;

        foreach (var chore in affected)
        {
            var eligible = chore.Assignees
                .Where(u => u.Id != freezingUserId && !u.IsFrozen)
                .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
                .ToList();

            // A paired chore stays with the pair: the partner carries the rest of the turn alone. The
            // frozen member stays on as the stored partner, so the turn is still that pair's (and theirs
            // again if they're back before it ends) rather than sliding to another of the partner's pairs.
            if (chore.CurrentPartner() is { } partner)
            {
                reassignments.Add((chore, partner, freezingUserId));
                continue;
            }

            // Manual chores never pick a replacement on their own: they drop to unassigned and wait
            // for an admin, same as a chore with nobody left to take it.
            if (eligible.Count == 0 || chore.AssignmentStrategy == AssignmentStrategy.Manual)
            {
                unassignable.Add(chore);
                continue;
            }

            var (assignments, completions) = await RotationHistoryAsync(chore.Id, ct);
            var next = PickNext(chore, eligible.Select(u => u.Id).ToList(), assignments, completions, freezingUserId, null, now);
            reassignments.Add((chore, eligible.First(u => u.Id == next.Lead), next.Partner));
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

        foreach (var (chore, newAssignee, partnerId) in reassignments)
        {
            var previousAssigneeId = chore.CurrentAssigneeId;
            chore.CurrentAssigneeId = newAssignee.Id;
            chore.CurrentPartnerId = partnerId;
            _db.ChoreAssignments.Add(new ChoreAssignment
            {
                ChoreId = chore.Id,
                UserId = newAssignee.Id,
                PartnerUserId = partnerId,
                AssignedAt = now,
                PreviousAssigneeId = previousAssigneeId,
            });
            // The chore just moved owners; void any live offer so a stale claim can't resurrect it.
            await VoidOffersAsync(chore, OfferStatus.Cancelled, ct);
        }

        foreach (var chore in unassignable)
        {
            chore.CurrentAssigneeId = null;
            chore.CurrentPartnerId = null;
            await VoidOffersAsync(chore, OfferStatus.Cancelled, ct);
        }
    }
}
