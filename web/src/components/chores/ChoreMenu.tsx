import { useEffect, useRef, useState } from 'react'
import type { Chore } from '@/lib/types'
import { isIndependent } from '@/lib/chore-format'
import {
  DotsIcon, InfoIcon, SkipIcon, ReassignIcon, UndoIcon, CalendarIcon, EditIcon, TrashIcon,
} from '@/components/chores/icons'

export interface ChoreMenuProps {
  chore: Chore
  isAdmin: boolean
  undoPending: boolean
  skipPending: boolean
  deletePending: boolean
  onDetails: () => void
  onUndo: () => void
  onSkip: () => void
  onReassign: () => void
  onReschedule: () => void
  onEdit: () => void
  onDelete: () => void
}

export function ChoreMenu({ chore, isAdmin, undoPending, skipPending, deletePending, onDetails, onUndo, onSkip, onReassign, onReschedule, onEdit, onDelete }: ChoreMenuProps) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  const hasUndo = Boolean(chore.lastCompletion)
  const undoLabel = chore.lastCompletion?.isSkip ? 'Undo skip' : 'Undo'
  // Skip/reschedule in track mode target a specific person's schedule, so they're handled from the
  // details modal's per-person rows; the card menu only drives them for rotating chores.
  const canSkip = isAdmin && chore.repeatType !== 'OneTime' && Boolean(chore.dueAt) && !isIndependent(chore)
  const canReassign = !isIndependent(chore) && chore.assignees.length > 1
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
        aria-label="More options"
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
            Details
          </button>
          {canSkip && (
            <button
              type="button"
              disabled={skipPending}
              onClick={() => { setOpen(false); onSkip() }}
              className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent disabled:opacity-50"
            >
              <SkipIcon />
              Skip
            </button>
          )}
          {canReassign && (
            <button
              type="button"
              onClick={() => { setOpen(false); onReassign() }}
              className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent"
            >
              <ReassignIcon />
              Reassign
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
              Reschedule
            </button>
          )}
          {isAdmin && (
            <>
              <button
                type="button"
                onClick={() => { setOpen(false); onEdit() }}
                className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent"
              >
                <EditIcon />
                Edit
              </button>
              <button
                type="button"
                disabled={deletePending}
                onClick={() => { setOpen(false); onDelete() }}
                className="flex w-full items-center gap-2 px-3 py-2 text-sm text-destructive transition-colors hover:bg-destructive/10 disabled:opacity-50"
              >
                <TrashIcon />
                Delete
              </button>
            </>
          )}
        </div>
      )}
    </div>
  )
}
