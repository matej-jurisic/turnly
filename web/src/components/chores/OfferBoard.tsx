import { useMutation, useQueryClient } from '@tanstack/react-query'
import { choresApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import type { Chore } from '@/lib/types'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'

/**
 * "Up for grabs" band above the chore list: every offer the viewer can actually take, with its bounty
 * and a one-tap Claim. This is the feature's discovery surface and deliberately lives in the main
 * column rather than the `xl`-only rail, so it reaches phones and the native app too. Renders nothing
 * when there's nothing claimable, which is most of the time.
 */
export function OfferBoard({ chores, meId, onSelect }: { chores: Chore[]; meId?: string; onSelect: (chore: Chore) => void }) {
  const queryClient = useQueryClient()

  const claimMutation = useMutation({
    mutationFn: (choreId: string) => choresApi.claimOffer(choreId),
    onSuccess: (chore) => {
      toast.success(`"${chore.name}" is yours.`)
      void queryClient.invalidateQueries({ queryKey: ['chores'] })
      // A bounty moved between two balances.
      void queryClient.invalidateQueries({ queryKey: ['me'] })
      void queryClient.invalidateQueries({ queryKey: ['leaderboard'] })
    },
    // Losing a claim race is a 409 from the server; its message already explains it.
    onError: (err) => {
      toast.error(err instanceof ApiError ? err.message : 'Claim failed')
      void queryClient.invalidateQueries({ queryKey: ['chores'] })
    },
  })

  const claimable = chores.filter((c) => {
    const offer = c.openOffer
    if (!offer || !meId || offer.fromUser.id === meId) return false
    // Targeted offers are for their recipient only; open ones for any other assignee.
    return offer.toUser ? offer.toUser.id === meId : c.assignees.some((u) => u.id === meId)
  })

  if (claimable.length === 0) return null

  return (
    <section aria-label="Chores up for grabs">
      <h2 className="mb-2 px-1 text-sm font-semibold text-foreground">Up for grabs</h2>
      <Card className="divide-y divide-border p-0">
        {claimable.map((chore) => {
          const offer = chore.openOffer!
          return (
            <div key={chore.id} className="flex items-center gap-3 px-3 py-2.5">
              <button
                type="button"
                onClick={() => onSelect(chore)}
                className="flex min-w-0 flex-1 items-center gap-2 text-left"
              >
                {chore.emoji && <span aria-hidden>{chore.emoji}</span>}
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm text-foreground">{chore.name}</span>
                  <span className="block truncate text-xs text-muted-foreground">
                    from {offer.fromUser.displayName}
                    {offer.toUser ? ', just for you' : ''}
                  </span>
                </span>
              </button>
              {offer.bountyPoints > 0 && (
                <Badge tone="amber">+{offer.bountyPoints}</Badge>
              )}
              <Button
                size="sm"
                onClick={() => claimMutation.mutate(chore.id)}
                disabled={claimMutation.isPending}
              >
                Claim
              </Button>
            </div>
          )
        })}
      </Card>
    </section>
  )
}
