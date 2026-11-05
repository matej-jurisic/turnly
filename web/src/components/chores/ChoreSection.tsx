import { useState, type ReactNode } from 'react'
import { cn } from '@/lib/utils'

const COLLAPSE_KEY = 'turnly:chore-section:'

function readCollapsed(id: string, fallback: boolean): boolean {
  try {
    const v = localStorage.getItem(COLLAPSE_KEY + id)
    return v === null ? fallback : v === '1'
  } catch {
    return fallback
  }
}

export function ChoreSection({
  id,
  title,
  tone = 'default',
  count,
  defaultCollapsed = false,
  bare = false,
  children,
}: {
  /** Stable key the collapsed state is remembered under (per device). */
  id: string
  title: string
  tone?: 'default' | 'destructive' | 'neutral'
  count?: number
  defaultCollapsed?: boolean
  /** Render only the content, no header (the section is the only one on the page). */
  bare?: boolean
  children: ReactNode
}) {
  const [collapsed, setCollapsedState] = useState(() => readCollapsed(id, defaultCollapsed))
  const setCollapsed = (update: (v: boolean) => boolean) =>
    setCollapsedState((v) => {
      const next = update(v)
      try {
        localStorage.setItem(COLLAPSE_KEY + id, next ? '1' : '0')
      } catch {
        // Storage unavailable: the toggle still works for this visit.
      }
      return next
    })

  if (bare) return <section>{children}</section>

  const pillClass =
    tone === 'destructive'
      ? 'border-destructive/20 bg-destructive/10 text-destructive hover:bg-destructive/15'
      : 'border-border bg-card text-muted-foreground hover:bg-accent'

  return (
    <section className="space-y-6">
      <div className="flex items-center gap-3">
        <div className="h-px flex-1 bg-border" />
        <button
          type="button"
          onClick={() => setCollapsed((v) => !v)}
          aria-expanded={!collapsed}
          className={cn(
            'inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-xs font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring',
            pillClass,
          )}
        >
          {title}{count != null ? ` ${count}` : ''}
          <svg
            width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor"
            strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"
            className={cn('transition-transform', collapsed ? '-rotate-90' : '')}
          >
            <path d="m6 9 6 6 6-6" />
          </svg>
        </button>
        <div className="h-px flex-1 bg-border" />
      </div>
      {!collapsed && children}
    </section>
  )
}
