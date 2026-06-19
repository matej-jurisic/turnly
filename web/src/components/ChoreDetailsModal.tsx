import type { Chore, Weekday } from '@/lib/types'
import { Modal, Avatar } from '@/components/ui/Modal'
import { Badge } from '@/components/ui/Badge'

const WEEKDAY_SHORT: Record<Weekday, string> = {
  Sunday: 'Sun', Monday: 'Mon', Tuesday: 'Tue', Wednesday: 'Wed',
  Thursday: 'Thu', Friday: 'Fri', Saturday: 'Sat',
}
const WEEKDAY_ORDER: Weekday[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']
const MONTH_SHORT = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

function repeatLabel(chore: Chore): string {
  const SIMPLE: Record<string, string> = {
    OneTime: 'One-time', Daily: 'Daily', Weekly: 'Weekly',
    Monthly: 'Monthly', Yearly: 'Yearly',
  }
  if (chore.repeatType !== 'Custom') return SIMPLE[chore.repeatType] ?? chore.repeatType
  switch (chore.customMode) {
    case 'Interval': {
      const unit = (chore.intervalUnit ?? 'Week').toLowerCase()
      const n = chore.intervalCount ?? 1
      return n === 1 ? `Every ${unit}` : `Every ${n} ${unit}s`
    }
    case 'DaysOfWeek':
      return [...chore.weekdays]
        .sort((a, b) => WEEKDAY_ORDER.indexOf(a) - WEEKDAY_ORDER.indexOf(b))
        .map((d) => WEEKDAY_SHORT[d])
        .join(', ') || 'Days of week'
    case 'DaysOfMonth': {
      const days = [...chore.daysOfMonth].sort((a, b) => a - b).join(', ')
      const months = [...chore.months].sort((a, b) => a - b).map((m) => MONTH_SHORT[m - 1]).join(', ')
      return `Days ${days}${months ? ` · ${months}` : ''}`
    }
    case 'Frequency':
      return `${chore.frequencyCount ?? 1}×/${(chore.frequencyPeriod ?? 'Week').toLowerCase()}`
    default:
      return 'Custom'
  }
}

function formatDate(iso?: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
}

const STRATEGY_LABELS: Record<string, string> = {
  KeepLastAssigned: 'Keep last assigned',
  RoundRobin: 'Round robin',
  Random: 'Random',
  RandomExceptLastAssigned: 'Random (except last)',
  LeastAssigned: 'Least assigned',
  LeastCompleted: 'Least completed',
}

const SCHEDULING_LABELS: Record<string, string> = {
  FromScheduledDate: 'From scheduled date',
  FromCompletionDate: 'From completion date',
  ToFirstNextRepeat: 'To first next repeat',
}

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

  return (
    <Modal title={title} onClose={onClose}>
      <div className="max-h-[65vh] space-y-4 overflow-y-auto px-1 -mx-1">
        {chore.description && (
          <p className="text-sm text-muted-foreground">{chore.description}</p>
        )}

        {/* Schedule + points */}
        <div className="flex flex-wrap gap-2">
          <Badge tone="blue">{repeatLabel(chore)}</Badge>
          {chore.dueAt && <Badge tone="amber">Due {formatDate(chore.dueAt)}</Badge>}
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
                      : 'bg-accent text-muted-foreground')
                  }
                >
                  <Avatar color={u.avatarColor} name={u.displayName} size={16} />
                  {u.displayName}
                  {u.id === chore.currentAssignee?.id && (
                    <span className="ml-0.5 opacity-70">· current</span>
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

        {/* Last completion */}
        {chore.lastCompletion && (
          <div className="rounded-lg border border-border bg-accent/50 px-3 py-2.5 text-sm">
            <p className="mb-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">Last completion</p>
            <div className="flex items-center justify-between gap-2">
              <div className="flex items-center gap-1.5">
                <Avatar
                  color={chore.lastCompletion.completedBy.avatarColor}
                  name={chore.lastCompletion.completedBy.displayName}
                  size={20}
                />
                <span className="text-foreground">{chore.lastCompletion.completedBy.displayName}</span>
                <span className="text-muted-foreground">· {formatDate(chore.lastCompletion.completedAt)}</span>
              </div>
              <Badge tone="violet">+{chore.lastCompletion.pointsAwarded} pts</Badge>
            </div>
            {chore.lastCompletion.notes && (
              <p className="mt-1 text-muted-foreground italic">{chore.lastCompletion.notes}</p>
            )}
          </div>
        )}
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
