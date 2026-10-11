import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { choresApi, usersApi, ApiError } from '@/lib/api'
import { celebrate } from '@/lib/confetti'
import { celebrateAchievements } from '@/lib/achievementCelebration'
import { isIndependent, toLocalDateInput, toLocalDueInstant } from '@/lib/chore-format'
import { useAuthStore } from '@/store/auth'
import type { Chore, User } from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { Input, Label, Select } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'
import { TimeField } from '@/components/ui/TimeField'
import { plural, t, tRich } from '@/lib/i18n'

interface CompleteModalProps {
  chore: Chore
  onClose: () => void
  onDone: () => void
}

export function CompleteModal({ chore, onClose, onDone }: CompleteModalProps) {
  const currentUser = useAuthStore((s) => s.user)
  const isAdmin = currentUser?.role === 'Admin'

  // Track-mode chores can only be completed for one of their assignees (each has their own
  // schedule), so the credit picker is limited to them and defaults to a valid owner.
  const isIndep = isIndependent(chore)
  const trackUsers = chore.tracks.map((t) => t.user)
  const viewerOwnsTrack = trackUsers.some((u) => u.id === currentUser?.id)

  const [notes, setNotes] = useState('')
  const [completedByUserId, setCompletedByUserId] = useState(
    isIndep && !viewerOwnsTrack ? trackUsers[0]?.id ?? '' : currentUser?.id ?? '',
  )
  // Backdating: off by default (the completion is stamped "now"). Turning it on reveals a date and
  // time, pre-filled with the current moment so only the part that differs needs changing.
  const [backdate, setBackdate] = useState(false)
  const [doneDate, setDoneDate] = useState(() => toLocalDateInput(new Date()))
  const [doneTime, setDoneTime] = useState(() =>
    new Date().toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' }),
  )
  const [error, setError] = useState<string | null>(null)

  // Admins can credit the completion to another household member. The /users endpoint is
  // admin-only, which is fine since only admins see this picker.
  const { data: allUsers } = useQuery({ queryKey: ['users'], queryFn: usersApi.list, enabled: isAdmin && !isIndep })
  const users: User[] | undefined = isIndep ? trackUsers : allUsers

  const onBehalf = isAdmin && completedByUserId !== '' && completedByUserId !== currentUser?.id
  const mutation = useMutation({
    mutationFn: () =>
      choresApi.complete(chore.id, {
        notes: notes.trim() || null,
        completedByUserId: onBehalf ? completedByUserId : null,
        completedAt: backdate ? toLocalDueInstant(doneDate, doneTime) : null,
      }),
    onSuccess: (completed) => {
      celebrate()
      celebrateAchievements(completed.unlockedAchievements)
      onDone()
    },
    onError: (err) => setError(err instanceof ApiError ? err.message : t('complete.failed')),
  })

  return (
    <Modal title={t('complete.title', { name: chore.name })} onClose={onClose}>
      <form
        onSubmit={(e) => {
          e.preventDefault()
          setError(null)
          mutation.mutate()
        }}
        className="space-y-4"
      >
        <p className="text-sm text-muted-foreground">
          {tRich(onBehalf ? 'complete.earnsFor' : 'complete.youEarn', {
            points: <span className="font-medium text-foreground">{plural('points', chore.points)}</span>,
          })}
        </p>
        {isAdmin && (users?.length ?? 0) > 0 && (
          <div>
            <Label htmlFor="completed-by">{t('complete.completedBy')}</Label>
            <Select
              id="completed-by"
              value={completedByUserId}
              onChange={(e) => setCompletedByUserId(e.target.value)}
            >
              {users!.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.id === currentUser?.id ? t('complete.you', { name: u.displayName }) : u.displayName}
                </option>
              ))}
            </Select>
          </div>
        )}
        <div>
          <Label htmlFor="notes">{t('complete.notes')}</Label>
          <Input id="notes" value={notes} onChange={(e) => setNotes(e.target.value)} />
        </div>
        {backdate ? (
          <div className="space-y-1.5">
            <div className="flex items-center justify-between">
              <Label htmlFor="done-date">{t('complete.completedAt')}</Label>
              <button
                type="button"
                className="text-xs text-muted-foreground hover:text-foreground"
                onClick={() => setBackdate(false)}
              >
                {t('complete.useNow')}
              </button>
            </div>
            <div className="flex gap-2">
              <Input
                id="done-date"
                type="date"
                value={doneDate}
                max={toLocalDateInput(new Date())}
                onChange={(e) => setDoneDate(e.target.value)}
                required
              />
              <div className="w-28 shrink-0">
                <TimeField value={doneTime} onChange={setDoneTime} ariaLabel={t('complete.timeLabel')} />
              </div>
            </div>
          </div>
        ) : (
          <button
            type="button"
            className="text-sm text-primary hover:underline"
            onClick={() => setBackdate(true)}
          >
            {t('complete.backdate')}
          </button>
        )}
        {error && <p className="text-sm text-destructive">{error}</p>}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" disabled={mutation.isPending || (backdate && (!doneDate || !doneTime))}>
            {mutation.isPending ? t('common.saving') : t('chore.markComplete')}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
