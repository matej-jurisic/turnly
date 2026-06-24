using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Data;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;
using Turnly.Core.Enums;

namespace Turnly.Core.Services;

public class RedemptionService
{
    private readonly TurnlyDbContext _db;
    private readonly AchievementService _achievements;

    public RedemptionService(TurnlyDbContext db, AchievementService achievements)
    {
        _db = db;
        _achievements = achievements;
    }

    /// <summary>Lists redemptions. Admins (<paramref name="includeAll"/>) see everyone's;
    /// members see only their own.</summary>
    public async Task<List<RedemptionDto>> ListAsync(Guid userId, bool includeAll, CancellationToken ct = default)
    {
        var query = _db.Redemptions.Include(r => r.User).AsQueryable();
        if (!includeAll)
            query = query.Where(r => r.UserId == userId);

        // Order client-side: SQLite can't ORDER BY DateTimeOffset.
        return (await query.ToListAsync(ct))
            .OrderByDescending(r => r.RedeemedAt)
            .Select(RedemptionDto.FromEntity)
            .ToList();
    }

    public async Task<Result<RedemptionDto>> RedeemAsync(Guid userId, Guid awardId, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct);
        if (user is null)
            return Result.Fail<RedemptionDto>(Error.NotFound("User not found."));

        var award = await _db.Awards.FindAsync([awardId], ct);
        if (award is null)
            return Result.Fail<RedemptionDto>(Error.NotFound("Award not found."));

        if (user.Points < award.Cost)
            return Result.Fail<RedemptionDto>(Error.Conflict("Not enough points to redeem this award."));

        var redemption = new Redemption
        {
            AwardId = award.Id,
            UserId = userId,
            AwardName = award.Name,
            AwardEmoji = award.Emoji,
            PointsSpent = award.Cost,
            Status = RedemptionStatus.Pending
        };
        _db.Redemptions.Add(redemption);

        // Deduct points and log it (mirrors the award path in ChoreService.CompleteAsync).
        user.Points -= award.Cost;
        _db.PointsLog.Add(new PointsLogEntry
        {
            UserId = userId,
            Delta = -award.Cost,
            Type = PointsLogType.Redemption,
            RedemptionId = redemption.Id,
            Description = $"Redeemed: {award.Name}"
        });

        await _db.SaveChangesAsync(ct);

        // Redeeming can unlock the redemption-count achievements.
        await _achievements.EvaluateForUserAsync(userId, DateTimeOffset.UtcNow, ct);

        redemption.User = user;
        return Result.Success(RedemptionDto.FromEntity(redemption));
    }

    public async Task<Result<RedemptionDto>> FulfillAsync(Guid id, CancellationToken ct = default)
    {
        var redemption = await _db.Redemptions.Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (redemption is null)
            return Result.Fail<RedemptionDto>(Error.NotFound("Redemption not found."));
        if (redemption.Status != RedemptionStatus.Pending)
            return Result.Fail<RedemptionDto>(Error.Conflict("This redemption has already been fulfilled."));

        redemption.Status = RedemptionStatus.Fulfilled;
        redemption.FulfilledAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Result.Success(RedemptionDto.FromEntity(redemption));
    }

    /// <summary>Cancels a still-pending redemption: refunds the spent points and removes both the
    /// redemption and its points-log deduction (mirrors ChoreService.UndoCompletionAsync).</summary>
    public async Task<Result> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var redemption = await _db.Redemptions.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (redemption is null)
            return Result.Fail(Error.NotFound("Redemption not found."));
        if (redemption.Status != RedemptionStatus.Pending)
            return Result.Fail(Error.Conflict("A fulfilled redemption cannot be cancelled."));

        var logEntry = await _db.PointsLog.FirstOrDefaultAsync(e => e.RedemptionId == id, ct);
        if (logEntry is not null)
        {
            var user = await _db.Users.FindAsync([redemption.UserId], ct);
            if (user is not null)
                user.Points -= logEntry.Delta; // delta is negative, so this refunds
            _db.PointsLog.Remove(logEntry);
        }

        _db.Redemptions.Remove(redemption);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
