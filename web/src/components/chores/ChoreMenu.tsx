import { useEffect, useRef, useState } from 'react'
import type { Chore } from '@/lib/types'
import { isIndependent } from '@/lib/chore-format'
import {
  DotsIcon, InfoIcon, SkipIcon, ReassignIcon, UndoIcon, CalendarIcon, EditIcon, CopyIcon, TrashIcon,
  PauseIcon, PlayIcon, BellIcon, BellOffIcon,
} from '@/components/chores/icons'
import { t } from '@/lib/i18n'

export interface ChoreMenuProps {
  chore: Chore
  isAdmin: boolean
  /** The logged-in user's id, to gate member-only actions (offering a chore they hold). */
  meId?: string
  undoPending: boolean
  skipPending: boolean
  deletePending: boolean
  freezePending?: boolean
  mutePending?: boolean
  onDetails: () => void
  onToggleMute: () => void
  onUndo: () => void
  onSkip: () => void
  onReassign: () => void
  onOffer: () => void
  onReschedule: () => void
  onEdit: () => void
  onCopy: () => void
  onDelete: () => void
  onFreeze?: () => void
  onUnfreeze?: () => void
}

export function ChoreMenu({ chore, isAdmin, meId, undoPending, skipPending, deletePending, freezePending, mutePending, onDetails, onToggleMute, onUndo, onSkip, onReassign, onOffer, onReschedule, onEdit, onCopy, onDelete, onFreeze, onUnfreeze }: ChoreMenuProps) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  const hasUndo = Boolean(chore.lastCompletion)
  const undoLabel = chore.lastCompletion?.isSkip ? t('chores.undoSkipTitle') : t('common.undo')
  // Skip/reschedule in track mode target a specific person's schedule, so they're handled from the
  // details modal's per-person rows; the card menu only drives them for rotating chores.
  const canSkip = isAdmin && chore.repeatType !== 'OneTime' && Boolean(chore.dueAt) && !isIndependent(chore)
  // Only admins move a chore directly. Anyone holding one can offer it instead, but not while an
  // offer is already live (they withdraw it from the details modal first) or the chore is paused.
  // An unassigned chore can always be placed, even with a single candidate.
  const canReassign = isAdmin && !isIndependent(chore) && (chore.assignees.length > 1 || !chore.currentAssignee)
  // A pair's turn isn't one member's to hand over, so paired chores can't be offered at all.
  const canOffer = !isIndependent(chore) && chore.assignees.length > 1 && chore.pairs.length === 0
    && chore.currentAssignee?.id === meId && !chore.openOffer && !chore.isFrozen
  const canReschedule = isAdmin && Boolean(chore.dueAt) && !isIndependent(chore)

  useEffect(() => {
    if (!open) return
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [open])

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-label={t('menu.more')}
        aria-expanded={open}
        className="inline-flex h-9 w-9 items-center justify-center rounded-full text-muted-foreground transition-colors hover:bg-accent hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        <DotsIcon />
      </button>

      {open && (
        <div className="absolute right-0 top-full z-20 mt-1 min-w-[140px] rounded-lg border border-border bg-card py-1 shadow-pop">
          <button
            type="button"
            onClick={() => { setOpen(false); onDetails() }}
            className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent"
          >
            <InfoIcon />
            {t('menu.details')}
          </button>
          {/* Personal, so it's offered to everyone: muting only quiets the chore for you. */}
          <button
            type="button"
            disabled={mutePending}
            onClick={() => { setOpen(false); onToggleMute() }}
            className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent disabled:opacity-50"
          >
            {chore.isMuted ? <BellIcon /> : <BellOffIcon />}
            {chore.isMuted ? t('menu.unmute') : t('menu.mute')}
          </button>
          {canSkip && (
            <button
              type="button"
              disabled={skipPending}
              onClick={() => { setOpen(false); onSkip() }}
              className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent disabled:opacity-50"
            >
              <SkipIcon />
              {t('common.skip')}
            </button>
          )}
          {canReassign && (
            <button
              type="button"
              onClick={() => { setOpen(false); onReassign() }}
              className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent"
            >
              <ReassignIcon />
              {chore.currentAssignee ? t('menu.reassign') : t('menu.assign')}
            </button>
          )}
          {canOffer && (
            <button
              type="button"
              onClick={() => { setOpen(false); onOffer() }}
              className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent"
            >
              <ReassignIcon />
              {t('menu.offer')}
            </button>
          )}
          {hasUndo && (
            <button
              type="button"
              disabled={undoPending}
              onClick={() => { setOpen(false); onUndo() }}
              className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent disabled:opacity-50"
            >
              <UndoIcon />
              {undoLabel}
            </button>
          )}
          {canReschedule && (
            <button
              type="button"
              onClick={() => { setOpen(false); onReschedule() }}
              className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent"
            >
              <CalendarIcon />
              {t('menu.reschedule')}
            </button>
          )}
          {isAdmin && (
            <>
              {chore.isFrozen ? (
                <button
                  type="button"
                  disabled={freezePending}
                  onClick={() => { setOpen(false); onUnfreeze?.() }}
                  className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent disabled:opacity-50"
                >
                  <PlayIcon />
                  {t('menu.unpause')}
                </button>
              ) : (
                <button
                  type="button"
                  disabled={freezePending}
                  onClick={() => { setOpen(false); onFreeze?.() }}
                  className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent disabled:opacity-50"
                >
                  <PauseIcon />
                  {t('menu.pause')}
                </button>
              )}
              <button
                type="button"
                onClick={() => { setOpen(false); onEdit() }}
                className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent"
              >
                <EditIcon />
                {t('common.edit')}
              </button>
              <button
                type="button"
                onClick={() => { setOpen(false); onCopy() }}
                className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent"
              >
                <CopyIcon />
                {t('menu.copy')}
              </button>
              <button
                type="button"
                disabled={deletePending}
                onClick={() => { setOpen(false); onDelete() }}
                className="flex w-full items-center gap-2 px-3 py-2 text-sm text-destructive transition-colors hover:bg-destructive/10 disabled:opacity-50"
              >
                <TrashIcon />
                {t('common.delete')}
              </button>
            </>
          )}
        </div>
      )}
    </div>
  )
}
