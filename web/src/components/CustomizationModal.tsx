import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { gachaApi } from '@/lib/api'
import { useAuthStore } from '@/store/auth'
import { useThemeStore } from '@/lib/theme'
import { applyPalette } from '@/lib/palette'
import { syncAppearanceFromServer } from '@/lib/appearance'
import { frameClasses, RARITY_COLOR } from '@/lib/cosmetics'
import type { CosmeticRarity } from '@/lib/types'
import { toast } from '@/lib/toast'
import { Modal } from '@/components/ui/Modal'

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
      <Modal title="Customization" onClose={onClose}>
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
    <Modal title="Customization" onClose={onClose}>
      <div className="space-y-6">
        {/* Theme */}
        <section className="space-y-2">
          <h3 className="text-sm font-semibold text-foreground">Theme</h3>
          <div className="grid grid-cols-3 gap-2 sm:grid-cols-4">
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
          </div>
        </section>

        {/* Avatar color */}
        <section className="space-y-2">
          <h3 className="text-sm font-semibold text-foreground">Avatar color</h3>
          <div className="flex flex-wrap gap-3">
            {ownedColors.map((c) => {
              const selected = (c.value ?? '').toLowerCase() === activeColor
              return (
                <div key={c.key} className="flex flex-col items-center gap-1">
                  <button
                    type="button"
                    onClick={() => chooseColor(c.key)}
                    disabled={busy}
                    aria-pressed={selected}
                    title={c.name}
                    className={`h-9 w-9 rounded-full transition disabled:opacity-60 ${
                      selected ? 'ring-2 ring-ring ring-offset-2 ring-offset-card' : ''
                    }`}
                    style={{ backgroundColor: c.value ?? undefined }}
                  />
                  <RarityTag rarity={c.rarity} />
                </div>
              )
            })}
          </div>
        </section>

        {/* Avatar emoji */}
        <section className="space-y-2">
          <h3 className="text-sm font-semibold text-foreground">Avatar emoji</h3>
          <div className="grid grid-cols-3 gap-2 sm:grid-cols-4">
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
          </div>
        </section>

        {/* Avatar frame */}
        <section className="space-y-2">
          <h3 className="text-sm font-semibold text-foreground">Avatar frame</h3>
          <div className="grid grid-cols-3 gap-2 sm:grid-cols-4">
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
          </div>
        </section>
      </div>
    </Modal>
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
      className={`flex flex-col items-center gap-1.5 rounded-xl border p-2 text-center transition-colors disabled:opacity-60 ${
        selected ? 'border-primary bg-primary/5' : 'border-border hover:bg-accent'
      }`}
    >
      <span className="flex h-12 w-full items-center justify-center">{preview}</span>
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
  return (
    <span
      className="flex h-11 w-11 items-center justify-center rounded-lg border border-border"
      style={{
        background: mode === 'dark' ? '#0f0f11' : '#f5f5f7',
        color: mode === 'dark' ? '#ececef' : '#1d1d22',
      }}
    >
      <span className="h-4 w-4 rounded-full" style={{ background: mode === 'dark' ? '#7b6ef6' : '#5b4ee8' }} />
    </span>
  )
}

function PalettePreview({ paletteKey }: { paletteKey: string }) {
  // Scope the palette to this preview only via data-palette.
  return (
    <span data-palette={paletteKey} className="flex h-11 w-11 overflow-hidden rounded-lg border border-border bg-background">
      <span className="flex flex-1 flex-col gap-1 p-1.5">
        <span className="h-2 w-6 rounded-full bg-primary" />
        <span className="h-2 w-8 rounded-full bg-muted" />
        <span className="mt-auto h-2.5 w-full rounded bg-card" />
      </span>
    </span>
  )
}

function GlyphPreview({ emoji, color, name }: { emoji: string | null; color: string; name: string }) {
  const glyph = emoji || name.trim().slice(0, 2).toUpperCase()
  return (
    <span
      className="gx-av inline-flex h-9 w-9 items-center justify-center overflow-hidden rounded-full text-white"
      style={{ backgroundColor: color }}
    >
      {emoji ? (
        <span className="block w-full text-center text-lg leading-none">{glyph}</span>
      ) : (
        <span className="text-xs font-medium">{glyph}</span>
      )}
    </span>
  )
}

function FramePreview({ frameKey, color, name }: { frameKey: string | null; color: string; name: string }) {
  const initials = name.trim().slice(0, 2).toUpperCase()
  const inner = (
    <span
      className="gx-av inline-flex h-9 w-9 items-center justify-center rounded-full text-xs font-medium text-white"
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
