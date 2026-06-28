import type { Chore } from '@/lib/types'
import { Badge } from '@/components/ui/Badge'
import {
  choreHasDueTime, dueStatus, formatDate, isIndependent, nextDueTimeLabel, showStreak,
} from '@/lib/chore-format'
import { CheckIcon, ReassignIcon } from '@/components/chores/icons'

/** Actions shared by every chore-row layout (list / compact). Mirrors the object built by
 * `ChoresPage.itemProps`. */
export interface ChoreItemProps {
  chore: Chore
  isAdmin: boolean
  /** The logged-in user's id, to gate member-only actions and the accept/decline affordance. */
  meId?: string
  undoPending: boolean
  skipPending: boolean
  deletePending: boolean
  freezePending?: boolean
  onComplete: () => void
  onUndo: () => void
  onSkip: () => void
  onReassign: () => void
  onReschedule: () => void
  onEdit: () => void
  onCopy: () => void
  onDelete: () => void
  onDetails: () => void
  onFreeze?: () => void
  onUnfreeze?: () => void
}

const DUE_TONE = { overdue: 'red', today: 'amber', upcoming: 'blue', later: 'neutral' } as const

/** Plain-text assignee label (no avatar) for the compact row's second line. Rotating chores show the
 * current assignee; independent chores would overflow if every track owner were listed, so they
 * collapse to "First +N" (or just the name when there's a single assignee). */
function assigneeLabel(chore: Chore): string {
  if (isIndependent(chore)) {
    const names = chore.tracks.map((t) => t.user.displayName)
    if (names.length === 0) return ''
    if (names.length === 1) return names[0]
    return `${names[0]} +${names.length - 1}`
  }
  return chore.currentAssignee?.displayName ?? ''
}

/** A dense two-row chore layout for the compact view: name + due date on top, points + assignee
 * below. Same actions as `ChoreListItem`, less chrome. */
export function ChoreCompactItem(props: ChoreItemProps) {
  const { chore, meId, onComplete, onDetails } = props
  const canComplete = Boolean(chore.dueAt) && !chore.isFrozen
  const status = dueStatus(chore.dueAt, choreHasDueTime(chore))
  const assignee = assigneeLabel(chore)
  const pending = chore.pendingReassignment

  return (
    <div className="flex items-center gap-3 px-4 py-2.5">
      <button
        type="button"
        onClick={onComplete}
        disabled={!canComplete}
        aria-label="Mark complete"
        className="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-primary text-primary-foreground transition-colors hover:bg-primary/90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-30"
      >
        <CheckIcon />
      </button>

      <button
        type="button"
        onClick={onDetails}
        className="flex min-w-0 flex-1 flex-col gap-1 text-left"
      >
        {/* Row 1: name */}
        <div className="flex min-w-0 items-center gap-2">
          {chore.emoji && <span className="shrink-0 text-base leading-none">{chore.emoji}</span>}
          <span className="truncate text-sm font-medium text-foreground">{chore.name}</span>
          {showStreak(chore) && <span className="shrink-0 text-xs text-success">🔥 {chore.currentStreak}</span>}
        </div>

        {/* Row 2: points + assignee name + due date (far right) */}
        <div className="flex min-w-0 items-center gap-2">
          <Badge tone="violet" className="shrink-0">{chore.points} pts</Badge>
          {assignee && <span className="min-w-0 truncate text-xs text-muted-foreground">{assignee}</span>}
          {pending && (
            <Badge
              tone="amber"
              className="shrink-0 px-1"
              title={pending.toUser.id === meId ? 'Reassignment requested for you' : 'Reassignment pending'}
              aria-label={pending.toUser.id === meId ? 'Reassignment requested for you' : 'Reassignment pending'}
            >
              <ReassignIcon />
            </Badge>
          )}
          {chore.isFrozen ? (
            <Badge tone="neutral" className="ml-auto shrink-0">Paused</Badge>
          ) : chore.dueAt ? (
            <Badge tone={DUE_TONE[status]} className="ml-auto shrink-0">
              {formatDate(chore.dueAt)}{nextDueTimeLabel(chore) && ` · ${nextDueTimeLabel(chore)}`}
            </Badge>
          ) : null}
        </div>
      </button>
    </div>
  )
}
