import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { authApi, choresApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import type { Chore } from '@/lib/types'
import { useAuthStore } from '@/store/auth'
import { Button } from '@/components/ui/Button'
import { IntegerInput, Label, Select } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'

/**
 * Members put a chore up for someone else to take. Open by default (any other assignee can claim it,
 * first come first served) with an optional single recipient. The bounty comes out of the offerer's
 * balance right away and is refunded if nobody claims, so the cap here mirrors the server's.
 */
export function OfferModal({ chore, onClose, onDone }: { chore: Chore; onClose: () => void; onDone: () => void }) {
  const queryClient = useQueryClient()
  const meId = useAuthStore((s) => s.user?.id)
  // The store's copy is from login time; the server caps the bounty at the real balance, so read the
  // live one to keep the cap on screen honest.
  const storedPoints = useAuthStore((s) => s.user?.points ?? 0)
  const { data: me } = useQuery({ queryKey: ['me'], queryFn: authApi.me })
  const myPoints = me?.points ?? storedPoints

  const [toUserId, setToUserId] = useState('')
  const [bounty, setBounty] = useState(0)

  const candidates = chore.assignees.filter((u) => u.id !== meId)

  const mutation = useMutation({
    mutationFn: () => choresApi.offer(chore.id, { toUserId: toUserId || null, bountyPoints: bounty }),
    onSuccess: () => {
      toast.success(toUserId ? 'Offer sent.' : 'Chore is up for grabs.')
      // The bounty left the offerer's balance, so points on screen are stale.
      void queryClient.invalidateQueries({ queryKey: ['me'] })
      void queryClient.invalidateQueries({ queryKey: ['leaderboard'] })
      onDone()
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : 'Offer failed'),
  })

  return (
    <Modal title={`Offer ${chore.name}`} onClose={onClose}>
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); mutation.mutate() }}>
        <div className="space-y-1.5">
          <Label htmlFor="offer-to">Offer to</Label>
          <Select id="offer-to" value={toUserId} onChange={(e) => setToUserId(e.target.value)}>
            <option value="">Anyone assigned</option>
            {candidates.map((u) => (
              <option key={u.id} value={u.id}>{u.displayName}</option>
            ))}
          </Select>
          <p className="text-xs text-muted-foreground">
            {toUserId
              ? 'Only they can take it, and they can decline.'
              : 'First to claim it gets it.'}
          </p>
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="offer-bounty">Bounty</Label>
          <IntegerInput id="offer-bounty" min={0} max={myPoints} value={bounty} onCommit={setBounty} />
          <p className="text-xs text-muted-foreground">
            {bounty > 0
              ? 'Paid to whoever takes it. Refunded if nobody does.'
              : `Optional. ${myPoints} points available.`}
          </p>
        </div>

        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>Cancel</Button>
          <Button type="submit" disabled={mutation.isPending || bounty > myPoints || candidates.length === 0}>
            {mutation.isPending ? 'Saving…' : 'Offer'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
