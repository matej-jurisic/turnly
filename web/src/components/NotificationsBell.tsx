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
  return new Date(iso).toLocaleDateString('en-GB')
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

  const deleteItem = useMutation({
    mutationFn: (id: string) => notificationsApi.deleteInboxItem(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['inbox'] }),
  })

  const clearAll = useMutation({
    mutationFn: notificationsApi.clearInbox,
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
        <div className="fixed left-4 right-4 top-[4.5rem] z-30 overflow-hidden rounded-lg border border-border bg-card shadow-pop sm:absolute sm:left-auto sm:right-0 sm:top-full sm:mt-2 sm:w-80 sm:max-w-[calc(100vw-2rem)]">
          <div className="flex items-center justify-between gap-3 border-b border-border px-4 py-2.5">
            <span className="text-sm font-semibold text-foreground">Notifications</span>
            <div className="flex items-center gap-3">
              {unread > 0 && (
                <button
                  type="button"
                  onClick={() => markRead.mutate()}
                  className="text-xs text-muted-foreground underline-offset-2 hover:text-foreground hover:underline"
                >
                  Mark all read
                </button>
              )}
              {(items ?? []).length > 0 && (
                <button
                  type="button"
                  onClick={() => clearAll.mutate()}
                  className="text-xs text-muted-foreground underline-offset-2 hover:text-foreground hover:underline"
                >
                  Clear all
                </button>
              )}
            </div>
          </div>

          <div className="max-h-96 overflow-y-auto">
            {(items ?? []).length === 0 ? (
              <p className="px-4 py-8 text-center text-sm text-muted-foreground">No notifications yet.</p>
            ) : (
              (items ?? []).map((n) => {
                const clickable = Boolean(n.choreId)
                return (
                  <div
                    key={n.id}
                    className={cn(
                      'relative flex items-start transition-colors',
                      clickable && 'hover:bg-accent',
                      !n.read && 'bg-primary/5',
                    )}
                  >
                    <button
                      type="button"
                      onClick={() => onItemClick(n)}
                      disabled={!clickable}
                      className={cn(
                        'flex flex-1 items-start gap-2.5 py-3 pl-4 pr-9 text-left',
                        !clickable && 'cursor-default',
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
                    <button
                      type="button"
                      onClick={() => deleteItem.mutate(n.id)}
                      aria-label="Delete notification"
                      className="absolute right-1.5 top-2 inline-flex h-7 w-7 items-center justify-center rounded-full text-muted-foreground/70 transition-colors hover:bg-accent hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                    >
                      <CloseIcon />
                    </button>
                  </div>
                )
              })
            )}
          </div>
        </div>
      )}
    </div>
  )
}

function CloseIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M18 6 6 18M6 6l12 12" />
    </svg>
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
