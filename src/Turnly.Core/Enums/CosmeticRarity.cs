namespace Turnly.Core.Enums;

/// <summary>How rare a gacha cosmetic is. Drives the pull odds, the dust awarded for a duplicate,
/// and the dust cost to craft it directly. Ordered from most to least common.</summary>
public enum CosmeticRarity
{
    Common,
    Rare,
    Epic,
    Legendary
}
