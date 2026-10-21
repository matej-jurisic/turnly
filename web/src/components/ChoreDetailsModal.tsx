import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { Chore, ChoreHistoryEntry } from '@/lib/types'
import { choresApi, historyApi, usersApi, ApiError } from '@/lib/api'
import { confirm } from '@/lib/confirm'
import { toast } from '@/lib/toast'
import { useAuthStore } from '@/store/auth'
import { Modal, Avatar } from '@/components/ui/Modal'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Input, Label, Select } from '@/components/ui/Field'
import { TimeField } from '@/components/ui/TimeField'
import { CheckIcon, DotsIcon, EditIcon, SkipIcon, TrashIcon } from '@/components/chores/icons'
import {
  calendarDaysAgo, choreDueStatus, choreHasDueTime, completionProgressLabel, dueStatus, formatDate, formatGrace,
  isIndependent, notificationRecipientsLabel, notificationTimingLabel, notificationTypeLabel,
  relativeDayLabel, repeatLabel, showStreak, trackIsDone, trackStatusText, strategyLabel, schedulingLabel, rotationPeriodLabel, holdsChore, pairLabel,
  toLocalDateInput, toLocalDueInstant, canComplete, isOnDemand, occurrenceLabel,
} from '@/lib/chore-format'
import { plural, t, tRich } from '@/lib/i18n'
import { dayStartMs } from '@/lib/day-start'

interface ChoreDetailsModalProps {
  chore: Chore
  onClose: () => void
  onComplete?: () => void
}

export function ChoreDetailsModal({ chore, onClose, onComplete }: ChoreDetailsModalProps) {
  const queryClient = useQueryClient()
  const isAdmin = useAuthStore((s) => s.user?.role === 'Admin')
  const meId = useAuthStore((s) => s.user?.id)

  const skipMutation = useMutation({
    mutationFn: (userId: string) => choresApi.skip(chore.id, { userId }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['chores'] })
      void queryClient.invalidateQueries({ queryKey: ['history'] })
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('chores.skipFailed')),
  })

  const offer = chore.openOffer
  const isMyOffer = offer?.fromUser.id === meId
  const canClaim = !!offer && !isMyOffer && chore.assignees.some((u) => u.id === meId)

  // A claim moves points between two people, so the balances on screen go stale too.
  const invalidateOffer = () => {
    void queryClient.invalidateQueries({ queryKey: ['chores'] })
    void queryClient.invalidateQueries({ queryKey: ['me'] })
    void queryClient.invalidateQueries({ queryKey: ['leaderboard'] })
  }

  const claimMutation = useMutation({
    mutationFn: () => choresApi.claimOffer(chore.id),
    onSuccess: () => {
      toast.success(offer?.bountyPoints ? plural('details.claimedFor', offer.bountyPoints) : t('details.claimed'))
      invalidateOffer()
      onClose()
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('offer.claimFailed')),
  })

  const withdrawMutation = useMutation({
    mutationFn: () => choresApi.withdrawOffer(chore.id),
    onSuccess: () => {
      toast.success(t('details.offerWithdrawn'))
      invalidateOffer()
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('details.withdrawFailed')),
  })

  const offerBusy = claimMutation.isPending || withdrawMutation.isPending

  const onSkipTrack = async (userId: string, name: string) => {
    if (await confirm({
      title: t('chores.skipTitle'),
      message: t('details.skipTrackMessage', { name }),
      confirmLabel: t('common.skip'),
    })) {
      skipMutation.mutate(userId)
    }
  }

  const title = (
    <span className="flex items-center gap-2">
      {chore.emoji && <span>{chore.emoji}</span>}
      <span>{chore.name}</span>
    </span>
  )

  const scheduled = !isOnDemand(chore)
  const showSchedulingPref = chore.repeatType !== 'OneTime' && scheduled

  const overdueDays = chore.dueAt && choreDueStatus(chore) === 'overdue'
    ? calendarDaysAgo(chore.dueAt)
    : 0

  return (
    <Modal title={title} onClose={onClose}>
      <div className="max-h-[65vh] divide-y divide-border overflow-y-auto px-1 -mx-1 [&>*]:py-4 [&>*:first-child]:pt-0 [&>*:last-child]:pb-0">
        {chore.description && (
          <p className="text-sm text-muted-foreground">{chore.description}</p>
        )}

        {offer && (
          <div>
            <div className="rounded-lg border border-border bg-accent/50 px-3 py-2.5 text-sm">
              <p className="text-muted-foreground">
                {tRich(isMyOffer ? 'offer.sentence.youOpen' : 'offer.sentence.nameOpen', {
                  from: <strong className="text-foreground">{offer.fromUser.displayName}</strong>,
                })}.
                {offer.bountyPoints > 0 && (
                  <> {tRich('offer.bountyEarns', {
                    points: <strong className="text-foreground">{plural('points', offer.bountyPoints)}</strong>,
                  })}</>
                )}
              </p>
              {(canClaim || isMyOffer || isAdmin) && (
                <div className="mt-2.5 flex gap-2">
                  {canClaim && (
                    <Button size="sm" onClick={() => claimMutation.mutate()} disabled={offerBusy}>
                      {offer.bountyPoints > 0 ? t('offer.claimFor', { count: offer.bountyPoints }) : t('offer.claim')}
                    </Button>
                  )}
                  {(isMyOffer || isAdmin) && (
                    <Button size="sm" variant="secondary" onClick={() => withdrawMutation.mutate()} disabled={offerBusy}>{t('offer.withdraw')}</Button>
                  )}
                </div>
              )}
            </div>
          </div>
        )}

        {chore.isFrozen && (
          <div>
            <div className="rounded-lg border border-border bg-accent/50 px-3 py-2 text-sm text-muted-foreground">
              {tRich('details.pausedNote', {
                paused: <strong className="text-foreground">{t('details.pausedWord')}</strong>,
              })}
            </div>
          </div>
        )}

        {/* Schedule + points */}
        <div className="flex flex-wrap gap-2">
          <Badge tone="blue">{repeatLabel(chore)}</Badge>
          {chore.isFrozen && <Badge tone="neutral">{t('chores.paused')}</Badge>}
          {!chore.isFrozen && chore.dueAt && (
            overdueDays > 0
              ? <Badge tone="red">{plural('details.overdueBy', overdueDays)}</Badge>
              : <Badge tone="amber">{t('details.due', { date: formatDate(chore.dueAt) })}{chore.dueLabel && ` · ${chore.dueLabel}`}</Badge>
          )}
          <Badge tone="violet">{t('common.pts', { count: chore.points })}</Badge>
          {chore.completionsRequired > 1 && (
            <Badge tone="neutral">{completionProgressLabel(chore)}</Badge>
          )}
          {!isIndependent(chore) && showStreak(chore) && (
            <Badge tone="green">{t('details.streak', { count: chore.currentStreak })}</Badge>
          )}
        </div>

        {/* Per-person schedules (track mode) */}
        {isIndependent(chore) ? (
          <div>
            <p className="mb-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">
              {t('details.perPerson')}
            </p>
            <div className="space-y-1.5">
              {chore.tracks.map((track) => {
                const overdue = dueStatus(track.dueAt, choreHasDueTime(chore)) === 'overdue'
                return (
                  <div
                    key={track.user.id}
                    className="flex items-center justify-between gap-2 rounded-lg border border-border bg-accent/50 px-3 py-2"
                  >
                    <div className="flex min-w-0 items-center gap-2">
                      <Avatar color={track.user.avatarColor} name={track.user.displayName} size={20} frame={track.user.equippedFrameKey} emoji={track.user.avatarEmoji} />
                      <div className="min-w-0">
                        <span className="flex items-center gap-1.5 truncate text-sm text-foreground">
                          {track.user.displayName}
                          {track.streak >= 2 && <span className="text-xs text-success">🔥 {track.streak}</span>}
                        </span>
                        <span
                          className={
                            'block text-xs ' +
                            (overdue ? 'text-warning' : trackIsDone(chore, track) ? 'text-success' : 'text-muted-foreground')
                          }
                        >
                          {trackStatusText(chore, track)}
                        </span>
                      </div>
                    </div>
                    {isAdmin && track.dueAt && (
                      <button
                        type="button"
                        onClick={() => onSkipTrack(track.user.id, track.user.displayName)}
                        disabled={skipMutation.isPending}
                        aria-label={t('details.skipTrackLabel', { name: track.user.displayName })}
                        className="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-muted-foreground transition-colors hover:bg-accent hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50"
                      >
                        <SkipIcon />
                      </button>
                    )}
                  </div>
                )
              })}
            </div>
          </div>
        ) : chore.assignees.length > 0 && (
          <div>
            <p className="mb-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">{t('details.assignees')}</p>
            <div className="flex flex-wrap gap-2">
              {chore.assignees.map((u) => (
                <span
                  key={u.id}
                  className={
                    'flex items-center gap-1.5 rounded-md px-2 py-1 text-xs ' +
                    (holdsChore(chore, u.id)
                      ? 'bg-primary/10 text-primary ring-1 ring-primary'
                      : (u.id === chore.nextAssignee?.id || u.id === chore.nextPartner?.id)
                        ? 'bg-info/10 text-info ring-1 ring-info'
                        : 'bg-accent text-muted-foreground')
                  }
                >
                  <Avatar color={u.avatarColor} name={u.displayName} size={16} frame={u.equippedFrameKey} emoji={u.avatarEmoji} />
                  {u.displayName}
                  {holdsChore(chore, u.id) && (
                    <span className="ml-0.5 opacity-70">· {t('details.current')}</span>
                  )}
                  {(u.id === chore.nextAssignee?.id || u.id === chore.nextPartner?.id) && !holdsChore(chore, u.id) && (
                    <span className="ml-0.5 opacity-70">· {t('details.nextTag')}</span>
                  )}
                </span>
              ))}
            </div>
            {chore.pairs.length > 0 && (
              <p className="mt-1.5 text-xs text-muted-foreground">
                {t('details.pairs', {
                  pairs: chore.pairs
                    .map((pair) => pair.map((id) => chore.assignees.find((u) => u.id === id)?.displayName ?? '?').join(' & '))
                    .join(', '),
                })}
              </p>
            )}
          </div>
        )}

        {/* Tags */}
        {chore.tags.length > 0 && (
          <div className="flex flex-wrap gap-1.5">
            {chore.tags.map((tag) => (
              <Badge key={tag} tone="neutral">{tag}</Badge>
            ))}
          </div>
        )}

        {/* Meta grid */}
        <dl className="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
          {chore.repeatType !== 'OneTime' && (
            <div>
              <dt className="text-xs text-muted-foreground">{t('details.assignment')}</dt>
              <dd className="text-foreground">
                {strategyLabel(chore.assignmentStrategy)}
                {chore.rotateOnEachCompletion && (
                  <span className="block text-xs text-muted-foreground">{t('details.rotatesEach')}</span>
                )}
                {chore.rotationPeriod !== 'EveryOccurrence' && (
                  <span className="block text-xs text-muted-foreground">
                    {t('details.handsOver', { period: rotationPeriodLabel(chore.rotationPeriod).toLowerCase() })}
                  </span>
                )}
              </dd>
            </div>
          )}
          {showSchedulingPref && (
            <div>
              <dt className="text-xs text-muted-foreground">{t('details.nextDue')}</dt>
              <dd className="text-foreground">{schedulingLabel(chore.schedulingPreference)}</dd>
              {chore.schedulingPreference === 'SmartScheduling' && chore.graceMinutes ? (
                <span className="block text-xs text-muted-foreground">
                  {t('details.resetsIfEarly', { grace: formatGrace(chore.graceMinutes) })}
                </span>
              ) : null}
            </div>
          )}
          {chore.repeatType !== 'OneTime' && scheduled && (
            <div>
              <dt className="text-xs text-muted-foreground">{t('details.autoAdvance')}</dt>
              <dd className="text-foreground">{chore.autoAdvanceIncomplete ? t('common.on') : t('common.off')}</dd>
              {chore.autoAdvanceIncomplete && chore.completionWindowMinutes ? (
                <span className="block text-xs text-muted-foreground">
                  {t('details.expiresAfter', { window: formatGrace(chore.completionWindowMinutes) })}
                </span>
              ) : null}
            </div>
          )}
          {scheduled && (
            <div>
              <dt className="text-xs text-muted-foreground">{t('details.startDate')}</dt>
              <dd className="text-foreground">{formatDate(chore.startDate)}</dd>
            </div>
          )}
          <div>
            <dt className="text-xs text-muted-foreground">{t('details.added')}</dt>
            <dd className="text-foreground">{formatDate(chore.createdAt)}</dd>
          </div>
        </dl>

        {/* Reminders */}
        {chore.notifications.length > 0 && (
          <div>
            <p className="mb-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">{t('details.reminders')}</p>
            <div className="space-y-1.5">
              {chore.notifications.map((n) => (
                <div key={n.id} className="flex flex-wrap items-center gap-1.5 text-sm">
                  <Badge tone="blue">{notificationTypeLabel(n)}</Badge>
                  <span className="text-foreground">{notificationTimingLabel(n)}</span>
                  <span className="text-muted-foreground">· {notificationRecipientsLabel(n)}</span>
                </div>
              ))}
            </div>
          </div>
        )}

        <ChoreStats choreId={chore.id} />

        <ActivityList chore={chore} />
      </div>

      {onComplete && !chore.isFrozen && (
        <div className="mt-4 flex justify-end border-t border-border pt-4">
          <button
            type="button"
            onClick={onComplete}
            disabled={!canComplete(chore)}
            aria-label={t('chore.markComplete')}
            className="inline-flex h-9 w-9 items-center justify-center rounded-full bg-primary text-primary-foreground transition-colors hover:bg-primary/90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-30"
          >
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M20 6 9 17l-5-5" />
            </svg>
          </button>
        </div>
      )}
    </Modal>
  )
}

/** Summary stats + per-assignee contribution, derived from the same activity feed as the log. */
function ChoreStats({ choreId }: { choreId: string }) {
  const { data: activity } = useQuery({
    queryKey: ['history', { choreId }],
    queryFn: () => historyApi.list({ choreId }),
  })

  if (!activity || activity.length === 0) return null

  const completions = activity.filter((e) => e.kind === 'completion')
  const skips = activity.filter((e) => e.kind === 'skip')
  const expired = activity.filter((e) => e.kind === 'expired')
  const totalPoints = completions.reduce((sum, e) => sum + e.pointsAwarded, 0)
  // Activity is newest-first, so the first completion is the most recent.
  const lastCompletedAt = completions[0]?.at

  // Tally completions per actor, keeping the actor for avatar/name, sorted by count desc. A paired
  // completion counts for the partner as well.
  const byActor = new Map<string, { actor: ChoreHistoryEntry['actor']; count: number }>()
  for (const e of completions) {
    for (const person of [e.actor, e.partner]) {
      if (!person) continue
      const existing = byActor.get(person.id)
      if (existing) existing.count += 1
      else byActor.set(person.id, { actor: person, count: 1 })
    }
  }
  const contributors = [...byActor.values()].sort((a, b) => b.count - a.count)

  return (
    <div>
      <p className="mb-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">{t('details.stats')}</p>
      <dl className="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
        <div>
          <dt className="text-xs text-muted-foreground">{t('details.completed')}</dt>
          <dd className="text-foreground">{plural('count.times', completions.length)}</dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">{t('details.pointsEarned')}</dt>
          <dd className="text-foreground">{totalPoints}</dd>
        </div>
        {lastCompletedAt && (
          <div>
            <dt className="text-xs text-muted-foreground">{t('details.lastCompleted')}</dt>
            <dd className="text-foreground">{relativeDayLabel(lastCompletedAt)}</dd>
          </div>
        )}
        {skips.length > 0 && (
          <div>
            <dt className="text-xs text-muted-foreground">{t('details.skipped')}</dt>
            <dd className="text-foreground">{plural('count.times', skips.length)}</dd>
          </div>
        )}
        {expired.length > 0 && (
          <div>
            <dt className="text-xs text-muted-foreground">{t('details.expired')}</dt>
            <dd className="text-foreground">{plural('count.slots', expired.length)}</dd>
          </div>
        )}
      </dl>

      {contributors.length > 0 && (
        <div className="mt-3 flex flex-wrap gap-2">
          {contributors.map(({ actor, count }) => (
            <span
              key={actor!.id}
              className="flex items-center gap-1.5 rounded-md bg-accent px-2 py-1 text-xs text-muted-foreground"
            >
              <Avatar color={actor!.avatarColor} name={actor!.displayName} size={16} />
              {actor!.displayName}
              <span className="opacity-70">· {count}</span>
            </span>
          ))}
        </div>
      )}
    </div>
  )
}

/** A completion logged after its occurrence was due: the one kind whose time an admin can correct.
 * An all-day chore is on time until the household's day start, matching the server's streaks. */
function isLateCompletion(entry: ChoreHistoryEntry, chore: Chore): boolean {
  const grace = choreHasDueTime(chore) ? 0 : dayStartMs()
  return entry.kind === 'completion' && !!entry.occurrenceDueAt
    && new Date(entry.at).getTime() > new Date(entry.occurrenceDueAt).getTime() + grace
}

/** Per-chore activity log (completions, skips, and auto-expired slots). Admins can delete entries to
 * fix up history, mark an auto-expired slot as completed, and correct the time of a late completion;
 * none of these reschedule the chore. */
function ActivityList({ chore }: { chore: Chore }) {
  const choreId = chore.id
  const isAdmin = useAuthStore((s) => s.user?.role === 'Admin')
  const queryClient = useQueryClient()
  const [editingId, setEditingId] = useState<string | null>(null)

  const { data: activity, isLoading } = useQuery({
    queryKey: ['history', { choreId }],
    queryFn: () => historyApi.list({ choreId }),
  })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => choresApi.deleteActivity(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['history'] })
      void queryClient.invalidateQueries({ queryKey: ['chores'] })
      void queryClient.invalidateQueries({ queryKey: ['me'] })
      void queryClient.invalidateQueries({ queryKey: ['leaderboard'] })
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('chores.deleteFailed')),
  })

  const onDelete = async (entry: ChoreHistoryEntry) => {
    const { title, message } =
      entry.kind === 'skip'
        ? { title: t('activity.deleteSkipTitle'), message: t('activity.deleteSkipMessage') }
        : entry.kind === 'expired'
          ? { title: t('activity.deleteExpiredTitle'), message: t('activity.deleteExpiredMessage') }
          : { title: t('activity.deleteCompletionTitle'), message: plural('activity.deleteCompletionMessage', entry.pointsAwarded) }
    if (await confirm({ title, message, confirmLabel: t('common.delete') })) {
      deleteMutation.mutate(entry.id)
    }
  }

  return (
    <div>
      <p className="mb-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">{t('activity.title')}</p>
      {isLoading && <p className="text-sm text-muted-foreground">{t('common.loading')}</p>}
      {!isLoading && (activity?.length ?? 0) === 0 && (
        <p className="text-sm text-muted-foreground">{t('activity.empty')}</p>
      )}
      <div className="space-y-2">
        {(activity ?? []).map((entry) => (
          <div key={entry.id} className="rounded-lg border border-border bg-accent/50 px-3 py-2.5 text-sm">
            <div className="flex items-start gap-2">
              <span className="mt-0.5 shrink-0">
                <Avatar
                  color={entry.actor?.avatarColor ?? 'var(--muted)'}
                  name={entry.actor?.displayName ?? '-'}
                  size={20}
                />
              </span>
              <div className="min-w-0 flex-1">
                <div className="flex items-center justify-between gap-2">
                  <p className="min-w-0 truncate text-foreground">
                    {entry.kind === 'expired' ? t('activity.autoExpired') : entry.actor && pairLabel(entry.actor, entry.partner)}
                  </p>
                  <div className="flex shrink-0 items-center gap-1.5 whitespace-nowrap">
                    {entry.kind === 'expired' ? (
                      <Badge tone="amber">{t('details.expired')}</Badge>
                    ) : entry.kind === 'skip' ? (
                      <Badge tone="neutral">{t('details.skipped')}</Badge>
                    ) : (
                      <>
                        {entry.isRescue && <Badge tone="violet">{t('details.rescue')}</Badge>}
                        <span className="text-xs font-medium text-foreground">{t('common.ptsPlus', { count: entry.pointsAwarded })}</span>
                      </>
                    )}
                  </div>
                </div>
                <p className="mt-0.5 flex flex-wrap gap-x-1.5 text-xs text-muted-foreground">
                  <span className="whitespace-nowrap">{formatDate(entry.at)}</span>
                  {entry.occurrenceDueAt && (
                    <span className="whitespace-nowrap">· {t('activity.forOccurrence', { due: occurrenceLabel(entry, choreHasDueTime(chore)) })}</span>
                  )}
                </p>
              </div>
              {isAdmin && (
                <ActivityMenu
                  fixLabel={
                    entry.kind === 'expired'
                      ? t('activity.markCompleted')
                      : isLateCompletion(entry, chore) ? t('activity.editTime') : null
                  }
                  fixIcon={entry.kind === 'expired' ? <CheckIcon /> : <EditIcon />}
                  deletePending={deleteMutation.isPending}
                  onFix={() => setEditingId(editingId === entry.id ? null : entry.id)}
                  onDelete={() => onDelete(entry)}
                />
              )}
            </div>
            {entry.notes && (
              <p className="mt-1 ml-7 break-words text-muted-foreground italic">{entry.notes}</p>
            )}
            {isAdmin && editingId === entry.id && (
              <ActivityEditForm chore={chore} entry={entry} onClose={() => setEditingId(null)} />
            )}
          </div>
        ))}
      </div>
    </div>
  )
}

/** Admin actions for one activity entry, folded into a "more" popover so the row keeps its width. */
function ActivityMenu({ fixLabel, fixIcon, deletePending, onFix, onDelete }: {
  fixLabel: string | null
  fixIcon: React.ReactNode
  deletePending: boolean
  onFix: () => void
  onDelete: () => void
}) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [open])

  return (
    <div ref={ref} className="relative -my-1 -mr-1.5 shrink-0">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-label={t('menu.more')}
        aria-expanded={open}
        className="inline-flex h-8 w-8 items-center justify-center rounded-full text-muted-foreground transition-colors hover:bg-accent hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        <DotsIcon />
      </button>
      {open && (
        <div className="absolute right-0 top-full z-20 mt-1 min-w-[160px] rounded-lg border border-border bg-card py-1 shadow-pop">
          {fixLabel && (
            <button
              type="button"
              onClick={() => { setOpen(false); onFix() }}
              className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent"
            >
              {fixIcon}
              {fixLabel}
            </button>
          )}
          <button
            type="button"
            disabled={deletePending}
            onClick={() => { setOpen(false); onDelete() }}
            className="flex w-full items-center gap-2 px-3 py-2 text-sm text-destructive transition-colors hover:bg-destructive/10 disabled:opacity-50"
          >
            <TrashIcon />
            {t('activity.deleteEntry')}
          </button>
        </div>
      )}
    </div>
  )
}

/** Inline admin form under an activity entry. An auto-expired entry gets "who" and "when" and becomes
 * a completion; a completion only gets "when". */
function ActivityEditForm({ chore, entry, onClose }: { chore: Chore; entry: ChoreHistoryEntry; onClose: () => void }) {
  const queryClient = useQueryClient()
  const isExpired = entry.kind === 'expired'
  // A per-person chore's expired slot belongs to one schedule, so there is nobody to choose.
  const pickWho = isExpired && !isIndependent(chore)

  // An expired entry is stamped with when it expired, so the occurrence's due time is the better guess.
  const initial = new Date(isExpired ? entry.occurrenceDueAt ?? entry.at : entry.at)
  const [date, setDate] = useState(() => toLocalDateInput(initial))
  const [time, setTime] = useState(() => initial.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' }))
  const [who, setWho] = useState(entry.actor?.id ?? '')
  const [error, setError] = useState<string | null>(null)

  // Same list as completing on someone's behalf: any household member can be credited.
  const { data: users } = useQuery({ queryKey: ['users'], queryFn: usersApi.list, enabled: pickWho })
  const completedByUserId = who || users?.[0]?.id || ''

  const mutation = useMutation({
    mutationFn: () =>
      choresApi.updateActivity(entry.id, {
        completedAt: toLocalDueInstant(date, time),
        completedByUserId: pickWho ? completedByUserId : null,
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['history'] })
      void queryClient.invalidateQueries({ queryKey: ['chores'] })
      void queryClient.invalidateQueries({ queryKey: ['me'] })
      void queryClient.invalidateQueries({ queryKey: ['leaderboard'] })
      onClose()
    },
    onError: (err) => setError(err instanceof ApiError ? err.message : t('activity.updateFailed')),
  })

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault()
        setError(null)
        mutation.mutate()
      }}
      className="mt-2.5 space-y-3 border-t border-border pt-2.5"
    >
      {pickWho && (
        <div>
          <Label htmlFor={`activity-who-${entry.id}`}>{t('complete.completedBy')}</Label>
          <Select id={`activity-who-${entry.id}`} value={completedByUserId} onChange={(e) => setWho(e.target.value)}>
            {(users ?? (entry.actor ? [entry.actor] : [])).map((u) => (
              <option key={u.id} value={u.id}>{u.displayName}</option>
            ))}
          </Select>
        </div>
      )}
      <div>
        <Label htmlFor={`activity-date-${entry.id}`}>{t('complete.completedAt')}</Label>
        <div className="flex gap-2">
          <Input
            id={`activity-date-${entry.id}`}
            type="date"
            value={date}
            max={toLocalDateInput(new Date())}
            onChange={(e) => setDate(e.target.value)}
            required
          />
          <div className="w-28 shrink-0">
            <TimeField value={time} onChange={setTime} ariaLabel={t('complete.timeLabel')} />
          </div>
        </div>
      </div>
      {error && <p className="text-sm text-destructive">{error}</p>}
      <div className="flex justify-end gap-2">
        <Button type="button" size="sm" variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
        <Button type="submit" size="sm" disabled={mutation.isPending || !date || !time || (pickWho && !completedByUserId)}>
          {mutation.isPending ? t('common.saving') : isExpired ? t('activity.markCompleted') : t('common.save')}
        </Button>
      </div>
    </form>
  )
}
