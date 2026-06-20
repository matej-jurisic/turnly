import type { Chore } from '@/lib/types'
import { Badge } from '@/components/ui/Badge'
import { Card } from '@/components/ui/Card'
import { Avatar } from '@/components/ui/Modal'
import { formatDate, formatDueTime, repeatLabel } from '@/lib/chore-format'
import { CheckIcon } from '@/components/chores/icons'
import { ChoreMenu } from '@/components/chores/ChoreMenu'
import { SwipeRow } from '@/components/chores/SwipeRow'

export function ChoreListItem({
  chore,
  isAdmin,
  undoPending,
  skipPending,
  deletePending,
  onComplete,
  onUndo,
  onSkip,
  onReassign,
  onEdit,
  onDelete,
  onDetails,
}: {
  chore: Chore
  isAdmin: boolean
  undoPending: boolean
  skipPending: boolean
  deletePending: boolean
  onComplete: () => void
  onUndo: () => void
  onSkip: () => void
  onReassign: () => void
  onEdit: () => void
  onDelete: () => void
  onDetails: () => void
}) {
  const canComplete = Boolean(chore.dueAt)
  return (
    <div className="relative">
      <div className="absolute left-4 top-0 z-10 flex -translate-y-1/2 items-center gap-2">
        {chore.dueAt && (
          <Badge tone="amber" className="border border-warning bg-card">
            Due {formatDate(chore.dueAt)}{chore.dueTime && ` · ${formatDueTime(chore.dueTime)}`}
          </Badge>
        )}
        {chore.customMode === 'Frequency' ? (
          <Badge tone="violet" className="border border-primary bg-card">
            {chore.frequencyProgress ?? 0}/{chore.frequencyCount ?? 1} this {(chore.frequencyPeriod ?? 'Week').toLowerCase()}
          </Badge>
        ) : (
          <Badge tone="blue" className="border border-info bg-card">{repeatLabel(chore)}</Badge>
        )}
      </div>
      <SwipeRow onSwipeRight={canComplete ? onComplete : undefined} onSwipeLeft={onDetails}>
        <Card className="min-w-0 p-4">
          <div className="space-y-2">
            <div className="flex justify-between gap-3">
              <div className="flex min-w-0 items-center gap-2 self-center">
                {chore.emoji && <span className="shrink-0 text-xl leading-tight">{chore.emoji}</span>}
                <span className="line-clamp-2 font-semibold text-foreground">{chore.name}</span>
              </div>

              <div className="flex shrink-0 items-center gap-1 self-start">
                <button
                  type="button"
                  onClick={onComplete}
                  disabled={!canComplete}
                  aria-label="Mark complete"
                  className="inline-flex h-9 w-9 items-center justify-center rounded-full bg-primary text-primary-foreground transition-colors hover:bg-primary/90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-30"
                >
                  <CheckIcon />
                </button>
                <ChoreMenu
                  chore={chore}
                  isAdmin={isAdmin}
                  undoPending={undoPending}
                  skipPending={skipPending}
                  deletePending={deletePending}
                  onDetails={onDetails}
                  onUndo={onUndo}
                  onSkip={onSkip}
                  onReassign={onReassign}
                  onEdit={onEdit}
                  onDelete={onDelete}
                />
              </div>
            </div>

            {chore.description && (
              <p className="text-sm text-muted-foreground">{chore.description}</p>
            )}
            <div className="mt-2.5 flex items-center justify-between gap-3">
              <div className="flex min-w-0 flex-wrap items-center gap-2">
                <Badge tone="violet">{chore.points} pts</Badge>
                {chore.tags.map((tag) => (
                  <Badge key={tag} tone="neutral">{tag}</Badge>
                ))}
              </div>
              {chore.currentAssignee && (
                <div className="flex shrink-0 items-center gap-2">
                  <Avatar color={chore.currentAssignee.avatarColor} name={chore.currentAssignee.displayName} size={24} />
                  <span className="text-sm text-muted-foreground">{chore.currentAssignee.displayName}</span>
                  {chore.nextAssignee && (
                    <span
                      className="flex items-center gap-1 text-muted-foreground"
                      title={`Next: ${chore.nextAssignee.displayName}`}
                    >
                      <span aria-hidden="true">→</span>
                      <Avatar color={chore.nextAssignee.avatarColor} name={chore.nextAssignee.displayName} size={20} />
                      <span className="sr-only">Next: {chore.nextAssignee.displayName}</span>
                    </span>
                  )}
                </div>
              )}
            </div>
          </div>
        </Card>
      </SwipeRow>
    </div>
  )
}
