import { useMemo, useState } from 'react'
import type { Chore } from '@/lib/types'
import { Card } from '@/components/ui/Card'
import { choreHasDueTime, dueStatus } from '@/lib/chore-format'
import { ChoreCompactItem, type ChoreItemProps } from '@/components/chores/ChoreCompactItem'
import { cn } from '@/lib/utils'

const WEEKDAY_HEADERS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']
const COUNT_TONE: Record<string, string> = {
  overdue: 'bg-destructive/15 text-destructive',
  today: 'bg-warning/15 text-warning',
  upcoming: 'bg-info/15 text-info',
  later: 'bg-accent text-muted-foreground',
}
const STATUS_RANK: Record<string, number> = { overdue: 0, today: 1, upcoming: 2, later: 3 }

/** Local YYYY-MM-DD key for a date (used to bucket chores onto calendar days). */
function dayKey(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/** Parse a YYYY-MM-DD key back into a local Date (midnight). */
function parseKey(key: string): Date {
  const [y, m, d] = key.split('-').map(Number)
  return new Date(y, m - 1, d)
}

/** The most-urgent status among a day's chores — drives the count pill's tone. */
function worstStatus(chores: Chore[]): string {
  return chores.reduce((acc, c) => {
    const s = dueStatus(c.dueAt, choreHasDueTime(c))
    return STATUS_RANK[s] < STATUS_RANK[acc] ? s : acc
  }, 'later')
}

/** Month calendar with a per-day chore count. Selecting a day lists that day's chores (compact
 * layout) below the grid. Chores with no due date (unscheduled / one-time done) don't appear. */
export function ChoreCalendar({
  chores,
  itemProps,
}: {
  chores: Chore[]
  itemProps: (chore: Chore) => ChoreItemProps
}) {
  const [view, setView] = useState(() => {
    const now = new Date()
    return new Date(now.getFullYear(), now.getMonth(), 1)
  })
  const [selected, setSelected] = useState<string>(() => dayKey(new Date()))

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
    setSelected(todayKey)
  }

  const selectedChores = byDay.get(selected) ?? []
  const selectedLabel = parseKey(selected).toLocaleDateString('en-GB', {
    weekday: 'long', day: 'numeric', month: 'long',
  })

  return (
    <div className="space-y-4">
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
            const isSelected = key === selected
            const dayChores = byDay.get(key) ?? []
            return (
              <button
                key={i}
                type="button"
                onClick={() => setSelected(key)}
                aria-pressed={isSelected}
                className={cn(
                  // min-height reserves room for the count pill so a day with chores is the same
                  // height as one without (the date stays top-aligned either way).
                  'flex min-h-[4rem] flex-col items-center gap-1 border-b border-r border-border p-1.5 transition-colors last:border-r-0 [&:nth-child(7n)]:border-r-0 hover:bg-accent/60',
                  !inMonth && 'bg-accent/30',
                  isSelected && 'bg-primary/5 ring-1 ring-inset ring-primary',
                )}
              >
                <span
                  className={cn(
                    'flex h-6 w-6 items-center justify-center rounded-full text-xs',
                    isToday ? 'bg-primary font-semibold text-primary-foreground' : inMonth ? 'text-foreground' : 'text-muted-foreground',
                  )}
                >
                  {day.getDate()}
                </span>
                {dayChores.length > 0 && (
                  <span className={cn('rounded-full px-2 py-0.5 text-[11px] font-medium leading-none', COUNT_TONE[worstStatus(dayChores)])}>
                    {dayChores.length}
                  </span>
                )}
              </button>
            )
          })}
        </div>
      </Card>

      <div>
        <h3 className="mb-2 px-1 text-sm font-semibold text-foreground">{selectedLabel}</h3>
        {selectedChores.length > 0 ? (
          <Card className="divide-y divide-border p-0">
            {selectedChores.map((c) => <ChoreCompactItem key={c.id} {...itemProps(c)} />)}
          </Card>
        ) : (
          <p className="px-1 text-sm text-muted-foreground">No chores due on this day.</p>
        )}
      </div>
    </div>
  )
}
