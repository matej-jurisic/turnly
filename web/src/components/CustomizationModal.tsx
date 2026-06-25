import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { gachaApi } from '@/lib/api'
import { useAuthStore } from '@/store/auth'
import { useThemeStore } from '@/lib/theme'
import { applyPalette } from '@/lib/palette'
import { syncAppearanceFromServer } from '@/lib/appearance'
import { frameClasses } from '@/lib/cosmetics'
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

  const { data: state } = useQuery({ queryKey: ['gacha'], queryFn: gachaApi.state })

  const equip = useMutation({
    mutationFn: ({ slot, key }: { slot: 'Frame' | 'Theme' | 'Color'; key: string | null }) =>
      gachaApi.equip(slot, key),
    onSuccess: (_data, { slot, key }) => {
      if (slot === 'Theme') applyPalette(key)
      return syncAppearanceFromServer(queryClient)
    },
    onError: (e: Error) => toast.error(e.message),
  })

  if (!user) return null

  const ownedThemes = (state?.cosmetics ?? []).filter((c) => c.slot === 'Theme' && c.owned)
  const ownedFrames = (state?.cosmetics ?? []).filter((c) => c.slot === 'Frame' && c.owned)
  const ownedColors = (state?.cosmetics ?? []).filter((c) => c.slot === 'Color' && c.owned)
  const activePalette = user.equippedThemeKey ?? null
  const activeFrame = user.equippedFrameKey ?? null
  const activeColor = user.avatarColor.toLowerCase()

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
                selected={activePalette === c.key}
                disabled={busy}
                onClick={() => choosePalette(c.key)}
                preview={<PalettePreview paletteKey={c.key} />}
              />
            ))}
          </div>
          {ownedThemes.length === 0 && (
            <p className="text-xs text-muted-foreground">
              Unlock theme palettes in the Gacha to add more options here.
            </p>
          )}
        </section>

        {/* Avatar color */}
        <section className="space-y-2">
          <h3 className="text-sm font-semibold text-foreground">Avatar color</h3>
          <div className="flex flex-wrap gap-2">
            {ownedColors.map((c) => {
              const selected = (c.value ?? '').toLowerCase() === activeColor
              return (
                <button
                  key={c.key}
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
              )
            })}
          </div>
          {ownedColors.length <= 1 && (
            <p className="text-xs text-muted-foreground">Unlock more avatar colors in the Gacha.</p>
          )}
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
                selected={activeFrame === c.key}
                disabled={busy}
                onClick={() => chooseFrame(c.key)}
                preview={<FramePreview frameKey={c.key} color={user.avatarColor} name={user.displayName} />}
              />
            ))}
          </div>
          {ownedFrames.length === 0 && (
            <p className="text-xs text-muted-foreground">
              Unlock avatar frames in the Gacha to add more options here.
            </p>
          )}
        </section>
      </div>
    </Modal>
  )
}

function SwatchTile({
  label,
  selected,
  disabled,
  onClick,
  preview,
}: {
  label: string
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
    </button>
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
