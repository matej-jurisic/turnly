namespace Turnly.Core.Entities;

/// <summary>Records that a user owns a gacha cosmetic. The cosmetic definitions themselves live in
/// code (<see cref="Turnly.Core.Cosmetics.CosmeticCatalog"/>); this row is the per-user "unlocked"
/// marker, keyed by the definition's stable <see cref="CosmeticKey"/>. One row per
/// (user, cosmetic) — a duplicate pull pays out dust instead of adding a row, but bumps
/// <see cref="Count"/>.</summary>
public class UserCosmetic
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Stable key of the owned cosmetic (matches a <c>CosmeticDefinition.Key</c>).</summary>
    public required string CosmeticKey { get; set; }

    /// <summary>How many times the user has obtained this cosmetic (1 on first unlock; bumped on
    /// each duplicate pull, which also awards dust).</summary>
    public int Count { get; set; } = 1;

    public DateTimeOffset FirstObtainedAt { get; set; } = DateTimeOffset.UtcNow;
}
