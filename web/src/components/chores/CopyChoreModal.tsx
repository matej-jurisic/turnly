import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { choresApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import type { Chore } from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { Input, Label } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'
import { t, tRich } from '@/lib/i18n'

export function CopyChoreModal({ chore, onClose, onDone }: { chore: Chore; onClose: () => void; onDone: () => void }) {
  const [newName, setNewName] = useState(chore.name)

  const mutation = useMutation({
    mutationFn: () => choresApi.copy(chore.id, { newName: newName.trim() }),
    onSuccess: () => {
      toast.success(t('copy.done'))
      onDone()
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('copy.failed')),
  })

  return (
    <Modal title={t('copy.title')} onClose={onClose}>
      <form
        className="space-y-4"
        onSubmit={(e) => { e.preventDefault(); mutation.mutate() }}
      >
        <p className="text-sm text-muted-foreground">
          {tRich('copy.intro', {
            name: <span className="font-semibold text-foreground">{chore.name}</span>,
          })}
        </p>
        <div className="space-y-1.5">
          <Label htmlFor="copy-name">{t('form.name')}</Label>
          <Input
            id="copy-name"
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
            autoFocus
            required
          />
        </div>
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" disabled={mutation.isPending || !newName.trim()}>
            {mutation.isPending ? t('copy.copying') : t('menu.copy')}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
