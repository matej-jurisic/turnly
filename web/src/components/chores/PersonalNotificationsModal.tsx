import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { notificationsApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import type { Chore, ChoreNotification, ChoreNotificationInput } from '@/lib/types'
import { isIndependent } from '@/lib/chore-format'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { NotificationsEditor } from '@/components/NotificationsEditor'
import { t } from '@/lib/i18n'

const toInput = (n: ChoreNotification): ChoreNotificationInput => ({
  type: n.type,
  timing: n.timing,
  offsetValue: n.offsetValue,
  offsetUnit: n.offsetUnit,
  recipients: n.recipients,
})

/**
 * The viewer's own notification entries for one chore, edited with the same editor the chore form
 * uses. They replace the chore's entries for the viewer only. Starts from the viewer's entries, or a
 * copy of the chore's when they have none yet; saving an empty list goes back to the chore's.
 */
export function PersonalNotificationsModal({ chore, onClose }: { chore: Chore; onClose: () => void }) {
  const queryClient = useQueryClient()
  const hasOwn = chore.myNotifications.length > 0
  const [entries, setEntries] = useState<ChoreNotificationInput[]>(
    (hasOwn ? chore.myNotifications : chore.notifications).map(toInput),
  )

  const mutation = useMutation({
    mutationFn: (next: ChoreNotificationInput[]) => notificationsApi.setPersonal(chore.id, next),
    onSuccess: (saved) => {
      queryClient.invalidateQueries({ queryKey: ['chores'] })
      toast.success(saved.length > 0 ? t('myNotif.saved') : t('myNotif.reset'))
      onClose()
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('common.error')),
  })

  return (
    <Modal title={t('myNotif.title', { name: chore.name })} onClose={onClose}>
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); mutation.mutate(entries) }}>
        <NotificationsEditor value={entries} onChange={setEntries} independent={isIndependent(chore)} personal />

        <div className="flex flex-wrap justify-end gap-2">
          {hasOwn && (
            <Button type="button" variant="secondary" className="mr-auto" disabled={mutation.isPending} onClick={() => mutation.mutate([])}>
              {t('myNotif.useChore')}
            </Button>
          )}
          <Button type="button" variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" disabled={mutation.isPending}>
            {mutation.isPending ? t('common.saving') : t('common.save')}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
