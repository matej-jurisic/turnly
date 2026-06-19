using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Data;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;

namespace Turnly.Core.Services;

public class TagService
{
    private readonly TurnlyDbContext _db;

    public TagService(TurnlyDbContext db)
    {
        _db = db;
    }

    public async Task<List<TagDto>> ListAsync(CancellationToken ct = default)
        => await _db.Tags
            .OrderBy(t => t.Name)
            .Select(t => new TagDto(t.Id, t.Name))
            .ToListAsync(ct);

    public async Task<Result<TagDto>> CreateAsync(string name, CancellationToken ct = default)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
            return Result.Fail<TagDto>(Error.Validation("Tag name is required."));
        if (trimmed.Length > 50)
            return Result.Fail<TagDto>(Error.Validation("Tag name must be 50 characters or fewer."));

        var exists = await _db.Tags.AnyAsync(t => t.Name.ToLower() == trimmed.ToLower(), ct);
        if (exists)
            return Result.Fail<TagDto>(Error.Conflict("A tag with that name already exists."));

        var tag = new Tag { Name = trimmed };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync(ct);
        return Result.Success(TagDto.FromEntity(tag));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var tag = await _db.Tags.FindAsync([id], ct);
        if (tag is null)
            return Result.Fail(Error.NotFound("Tag not found."));

        _db.Tags.Remove(tag);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Resolves a set of tag names to entities, reusing existing tags (case-insensitive)
    /// and creating any that don't exist yet. Caller is responsible for SaveChanges.</summary>
    public async Task<List<Tag>> ResolveAsync(IEnumerable<string>? names, CancellationToken ct = default)
    {
        var cleaned = (names ?? [])
            .Select(n => n?.Trim() ?? string.Empty)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleaned.Count == 0)
            return [];

        // Match case-insensitively in memory — SQLite's default text comparison is
        // case-sensitive, and a household's tag set is small.
        var existing = await _db.Tags.ToListAsync(ct);

        var result = new List<Tag>();
        foreach (var name in cleaned)
        {
            var match = existing.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                match = new Tag { Name = name };
                _db.Tags.Add(match);
                existing.Add(match);
            }
            result.Add(match);
        }

        return result;
    }
}
