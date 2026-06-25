import type { CosmeticRarity, CosmeticSlot } from '@/lib/types'

/**
 * Frontend visual registry for gacha cosmetics. The backend catalog
 * (`Core/Cosmetics/CosmeticCatalog.cs`) owns the keys + metadata; this file owns how each one looks.
 * Frame visuals are CSS classes defined in `index.css`; theme palettes are applied as a
 * `data-palette` attribute whose value is the cosmetic key (scopes also live in `index.css`).
 */

/** Frames that animate (get the spin class on top of their ring class). */
const ANIMATED_FRAMES = new Set(['frame-holo', 'frame-frostfire', 'frame-liquid-gold'])

/** The class list to put on the avatar's frame wrapper for a given frame key. Empty for no/unknown
 * frame, so the avatar just renders bare. */
export function frameClasses(key: string | null | undefined): string {
  if (!key) return ''
  return ANIMATED_FRAMES.has(key) ? `gx-frame ${key} gx-spin` : `gx-frame ${key}`
}

/**
 * Rarity -> a FIXED color (hex), deliberately not tied to the theme tokens. Rarity is stable
 * information, so its colors must stay distinct regardless of which app palette is equipped (an
 * equipped palette recolors `--primary`/`--info`, which would otherwise make e.g. Epic and Rare
 * collide under a blue palette like Sky).
 */
export const RARITY_COLOR: Record<CosmeticRarity, string> = {
  Common: '#9ca3af', // gray
  Rare: '#3b82f6', // blue
  Epic: '#a855f7', // purple
  Legendary: '#f59e0b', // gold
}

/** Display order, rarest first. */
export const RARITY_ORDER: CosmeticRarity[] = ['Legendary', 'Epic', 'Rare', 'Common']

/** Short, user-facing label for a cosmetic slot (the gacha "category"). */
export const SLOT_LABEL: Record<CosmeticSlot, string> = {
  Frame: 'Frame',
  Theme: 'Theme',
  Color: 'Color',
}
