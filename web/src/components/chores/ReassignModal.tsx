import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { choresApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import type { Chore } from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { Label, Select } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'
import { pairLabel, partnerOf } from '@/lib/chore-format'
import { t, tRich } from '@/lib/i18n'

/** Admin-only direct move. Members hand a chore over via `OfferModal` instead. */
export function ReassignModal({ chore, onClose, onDone }: { chore: Chore; onClose: () => void; onDone: () => void }) {
  const [assigneeId, setAssigneeId] = useState(chore.currentAssignee?.id ?? chore.assignees[0]?.id ?? '')
  // Nobody holds it yet (a Manual chore waiting on an admin), so this is a first placement.
  const unassigned = !chore.currentAssignee

  const mutation = useMutation({
    mutationFn: () => choresApi.reassign(chore.id, { assigneeId }),
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
          <Select id="reassign-assignee" value={assigneeId} onChange={(e) => setAssigneeId(e.target.value)}>
            {chore.assignees.map((u) => (
              <option key={u.id} value={u.id}>{pairLabel(u, partnerOf(chore, u.id))}</option>
            ))}
          </Select>
        </div>
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" disabled={mutation.isPending || !assigneeId}>
            {mutation.isPending ? t('common.saving') : unassigned ? t('menu.assign') : t('menu.reassign')}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
