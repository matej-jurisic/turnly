namespace Turnly.Core.Entities;

/// <summary>A reusable, freeform label for grouping/filtering chores. Resolved by name
/// (find-or-create) when a chore is saved; shared across chores via many-to-many.</summary>
public class Tag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }

    public ICollection<Chore> Chores { get; set; } = new List<Chore>();
}
