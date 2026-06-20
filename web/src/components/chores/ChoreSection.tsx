import { useState, type ReactNode } from 'react'
import { cn } from '@/lib/utils'

export function ChoreSection({
  title,
  tone = 'default',
  count,
  children,
}: {
  title: string
  tone?: 'default' | 'destructive'
  count?: number
  children: ReactNode
}) {
  const [collapsed, setCollapsed] = useState(false)

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
