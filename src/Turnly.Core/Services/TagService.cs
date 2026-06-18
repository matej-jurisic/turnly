using Microsoft.EntityFrameworkCore;
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
