namespace Turnly.Core.Entities;

/// <summary>A reward a household member can redeem by spending points. Created and managed by
/// admins. Deleting an award keeps past <see cref="Redemption"/>s intact (they snapshot the
/// name/emoji/cost), so its foreign key is nulled out rather than cascade-deleted.</summary>
public class Award
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Emoji { get; set; }

    /// <summary>Points it costs to redeem this award.</summary>
    public int Cost { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Redemption> Redemptions { get; set; } = new List<Redemption>();
}
