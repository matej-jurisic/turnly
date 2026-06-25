import { useEffect, useRef, useState } from 'react'
import type { User } from '@/lib/types'
import {
  DotsIcon, EditIcon, TrashIcon, PauseIcon, PlayIcon,
} from '@/components/chores/icons'

function KeyIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <circle cx="7.5" cy="15.5" r="4.5" />
      <path d="M10.7 12.3 21 2" />
      <path d="m16 7 3 3" />
      <path d="m18 5 3 3" />
    </svg>
  )
}

function CoinsIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <circle cx="8" cy="8" r="6" />
      <path d="M18.09 10.37A6 6 0 1 1 10.34 18" />
      <path d="M7 6h1v4" />
      <path d="m16.71 13.88.7.71-2.82 2.82" />
    </svg>
  )
}

export interface UserMenuProps {
  user: User
  isSelf: boolean
  unfreezePending: boolean
  deletePending: boolean
  onEdit: () => void
  onPassword: () => void
  onPoints: () => void
  onFreeze: () => void
  onUnfreeze: () => void
  onDelete: () => void
}

export function UserMenu({ user, isSelf, unfreezePending, deletePending, onEdit, onPassword, onPoints, onFreeze, onUnfreeze, onDelete }: UserMenuProps) {
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

  const itemClass = 'flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent disabled:opacity-50'

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
        <div className="absolute right-0 top-full z-20 mt-1 min-w-[160px] rounded-lg border border-border bg-card py-1 shadow-pop">
          <button type="button" onClick={() => { setOpen(false); onEdit() }} className={itemClass}>
            <EditIcon />
            Edit
          </button>
          <button type="button" onClick={() => { setOpen(false); onPassword() }} className={itemClass}>
            <KeyIcon />
            Password
          </button>
          <button type="button" onClick={() => { setOpen(false); onPoints() }} className={itemClass}>
            <CoinsIcon />
            Points
          </button>
          {user.isFrozen ? (
            <button
              type="button"
              disabled={unfreezePending}
              onClick={() => { setOpen(false); onUnfreeze() }}
              className={itemClass}
            >
              <PlayIcon />
              Unfreeze
            </button>
          ) : (
            <button
              type="button"
              disabled={isSelf}
              onClick={() => { setOpen(false); onFreeze() }}
              className={itemClass}
            >
              <PauseIcon />
              Freeze
            </button>
          )}
          <button
            type="button"
            disabled={isSelf || deletePending}
            onClick={() => { setOpen(false); onDelete() }}
            className="flex w-full items-center gap-2 px-3 py-2 text-sm text-destructive transition-colors hover:bg-destructive/10 disabled:opacity-50"
          >
            <TrashIcon />
            Delete
          </button>
        </div>
      )}
    </div>
  )
}
