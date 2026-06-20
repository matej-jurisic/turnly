import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { Chore, ChoreHistoryEntry } from '@/lib/types'
import { choresApi, historyApi, ApiError } from '@/lib/api'
import { confirm } from '@/lib/confirm'
import { toast } from '@/lib/toast'
import { useAuthStore } from '@/store/auth'
import { Modal, Avatar } from '@/components/ui/Modal'
import { Badge } from '@/components/ui/Badge'
import { TrashIcon } from '@/components/chores/icons'
import {
  calendarDaysAgo, choreDueStatus, formatDate, notificationRecipientsLabel,
  notificationTimingLabel, notificationTypeLabel, relativeDayLabel, repeatLabel,
  STRATEGY_LABELS, SCHEDULING_LABELS,
} from '@/lib/chore-format'

interface ChoreDetailsModalProps {
  chore: Chore
  onClose: () => void
  onComplete?: () => void
}

export function ChoreDetailsModal({ chore, onClose, onComplete }: ChoreDetailsModalProps) {
  const title = (
    <span className="flex items-center gap-2">
      {chore.emoji && <span>{chore.emoji}</span>}
      <span>{chore.name}</span>
    </span>
  )

  const showSchedulingPref =
    chore.repeatType !== 'OneTime' &&
    !(chore.repeatType === 'Custom' && chore.customMode === 'Frequency')

  const overdueDays = chore.dueAt && choreDueStatus(chore) === 'overdue'
    ? calendarDaysAgo(chore.dueAt)
    : 0

  return (
    <Modal title={title} onClose={onClose}>
      <div className="max-h-[65vh] divide-y divide-border overflow-y-auto px-1 -mx-1 [&>*]:py-4 [&>*:first-child]:pt-0 [&>*:last-child]:pb-0">
        {chore.description && (
          <p className="text-sm text-muted-foreground">{chore.description}</p>
        )}

        {/* Schedule + points */}
        <div className="flex flex-wrap gap-2">
          <Badge tone="blue">{repeatLabel(chore)}</Badge>
          {chore.dueAt && (
            overdueDays > 0
              ? <Badge tone="red">Overdue by {overdueDays} {overdueDays === 1 ? 'day' : 'days'}</Badge>
              : <Badge tone="amber">Due {formatDate(chore.dueAt)}</Badge>
          )}
          <Badge tone="violet">{chore.points} pts</Badge>
          {chore.customMode === 'Frequency' && (
            <Badge tone="neutral">
              {chore.frequencyProgress ?? 0}/{chore.frequencyCount ?? 1} this {(chore.frequencyPeriod ?? 'Week').toLowerCase()}
            </Badge>
          )}
        </div>

        {/* Assignees */}
        {chore.assignees.length > 0 && (
          <div>
            <p className="mb-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">Assignees</p>
            <div className="flex flex-wrap gap-2">
              {chore.assignees.map((u) => (
                <span
                  key={u.id}
                  className={
                    'flex items-center gap-1.5 rounded-md px-2 py-1 text-xs ' +
                    (u.id === chore.currentAssignee?.id
                      ? 'bg-primary/10 text-primary ring-1 ring-primary'
                      : u.id === chore.nextAssignee?.id
                        ? 'bg-accent text-muted-foreground ring-1 ring-border'
                        : 'bg-accent text-muted-foreground')
                  }
                >
                  <Avatar color={u.avatarColor} name={u.displayName} size={16} />
                  {u.displayName}
                  {u.id === chore.currentAssignee?.id && (
                    <span className="ml-0.5 opacity-70">· current</span>
                  )}
                  {u.id === chore.nextAssignee?.id && u.id !== chore.currentAssignee?.id && (
                    <span className="ml-0.5 opacity-70">· next</span>
                  )}
                </span>
              ))}
            </div>
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
              <dt className="text-xs text-muted-foreground">Assignment</dt>
              <dd className="text-foreground">{STRATEGY_LABELS[chore.assignmentStrategy] ?? chore.assignmentStrategy}</dd>
            </div>
          )}
          {showSchedulingPref && (
            <div>
              <dt className="text-xs text-muted-foreground">Next due</dt>
              <dd className="text-foreground">{SCHEDULING_LABELS[chore.schedulingPreference] ?? chore.schedulingPreference}</dd>
            </div>
          )}
          <div>
            <dt className="text-xs text-muted-foreground">Start date</dt>
            <dd className="text-foreground">{formatDate(chore.startDate)}</dd>
          </div>
          <div>
            <dt className="text-xs text-muted-foreground">Added</dt>
            <dd className="text-foreground">{formatDate(chore.createdAt)}</dd>
          </div>
        </dl>

        {/* Reminders */}
        {chore.notifications.length > 0 && (
          <div>
            <p className="mb-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">Reminders</p>
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

        <ActivityList choreId={chore.id} />
      </div>

      {onComplete && (
        <div className="mt-4 flex justify-end border-t border-border pt-4">
          <button
            type="button"
            onClick={onComplete}
            disabled={!chore.dueAt}
            aria-label="Mark complete"
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
  const totalPoints = completions.reduce((sum, e) => sum + e.pointsAwarded, 0)
  // Activity is newest-first, so the first completion is the most recent.
  const lastCompletedAt = completions[0]?.at

  // Tally completions per actor, keeping the actor for avatar/name, sorted by count desc.
  const byActor = new Map<string, { actor: ChoreHistoryEntry['actor']; count: number }>()
  for (const e of completions) {
    if (!e.actor) continue
    const existing = byActor.get(e.actor.id)
    if (existing) existing.count += 1
    else byActor.set(e.actor.id, { actor: e.actor, count: 1 })
  }
  const contributors = [...byActor.values()].sort((a, b) => b.count - a.count)

  return (
    <div>
      <p className="mb-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">Stats</p>
      <dl className="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
        <div>
          <dt className="text-xs text-muted-foreground">Completed</dt>
          <dd className="text-foreground">{completions.length}{completions.length === 1 ? ' time' : ' times'}</dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">Points earned</dt>
          <dd className="text-foreground">{totalPoints}</dd>
        </div>
        {lastCompletedAt && (
          <div>
            <dt className="text-xs text-muted-foreground">Last completed</dt>
            <dd className="text-foreground">{relativeDayLabel(lastCompletedAt)}</dd>
          </div>
        )}
        {skips.length > 0 && (
          <div>
            <dt className="text-xs text-muted-foreground">Skipped</dt>
            <dd className="text-foreground">{skips.length}{skips.length === 1 ? ' time' : ' times'}</dd>
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

/** Per-chore activity log (completions + skips). Admins can delete entries to fix up history;
 * deletion reverses the entry's points but doesn't reschedule the chore. */
function ActivityList({ choreId }: { choreId: string }) {
  const isAdmin = useAuthStore((s) => s.user?.role === 'Admin')
  const queryClient = useQueryClient()

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
    onError: (err) => toast.error(err instanceof ApiError ? err.message : 'Delete failed'),
  })

  const onDelete = async (entry: ChoreHistoryEntry) => {
    if (
      await confirm({
        title: entry.kind === 'skip' ? 'Delete skip' : 'Delete completion',
        message: entry.kind === 'skip'
          ? 'Delete this skip from the log? The chore schedule is not changed.'
          : `Delete this completion? ${entry.pointsAwarded} points will be reversed and the chore schedule is not changed.`,
        confirmLabel: 'Delete',
      })
    ) {
      deleteMutation.mutate(entry.id)
    }
  }

  return (
    <div>
      <p className="mb-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">Activity</p>
      {isLoading && <p className="text-sm text-muted-foreground">Loading…</p>}
      {!isLoading && (activity?.length ?? 0) === 0 && (
        <p className="text-sm text-muted-foreground">No activity yet.</p>
      )}
      <div className="space-y-2">
        {(activity ?? []).map((entry) => (
          <div key={entry.id} className="rounded-lg border border-border bg-accent/50 px-3 py-2.5 text-sm">
            <div className="flex items-center justify-between gap-2">
              <div className="flex min-w-0 items-center gap-1.5">
                <Avatar
                  color={entry.actor?.avatarColor ?? 'var(--muted)'}
                  name={entry.actor?.displayName ?? '?'}
                  size={20}
                />
                <span className="truncate text-foreground">{entry.actor?.displayName}</span>
                <span className="shrink-0 text-muted-foreground">· {formatDate(entry.at)}</span>
              </div>
              <div className="flex shrink-0 items-center gap-2">
                {entry.kind === 'skip' ? (
                  <Badge tone="neutral">Skipped</Badge>
                ) : (
                  <Badge tone="violet">+{entry.pointsAwarded} pts</Badge>
                )}
                {isAdmin && (
                  <button
                    type="button"
                    onClick={() => onDelete(entry)}
                    disabled={deleteMutation.isPending}
                    aria-label="Delete entry"
                    className="inline-flex h-7 w-7 items-center justify-center rounded-full text-muted-foreground transition-colors hover:bg-destructive/10 hover:text-destructive focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50"
                  >
                    <TrashIcon />
                  </button>
                )}
              </div>
            </div>
            {entry.notes && (
              <p className="mt-1 text-muted-foreground italic">{entry.notes}</p>
            )}
          </div>
        ))}
      </div>
    </div>
  )
}
