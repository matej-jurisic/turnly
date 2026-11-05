import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { choresApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import type { Chore } from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { Label, Select } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'
import { defaultSlot, focusSlot, hasSlotTurns, pairLabel, turnUnits } from '@/lib/chore-format'
import { SlotPicker } from '@/components/chores/SlotPicker'
import { t, tRich } from '@/lib/i18n'

/** Admin-only direct move. Members hand a chore over via `OfferModal` instead. */
export function ReassignModal({ chore: fullChore, onClose, onDone }: { chore: Chore; onClose: () => void; onDone: () => void }) {
  // A slot-turn chore moves one slot at a time.
  const slotted = hasSlotTurns(fullChore)
  const [slotTime, setSlotTime] = useState(() => defaultSlot(fullChore)?.time)
  const chore = focusSlot(fullChore, slotted ? fullChore.slotTurns.find((s) => s.time === slotTime) : undefined)
  // One entry per pair or lone assignee; a person in several pairs is listed once per pair.
  const units = turnUnits(chore)
  const heldUnit = (c: Chore) => {
    const holders = [c.currentAssignee?.id, c.currentPartner?.id].filter(Boolean)
    const held = units.findIndex((u) => holders.length > 0
      && holders.every((id) => id === u.user.id || id === u.partner?.id)
      && holders.length === (u.partner ? 2 : 1))
    return Math.max(held, 0)
  }
  const [selected, setSelected] = useState(() => heldUnit(chore))
  const unit = units[selected]
  // Nobody holds it yet (a Manual chore waiting on an admin), so this is a first placement.
  const unassigned = !chore.currentAssignee
  const pickSlot = (time: string) => {
    setSlotTime(time)
    setSelected(heldUnit(focusSlot(fullChore, fullChore.slotTurns.find((s) => s.time === time))))
  }

  const mutation = useMutation({
    mutationFn: () => choresApi.reassign(chore.id, {
      assigneeId: unit.user.id,
      partnerId: unit.partner?.id ?? null,
      slotTime: slotted ? slotTime : null,
    }),
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
        {slotted && <SlotPicker slots={fullChore.slotTurns} value={slotTime} onChange={pickSlot} />}
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
