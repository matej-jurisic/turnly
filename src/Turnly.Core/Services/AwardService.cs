using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Data;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;

namespace Turnly.Core.Services;

public class AwardService
{
    private readonly TurnlyDbContext _db;

    public AwardService(TurnlyDbContext db)
    {
        _db = db;
    }

    public async Task<List<AwardDto>> ListAsync(CancellationToken ct = default)
        => (await _db.Awards.ToListAsync(ct))
            .OrderBy(a => a.Cost)
            .ThenBy(a => a.Name)
            .Select(AwardDto.FromEntity)
            .ToList();

    public async Task<Result<AwardDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var award = await _db.Awards.FindAsync([id], ct);
        return award is null
            ? Result.Fail<AwardDto>(Error.NotFound("Award not found."))
            : Result.Success(AwardDto.FromEntity(award));
    }

    public async Task<Result<AwardDto>> CreateAsync(CreateAwardRequest req, CancellationToken ct = default)
    {
        if (Validators.AwardName(req.Name) is { } nameError)
            return Result.Fail<AwardDto>(nameError);
        if (Validators.AwardCost(req.Cost) is { } costError)
            return Result.Fail<AwardDto>(costError);

        var award = new Award
        {
            Name = req.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            Emoji = string.IsNullOrWhiteSpace(req.Emoji) ? null : req.Emoji.Trim(),
            Cost = req.Cost
        };
        _db.Awards.Add(award);
        await _db.SaveChangesAsync(ct);
        return Result.Success(AwardDto.FromEntity(award));
    }

    public async Task<Result<AwardDto>> UpdateAsync(Guid id, UpdateAwardRequest req, CancellationToken ct = default)
    {
        if (Validators.AwardName(req.Name) is { } nameError)
            return Result.Fail<AwardDto>(nameError);
        if (Validators.AwardCost(req.Cost) is { } costError)
            return Result.Fail<AwardDto>(costError);

        var award = await _db.Awards.FindAsync([id], ct);
        if (award is null)
            return Result.Fail<AwardDto>(Error.NotFound("Award not found."));

        award.Name = req.Name.Trim();
        award.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        award.Emoji = string.IsNullOrWhiteSpace(req.Emoji) ? null : req.Emoji.Trim();
        award.Cost = req.Cost;

        await _db.SaveChangesAsync(ct);
        return Result.Success(AwardDto.FromEntity(award));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var award = await _db.Awards.FindAsync([id], ct);
        if (award is null)
            return Result.Fail(Error.NotFound("Award not found."));

        // Past redemptions snapshot the award, so deleting it just nulls their AwardId.
        _db.Awards.Remove(award);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
