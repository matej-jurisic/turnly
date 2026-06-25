using Microsoft.EntityFrameworkCore;
using Turnly.Core.Common;
using Turnly.Core.Cosmetics;
using Turnly.Core.Data;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;
using Turnly.Core.Enums;

namespace Turnly.Core.Services;

/// <summary>The gacha: spend points on random pulls across the code-defined
/// <see cref="CosmeticCatalog"/>, with a pity counter that guarantees a Legendary, a dust economy
/// for duplicates, and direct crafting. Points deductions mirror
/// <see cref="RedemptionService.RedeemAsync"/> (negative <see cref="PointsLogEntry"/> +
/// <c>User.Points</c>). Equipped cosmetics live on <see cref="User"/> so they project through
/// <c>UserDto.FromEntity</c>. The RNG is injectable so pulls are unit-testable with a seed.</summary>
public class GachaService
{
    private readonly TurnlyDbContext _db;
    private readonly Random _random;

    public GachaService(TurnlyDbContext db, Random? random = null)
    {
        _db = db;
        _random = random ?? Random.Shared;
    }

    /// <summary>Read-only snapshot for the gacha page: balances, pricing, pity, odds, and the full
    /// catalog projected with the user's ownership/equip state.</summary>
    public async Task<Result<GachaStateDto>> GetStateAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct);
        if (user is null)
            return Result.Fail<GachaStateDto>(Error.NotFound("User not found."));

        var owned = await OwnedAsync(userId, ct);
        var cosmetics = CosmeticCatalog.All
            .Select(def => ToDto(def, user, owned))
            .ToArray();

        var odds = CosmeticCatalog.Rarities
            .Select(r => new RarityOddsDto(r.Rarity, r.Odds, r.DustAward, r.DustCraftCost))
            .ToArray();

        return Result.Success(new GachaStateDto(
            user.Points, user.Dust, CosmeticCatalog.PullCost, CosmeticCatalog.TenPullCost,
            user.PullsSinceLegendary, CosmeticCatalog.PityThreshold, odds, cosmetics));
    }

    /// <summary>Spends points on <paramref name="count"/> rolls (1 or 10). Each roll picks a rarity by
    /// the published odds — unless the pity counter forces a Legendary — then a random cosmetic of
    /// that rarity. A new cosmetic is unlocked; a duplicate awards dust. Saves once.</summary>
    public async Task<Result<List<PullResultDto>>> PullAsync(Guid userId, int count, DateTimeOffset now, CancellationToken ct = default)
    {
        if (count is not (1 or 10))
            return Result.Fail<List<PullResultDto>>(Error.Validation("You can pull 1 or 10 at a time."));

        var user = await _db.Users.FindAsync([userId], ct);
        if (user is null)
            return Result.Fail<List<PullResultDto>>(Error.NotFound("User not found."));

        var cost = count == 10 ? CosmeticCatalog.TenPullCost : CosmeticCatalog.PullCost;
        if (user.Points < cost)
            return Result.Fail<List<PullResultDto>>(Error.Conflict("Not enough points for this pull."));

        // Deduct points up front (mirrors RedemptionService.RedeemAsync).
        user.Points -= cost;
        _db.PointsLog.Add(new PointsLogEntry
        {
            UserId = userId,
            Delta = -cost,
            Type = PointsLogType.GachaPull,
            Description = count == 10 ? "Gacha pull x10" : "Gacha pull"
        });

        // Track ownership in-memory so a brand-new item pulled twice in one batch dupes correctly.
        var ownedKeys = (await OwnedAsync(userId, ct)).Keys.ToHashSet();
        var rowsByKey = await _db.UserCosmetics
            .Where(c => c.UserId == userId)
            .ToDictionaryAsync(c => c.CosmeticKey, ct);

        var results = new List<PullResultDto>(count);
        for (var i = 0; i < count; i++)
        {
            var rarity = user.PullsSinceLegendary + 1 >= CosmeticCatalog.PityThreshold
                ? CosmeticRarity.Legendary
                : RollRarity();

            var pool = CosmeticCatalog.All.Where(c => c.Rarity == rarity && !c.Default).ToList();
            // Defensive: a rarity with no catalog items just pays out dust.
            if (pool.Count == 0)
            {
                user.Dust += CosmeticCatalog.Config(rarity).DustAward;
                continue;
            }

            var def = pool[_random.Next(pool.Count)];
            var isNew = !ownedKeys.Contains(def.Key);
            var dustAwarded = 0;

            if (isNew)
            {
                ownedKeys.Add(def.Key);
                var row = new UserCosmetic { UserId = userId, CosmeticKey = def.Key, Count = 1, FirstObtainedAt = now };
                rowsByKey[def.Key] = row;
                _db.UserCosmetics.Add(row);
            }
            else
            {
                dustAwarded = CosmeticCatalog.Config(def.Rarity).DustAward;
                user.Dust += dustAwarded;
                if (rowsByKey.TryGetValue(def.Key, out var row))
                    row.Count++;
            }

            // Pity: reset on any Legendary, otherwise advance.
            user.PullsSinceLegendary = def.Rarity == CosmeticRarity.Legendary ? 0 : user.PullsSinceLegendary + 1;

            results.Add(new PullResultDto(ToDto(def, user, ownedKeys), isNew, dustAwarded));
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success(results);
    }

    /// <summary>Spends dust to obtain a specific cosmetic the user doesn't already own.</summary>
    public async Task<Result> CraftAsync(Guid userId, string key, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct);
        if (user is null)
            return Result.Fail(Error.NotFound("User not found."));

        var def = CosmeticCatalog.Find(key);
        if (def is null)
            return Result.Fail(Error.NotFound("Cosmetic not found."));

        var alreadyOwned = def.Default || await _db.UserCosmetics.AnyAsync(c => c.UserId == userId && c.CosmeticKey == key, ct);
        if (alreadyOwned)
            return Result.Fail(Error.Conflict("You already own this cosmetic."));

        var cost = CosmeticCatalog.Config(def.Rarity).DustCraftCost;
        if (user.Dust < cost)
            return Result.Fail(Error.Conflict("Not enough dust to craft this cosmetic."));

        user.Dust -= cost;
        _db.UserCosmetics.Add(new UserCosmetic { UserId = userId, CosmeticKey = key, Count = 1 });
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Equips (or, with a null key, unequips) a cosmetic in the given slot. The user must own
    /// the cosmetic and it must belong to that slot.</summary>
    public async Task<Result> EquipAsync(Guid userId, CosmeticSlot slot, string? key, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct);
        if (user is null)
            return Result.Fail(Error.NotFound("User not found."));

        CosmeticDefinition? def = null;
        if (key is not null)
        {
            def = CosmeticCatalog.Find(key);
            if (def is null || def.Slot != slot)
                return Result.Fail(Error.Validation("That cosmetic does not fit this slot."));

            var owns = def.Default || await _db.UserCosmetics.AnyAsync(c => c.UserId == userId && c.CosmeticKey == key, ct);
            if (!owns)
                return Result.Fail(Error.Forbidden("You do not own this cosmetic."));
        }

        switch (slot)
        {
            case CosmeticSlot.Frame:
                user.EquippedFrameKey = key;
                break;
            case CosmeticSlot.Theme:
                user.EquippedThemeKey = key;
                break;
            case CosmeticSlot.Color:
                // A color is always set to a concrete value — there's no "no color" state.
                if (def?.Value is null)
                    return Result.Fail(Error.Validation("Pick a color to equip."));
                user.AvatarColor = def.Value;
                break;
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Picks a rarity by the catalog's cumulative odds.</summary>
    private CosmeticRarity RollRarity()
    {
        var roll = _random.NextDouble();
        var cumulative = 0.0;
        foreach (var r in CosmeticCatalog.Rarities)
        {
            cumulative += r.Odds;
            if (roll < cumulative)
                return r.Rarity;
        }
        return CosmeticCatalog.Rarities[^1].Rarity; // rounding guard
    }

    private async Task<Dictionary<string, int>> OwnedAsync(Guid userId, CancellationToken ct) =>
        await _db.UserCosmetics
            .Where(c => c.UserId == userId)
            .ToDictionaryAsync(c => c.CosmeticKey, c => c.Count, ct);

    private static CosmeticDto ToDto(CosmeticDefinition def, User user, IReadOnlyDictionary<string, int> owned) =>
        ToDto(def, user, def.Default || owned.ContainsKey(def.Key), owned.TryGetValue(def.Key, out var n) ? n : 0);

    private static CosmeticDto ToDto(CosmeticDefinition def, User user, IReadOnlySet<string> ownedKeys) =>
        ToDto(def, user, def.Default || ownedKeys.Contains(def.Key), 0);

    private static CosmeticDto ToDto(CosmeticDefinition def, User user, bool owned, int count)
    {
        var equipped = def.Slot switch
        {
            CosmeticSlot.Frame => user.EquippedFrameKey == def.Key,
            CosmeticSlot.Theme => user.EquippedThemeKey == def.Key,
            CosmeticSlot.Color => string.Equals(user.AvatarColor, def.Value, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        return new CosmeticDto(def.Key, def.Name, def.Description, def.Slot, def.Rarity,
            owned, equipped, count, CosmeticCatalog.Config(def.Rarity).DustCraftCost, def.Value);
    }
}
