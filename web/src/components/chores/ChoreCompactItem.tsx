import type { Chore } from '@/lib/types'
import { Badge } from '@/components/ui/Badge'
import { Avatar } from '@/components/ui/Modal'
import {
  choreHasDueTime, dueStatus, formatDate, isIndependent, nextDueTimeLabel, showStreak, trackIsDone,
} from '@/lib/chore-format'
import { CheckIcon } from '@/components/chores/icons'
import { ChoreMenu } from '@/components/chores/ChoreMenu'

/** Actions shared by every chore-row layout (list / compact). Mirrors the object built by
 * `ChoresPage.itemProps`. */
export interface ChoreItemProps {
  chore: Chore
  isAdmin: boolean
  undoPending: boolean
  skipPending: boolean
  deletePending: boolean
  onComplete: () => void
  onUndo: () => void
  onSkip: () => void
  onReassign: () => void
  onReschedule: () => void
  onEdit: () => void
  onCopy: () => void
  onDelete: () => void
  onDetails: () => void
}

const DUE_TONE = { overdue: 'red', today: 'amber', upcoming: 'blue', later: 'neutral' } as const

/** A dense single-row chore layout for the compact view. Same actions as `ChoreListItem`, less chrome. */
export function ChoreCompactItem(props: ChoreItemProps) {
  const { chore, onComplete, onDetails } = props
  const canComplete = Boolean(chore.dueAt)
  const status = dueStatus(chore.dueAt, choreHasDueTime(chore))

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
        className="flex min-w-0 flex-1 items-center gap-2 text-left"
      >
        {chore.emoji && <span className="shrink-0 text-base leading-none">{chore.emoji}</span>}
        <span className="truncate text-sm font-medium text-foreground">{chore.name}</span>
        {showStreak(chore) && <span className="shrink-0 text-xs text-success">🔥 {chore.currentStreak}</span>}
      </button>

      {chore.dueAt && (
        <Badge tone={DUE_TONE[status]} className="hidden shrink-0 sm:inline-flex">
          {formatDate(chore.dueAt)}{nextDueTimeLabel(chore) && ` · ${nextDueTimeLabel(chore)}`}
        </Badge>
      )}

      <Badge tone="violet" className="hidden shrink-0 sm:inline-flex">{chore.points} pts</Badge>

      {isIndependent(chore) ? (
        <div className="flex shrink-0 items-center -space-x-1.5">
          {chore.tracks.map((t) => (
            <span
              key={t.user.id}
              title={t.user.displayName}
              className={'flex rounded-full ring-2 ring-card ' + (trackIsDone(chore, t) ? 'grayscale' : '')}
            >
              <Avatar color={t.user.avatarColor} name={t.user.displayName} size={22} />
            </span>
          ))}
        </div>
      ) : (
        chore.currentAssignee && (
          <Avatar color={chore.currentAssignee.avatarColor} name={chore.currentAssignee.displayName} size={22} />
        )
      )}

      <ChoreMenu {...props} />
    </div>
  )
}
