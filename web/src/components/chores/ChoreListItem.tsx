import { useState } from 'react'
import type { Chore, User } from '@/lib/types'
import { Badge } from '@/components/ui/Badge'
import { Card } from '@/components/ui/Card'
import { Avatar, Modal } from '@/components/ui/Modal'
import {
  compactRepeatLabel, completionProgressLabel, formatDueDate, isIndependent, nextDueTimeLabel,
  offerBadgeText, offerSentence, repeatLabel, showStreak, trackIsDone, trackStatusText, pairLabel,
} from '@/lib/chore-format'
import { BellOffIcon, CheckIcon, ReassignIcon } from '@/components/chores/icons'
import { ChoreMenu } from '@/components/chores/ChoreMenu'
import { SwipeRow } from '@/components/chores/SwipeRow'
import { t } from '@/lib/i18n'

export function ChoreListItem({
  chore,
  isAdmin,
  meId,
  undoPending,
  skipPending,
  deletePending,
  freezePending,
  mutePending,
  onComplete,
  onToggleMute,
  onUndo,
  onSkip,
  onReassign,
  onOffer,
  onReschedule,
  onEdit,
  onCopy,
  onDelete,
  onDetails,
  onFreeze,
  onUnfreeze,
}: {
  chore: Chore
  isAdmin: boolean
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
  onReschedule: () => void
  onEdit: () => void
  onCopy: () => void
  onDelete: () => void
  onDetails: () => void
  onFreeze?: () => void
  onUnfreeze?: () => void
}) {
  const canComplete = Boolean(chore.dueAt) && !chore.isFrozen
  // Paired chores drop the names on phones (two names crowd the row), so the avatars open a dialog
  // that spells them out.
  const isPaired = Boolean(chore.currentPartner || chore.nextPartner)
  const [showHolders, setShowHolders] = useState(false)
  const HolderGroup = isPaired ? 'button' : 'div'
  return (
    <div className="relative">
      <div className="absolute left-4 right-4 top-0 z-10 flex -translate-y-1/2 items-center gap-2">
        {chore.isFrozen ? (
          <Badge tone="neutral" className="shrink-0 border border-border bg-card">{t('chores.paused')}</Badge>
        ) : chore.dueAt ? (
          <Badge tone="violet" className="shrink-0 border border-primary bg-card">
            {formatDueDate(chore.dueAt)}{nextDueTimeLabel(chore) && ` · ${nextDueTimeLabel(chore)}`}
          </Badge>
        ) : null}
        {chore.completionsRequired > 1 ? (
          <Badge tone="secondary" className="min-w-0 border border-border" title={completionProgressLabel(chore)}>
            <span className="truncate">{completionProgressLabel(chore)}</span>
          </Badge>
        ) : (
          <Badge tone="secondary" className="min-w-0 border border-border" title={repeatLabel(chore)}>
            <span className="truncate">{compactRepeatLabel(chore)}</span>
          </Badge>
        )}
      </div>
      <SwipeRow onSwipeRight={canComplete ? onComplete : undefined} onSwipeLeft={onDetails}>
        <Card className="min-w-0 p-4">
          <div className="space-y-2">
            <div className="flex justify-between gap-3">
              <div className="flex min-w-0 items-center gap-2 self-center">
                {chore.emoji && <span className="shrink-0 text-xl leading-tight">{chore.emoji}</span>}
                <span className="line-clamp-2 font-semibold text-foreground">{chore.name}</span>
                {chore.isMuted && (
                  <span
                    className="shrink-0 text-muted-foreground"
                    title={t('chore.mutedTitle')}
                    aria-label={t('chore.mutedLabel')}
                  >
                    <BellOffIcon />
                  </span>
                )}
              </div>

              <div className="flex shrink-0 items-center gap-1 self-start">
                <button
                  type="button"
                  onClick={onComplete}
                  disabled={!canComplete}
                  aria-label={t('chore.markComplete')}
                  className="inline-flex h-9 w-9 items-center justify-center rounded-full bg-primary text-primary-foreground transition-colors hover:bg-primary/90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-30"
                >
                  <CheckIcon />
                </button>
                <ChoreMenu
                  chore={chore}
                  isAdmin={isAdmin}
                  meId={meId}
                  undoPending={undoPending}
                  skipPending={skipPending}
                  deletePending={deletePending}
                  freezePending={freezePending}
                  mutePending={mutePending}
                  onDetails={onDetails}
                  onToggleMute={onToggleMute}
                  onUndo={onUndo}
                  onSkip={onSkip}
                  onReassign={onReassign}
                  onOffer={onOffer}
                  onReschedule={onReschedule}
                  onEdit={onEdit}
                  onCopy={onCopy}
                  onDelete={onDelete}
                  onFreeze={onFreeze}
                  onUnfreeze={onUnfreeze}
                />
              </div>
            </div>

            {chore.description && (
              <p className="text-sm text-muted-foreground">{chore.description}</p>
            )}
            <div className="mt-2.5 flex items-center justify-between gap-3">
              <div className="flex min-w-0 flex-wrap items-center gap-2">
                <Badge tone="violet">{t('common.pts', { count: chore.points })}</Badge>
                {chore.openOffer && (() => {
                  const offer = chore.openOffer!
                  const label = offerSentence(offer, meId)
                  return (
                    <Badge tone="amber" className="max-w-full gap-1" title={label} aria-label={label}>
                      <ReassignIcon />
                      <span className="truncate">{offerBadgeText(offer, meId)}</span>
                      {offer.bountyPoints > 0 && <span className="shrink-0">+{offer.bountyPoints}</span>}
                    </Badge>
                  )
                })()}
                {showStreak(chore) && (
                  <Badge tone="secondary" className="border border-border">🔥 {chore.currentStreak}</Badge>
                )}
                {chore.tags.map((tag) => (
                  <Badge key={tag} tone="neutral">{tag}</Badge>
                ))}
              </div>
              {isIndependent(chore) ? (
                <div className="flex shrink-0 items-center gap-1.5">
                  {chore.tracks.map((t) => {
                    const done = trackIsDone(chore, t)
                    return (
                      <span
                        key={t.user.id}
                        title={`${t.user.displayName}: ${trackStatusText(chore, t)}`}
                        className="relative inline-flex rounded-full"
                      >
                        <span className={'flex ' + (done ? 'grayscale' : '')}>
                          <Avatar color={t.user.avatarColor} name={t.user.displayName} size={24} frame={t.user.equippedFrameKey} emoji={t.user.avatarEmoji} />
                        </span>
                        {done && (
                          <span className="absolute -bottom-0.5 -right-0.5 flex h-3.5 w-3.5 items-center justify-center rounded-full bg-success text-success-foreground">
                            <CheckIcon />
                          </span>
                        )}
                      </span>
                    )
                  })}
                </div>
              ) : !chore.currentAssignee ? (
                <Badge tone="amber">{t('chore.unassigned')}</Badge>
              ) : (
                <HolderGroup
                  {...(isPaired && {
                    type: 'button' as const,
                    onClick: () => setShowHolders(true),
                    'aria-label': t('chore.assignedTo', { name: pairLabel(chore.currentAssignee, chore.currentPartner) }),
                  })}
                  className={`flex shrink-0 items-center gap-2 ${isPaired ? 'rounded-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring' : ''}`}
                >
                  <span className={`text-sm text-muted-foreground ${chore.currentPartner ? 'hidden sm:inline' : ''}`}>
                    {pairLabel(chore.currentAssignee, chore.currentPartner)}
                  </span>
                  <span className="flex gap-1">
                    <Avatar color={chore.currentAssignee.avatarColor} name={chore.currentAssignee.displayName} size={24} frame={chore.currentAssignee.equippedFrameKey} emoji={chore.currentAssignee.avatarEmoji} />
                    {chore.currentPartner && (
                      <Avatar color={chore.currentPartner.avatarColor} name={chore.currentPartner.displayName} size={24} frame={chore.currentPartner.equippedFrameKey} emoji={chore.currentPartner.avatarEmoji} />
                    )}
                  </span>
                  {chore.nextAssignee && (
                    <span
                      className="flex items-center gap-1 text-muted-foreground"
                      title={t('chore.nextName', { name: pairLabel(chore.nextAssignee, chore.nextPartner) })}
                    >
                      <span aria-hidden="true">→</span>
                      <span className="flex gap-1">
                        <Avatar color={chore.nextAssignee.avatarColor} name={chore.nextAssignee.displayName} size={20} frame={chore.nextAssignee.equippedFrameKey} emoji={chore.nextAssignee.avatarEmoji} />
                        {chore.nextPartner && (
                          <Avatar color={chore.nextPartner.avatarColor} name={chore.nextPartner.displayName} size={20} frame={chore.nextPartner.equippedFrameKey} emoji={chore.nextPartner.avatarEmoji} />
                        )}
                      </span>
                      <span className="sr-only">{t('chore.nextName', { name: pairLabel(chore.nextAssignee, chore.nextPartner) })}</span>
                    </span>
                  )}
                </HolderGroup>
              )}
            </div>
          </div>
        </Card>
      </SwipeRow>
      {showHolders && chore.currentAssignee && (
        <Modal title={chore.name} onClose={() => setShowHolders(false)} widthClassName="max-w-xs">
          <div className="space-y-4">
            <HolderList label={t('chore.now')} users={[chore.currentAssignee, chore.currentPartner]} />
            {chore.nextAssignee && <HolderList label={t('chore.next')} users={[chore.nextAssignee, chore.nextPartner]} />}
          </div>
        </Modal>
      )}
    </div>
  )
}

/** One labelled group of people (the current pair, or the next one) with their full names. */
function HolderList({ label, users }: { label: string; users: (User | null | undefined)[] }) {
  return (
    <div>
      <p className="mb-1.5 text-xs font-medium text-muted-foreground">{label}</p>
      <ul className="space-y-2">
        {users.filter((u): u is User => Boolean(u)).map((u) => (
          <li key={u.id} className="flex items-center gap-2.5 text-sm">
            <Avatar color={u.avatarColor} name={u.displayName} size={28} frame={u.equippedFrameKey} emoji={u.avatarEmoji} />
            {u.displayName}
          </li>
        ))}
      </ul>
    </div>
  )
}
