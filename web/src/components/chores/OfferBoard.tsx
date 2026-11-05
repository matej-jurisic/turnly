import { useMutation, useQueryClient } from '@tanstack/react-query'
import { choresApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import type { Chore, ChoreOffer } from '@/lib/types'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { t } from '@/lib/i18n'
import { slotName } from '@/lib/chore-format'

/**
 * "Up for grabs" band above the chore list: every offer the viewer can actually take, with its bounty
 * and a one-tap Claim. This is the feature's discovery surface and deliberately lives in the main
 * column rather than the `xl`-only rail, so it reaches phones and the native app too. Renders nothing
 * when there's nothing claimable, which is most of the time.
 */
export function OfferBoard({ chores, meId, onSelect }: { chores: Chore[]; meId?: string; onSelect: (chore: Chore) => void }) {
  const queryClient = useQueryClient()

  const claimMutation = useMutation({
    mutationFn: ({ choreId, slotTime }: { choreId: string; slotTime?: string | null }) =>
      choresApi.claimOffer(choreId, slotTime),
    onSuccess: (chore) => {
      toast.success(t('board.yours', { name: chore.name }))
      void queryClient.invalidateQueries({ queryKey: ['chores'] })
      // A bounty moved between two balances.
      void queryClient.invalidateQueries({ queryKey: ['me'] })
      void queryClient.invalidateQueries({ queryKey: ['leaderboard'] })
    },
    // Losing a claim race is a 409 from the server; its message already explains it.
    onError: (err) => {
      toast.error(err instanceof ApiError ? err.message : t('offer.claimFailed'))
      void queryClient.invalidateQueries({ queryKey: ['chores'] })
    },
  })

  // One row per offer: a slot-turn chore can have one on each slot.
  const claimable = chores.flatMap((c) => {
    if (!meId || !c.assignees.some((u) => u.id === meId)) return []
    const offers = c.turnPerSlot && c.slotTurns.length
      ? c.slotTurns.flatMap((s) => (s.openOffer ? [{ offer: s.openOffer, slot: slotName(s) }] : []))
      : c.openOffer ? [{ offer: c.openOffer, slot: null }] : []
    return offers
      .filter(({ offer }) => offer.fromUser.id !== meId)
      .map(({ offer, slot }) => ({ chore: c, offer: offer as ChoreOffer, slot }))
  })

  if (claimable.length === 0) return null

  return (
    <section aria-label={t('board.label')}>
      <h2 className="mb-2 px-1 text-sm font-semibold text-foreground">{t('offer.upForGrabs')}</h2>
      <Card className="divide-y divide-border p-0">
        {claimable.map(({ chore, offer, slot }) => {
          return (
            <div key={offer.id} className="flex items-center gap-3 px-3 py-2.5">
              <button
                type="button"
                onClick={() => onSelect(chore)}
                className="flex min-w-0 flex-1 items-center gap-2 text-left"
              >
                {chore.emoji && <span aria-hidden>{chore.emoji}</span>}
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm text-foreground">{slot ? `${chore.name} (${slot})` : chore.name}</span>
                  <span className="block truncate text-xs text-muted-foreground">
                    {t('board.from', { name: offer.fromUser.displayName })}
                  </span>
                </span>
              </button>
              {offer.bountyPoints > 0 && (
                <Badge tone="amber">+{offer.bountyPoints}</Badge>
              )}
              <Button
                size="sm"
                onClick={() => claimMutation.mutate({ choreId: chore.id, slotTime: offer.slotTime })}
                disabled={claimMutation.isPending}
              >
                {t('offer.claim')}
              </Button>
            </div>
          )
        })}
      </Card>
    </section>
  )
}
