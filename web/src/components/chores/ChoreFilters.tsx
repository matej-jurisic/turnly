import { useEffect, useRef, useState } from 'react'
import type { RepeatType, User } from '@/lib/types'
import { Avatar } from '@/components/ui/Modal'
import { REPEAT_OPTIONS } from '@/lib/chore-format'

export type DueStatus = 'overdue' | 'today' | 'upcoming' | 'later'

export interface ChoreFilterState {
  tags: string[]
  assignees: string[]
  /** When set, the assignee filter also matches chores where the member is the *next* assignee. */
  includeNext: boolean
  due: DueStatus[]
  repeat: RepeatType[]
}

export const emptyFilters: ChoreFilterState = {
  tags: [], assignees: [], includeNext: false, due: [], repeat: [],
}

/** Number of active filter values (the "includeNext" modifier isn't counted on its own). */
export function filterCount(f: ChoreFilterState): number {
  return f.tags.length + f.assignees.length + f.due.length + f.repeat.length
}

const DUE_OPTIONS: { value: DueStatus; label: string }[] = [
  { value: 'overdue', label: 'Overdue' },
  { value: 'today', label: 'Today' },
  { value: 'upcoming', label: 'This week' },
  { value: 'later', label: 'Later' },
]
function toggle<T>(list: T[], value: T): T[] {
  return list.includes(value) ? list.filter((v) => v !== value) : [...list, value]
}

function sameSet(a: string[], b: string[]): boolean {
  return a.length === b.length && a.every((v) => b.includes(v))
}
/** Two filter states are equivalent (order-insensitive across the list fields). */
function sameFilters(a: ChoreFilterState, b: ChoreFilterState): boolean {
  return (
    sameSet(a.tags, b.tags) &&
    sameSet(a.assignees, b.assignees) &&
    a.includeNext === b.includeNext &&
    sameSet(a.due, b.due) &&
    sameSet(a.repeat, b.repeat)
  )
}

export function ChoreFilters({
  value,
  onChange,
  tags,
  assignees,
  currentUserId,
}: {
  value: ChoreFilterState
  onChange: (next: ChoreFilterState) => void
  tags: string[]
  assignees: User[]
  currentUserId?: string
}) {
  const [open, setOpen] = useState(false)
  const [presetOpen, setPresetOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  const presetRef = useRef<HTMLDivElement>(null)
  const count = filterCount(value)

  // Quick-filter presets: each applies an exact filter state (and toggles back off when active).
  // "Mine"/"My turn next" only make sense with more than one member to distinguish.
  const presets: { label: string; state: ChoreFilterState }[] = []
  if (currentUserId && assignees.length > 1) {
    presets.push({ label: 'Mine', state: { ...emptyFilters, assignees: [currentUserId] } })
  }
  presets.push({ label: 'Overdue', state: { ...emptyFilters, due: ['overdue'] } })
  presets.push({ label: 'Today', state: { ...emptyFilters, due: ['today'] } })
  if (currentUserId && assignees.length > 1) {
    presets.push({ label: 'My turn next', state: { ...emptyFilters, assignees: [currentUserId], includeNext: true } })
  }
  const activePreset = presets.find((p) => sameFilters(value, p.state))

  useEffect(() => {
    if (!open) return
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [open])

  useEffect(() => {
    if (!presetOpen) return
    const handler = (e: MouseEvent) => {
      if (presetRef.current && !presetRef.current.contains(e.target as Node)) setPresetOpen(false)
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [presetOpen])

  const showAssignees = assignees.length > 1

  return (
    <div className="flex items-center justify-end gap-2">
      {presets.length > 0 && (
        <div ref={presetRef} className="relative mr-auto">
          <button
            type="button"
            onClick={() => setPresetOpen((v) => !v)}
            aria-expanded={presetOpen}
            className={
              'inline-flex items-center gap-2 rounded-lg border px-3 py-1.5 text-sm transition-colors focus:outline-none focus:ring-2 focus:ring-ring ' +
              (activePreset
                ? 'border-primary bg-primary/10 text-primary'
                : 'border-border bg-card text-foreground hover:bg-accent')
            }
          >
            {activePreset?.label ?? 'Quick views'}
            <ChevronIcon />
          </button>

          {presetOpen && (
            <div className="absolute left-0 top-full z-20 mt-2 w-44 max-w-[calc(100vw-2rem)] rounded-lg border border-border bg-card p-1 shadow-pop">
              {presets.map((p) => {
                const active = sameFilters(value, p.state)
                return (
                  <button
                    key={p.label}
                    type="button"
                    onClick={() => {
                      onChange(active ? emptyFilters : p.state)
                      setPresetOpen(false)
                    }}
                    className={
                      'flex w-full items-center justify-between rounded-md px-2.5 py-1.5 text-left text-sm transition-colors ' +
                      (active ? 'bg-primary/10 text-primary' : 'text-foreground hover:bg-accent')
                    }
                  >
                    {p.label}
                    {active && <CheckIcon />}
                  </button>
                )
              })}
            </div>
          )}
        </div>
      )}
      {count > 0 && (
        <button
          type="button"
          onClick={() => onChange(emptyFilters)}
          className="text-sm text-muted-foreground underline-offset-2 hover:text-foreground hover:underline"
        >
          Clear
        </button>
      )}
      <div ref={ref} className="relative">
        <button
          type="button"
          onClick={() => setOpen((v) => !v)}
          aria-expanded={open}
          className="inline-flex items-center gap-2 rounded-lg border border-border bg-card px-3 py-1.5 text-sm text-foreground transition-colors hover:bg-accent focus:outline-none focus:ring-2 focus:ring-ring"
        >
          <SlidersIcon />
          Filters
          {count > 0 && (
            <span className="inline-flex h-5 min-w-5 items-center justify-center rounded-full bg-primary px-1.5 text-xs font-medium text-primary-foreground">
              {count}
            </span>
          )}
        </button>

        {open && (
          <div className="absolute right-0 top-full z-20 mt-2 w-80 max-w-[calc(100vw-2rem)] space-y-4 rounded-lg border border-border bg-card p-4 shadow-pop">
            <Group label="Due">
              {DUE_OPTIONS.map((o) => (
                <TogglePill
                  key={o.value}
                  active={value.due.includes(o.value)}
                  onClick={() => onChange({ ...value, due: toggle(value.due, o.value) })}
                >
                  {o.label}
                </TogglePill>
              ))}
            </Group>

            {tags.length > 0 && (
              <Group label="Tags">
                {tags.map((t) => (
                  <TogglePill
                    key={t}
                    active={value.tags.includes(t)}
                    onClick={() => onChange({ ...value, tags: toggle(value.tags, t) })}
                  >
                    {t}
                  </TogglePill>
                ))}
              </Group>
            )}

            {showAssignees && (
              <div>
                <p className="mb-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">Assignee</p>
                <div className="flex flex-wrap gap-1.5">
                  {assignees.map((u) => (
                    <TogglePill
                      key={u.id}
                      active={value.assignees.includes(u.id)}
                      onClick={() => onChange({ ...value, assignees: toggle(value.assignees, u.id) })}
                    >
                      <Avatar color={u.avatarColor} name={u.displayName} size={16} />
                      {u.displayName}
                    </TogglePill>
                  ))}
                </div>
                <label className="mt-2 flex items-center gap-2 text-sm text-muted-foreground">
                  <input
                    type="checkbox"
                    checked={value.includeNext}
                    onChange={(e) => onChange({ ...value, includeNext: e.target.checked })}
                    className="h-4 w-4 accent-primary"
                  />
                  Also match where they're up next
                </label>
              </div>
            )}

            <Group label="Repeat">
              {REPEAT_OPTIONS.map((o) => (
                <TogglePill
                  key={o.value}
                  active={value.repeat.includes(o.value)}
                  onClick={() => onChange({ ...value, repeat: toggle(value.repeat, o.value) })}
                >
                  {o.label}
                </TogglePill>
              ))}
            </Group>

            <div className="flex justify-end border-t border-border pt-3">
              <button
                type="button"
                onClick={() => onChange(emptyFilters)}
                disabled={count === 0}
                className="text-sm text-muted-foreground underline-offset-2 hover:text-foreground hover:underline disabled:cursor-default disabled:opacity-40 disabled:no-underline disabled:hover:text-muted-foreground"
              >
                Clear all
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}

function ChevronIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <polyline points="6 9 12 15 18 9" />
    </svg>
  )
}

function CheckIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <polyline points="20 6 9 17 4 12" />
    </svg>
  )
}

function Group({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <p className="mb-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</p>
      <div className="flex flex-wrap gap-1.5">{children}</div>
    </div>
  )
}

function TogglePill({ active, onClick, children }: { active: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={
        'inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-sm transition-colors ' +
        (active
          ? 'bg-primary/10 text-primary ring-1 ring-primary'
          : 'bg-accent text-muted-foreground hover:text-foreground')
      }
    >
      {children}
    </button>
  )
}

function SlidersIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <line x1="4" y1="21" x2="4" y2="14" />
      <line x1="4" y1="10" x2="4" y2="3" />
      <line x1="12" y1="21" x2="12" y2="12" />
      <line x1="12" y1="8" x2="12" y2="3" />
      <line x1="20" y1="21" x2="20" y2="16" />
      <line x1="20" y1="12" x2="20" y2="3" />
      <line x1="1" y1="14" x2="7" y2="14" />
      <line x1="9" y1="8" x2="15" y2="8" />
      <line x1="17" y1="16" x2="23" y2="16" />
    </svg>
  )
}
