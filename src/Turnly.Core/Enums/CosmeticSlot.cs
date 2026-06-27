namespace Turnly.Core.Enums;

/// <summary>The cosmetic category a gacha item occupies. A user equips at most one item per slot.
/// v1 ships two slots; more (avatar effects, titles, nameplates) can be added without a migration
/// since the catalog is code-defined.</summary>
public enum CosmeticSlot
{
    /// <summary>A decorative ring around the user's avatar. Visible to everyone.</summary>
    Frame,

    /// <summary>An app-wide color palette. Only recolors the owner's own view.</summary>
    Theme,

    /// <summary>The fill color of the user's avatar. Visible to everyone. Equipping one sets
    /// <see cref="Entities.User.AvatarColor"/> to the cosmetic's value.</summary>
    Color,

    /// <summary>An emoji shown on the avatar in place of the user's initials. Visible to everyone.
    /// Equipping one sets <see cref="Entities.User.AvatarEmoji"/> to the cosmetic's value (the emoji
    /// char); unequipping (null key) clears it and the avatar falls back to initials.</summary>
    Emoji
}
