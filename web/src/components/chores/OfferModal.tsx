import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { authApi, choresApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import type { Chore } from '@/lib/types'
import { useAuthStore } from '@/store/auth'
import { Button } from '@/components/ui/Button'
import { IntegerInput, Label, Select } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'
import { plural, t } from '@/lib/i18n'

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
      const recipient = candidates.find((u) => u.id === toUserId)
      toast.success(recipient ? t('offerModal.offeredTo', { name: recipient.displayName }) : t('offerModal.open'))
      // The bounty left the offerer's balance, so points on screen are stale.
      void queryClient.invalidateQueries({ queryKey: ['me'] })
      void queryClient.invalidateQueries({ queryKey: ['leaderboard'] })
      onDone()
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('offerModal.failed')),
  })

  return (
    <Modal title={t('offerModal.title', { name: chore.name })} onClose={onClose}>
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); mutation.mutate() }}>
        <div className="space-y-1.5">
          <Label htmlFor="offer-to">{t('offerModal.to')}</Label>
          <Select id="offer-to" value={toUserId} onChange={(e) => setToUserId(e.target.value)}>
            <option value="">{t('offerModal.anyone')}</option>
            {candidates.map((u) => (
              <option key={u.id} value={u.id}>{u.displayName}</option>
            ))}
          </Select>
          <p className="text-xs text-muted-foreground">
            {toUserId
              ? t('offerModal.targetHint')
              : t('offerModal.openHint')}
          </p>
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="offer-bounty">{t('offerModal.bounty')}</Label>
          <IntegerInput id="offer-bounty" min={0} max={myPoints} value={bounty} onCommit={setBounty} />
          <p className="text-xs text-muted-foreground">
            {bounty > 0
              ? t('offerModal.bountyHint')
              : plural('offerModal.available', myPoints)}
          </p>
        </div>

        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" disabled={mutation.isPending || bounty > myPoints || candidates.length === 0}>
            {mutation.isPending ? t('common.saving') : t('menu.offer')}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
