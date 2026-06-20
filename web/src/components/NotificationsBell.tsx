import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { notificationsApi } from '@/lib/api'
import type { NotificationInboxItem } from '@/lib/types'
import { cn } from '@/lib/utils'

interface NotificationsBellProps {
  /** Opens the chore details modal for the given chore id (resolved by the parent). */
  onOpenChore: (choreId: string) => void
}

function timeAgo(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime()
  const mins = Math.floor(diff / 60_000)
  if (mins < 1) return 'just now'
  if (mins < 60) return `${mins}m ago`
  const hours = Math.floor(mins / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)
  if (days < 7) return `${days}d ago`
  return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
}

export function NotificationsBell({ onOpenChore }: NotificationsBellProps) {
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  const { data: items } = useQuery({
    queryKey: ['inbox'],
    queryFn: notificationsApi.inbox,
    refetchInterval: 60_000,
    refetchOnWindowFocus: true,
  })

  const unread = (items ?? []).filter((n) => !n.read).length

  const markRead = useMutation({
    mutationFn: notificationsApi.markInboxRead,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['inbox'] }),
  })

  // Mark everything read when the panel closes, so unread highlights persist while it's open.
  const close = () => {
    setOpen(false)
    if (unread > 0) markRead.mutate()
  }

  useEffect(() => {
    if (!open) return
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) close()
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, unread])

  const onItemClick = (n: NotificationInboxItem) => {
    if (n.choreId) onOpenChore(n.choreId)
    close()
  }

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => (open ? close() : setOpen(true))}
        aria-label="Notifications"
        aria-expanded={open}
        className="relative inline-flex h-9 w-9 items-center justify-center rounded-full text-muted-foreground transition-colors hover:bg-accent hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        <BellIcon />
        {unread > 0 && (
          <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-primary px-1 text-[10px] font-medium leading-none text-primary-foreground">
            {unread > 9 ? '9+' : unread}
          </span>
        )}
      </button>

      {open && (
        <div className="absolute right-0 top-full z-30 mt-2 w-80 max-w-[calc(100vw-2rem)] overflow-hidden rounded-lg border border-border bg-card shadow-pop">
          <div className="flex items-center justify-between border-b border-border px-4 py-2.5">
            <span className="text-sm font-semibold text-foreground">Notifications</span>
            {unread > 0 && (
              <button
                type="button"
                onClick={() => markRead.mutate()}
                className="text-xs text-muted-foreground underline-offset-2 hover:text-foreground hover:underline"
              >
                Mark all read
              </button>
            )}
          </div>

          <div className="max-h-96 overflow-y-auto">
            {(items ?? []).length === 0 ? (
              <p className="px-4 py-8 text-center text-sm text-muted-foreground">No notifications yet.</p>
            ) : (
              (items ?? []).map((n) => {
                const clickable = Boolean(n.choreId)
                return (
                  <button
                    key={n.id}
                    type="button"
                    onClick={() => onItemClick(n)}
                    disabled={!clickable}
                    className={cn(
                      'flex w-full items-start gap-2.5 px-4 py-3 text-left transition-colors',
                      clickable ? 'hover:bg-accent' : 'cursor-default',
                      !n.read && 'bg-primary/5',
                    )}
                  >
                    <span
                      className={cn(
                        'mt-1.5 h-2 w-2 shrink-0 rounded-full',
                        n.read ? 'bg-transparent' : 'bg-primary',
                      )}
                    />
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-sm font-medium text-foreground">{n.title}</span>
                      <span className="block text-sm text-muted-foreground">{n.body}</span>
                      <span className="mt-0.5 block text-xs text-muted-foreground">{timeAgo(n.createdAt)}</span>
                    </span>
                  </button>
                )
              })
            )}
          </div>
        </div>
      )}
    </div>
  )
}

function BellIcon() {
  return (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9" />
      <path d="M13.73 21a2 2 0 0 1-3.46 0" />
    </svg>
  )
}
