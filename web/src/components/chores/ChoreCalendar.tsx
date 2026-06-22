import { useMemo, useState } from 'react'
import type { Chore } from '@/lib/types'
import { Card } from '@/components/ui/Card'
import { choreHasDueTime, dueStatus } from '@/lib/chore-format'
import { cn } from '@/lib/utils'

const WEEKDAY_HEADERS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']
const CHIP_TONE: Record<string, string> = {
  overdue: 'bg-destructive/15 text-destructive',
  today: 'bg-warning/15 text-warning',
  upcoming: 'bg-info/15 text-info',
  later: 'bg-accent text-muted-foreground',
}
const MAX_CHIPS = 3

/** Local YYYY-MM-DD key for a date (used to bucket chores onto calendar days). */
function dayKey(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/** Month calendar placing each scheduled chore on its due day. Clicking a chore opens its details.
 * Chores with no due date (unscheduled / one-time done) don't appear. */
export function ChoreCalendar({ chores, onSelect }: { chores: Chore[]; onSelect: (chore: Chore) => void }) {
  const [view, setView] = useState(() => {
    const now = new Date()
    return new Date(now.getFullYear(), now.getMonth(), 1)
  })

  const byDay = useMemo(() => {
    const map = new Map<string, Chore[]>()
    for (const c of chores) {
      if (!c.dueAt) continue
      const key = dayKey(new Date(c.dueAt))
      const list = map.get(key)
      if (list) list.push(c)
      else map.set(key, [c])
    }
    return map
  }, [chores])

  const cells = useMemo(() => {
    const first = new Date(view.getFullYear(), view.getMonth(), 1)
    const startOffset = (first.getDay() + 6) % 7 // days since Monday
    return Array.from({ length: 42 }, (_, i) =>
      new Date(view.getFullYear(), view.getMonth(), 1 - startOffset + i),
    )
  }, [view])

  const todayKey = dayKey(new Date())
  const monthLabel = view.toLocaleDateString('en-GB', { month: 'long', year: 'numeric' })
  const shiftMonth = (delta: number) =>
    setView((v) => new Date(v.getFullYear(), v.getMonth() + delta, 1))
  const goToday = () => {
    const now = new Date()
    setView(new Date(now.getFullYear(), now.getMonth(), 1))
  }

  return (
    <Card className="overflow-hidden p-0">
      <div className="flex items-center justify-between border-b border-border px-4 py-3">
        <h2 className="text-base font-semibold text-foreground">{monthLabel}</h2>
        <div className="flex items-center gap-1">
          <button
            type="button"
            onClick={goToday}
            className="rounded-md px-2.5 py-1 text-sm text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
          >
            Today
          </button>
          <button type="button" onClick={() => shiftMonth(-1)} aria-label="Previous month" className="flex h-8 w-8 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-accent hover:text-foreground">‹</button>
          <button type="button" onClick={() => shiftMonth(1)} aria-label="Next month" className="flex h-8 w-8 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-accent hover:text-foreground">›</button>
        </div>
      </div>

      <div className="grid grid-cols-7 border-b border-border bg-accent/40">
        {WEEKDAY_HEADERS.map((d) => (
          <div key={d} className="px-2 py-1.5 text-center text-xs font-medium text-muted-foreground">{d}</div>
        ))}
      </div>

      <div className="grid grid-cols-7">
        {cells.map((day, i) => {
          const key = dayKey(day)
          const inMonth = day.getMonth() === view.getMonth()
          const isToday = key === todayKey
          const dayChores = byDay.get(key) ?? []
          return (
            <div
              key={i}
              className={cn(
                'min-h-[5.5rem] border-b border-r border-border p-1.5 last:border-r-0 [&:nth-child(7n)]:border-r-0',
                !inMonth && 'bg-accent/30',
              )}
            >
              <div className="mb-1 flex justify-end">
                <span
                  className={cn(
                    'flex h-5 w-5 items-center justify-center rounded-full text-xs',
                    isToday ? 'bg-primary font-semibold text-primary-foreground' : inMonth ? 'text-foreground' : 'text-muted-foreground',
                  )}
                >
                  {day.getDate()}
                </span>
              </div>
              <div className="space-y-0.5">
                {dayChores.slice(0, MAX_CHIPS).map((c) => {
                  const tone = CHIP_TONE[dueStatus(c.dueAt, choreHasDueTime(c))]
                  return (
                    <button
                      key={c.id}
                      type="button"
                      onClick={() => onSelect(c)}
                      title={c.name}
                      className={cn('block w-full truncate rounded px-1.5 py-0.5 text-left text-xs transition-opacity hover:opacity-80', tone)}
                    >
                      {c.emoji ? `${c.emoji} ` : ''}{c.name}
                    </button>
                  )
                })}
                {dayChores.length > MAX_CHIPS && (
                  <button
                    type="button"
                    onClick={() => onSelect(dayChores[MAX_CHIPS])}
                    className="px-1.5 text-xs text-muted-foreground hover:text-foreground"
                  >
                    +{dayChores.length - MAX_CHIPS} more
                  </button>
                )}
              </div>
            </div>
          )
        })}
      </div>
    </Card>
  )
}
