import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { choresApi, usersApi, ApiError } from '@/lib/api'
import { celebrate } from '@/lib/confetti'
import { celebrateAchievements } from '@/lib/achievementCelebration'
import { creditedPartner, formatDate, isAnyone, isIndependent, peopleLabel, toLocalDateInput, toLocalDueInstant } from '@/lib/chore-format'
import { toast } from '@/lib/toast'
import { useAuthStore } from '@/store/auth'
import type { Chore, User } from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { Input, Label, Select } from '@/components/ui/Field'
import { Avatar, Modal } from '@/components/ui/Modal'
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

  const isIndep = isIndependent(chore)
  const trackUsers = chore.tracks.map((t) => t.user)
  const viewerOwnsTrack = trackUsers.some((u) => u.id === currentUser?.id)
  // On an Anyone chore a member may also name the assignees who did it with them (never dropping
  // themselves); admins can name anyone on any chore.
  const memberPicks = !isAdmin && isAnyone(chore) && chore.assignees.some((u) => u.id === currentUser?.id)
  const canPick = isAdmin || memberPicks

  const [notes, setNotes] = useState('')
  // Admins pick who did it. Track-mode chores are completed for one assignee at a time (each has
  // their own schedule), defaulting to a valid owner; any other chore can credit any number of
  // people, assignees or not, defaulting to whoever the server would credit for the viewer.
  const defaultPartner = !isIndep && currentUser ? creditedPartner(chore, currentUser.id) : null
  const defaultIds = isIndep
    ? [(viewerOwnsTrack ? currentUser?.id : trackUsers[0]?.id) ?? '']
    : [currentUser?.id ?? '', ...(defaultPartner ? [defaultPartner.id] : [])]
  const [pickedIds, setPickedIds] = useState<string[]>(defaultIds)
  // Backdating: off by default (the completion is stamped "now"). Turning it on reveals a date and
  // time, pre-filled with the current moment so only the part that differs needs changing.
  const [backdate, setBackdate] = useState(false)
  const [doneDate, setDoneDate] = useState(() => toLocalDateInput(new Date()))
  const [doneTime, setDoneTime] = useState(() =>
    new Date().toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' }),
  )
  const [error, setError] = useState<string | null>(null)

  // The /users endpoint is admin-only; members pick from the chore's own assignees.
  const { data: allUsers } = useQuery({ queryKey: ['users'], queryFn: usersApi.list, enabled: isAdmin && !isIndep })
  const users: User[] = (isIndep ? trackUsers : memberPicks ? chore.assignees : allUsers) ?? []
  const nameOf = (u: User) => (u.id === currentUser?.id ? t('complete.you', { name: u.displayName }) : u.displayName)
  const toggle = (id: string) =>
    setPickedIds((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]))

  // Who gets the points: what was picked, or the viewer and their default partner.
  const credited: User[] = canPick
    ? pickedIds.map((id) => users.find((u) => u.id === id)).filter((u): u is User => !!u)
    : [currentUser, defaultPartner].filter((u): u is User => !!u)
  const includesMe = credited.some((u) => u.id === currentUser?.id)
  // Only send the list when it isn't what the server would credit anyway (on a track chore that's
  // just the viewer, who may not own a track).
  const serverDefault = isIndep ? [currentUser?.id ?? ''] : defaultIds
  const isDefault = pickedIds.length === serverDefault.length && serverDefault.every((id) => pickedIds.includes(id))
  const completedByUserIds = canPick && !isDefault ? pickedIds : null
  const mutation = useMutation({
    mutationFn: () =>
      choresApi.complete(chore.id, {
        notes: notes.trim() || null,
        completedByUserIds,
        completedAt: backdate ? toLocalDueInstant(doneDate, doneTime) : null,
      }),
    onSuccess: (completed) => {
      celebrate()
      celebrateAchievements(completed.unlockedAchievements)
      if (completed.filledExpiredDueAt)
        toast.success(t('complete.filledExpired', { date: formatDate(completed.filledExpiredDueAt) }))
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
        {credited.length > 0 && (
          <p className="text-sm text-muted-foreground">
            {tRich(
              credited.length > 1
                ? includesMe && !canPick ? 'complete.youEarnWith' : 'complete.earnsForEach'
                : includesMe ? 'complete.youEarn' : 'complete.earnsFor',
              {
                points: <span className="font-medium text-foreground">{plural('points', chore.points)}</span>,
                names: <>{peopleLabel(credited.map((u) => ({ displayName: nameOf(u) })))}</>,
                name: <>{defaultPartner?.displayName}</>,
              },
            )}
          </p>
        )}
        {isAdmin && isIndep && users.length > 0 && (
          <div>
            <Label htmlFor="completed-by">{t('complete.completedBy')}</Label>
            <Select id="completed-by" value={pickedIds[0] ?? ''} onChange={(e) => setPickedIds([e.target.value])}>
              {users.map((u) => (
                <option key={u.id} value={u.id}>{nameOf(u)}</option>
              ))}
            </Select>
          </div>
        )}
        {canPick && !isIndep && users.length > 0 && (
          <div>
            <Label>{t('complete.completedBy')}</Label>
            <div className="flex flex-wrap gap-1">
              {users.map((u) => (
                <button
                  key={u.id}
                  type="button"
                  aria-pressed={pickedIds.includes(u.id)}
                  disabled={memberPicks && u.id === currentUser?.id}
                  onClick={() => toggle(u.id)}
                  className={
                    'flex items-center gap-1.5 rounded-md px-2 py-1 text-xs transition-colors ' +
                    (pickedIds.includes(u.id)
                      ? 'bg-primary/10 text-primary ring-1 ring-primary'
                      : 'bg-accent text-muted-foreground hover:text-foreground')
                  }
                >
                  <Avatar color={u.avatarColor} name={u.displayName} size={16} frame={u.equippedFrameKey} emoji={u.avatarEmoji} />
                  {nameOf(u)}
                </button>
              ))}
            </div>
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
          <Button type="submit" disabled={mutation.isPending || credited.length === 0 || (backdate && (!doneDate || !doneTime))}>
            {mutation.isPending ? t('common.saving') : t('chore.markComplete')}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
