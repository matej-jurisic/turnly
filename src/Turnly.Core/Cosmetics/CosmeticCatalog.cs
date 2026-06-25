using Turnly.Core.Enums;

namespace Turnly.Core.Cosmetics;

/// <summary>A code-defined gacha cosmetic: a stable <see cref="Key"/> (the contract shared with the
/// frontend, which owns the actual visual — frame CSS / theme palette — keyed by this same key),
/// presentation text, a <see cref="Slot"/>, and a <see cref="Rarity"/>. Adding a cosmetic is one
/// entry here + one frontend visual entry; no schema or migration.
/// <para><see cref="Value"/> is the concrete payload for slots the backend resolves itself — the hex
/// for <see cref="CosmeticSlot.Color"/> cosmetics (null for Frame/Theme, whose visual is frontend
/// CSS). <see cref="Default"/> marks a cosmetic everyone owns for free; defaults are excluded from
/// the pull pool.</para></summary>
public record CosmeticDefinition(
    string Key,
    string Name,
    string Description,
    CosmeticSlot Slot,
    CosmeticRarity Rarity,
    string? Value = null,
    bool Default = false);

/// <summary>Per-rarity tuning: the pull <see cref="Odds"/> (must sum to 1 across rarities), the
/// <see cref="DustAward"/> a duplicate pays out, and the <see cref="DustCraftCost"/> to make one
/// directly. Craft cost is higher than the dupe award so duplicates don't trivially craft.</summary>
public record RarityConfig(CosmeticRarity Rarity, double Odds, int DustAward, int DustCraftCost);

/// <summary>The fixed catalog of gacha cosmetics plus the economy constants (avatar frames, app
/// theme palettes, and avatar colors). All numbers here are tunable knobs.</summary>
public static class CosmeticCatalog
{
    /// <summary>Point cost of a single pull.</summary>
    public const int PullCost = 50;

    /// <summary>Point cost of a ten-pull (a ~10% discount over ten singles).</summary>
    public const int TenPullCost = 450;

    /// <summary>Pulls without a Legendary after which the next pull is forced to a Legendary.</summary>
    public const int PityThreshold = 60;

    /// <summary>Rarity odds + dust economy. Odds sum to 1.0.</summary>
    public static readonly IReadOnlyList<RarityConfig> Rarities =
    [
        new(CosmeticRarity.Common,    0.60,   5,  20),
        new(CosmeticRarity.Rare,      0.28,  15,  60),
        new(CosmeticRarity.Epic,      0.10,  40, 160),
        new(CosmeticRarity.Legendary, 0.02, 100, 400),
    ];

    public static RarityConfig Config(CosmeticRarity rarity) =>
        Rarities.First(r => r.Rarity == rarity);

    public static readonly IReadOnlyList<CosmeticDefinition> All =
    [
        // ---- Avatar frames ----------------------------------------------------------------
        // Common: simple static rings.
        new("frame-gold-thin",   "Gold Ring",      "A thin gold ring around your avatar.",   CosmeticSlot.Frame, CosmeticRarity.Common),
        new("frame-silver",      "Silver Ring",    "A thin silver ring around your avatar.",  CosmeticSlot.Frame, CosmeticRarity.Common),
        new("frame-bronze",      "Bronze Ring",    "A thin bronze ring around your avatar.",  CosmeticSlot.Frame, CosmeticRarity.Common),
        new("frame-slate",       "Slate Ring",     "A cool slate ring around your avatar.",   CosmeticSlot.Frame, CosmeticRarity.Common),
        // Rare: fancier static rings.
        new("frame-dashed",      "Achiever",       "A dashed violet ring.",                   CosmeticSlot.Frame, CosmeticRarity.Rare),
        new("frame-double",      "Double Ring",    "A crisp double ring.",                    CosmeticSlot.Frame, CosmeticRarity.Rare),
        new("frame-beaded",      "Beaded Ring",    "A playful beaded ring.",                  CosmeticSlot.Frame, CosmeticRarity.Rare),
        // Epic: animated rings.
        new("frame-holo",        "Holographic",    "A shifting holographic ring.",            CosmeticSlot.Frame, CosmeticRarity.Epic),
        new("frame-frostfire",   "Frostfire",      "A spinning ice-and-flame ring.",          CosmeticSlot.Frame, CosmeticRarity.Epic),
        // Legendary: showpiece animated ring.
        new("frame-liquid-gold", "Liquid Gold",    "A molten, rotating gold ring.",           CosmeticSlot.Frame, CosmeticRarity.Legendary),

        // ---- App theme palettes -----------------------------------------------------------
        // Rare: light recolors.
        new("theme-peach",       "Peach",          "A warm peach palette.",                   CosmeticSlot.Theme, CosmeticRarity.Rare),
        new("theme-sky",         "Sky",            "A breezy sky-blue palette.",              CosmeticSlot.Theme, CosmeticRarity.Rare),
        new("theme-mint",        "Mint",           "A fresh light mint palette.",             CosmeticSlot.Theme, CosmeticRarity.Rare),
        new("theme-lavender",    "Lavender",       "A soft lavender palette.",                CosmeticSlot.Theme, CosmeticRarity.Rare),
        new("theme-rosewater",   "Rosewater",      "A delicate blush palette.",               CosmeticSlot.Theme, CosmeticRarity.Rare),
        // Epic: distinctive palettes.
        new("theme-midnight",    "Midnight",       "A deep navy palette.",                    CosmeticSlot.Theme, CosmeticRarity.Epic),
        new("theme-sakura",      "Sakura",         "A soft pink blossom palette.",            CosmeticSlot.Theme, CosmeticRarity.Epic),
        new("theme-matrix",      "Matrix",         "Green-on-black terminal vibes.",          CosmeticSlot.Theme, CosmeticRarity.Epic),
        new("theme-sepia",       "Sepia",          "A warm newsprint palette.",               CosmeticSlot.Theme, CosmeticRarity.Epic),
        new("theme-crimson",     "Crimson",        "A dark blood-red palette.",               CosmeticSlot.Theme, CosmeticRarity.Epic),
        new("theme-ocean",       "Ocean",          "A deep ocean-blue palette.",              CosmeticSlot.Theme, CosmeticRarity.Epic),
        new("theme-graphite",    "Graphite",       "A sleek monochrome palette.",             CosmeticSlot.Theme, CosmeticRarity.Epic),
        // Legendary: premium palettes.
        new("theme-galaxy",      "Galaxy",         "A cosmic purple starfield palette.",      CosmeticSlot.Theme, CosmeticRarity.Legendary),
        new("theme-aurora",      "Aurora",         "Shifting northern-lights palette.",       CosmeticSlot.Theme, CosmeticRarity.Legendary),
        new("theme-sunset",      "Sunset",         "A glowing dusk-orange palette.",          CosmeticSlot.Theme, CosmeticRarity.Legendary),
        new("theme-nebula",      "Nebula",         "A radiant cosmic-magenta palette.",       CosmeticSlot.Theme, CosmeticRarity.Legendary),

        // ---- Avatar colors ----------------------------------------------------------------
        // The default purple is free (owned by everyone, never rolled). The rest are collectible.
        new("color-indigo", "Indigo", "The classic purple avatar.", CosmeticSlot.Color, CosmeticRarity.Common, Value: "#6366f1", Default: true),
        new("color-amber",  "Amber",  "A warm amber avatar.",       CosmeticSlot.Color, CosmeticRarity.Common, Value: "#f59e0b"),
        new("color-green",  "Green",  "A fresh green avatar.",      CosmeticSlot.Color, CosmeticRarity.Common, Value: "#22c55e"),
        new("color-blue",   "Blue",   "A clean blue avatar.",       CosmeticSlot.Color, CosmeticRarity.Common, Value: "#3b82f6"),
        new("color-slate",  "Slate",  "A cool slate avatar.",       CosmeticSlot.Color, CosmeticRarity.Common, Value: "#64748b"),
        new("color-cyan",   "Cyan",   "A bright cyan avatar.",      CosmeticSlot.Color, CosmeticRarity.Rare, Value: "#06b6d4"),
        new("color-teal",   "Teal",   "A calm teal avatar.",        CosmeticSlot.Color, CosmeticRarity.Rare, Value: "#14b8a6"),
        new("color-pink",   "Pink",   "A playful pink avatar.",     CosmeticSlot.Color, CosmeticRarity.Epic, Value: "#ec4899"),
        new("color-purple", "Purple", "A deep purple avatar.",      CosmeticSlot.Color, CosmeticRarity.Epic, Value: "#a855f7"),
        new("color-rose",   "Rose",   "An elegant rose avatar.",    CosmeticSlot.Color, CosmeticRarity.Legendary, Value: "#e11d48"),
    ];

    public static CosmeticDefinition? Find(string key) => All.FirstOrDefault(c => c.Key == key);
}
