import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { gachaApi } from '@/lib/api'
import { useAuthStore } from '@/store/auth'
import { syncAppearanceFromServer } from '@/lib/appearance'
import { celebrate } from '@/lib/confetti'
import { frameClasses, RARITY_COLOR, RARITY_ORDER } from '@/lib/cosmetics'
import { toast } from '@/lib/toast'
import type { Cosmetic, CosmeticRarity, CosmeticSlot, PullResult } from '@/lib/types'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/Card'
import { Modal } from '@/components/ui/Modal'

export function GachaPage() {
  const user = useAuthStore((s) => s.user)
  const queryClient = useQueryClient()
  const [reveal, setReveal] = useState<PullResult[] | null>(null)

  const { data: state, isLoading } = useQuery({ queryKey: ['gacha'], queryFn: gachaApi.state })

  const pull = useMutation({
    mutationFn: (count: number) => gachaApi.pull(count),
    onSuccess: async (results) => {
      await syncAppearanceFromServer(queryClient)
      setReveal(results)
    },
    onError: (e: Error) => toast.error(e.message),
  })

  const craft = useMutation({
    mutationFn: (key: string) => gachaApi.craft(key),
    onSuccess: async () => {
      await syncAppearanceFromServer(queryClient)
      toast.success('Crafted!')
    },
    onError: (e: Error) => toast.error(e.message),
  })

  if (!user) return null
  if (isLoading || !state) {
    return <div className="text-muted-foreground">Loading…</div>
  }

  const busy = pull.isPending || craft.isPending
  const pityPct = Math.min(100, Math.round((state.pullsSinceLegendary / state.pityThreshold) * 100))

  // Group the catalog by slot, then by rarity (rarest first).
  const bySlot: Record<CosmeticSlot, Cosmetic[]> = { Frame: [], Theme: [], Color: [] }
  for (const c of state.cosmetics) bySlot[c.slot].push(c)

  return (
    <div className="space-y-8">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold text-foreground">Gacha</h1>
        <div className="flex items-center gap-2">
          <Badge tone="violet">{state.points} pts</Badge>
          <Badge tone="amber">{state.dust} dust</Badge>
        </div>
      </div>

      {/* Pull panel */}
      <Card>
        <CardHeader>
          <CardTitle>Pull</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex flex-wrap gap-3">
            <Button
              onClick={() => pull.mutate(1)}
              disabled={busy || state.points < state.pullCost}
            >
              Pull x1 ({state.pullCost} pts)
            </Button>
            <Button
              variant="secondary"
              onClick={() => pull.mutate(10)}
              disabled={busy || state.points < state.tenPullCost}
            >
              Pull x10 ({state.tenPullCost} pts)
            </Button>
          </div>

          {/* Pity */}
          <div className="space-y-1">
            <div className="flex justify-between text-xs text-muted-foreground">
              <span>Pity: guaranteed Legendary</span>
              <span>
                {state.pullsSinceLegendary} / {state.pityThreshold}
              </span>
            </div>
            <div className="h-2 overflow-hidden rounded-full bg-muted">
              <div className="h-full rounded-full bg-primary transition-all" style={{ width: `${pityPct}%` }} />
            </div>
          </div>

          <OddsDisclosure
            odds={state.odds.map((o) => ({ rarity: o.rarity, odds: o.odds }))}
          />
        </CardContent>
      </Card>

      {/* Collection */}
      <p className="text-sm text-muted-foreground">
        Equip what you unlock from the account menu (Customization).
      </p>
      <CollectionSection
        title="Avatar frames"
        items={bySlot.Frame}
        userColor={user.avatarColor}
        userName={user.displayName}
        dust={state.dust}
        busy={busy}
        onCraft={(key) => craft.mutate(key)}
      />
      <CollectionSection
        title="Avatar colors"
        items={bySlot.Color}
        userColor={user.avatarColor}
        userName={user.displayName}
        dust={state.dust}
        busy={busy}
        onCraft={(key) => craft.mutate(key)}
      />
      <CollectionSection
        title="App themes"
        items={bySlot.Theme}
        userColor={user.avatarColor}
        userName={user.displayName}
        dust={state.dust}
        busy={busy}
        onCraft={(key) => craft.mutate(key)}
      />

      {reveal && <RevealModal results={reveal} onClose={() => setReveal(null)} />}
    </div>
  )
}

function OddsDisclosure({ odds }: { odds: { rarity: CosmeticRarity; odds: number }[] }) {
  return (
    <details className="text-sm">
      <summary className="cursor-pointer text-muted-foreground">Drop rates</summary>
      <ul className="mt-2 space-y-1">
        {RARITY_ORDER.map((r) => {
          const o = odds.find((x) => x.rarity === r)
          if (!o) return null
          return (
            <li key={r} className="flex items-center justify-between">
              <RarityBadge rarity={r} />
              <span className="text-muted-foreground">{(o.odds * 100).toFixed(o.odds < 0.1 ? 1 : 0)}%</span>
            </li>
          )
        })}
      </ul>
    </details>
  )
}

function CollectionSection({
  title,
  items,
  userColor,
  userName,
  dust,
  busy,
  onCraft,
}: {
  title: string
  items: Cosmetic[]
  userColor: string
  userName: string
  dust: number
  busy: boolean
  onCraft: (key: string) => void
}) {
  const ordered = [...items].sort(
    (a, b) => RARITY_ORDER.indexOf(a.rarity) - RARITY_ORDER.indexOf(b.rarity),
  )
  const ownedCount = items.filter((c) => c.owned).length

  return (
    <section className="space-y-3">
      <div className="flex items-center justify-between">
        <h2 className="text-base font-semibold text-foreground">{title}</h2>
        <span className="text-sm text-muted-foreground">
          {ownedCount} / {items.length}
        </span>
      </div>
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
        {ordered.map((c) => (
          <CosmeticCard
            key={c.key}
            cosmetic={c}
            userColor={userColor}
            userName={userName}
            dust={dust}
            busy={busy}
            onCraft={() => onCraft(c.key)}
          />
        ))}
      </div>
    </section>
  )
}

function CosmeticCard({
  cosmetic: c,
  userColor,
  userName,
  dust,
  busy,
  onCraft,
}: {
  cosmetic: Cosmetic
  userColor: string
  userName: string
  dust: number
  busy: boolean
  onCraft: () => void
}) {
  return (
    <div
      className={`flex flex-col items-center gap-2 rounded-xl border p-3 text-center ${
        c.equipped ? 'border-primary bg-primary/5' : 'border-border bg-card'
      } ${c.owned ? '' : 'opacity-90'}`}
    >
      <CosmeticPreview cosmetic={c} userColor={userColor} userName={userName} />
      <div className="min-h-[2.5rem] space-y-0.5">
        <div className="text-sm font-medium text-foreground">{c.name}</div>
        <RarityBadge rarity={c.rarity} />
      </div>

      {c.owned ? (
        <span className={`text-xs ${c.equipped ? 'font-medium text-primary' : 'text-muted-foreground'}`}>
          {c.equipped ? 'Equipped' : 'Owned'}
        </span>
      ) : (
        <Button
          size="sm"
          variant="secondary"
          className="w-full"
          disabled={busy || dust < c.dustCraftCost}
          onClick={onCraft}
          title={dust < c.dustCraftCost ? 'Not enough dust' : undefined}
        >
          Craft ({c.dustCraftCost} dust)
        </Button>
      )}
    </div>
  )
}

/** Visual preview: a framed avatar for frame cosmetics, a palette swatch for theme cosmetics. */
function CosmeticPreview({
  cosmetic: c,
  userColor,
  userName,
}: {
  cosmetic: Cosmetic
  userColor: string
  userName: string
}) {
  const dimmed = c.owned ? '' : 'opacity-60 grayscale'

  if (c.slot === 'Color') {
    const initials = userName.trim().slice(0, 2).toUpperCase()
    return (
      <span
        className={`inline-flex h-12 w-12 items-center justify-center rounded-full text-base font-medium text-white ${dimmed}`}
        style={{ backgroundColor: c.value ?? '#6366f1' }}
      >
        {initials}
      </span>
    )
  }

  if (c.slot === 'Theme') {
    // Scope the palette to this preview box only via the data-palette attribute.
    return (
      <div
        data-palette={c.key}
        className={`flex h-12 w-16 overflow-hidden rounded-lg border border-border bg-background ${dimmed}`}
      >
        <div className="flex flex-1 flex-col gap-1 p-1.5">
          <div className="h-2 w-8 rounded-full bg-primary" />
          <div className="h-2 w-10 rounded-full bg-muted" />
          <div className="mt-auto h-3 w-full rounded bg-card" />
        </div>
      </div>
    )
  }

  const initials = userName.trim().slice(0, 2).toUpperCase()
  return (
    <span className={`${frameClasses(c.key)} ${dimmed}`} style={{ padding: 4 }}>
      <span
        className="gx-av inline-flex h-12 w-12 items-center justify-center rounded-full text-base font-medium text-white"
        style={{ backgroundColor: userColor }}
      >
        {initials}
      </span>
    </span>
  )
}

function RevealModal({ results, onClose }: { results: PullResult[]; onClose: () => void }) {
  // A burst on open; reduced-motion aware via celebrate().
  useEffect(() => {
    celebrate()
  }, [])

  const newCount = results.filter((r) => r.isNew).length
  const dust = results.reduce((sum, r) => sum + r.dustAwarded, 0)

  return (
    <Modal title={results.length > 1 ? 'Your 10-pull' : 'You pulled'} onClose={onClose}>
      <div className="space-y-4">
        <div className="grid grid-cols-3 gap-3 sm:grid-cols-5">
          {results.map((r, i) => (
            <div key={i} className="flex flex-col items-center gap-1 text-center">
              <span
                className="inline-block h-3 w-3 rounded-full"
                style={{ background: RARITY_COLOR[r.cosmetic.rarity] }}
              />
              <span className="text-xs font-medium text-foreground">{r.cosmetic.name}</span>
              <span className="text-[11px] text-muted-foreground">
                {r.isNew ? 'New!' : `+${r.dustAwarded} dust`}
              </span>
            </div>
          ))}
        </div>
        <p className="text-center text-sm text-muted-foreground">
          {newCount > 0 ? `${newCount} new unlocked. ` : ''}
          {dust > 0 ? `${dust} dust from duplicates.` : ''}
          {newCount === 0 && dust === 0 ? 'Better luck next time.' : ''}
        </p>
        <div className="flex justify-center">
          <Button onClick={onClose}>Nice!</Button>
        </div>
      </div>
    </Modal>
  )
}

/** A rarity pill in fixed rarity colors (not theme tokens) so tiers stay distinct under any palette. */
function RarityBadge({ rarity }: { rarity: CosmeticRarity }) {
  const color = RARITY_COLOR[rarity]
  return (
    <span
      className="badge"
      style={{ background: `color-mix(in srgb, ${color} 16%, transparent)`, color }}
    >
      {rarity}
    </span>
  )
}
