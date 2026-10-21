import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { choresApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import type { Chore } from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { Label, Select } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'
import { pairLabel, turnUnits } from '@/lib/chore-format'
import { t, tRich } from '@/lib/i18n'

/** Admin-only direct move. Members hand a chore over via `OfferModal` instead. */
export function ReassignModal({ chore, onClose, onDone }: { chore: Chore; onClose: () => void; onDone: () => void }) {
  // One entry per pair or lone assignee; a person in several pairs is listed once per pair.
  const units = turnUnits(chore)
  const [selected, setSelected] = useState(() => {
    const holders = [chore.currentAssignee?.id, chore.currentPartner?.id].filter(Boolean)
    const held = units.findIndex((u) => holders.length > 0
      && holders.every((id) => id === u.user.id || id === u.partner?.id)
      && holders.length === (u.partner ? 2 : 1))
    return Math.max(held, 0)
  })
  const unit = units[selected]
  // Nobody holds it yet (a Manual chore waiting on an admin), so this is a first placement.
  const unassigned = !chore.currentAssignee

  const mutation = useMutation({
    mutationFn: () => choresApi.reassign(chore.id, { assigneeId: unit.user.id, partnerId: unit.partner?.id ?? null }),
    onSuccess: onDone,
    onError: (err) => toast.error(err instanceof ApiError ? err.message : unassigned ? t('reassign.failedAssign') : t('reassign.failedReassign')),
  })

  return (
    <Modal title={unassigned ? t('reassign.titleAssign') : t('reassign.titleReassign')} onClose={onClose}>
      <form
        className="space-y-4"
        onSubmit={(e) => { e.preventDefault(); mutation.mutate() }}
      >
        <p className="text-sm text-muted-foreground">
          {tRich(unassigned ? 'reassign.introAssign' : 'reassign.introReassign', {
            name: <span className="font-semibold text-foreground">{chore.name}</span>,
          })}
          {chore.assignmentStrategy !== 'Manual' && ` ${t('reassign.future')}`}
        </p>
        <div className="space-y-1.5">
          <Label htmlFor="reassign-assignee">{t('filters.assignee')}</Label>
          <Select id="reassign-assignee" value={selected} onChange={(e) => setSelected(Number(e.target.value))}>
            {units.map((u, i) => (
              <option key={`${u.user.id}-${u.partner?.id ?? ''}`} value={i}>{pairLabel(u.user, u.partner)}</option>
            ))}
          </Select>
        </div>
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" disabled={mutation.isPending || !unit}>
            {mutation.isPending ? t('common.saving') : unassigned ? t('menu.assign') : t('menu.reassign')}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
