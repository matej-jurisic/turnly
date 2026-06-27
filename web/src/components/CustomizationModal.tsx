import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { gachaApi } from '@/lib/api'
import { useAuthStore } from '@/store/auth'
import { useThemeStore } from '@/lib/theme'
import { applyPalette } from '@/lib/palette'
import { syncAppearanceFromServer } from '@/lib/appearance'
import { frameClasses, RARITY_COLOR } from '@/lib/cosmetics'
import type { CosmeticRarity } from '@/lib/types'
import { toast } from '@/lib/toast'
import { Modal, Avatar } from '@/components/ui/Modal'

/**
 * Appearance picker opened from the account menu. One place to choose how the app looks: the base
 * Light/Dark modes plus any owned theme palettes (mutually exclusive — a palette supersedes
 * light/dark), and any owned avatar frame. Equipping happens here, not on the gacha page.
 */
export function CustomizationModal({ onClose }: { onClose: () => void }) {
  const user = useAuthStore((s) => s.user)
  const theme = useThemeStore((s) => s.theme)
  const setTheme = useThemeStore((s) => s.setTheme)
  const queryClient = useQueryClient()
  const [tab, setTab] = useState<'theme' | 'color' | 'emoji' | 'frame'>('theme')

  const { data: state, isPending, isError, refetch } = useQuery({ queryKey: ['gacha'], queryFn: gachaApi.state })

  const equip = useMutation({
    mutationFn: ({ slot, key }: { slot: 'Frame' | 'Theme' | 'Color' | 'Emoji'; key: string | null }) =>
      gachaApi.equip(slot, key),
    onSuccess: (_data, { slot, key }) => {
      if (slot === 'Theme') applyPalette(key)
      return syncAppearanceFromServer(queryClient)
    },
    onError: (e: Error) => toast.error(e.message),
  })

  if (!user) return null

  // Until the gacha state resolves, `state` is undefined and every owned-filter below is empty -
  // which would silently render only the free defaults. Guard with explicit loading/error states so
  // a slow or failed fetch (retry is off) never masquerades as "you own nothing".
  if (!state) {
    return (
      <Modal title="Customization" onClose={onClose} widthClassName="max-w-2xl">
        {isError ? (
          <div className="flex flex-col items-center gap-3 py-10 text-center">
            <p className="text-sm text-muted-foreground">Could not load your collection.</p>
            <button
              type="button"
              onClick={() => refetch()}
              className="rounded-lg border border-border px-3 py-1.5 text-sm text-foreground transition-colors hover:bg-accent"
            >
              Try again
            </button>
          </div>
        ) : (
          <div className="flex items-center justify-center py-10 text-sm text-muted-foreground">
            {isPending ? 'Loading your collection...' : 'Loading...'}
          </div>
        )}
      </Modal>
    )
  }

  const ownedThemes = (state?.cosmetics ?? []).filter((c) => c.slot === 'Theme' && c.owned)
  const ownedFrames = (state?.cosmetics ?? []).filter((c) => c.slot === 'Frame' && c.owned)
  const ownedColors = (state?.cosmetics ?? []).filter((c) => c.slot === 'Color' && c.owned)
  const ownedEmojis = (state?.cosmetics ?? []).filter((c) => c.slot === 'Emoji' && c.owned)
  const activePalette = user.equippedThemeKey ?? null
  const activeFrame = user.equippedFrameKey ?? null
  const activeColor = user.avatarColor.toLowerCase()
  const activeEmoji = user.avatarEmoji ?? null

  // Selecting a base mode: clear any equipped palette and set light/dark.
  function chooseMode(mode: 'light' | 'dark') {
    setTheme(mode)
    if (activePalette) equip.mutate({ slot: 'Theme', key: null })
  }

  function choosePalette(key: string) {
    equip.mutate({ slot: 'Theme', key })
  }

  function chooseFrame(key: string | null) {
    equip.mutate({ slot: 'Frame', key })
  }

  function chooseColor(key: string) {
    equip.mutate({ slot: 'Color', key })
  }

  function chooseEmoji(key: string | null) {
    equip.mutate({ slot: 'Emoji', key })
  }

  const busy = equip.isPending

  return (
    <Modal title="Customization" onClose={onClose} widthClassName="max-w-2xl">
      <div className="space-y-5">
        {/* One tab per dimension so each view shows a single grid - keeps the modal from sprawling
            once a lot of cosmetics are unlocked. Color/Emoji/Frame all share a live avatar preview.
            The strip scrolls horizontally rather than cramming when it runs out of width, so it
            survives narrow screens and any number of future sections. */}
        <div className="flex gap-1 overflow-x-auto rounded-xl bg-muted p-1">
          <TabButton active={tab === 'theme'} onClick={() => setTab('theme')}>
            Theme
          </TabButton>
          <TabButton active={tab === 'color'} onClick={() => setTab('color')}>
            Color
          </TabButton>
          <TabButton active={tab === 'emoji'} onClick={() => setTab('emoji')}>
            Emoji
          </TabButton>
          <TabButton active={tab === 'frame'} onClick={() => setTab('frame')}>
            Frame
          </TabButton>
        </div>

        {/* Fixed-height shell so switching tabs never resizes the modal: the avatar preview is
            pinned and only the swatch grid scrolls. Height is capped at a comfortable desktop size
            but shrinks to leave room for the modal chrome on short screens - otherwise the shell
            would out-grow the Modal's own scroll area and you'd get a second scrollbar. */}
        <div className="flex h-[min(34rem,calc(100dvh-16rem))] flex-col">
          {tab !== 'theme' && (
            <div className="flex shrink-0 justify-center py-1">
              <Avatar color={user.avatarColor} name={user.displayName} emoji={activeEmoji} frame={activeFrame} size={72} />
            </div>
          )}

          <div className="@container mt-4 min-h-0 flex-1 overflow-y-auto first:mt-0 [scrollbar-gutter:stable_both-edges]">
            {tab === 'theme' && (
              <section className="grid grid-cols-2 gap-2 @sm:grid-cols-3 @lg:grid-cols-4">
                <SwatchTile
                  label="Light"
                  selected={!activePalette && theme === 'light'}
                  disabled={busy}
                  onClick={() => chooseMode('light')}
                  preview={<ModePreview mode="light" />}
                />
                <SwatchTile
                  label="Dark"
                  selected={!activePalette && theme === 'dark'}
                  disabled={busy}
                  onClick={() => chooseMode('dark')}
                  preview={<ModePreview mode="dark" />}
                />
                {ownedThemes.map((c) => (
                  <SwatchTile
                    key={c.key}
                    label={c.name}
                    rarity={c.rarity}
                    selected={activePalette === c.key}
                    disabled={busy}
                    onClick={() => choosePalette(c.key)}
                    preview={<PalettePreview paletteKey={c.key} />}
                  />
                ))}
              </section>
            )}

            {tab === 'color' && (
              <section className="grid grid-cols-2 gap-2 @sm:grid-cols-3 @lg:grid-cols-4">
                {ownedColors.map((c) => (
                  <SwatchTile
                    key={c.key}
                    label={c.name}
                    rarity={c.rarity}
                    selected={(c.value ?? '').toLowerCase() === activeColor}
                    disabled={busy}
                    onClick={() => chooseColor(c.key)}
                    preview={<ColorPreview color={c.value ?? null} />}
                  />
                ))}
              </section>
            )}

            {tab === 'emoji' && (
              <section className="grid grid-cols-2 gap-2 @sm:grid-cols-3 @lg:grid-cols-4">
                <SwatchTile
                  label="Initials"
                  selected={!activeEmoji}
                  disabled={busy}
                  onClick={() => chooseEmoji(null)}
                  preview={<GlyphPreview emoji={null} color={user.avatarColor} name={user.displayName} />}
                />
                {ownedEmojis.map((c) => (
                  <SwatchTile
                    key={c.key}
                    label={c.name}
                    rarity={c.rarity}
                    selected={activeEmoji === c.value}
                    disabled={busy}
                    onClick={() => chooseEmoji(c.key)}
                    preview={<GlyphPreview emoji={c.value ?? null} color={user.avatarColor} name={user.displayName} />}
                  />
                ))}
              </section>
            )}

            {tab === 'frame' && (
              <section className="grid grid-cols-2 gap-2 @sm:grid-cols-3 @lg:grid-cols-4">
                <SwatchTile
                  label="None"
                  selected={!activeFrame}
                  disabled={busy}
                  onClick={() => chooseFrame(null)}
                  preview={<FramePreview frameKey={null} color={user.avatarColor} name={user.displayName} />}
                />
                {ownedFrames.map((c) => (
                  <SwatchTile
                    key={c.key}
                    label={c.name}
                    rarity={c.rarity}
                    selected={activeFrame === c.key}
                    disabled={busy}
                    onClick={() => chooseFrame(c.key)}
                    preview={<FramePreview frameKey={c.key} color={user.avatarColor} name={user.displayName} />}
                  />
                ))}
              </section>
            )}
          </div>
        </div>
      </div>
    </Modal>
  )
}

function TabButton({ active, onClick, children }: { active: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={`min-w-fit flex-1 whitespace-nowrap rounded-lg px-3 py-1.5 text-sm transition-colors ${
        active ? 'bg-card font-semibold text-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground'
      }`}
    >
      {children}
    </button>
  )
}

function SwatchTile({
  label,
  rarity,
  selected,
  disabled,
  onClick,
  preview,
}: {
  label: string
  rarity?: CosmeticRarity
  selected: boolean
  disabled: boolean
  onClick: () => void
  preview: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-pressed={selected}
      className={`flex flex-col items-center gap-1.5 rounded-xl border px-2 py-4 text-center transition-colors disabled:opacity-60 ${
        selected ? 'border-primary bg-primary/5' : 'border-border hover:bg-accent'
      }`}
    >
      <span className="flex h-16 w-full items-center justify-center">{preview}</span>
      <span className="w-full truncate text-xs text-foreground">{label}</span>
      {rarity && <RarityTag rarity={rarity} />}
    </button>
  )
}

/** A compact rarity pill in fixed rarity colors (not theme tokens) so tiers stay distinct under any
 * palette. Mirrors the gacha page's RarityBadge at a smaller size. */
function RarityTag({ rarity }: { rarity: CosmeticRarity }) {
  const color = RARITY_COLOR[rarity]
  return (
    <span
      className="rounded-full px-1.5 py-0.5 text-[10px] font-medium leading-none"
      style={{ background: `color-mix(in srgb, ${color} 16%, transparent)`, color }}
    >
      {rarity}
    </span>
  )
}

function ModePreview({ mode }: { mode: 'light' | 'dark' }) {
  // Mirror PalettePreview's card mockup so Light/Dark read as the same kind of tile as the palettes,
  // just with hardcoded base-theme colors instead of scoped theme tokens.
  const c =
    mode === 'dark'
      ? { bg: '#0f0f11', border: '#2a2a2e', primary: '#7b6ef6', muted: '#2a2a2e', card: '#1c1c1f' }
      : { bg: '#f5f5f7', border: '#e2e2e6', primary: '#5b4ee8', muted: '#e2e2e6', card: '#ffffff' }
  return (
    <span className="flex h-14 w-14 overflow-hidden rounded-lg border" style={{ background: c.bg, borderColor: c.border }}>
      <span className="flex flex-1 flex-col gap-1 p-1.5">
        <span className="h-2 w-6 rounded-full" style={{ background: c.primary }} />
        <span className="h-2 w-8 rounded-full" style={{ background: c.muted }} />
        <span className="mt-auto h-2.5 w-full rounded" style={{ background: c.card }} />
      </span>
    </span>
  )
}

function PalettePreview({ paletteKey }: { paletteKey: string }) {
  // Scope the palette to this preview only via data-palette.
  return (
    <span data-palette={paletteKey} className="flex h-14 w-14 overflow-hidden rounded-lg border border-border bg-background">
      <span className="flex flex-1 flex-col gap-1 p-1.5">
        <span className="h-2 w-6 rounded-full bg-primary" />
        <span className="h-2 w-8 rounded-full bg-muted" />
        <span className="mt-auto h-2.5 w-full rounded bg-card" />
      </span>
    </span>
  )
}

function ColorPreview({ color }: { color: string | null }) {
  return <span className="h-12 w-12 rounded-full" style={{ backgroundColor: color ?? undefined }} />
}

function GlyphPreview({ emoji, color, name }: { emoji: string | null; color: string; name: string }) {
  const glyph = emoji || name.trim().slice(0, 2).toUpperCase()
  return (
    <span
      className="gx-av inline-flex h-12 w-12 items-center justify-center overflow-hidden rounded-full text-white"
      style={{ backgroundColor: color }}
    >
      {emoji ? (
        <span className="block w-full text-center text-2xl leading-none">{glyph}</span>
      ) : (
        <span className="text-sm font-medium">{glyph}</span>
      )}
    </span>
  )
}

function FramePreview({ frameKey, color, name }: { frameKey: string | null; color: string; name: string }) {
  const initials = name.trim().slice(0, 2).toUpperCase()
  const inner = (
    <span
      className="gx-av inline-flex h-12 w-12 items-center justify-center rounded-full text-sm font-medium text-white"
      style={{ backgroundColor: color }}
    >
      {initials}
    </span>
  )
  if (!frameKey) return inner
  return (
    <span className={frameClasses(frameKey)} style={{ padding: 3 }}>
      {inner}
    </span>
  )
}
