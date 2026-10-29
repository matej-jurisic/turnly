import type { Chore } from '@/lib/types'
import { Badge } from '@/components/ui/Badge'
import {
  canComplete, choreHasDueTime, dueStatus, formatDueDate, formatDueWeekday, isAnyone, isIndependent, isOnDemand, nextDueTimeLabel,
  offerSentence, showStreak, pairLabel,
} from '@/lib/chore-format'
import { BellOffIcon, CheckIcon, ReassignIcon } from '@/components/chores/icons'
import { t } from '@/lib/i18n'

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
  mutePending?: boolean
  onComplete: () => void
  onToggleMute: () => void
  onUndo: () => void
  onSkip: () => void
  onReassign: () => void
  onOffer: () => void
  onNudge: () => void
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
  if (isAnyone(chore)) return t('chore.anyone')
  return chore.currentAssignee ? pairLabel(chore.currentAssignee, chore.currentPartner) : t('chore.unassigned')
}

/** A dense two-row chore layout for the compact view: name + points on top, assignee + due date
 * below. Same actions as `ChoreListItem`, less chrome. */
export function ChoreCompactItem(props: ChoreItemProps) {
  const { chore, meId, onComplete, onDetails } = props
  const completable = canComplete(chore)
  const status = dueStatus(chore.dueAt, choreHasDueTime(chore))
  const assignee = assigneeLabel(chore)
  const offer = chore.openOffer
  const offerLabel = offer ? offerSentence(offer, meId) : ''

  return (
    <div className="flex items-center gap-3 px-4 py-2.5">
      <button
        type="button"
        onClick={onComplete}
        disabled={!completable}
        aria-label={t('chore.markComplete')}
        className="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-primary text-primary-foreground transition-colors hover:bg-primary/90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-30"
      >
        <CheckIcon />
      </button>

      <button
        type="button"
        onClick={onDetails}
        className="flex min-w-0 flex-1 flex-col gap-1 text-left"
      >
        {/* Row 1: name + points (far right) */}
        <div className="flex min-w-0 items-center gap-2">
          {chore.emoji && <span className="shrink-0 text-base leading-none">{chore.emoji}</span>}
          <span className="truncate text-sm font-medium text-foreground">{chore.name}</span>
          {chore.isMuted && (
            <span
              className="shrink-0 text-muted-foreground"
              title={t('chore.mutedTitle')}
              aria-label={t('chore.mutedLabel')}
            >
              <BellOffIcon />
            </span>
          )}
          {showStreak(chore) && <span className="shrink-0 text-xs text-success">🔥 {chore.currentStreak}</span>}
          <Badge tone="violet" className="ml-auto shrink-0">{t('common.pts', { count: chore.points })}</Badge>
        </div>

        {/* Row 2: assignee name + due date (far right) */}
        <div className="flex min-w-0 items-center gap-2">
          {assignee && <span className="min-w-0 truncate text-xs text-muted-foreground">{assignee}</span>}
          {offer && (
            <Badge tone="amber" className="min-w-0 gap-1" title={offerLabel} aria-label={offerLabel}>
              <ReassignIcon />
              <span className="truncate">{t('offer.upForGrabs')}</span>
              {offer.bountyPoints > 0 && <span className="shrink-0">+{offer.bountyPoints}</span>}
            </Badge>
          )}
          {chore.isFrozen ? (
            <Badge tone="neutral" className="ml-auto shrink-0">{t('chores.paused')}</Badge>
          ) : chore.dueAt ? (
            <Badge tone={DUE_TONE[status]} className="ml-auto shrink-0">
              {t('chores.dueDate', { day: formatDueWeekday(chore.dueAt), date: formatDueDate(chore.dueAt) })}{nextDueTimeLabel(chore) && ` · ${nextDueTimeLabel(chore)}`}
            </Badge>
          ) : isOnDemand(chore) ? (
            <Badge tone="neutral" className="ml-auto shrink-0">{t('repeat.OnDemand')}</Badge>
          ) : null}
        </div>
      </button>
    </div>
  )
}
