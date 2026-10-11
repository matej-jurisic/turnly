import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { choresApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import type { Chore } from '@/lib/types'
import { toLocalDueInstant } from '@/lib/chore-format'
import { Button } from '@/components/ui/Button'
import { Input, Label } from '@/components/ui/Field'
import { TimeField } from '@/components/ui/TimeField'
import { Modal } from '@/components/ui/Modal'
import { t, tRich } from '@/lib/i18n'

/** Local YYYY-MM-DD for a date input, derived from an ISO instant in the browser's timezone. */
function localDateInput(iso: string): string {
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

export function RescheduleModal({ chore, onClose, onDone }: { chore: Chore; onClose: () => void; onDone: () => void }) {
  const [date, setDate] = useState(chore.dueAt ? localDateInput(chore.dueAt) : '')
  const [time, setTime] = useState(chore.dueTime ?? '')

  const mutation = useMutation({
    mutationFn: () => choresApi.reschedule(chore.id, { dueAt: toLocalDueInstant(date, time), dueTime: time || null }),
    onSuccess: onDone,
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('reschedule.failed')),
  })

  return (
    <Modal title={t('reschedule.title')} onClose={onClose}>
      <form
        className="space-y-4"
        onSubmit={(e) => { e.preventDefault(); mutation.mutate() }}
      >
        <p className="text-sm text-muted-foreground">
          {tRich('reschedule.intro', {
            name: <span className="font-semibold text-foreground">{chore.name}</span>,
          })}
        </p>
        <div className="space-y-1.5">
          <Label htmlFor="reschedule-date">{t('reschedule.dueDate')}</Label>
          <Input id="reschedule-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} required />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="reschedule-time">{t('form.dueTime')}</Label>
          <TimeField id="reschedule-time" value={time} onChange={setTime} />
        </div>
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" disabled={mutation.isPending || !date}>
            {mutation.isPending ? t('common.saving') : t('menu.reschedule')}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
