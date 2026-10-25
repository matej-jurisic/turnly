import { useEffect, useRef, useState } from 'react'
import type { User } from '@/lib/types'
import { Avatar } from '@/components/ui/Modal'
import { REPEAT_OPTIONS } from '@/lib/chore-format'
import { localized, t } from '@/lib/i18n'
import { emptyFilters, filterCount, type ChoreFilterState, type DueStatus } from '@/lib/chore-filters'

export type ChoreView = 'list' | 'compact' | 'calendar' | 'config'
/** `adminOnly` views are offered to admins only (the config table exposes every chore's settings). */
const VIEW_OPTIONS: {
  value: ChoreView
  label: string
  Icon: () => React.ReactElement
  adminOnly?: boolean
}[] = [
  localized({ value: 'list' as const, Icon: ListViewIcon }, { label: 'view.list' }),
  localized({ value: 'compact' as const, Icon: CompactViewIcon }, { label: 'view.compact' }),
  localized({ value: 'calendar' as const, Icon: CalendarViewIcon }, { label: 'view.calendar' }),
  localized({ value: 'config' as const, Icon: ConfigViewIcon, adminOnly: true }, { label: 'view.config' }),
]

const DUE_OPTIONS: { value: DueStatus; label: string }[] = [
  localized({ value: 'overdue' as const }, { label: 'due.overdue' }),
  localized({ value: 'today' as const }, { label: 'due.today' }),
  localized({ value: 'upcoming' as const }, { label: 'due.upcoming' }),
  localized({ value: 'later' as const }, { label: 'due.later' }),
]
function toggle<T>(list: T[], value: T): T[] {
  return list.includes(value) ? list.filter((v) => v !== value) : [...list, value]
}

/** Close `open` when a mousedown lands outside `ref`. */
function useOutsideClose(open: boolean, ref: React.RefObject<HTMLDivElement | null>, close: () => void) {
  useEffect(() => {
    if (!open) return
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) close()
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [open, ref, close])
}

/** Shared round outline toolbar button (Quick views / View / Filters). */
function iconButtonClass(active: boolean): string {
  return (
    'relative inline-flex h-9 w-9 items-center justify-center rounded-full border transition-colors focus:outline-none focus:ring-2 focus:ring-ring ' +
    (active
      ? 'border-primary bg-primary/10 text-primary'
      : 'border-border bg-card text-muted-foreground hover:bg-accent hover:text-foreground')
  )
}

export function ChoreFilters({
  value,
  onChange,
  tags,
  assignees,
  currentUserId,
  view,
  onViewChange,
  isAdmin,
}: {
  value: ChoreFilterState
  onChange: (next: ChoreFilterState) => void
  tags: string[]
  assignees: User[]
  currentUserId?: string
  view: ChoreView
  onViewChange: (next: ChoreView) => void
  isAdmin: boolean
}) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  const count = filterCount(value)
  useOutsideClose(open, ref, () => setOpen(false))

  const showAssignees = assignees.length > 1

  return (
    <div className="flex items-center justify-end gap-2">
      {currentUserId && showAssignees && <MineToggle value={value} onChange={onChange} userId={currentUserId} />}

      <ViewMenu view={view} onViewChange={onViewChange} isAdmin={isAdmin} />

      <div ref={ref} className="relative">
        <button
          type="button"
          onClick={() => setOpen((v) => !v)}
          aria-expanded={open}
          aria-label={t('filters.title')}
          title={t('filters.title')}
          className={iconButtonClass(count > 0)}
        >
          <SlidersIcon />
          {count > 0 && (
            <span className="absolute -right-1 -top-1 inline-flex h-4 min-w-4 items-center justify-center rounded-full bg-primary px-1 text-[10px] font-medium text-primary-foreground">
              {count}
            </span>
          )}
        </button>

        {open && (
          <div className="absolute right-0 top-full z-20 mt-2 w-80 max-w-[calc(100vw-2rem)] space-y-4 rounded-lg border border-border bg-card p-4 shadow-pop">
            <Group label={t('filters.due')}>
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
              <Group label={t('filters.tags')}>
                {tags.map((tag) => (
                  <TogglePill
                    key={tag}
                    active={value.tags.includes(tag)}
                    onClick={() => onChange({ ...value, tags: toggle(value.tags, tag) })}
                  >
                    {tag}
                  </TogglePill>
                ))}
              </Group>
            )}

            {showAssignees && (
              <div>
                <p className="mb-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">{t('filters.assignee')}</p>
                <div className="flex flex-wrap gap-1.5">
                  {assignees.map((u) => (
                    <TogglePill
                      key={u.id}
                      active={value.assignees.includes(u.id)}
                      onClick={() => onChange({ ...value, assignees: toggle(value.assignees, u.id) })}
                    >
                      <Avatar color={u.avatarColor} name={u.displayName} size={16} frame={u.equippedFrameKey} emoji={u.avatarEmoji} />
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
                  {t('filters.includeNext')}
                </label>
              </div>
            )}

            <Group label={t('filters.repeat')}>
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
                {t('filters.clearAll')}
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}

/** One-tap "Mine | All" switch. It only drives the assignee filter, so due/tag/repeat filters
 * survive the switch. Neither side is lit when the assignee filter names someone else. */
function MineToggle({
  value,
  onChange,
  userId,
}: {
  value: ChoreFilterState
  onChange: (next: ChoreFilterState) => void
  userId: string
}) {
  const mine = value.assignees.length === 1 && value.assignees[0] === userId
  const all = value.assignees.length === 0
  const segment = (active: boolean) =>
    'h-full rounded-full px-3.5 text-sm transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-ring ' +
    (active ? 'bg-primary/10 text-primary' : 'text-muted-foreground hover:text-foreground')

  return (
    <div role="group" className="mr-auto inline-flex h-9 items-center gap-0.5 rounded-full border border-border bg-card p-0.5">
      <button
        type="button"
        aria-pressed={mine}
        onClick={() => onChange({ ...value, assignees: [userId] })}
        className={segment(mine)}
      >
        {t('filters.mine')}
      </button>
      <button
        type="button"
        aria-pressed={all}
        onClick={() => onChange({ ...value, assignees: [], includeNext: false })}
        className={segment(all)}
      >
        {t('filters.all')}
      </button>
    </div>
  )
}

/** Round "View" button + dropdown to switch between list / compact / calendar layouts (plus the
 * config table for admins). */
function ViewMenu({
  view,
  onViewChange,
  isAdmin,
}: {
  view: ChoreView
  onViewChange: (next: ChoreView) => void
  isAdmin: boolean
}) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  useOutsideClose(open, ref, () => setOpen(false))
  const Active = VIEW_OPTIONS.find((o) => o.value === view)?.Icon ?? ListViewIcon

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        aria-label={t('filters.view')}
        title={t('filters.view')}
        className={iconButtonClass(false)}
      >
        <Active />
      </button>

      {open && (
        <div className="absolute right-0 top-full z-20 mt-2 w-40 max-w-[calc(100vw-2rem)] rounded-lg border border-border bg-card p-1 shadow-pop">
          {VIEW_OPTIONS.filter((o) => isAdmin || !o.adminOnly).map((o) => {
            const active = o.value === view
            return (
              <button
                key={o.value}
                type="button"
                onClick={() => {
                  onViewChange(o.value)
                  setOpen(false)
                }}
                className={
                  'flex w-full items-center gap-2 rounded-md px-2.5 py-1.5 text-left text-sm transition-colors ' +
                  (active ? 'bg-primary/10 text-primary' : 'text-foreground hover:bg-accent')
                }
              >
                <o.Icon />
                <span className="flex-1">{o.label}</span>
                {active && <CheckIcon />}
              </button>
            )
          })}
        </div>
      )}
    </div>
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

function ListViewIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <line x1="8" y1="6" x2="21" y2="6" />
      <line x1="8" y1="12" x2="21" y2="12" />
      <line x1="8" y1="18" x2="21" y2="18" />
      <line x1="3" y1="6" x2="3.01" y2="6" />
      <line x1="3" y1="12" x2="3.01" y2="12" />
      <line x1="3" y1="18" x2="3.01" y2="18" />
    </svg>
  )
}

function CompactViewIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <line x1="3" y1="6" x2="21" y2="6" />
      <line x1="3" y1="10" x2="21" y2="10" />
      <line x1="3" y1="14" x2="21" y2="14" />
      <line x1="3" y1="18" x2="21" y2="18" />
    </svg>
  )
}

function CalendarViewIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <rect x="3" y="4" width="18" height="18" rx="2" ry="2" />
      <line x1="16" y1="2" x2="16" y2="6" />
      <line x1="8" y1="2" x2="8" y2="6" />
      <line x1="3" y1="10" x2="21" y2="10" />
    </svg>
  )
}

function ConfigViewIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <rect x="3" y="4" width="18" height="16" rx="2" ry="2" />
      <line x1="3" y1="10" x2="21" y2="10" />
      <line x1="3" y1="15" x2="21" y2="15" />
      <line x1="9" y1="4" x2="9" y2="20" />
    </svg>
  )
}
