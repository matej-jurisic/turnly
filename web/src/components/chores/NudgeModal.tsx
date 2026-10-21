import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { choresApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import type { Chore } from '@/lib/types'
import { nudgeCandidates } from '@/lib/chore-format'
import { useAuthStore } from '@/store/auth'
import { Button } from '@/components/ui/Button'
import { Label, Select, Textarea } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'
import { t } from '@/lib/i18n'

/** Mirrors `Validators.MaxNudgeNoteLength`. */
const MAX_NOTE = 200

/**
 * Anyone can remind whoever owes a chore, with an optional note. Defaults to everyone holding it
 * (a pair, or every open track on a per-person chore); the picker only appears when there's a
 * choice. Nothing about the chore changes, so there's nothing to invalidate.
 */
export function NudgeModal({ chore, onClose }: { chore: Chore; onClose: () => void }) {
  const meId = useAuthStore((s) => s.user?.id)
  const candidates = nudgeCandidates(chore, meId)

  const [toUserId, setToUserId] = useState('')
  const [note, setNote] = useState('')

  const mutation = useMutation({
    mutationFn: () => choresApi.nudge(chore.id, { toUserId: toUserId || null, note: note.trim() || null }),
    onSuccess: (nudged) => {
      toast.success(t('nudgeModal.sentTo', { names: nudged.map((u) => u.displayName).join(', ') }))
      onClose()
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('nudgeModal.failed')),
  })

  return (
    <Modal title={t('nudgeModal.title', { name: chore.name })} onClose={onClose}>
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); mutation.mutate() }}>
        {candidates.length > 1 && (
          <div className="space-y-1.5">
            <Label htmlFor="nudge-to">{t('nudgeModal.to')}</Label>
            <Select id="nudge-to" value={toUserId} onChange={(e) => setToUserId(e.target.value)}>
              <option value="">{t('nudgeModal.everyone')}</option>
              {candidates.map((u) => (
                <option key={u.id} value={u.id}>{u.displayName}</option>
              ))}
            </Select>
          </div>
        )}

        <div className="space-y-1.5">
          <Label htmlFor="nudge-note">{t('nudgeModal.note')}</Label>
          <Textarea
            id="nudge-note"
            rows={3}
            maxLength={MAX_NOTE}
            value={note}
            onChange={(e) => setNote(e.target.value)}
          />
          <div className="flex justify-between gap-3 text-xs text-muted-foreground">
            <p>{t('nudgeModal.hint')}</p>
            <span className="shrink-0 tabular-nums">{note.length}/{MAX_NOTE}</span>
          </div>
        </div>

        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" disabled={mutation.isPending || candidates.length === 0}>
            {mutation.isPending ? t('nudgeModal.sending') : t('nudgeModal.send')}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
