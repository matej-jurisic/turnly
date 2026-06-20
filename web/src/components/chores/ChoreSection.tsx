import type { ReactNode } from 'react'
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
  const pillClass =
    tone === 'destructive'
      ? 'border-destructive/20 bg-destructive/10 text-destructive'
      : 'border-border bg-card text-muted-foreground'

  return (
    <section className="space-y-6">
      <div className="flex items-center gap-3">
        <div className="h-px flex-1 bg-border" />
        <span className={cn('rounded-full border px-3 py-0.5 text-xs font-semibold', pillClass)}>
          {title}{count != null ? ` ${count}` : ''}
        </span>
        <div className="h-px flex-1 bg-border" />
      </div>
      {children}
    </section>
  )
}
