import { useEffect, useRef, useState } from 'react'
import { useAuthStore } from '@/store/auth'
import { Avatar } from '@/components/ui/Modal'
import { Badge } from '@/components/ui/Badge'
import { CustomizationModal } from '@/components/CustomizationModal'

export function UserMenu({ onLogout }: { onLogout: () => void }) {
  const user = useAuthStore((s) => s.user)
  const [open, setOpen] = useState(false)
  const [customizeOpen, setCustomizeOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    const onClick = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    document.addEventListener('mousedown', onClick)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onClick)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  if (!user) return null

  return (
    <div className="relative" ref={ref}>
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-label="Account menu"
        aria-haspopup="menu"
        aria-expanded={open}
        className="rounded-full focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-card"
      >
        <Avatar color={user.avatarColor} name={user.displayName} size={32} frame={user.equippedFrameKey} emoji={user.avatarEmoji} />
      </button>

      {open && (
        <div
          role="menu"
          className="absolute right-0 z-50 mt-2 w-56 overflow-hidden rounded-xl border border-border bg-popover text-popover-foreground shadow-pop"
        >
          <div className="border-b border-border px-3 py-3">
            <p className="truncate text-sm text-foreground">{user.displayName}</p>
            <div className="mt-1 flex items-center gap-2">
              <span className="truncate text-xs text-muted-foreground">@{user.username}</span>
              <Badge tone={user.role === 'Admin' ? 'violet' : 'neutral'}>{user.role}</Badge>
            </div>
          </div>

          <button
            type="button"
            role="menuitem"
            onClick={() => {
              setOpen(false)
              setCustomizeOpen(true)
            }}
            className="flex w-full items-center gap-2 px-3 py-2.5 text-sm text-foreground hover:bg-accent"
          >
            <PaletteIcon />
            Customization
          </button>

          <button
            type="button"
            role="menuitem"
            onClick={() => {
              setOpen(false)
              onLogout()
            }}
            className="flex w-full items-center gap-2 border-t border-border px-3 py-2.5 text-sm text-foreground hover:bg-accent"
          >
            <LogoutIcon />
            Sign out
          </button>
        </div>
      )}

      {customizeOpen && <CustomizationModal onClose={() => setCustomizeOpen(false)} />}
    </div>
  )
}

function PaletteIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <circle cx="13.5" cy="6.5" r="1.5" />
      <circle cx="17.5" cy="10.5" r="1.5" />
      <circle cx="8.5" cy="7.5" r="1.5" />
      <circle cx="6.5" cy="12.5" r="1.5" />
      <path d="M12 2C6.5 2 2 6.5 2 12s4.5 10 10 10c.926 0 1.648-.746 1.648-1.688 0-.437-.18-.835-.437-1.125-.29-.289-.438-.652-.438-1.125 0-.926.746-1.688 1.688-1.688H16.5c3.038 0 5.5-2.462 5.5-5.5C22 6.04 17.5 2 12 2z" />
    </svg>
  )
}

function LogoutIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9" />
    </svg>
  )
}
