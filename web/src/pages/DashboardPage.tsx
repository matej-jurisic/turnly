import { useState } from 'react'
import type { ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { choresApi, usersApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import { confirm } from '@/lib/confirm'
import type { Chore, LeaderboardEntry } from '@/lib/types'
import { Badge } from '@/components/ui/Badge'
import { Card } from '@/components/ui/Card'
import { Avatar } from '@/components/ui/Modal'
import { CompleteModal } from '@/components/CompleteModal'
import { cn } from '@/lib/utils'

// ── helpers ────────────────────────────────────────────────────────────────

function startOfDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate())
}

function choreDueStatus(chore: Chore): 'overdue' | 'today' | 'upcoming' | 'none' {
  if (!chore.dueAt) return 'none'
  const due = new Date(chore.dueAt)
  const todayStart = startOfDay(new Date())
  const tomorrowStart = new Date(todayStart.getTime() + 86_400_000)
  const weekEnd = new Date(todayStart.getTime() + 7 * 86_400_000)
  if (due < todayStart) return 'overdue'
  if (due < tomorrowStart) return 'today'
  if (due <= weekEnd) return 'upcoming'
  return 'none'
}

function dueLabelFor(chore: Chore): string {
  if (!chore.dueAt) return ''
  const due = new Date(chore.dueAt)
  const todayStart = startOfDay(new Date())
  const diffMs = due.getTime() - todayStart.getTime()
  const diffDays = Math.floor(diffMs / 86_400_000)

  if (diffDays < 0) {
    const n = -diffDays
    return n === 1 ? '1 day overdue' : `${n} days overdue`
  }
  if (diffDays === 0) {
    const hours = due.getHours()
    const mins = due.getMinutes()
    if (hours === 0 && mins === 0) return 'Due today'
    return `Due today at ${due.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })}`
  }
  if (diffDays === 1) return 'Tomorrow'
  return `In ${diffDays} days`
}

// ── main page ──────────────────────────────────────────────────────────────

export function DashboardPage() {
  const queryClient = useQueryClient()

  const { data: chores = [], isLoading: choresLoading } = useQuery({
    queryKey: ['chores'],
    queryFn: choresApi.list,
  })
  const { data: leaderboard = [] } = useQuery({
    queryKey: ['leaderboard'],
    queryFn: usersApi.leaderboard,
  })

  const [tagFilter, setTagFilter] = useState('')
  const [assigneeFilter, setAssigneeFilter] = useState('')
  const [completing, setCompleting] = useState<Chore | null>(null)

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['chores'] })
    void queryClient.invalidateQueries({ queryKey: ['leaderboard'] })
    void queryClient.invalidateQueries({ queryKey: ['me'] })
  }

  const undoMutation = useMutation({
    mutationFn: (completionId: string) => choresApi.undoCompletion(completionId),
    onSuccess: invalidate,
    onError: (err) => toast.error(err instanceof ApiError ? err.message : 'Undo failed'),
  })

  const confirmUndo = async (chore: Chore) => {
    if (
      await confirm({
        title: 'Undo completion',
        message: 'Undo the last completion? Points will be reversed.',
        confirmLabel: 'Undo',
      })
    ) {
      undoMutation.mutate(chore.lastCompletion!.id)
    }
  }

  // All unique tags from all chores
  const allTags = [...new Set(chores.flatMap((c) => c.tags))].sort()

  // Unique assignees (current assignees across all chores)
  const allAssignees = [
    ...new Map(
      chores
        .filter((c) => c.currentAssignee)
        .map((c) => [c.currentAssignee!.id, c.currentAssignee!]),
    ).values(),
  ].sort((a, b) => a.displayName.localeCompare(b.displayName))

  const filtered = chores.filter((c) => {
    if (tagFilter && !c.tags.includes(tagFilter)) return false
    if (assigneeFilter && c.currentAssignee?.id !== assigneeFilter) return false
    return true
  })

  const overdue = filtered.filter((c) => choreDueStatus(c) === 'overdue')
  const today = filtered.filter((c) => choreDueStatus(c) === 'today')
  const upcoming = filtered.filter((c) => choreDueStatus(c) === 'upcoming')

  return (
    <div className="space-y-8">
      {/* Header */}
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold text-foreground">Dashboard</h1>
        <span className="text-sm text-muted-foreground">
          {new Date().toLocaleDateString('en-GB')}
        </span>
      </div>

      {/* Stats */}
      <div className="flex flex-wrap gap-3">
        <StatChip
          label="Overdue"
          count={overdue.length}
          tone={overdue.length > 0 ? 'red' : 'neutral'}
        />
        <StatChip label="Due today" count={today.length} tone={today.length > 0 ? 'amber' : 'neutral'} />
        <StatChip label="This week" count={upcoming.length} tone="blue" />
      </div>

      {/* Filters */}
      {(allTags.length > 0 || allAssignees.length > 1) && (
        <div className="flex flex-wrap gap-3">
          {allTags.length > 0 && (
            <select
              value={tagFilter}
              onChange={(e) => setTagFilter(e.target.value)}
              className="rounded-lg border border-border bg-card px-3 py-1.5 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
            >
              <option value="">All tags</option>
              {allTags.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
          )}
          {allAssignees.length > 1 && (
            <select
              value={assigneeFilter}
              onChange={(e) => setAssigneeFilter(e.target.value)}
              className="rounded-lg border border-border bg-card px-3 py-1.5 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
            >
              <option value="">All members</option>
              {allAssignees.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.displayName}
                </option>
              ))}
            </select>
          )}
          {(tagFilter || assigneeFilter) && (
            <button
              type="button"
              onClick={() => { setTagFilter(''); setAssigneeFilter('') }}
              className="text-sm text-muted-foreground underline-offset-2 hover:text-foreground hover:underline"
            >
              Clear filters
            </button>
          )}
        </div>
      )}

      {choresLoading && <p className="text-muted-foreground">Loading…</p>}

      {/* Overdue */}
      {overdue.length > 0 && (
        <ChoreSection title="Overdue" accentClass="text-destructive" chores={overdue}>
          {(chore) => (
            <DashboardChoreCard
              key={chore.id}
              chore={chore}
                            undoPending={undoMutation.isPending}
              onComplete={() => setCompleting(chore)}
              onUndo={() => confirmUndo(chore)}
            />
          )}
        </ChoreSection>
      )}

      {/* Today */}
      <ChoreSection title="Today" accentClass="text-foreground" chores={today}>
        {(chore) => (
          <DashboardChoreCard
            key={chore.id}
            chore={chore}
                        undoPending={undoMutation.isPending}
            onComplete={() => setCompleting(chore)}
            onUndo={() => confirmUndo(chore)}
          />
        )}
      </ChoreSection>

      {/* Upcoming */}
      {upcoming.length > 0 && (
        <ChoreSection title="This week" accentClass="text-foreground" chores={upcoming}>
          {(chore) => (
            <DashboardChoreCard
              key={chore.id}
              chore={chore}
                            undoPending={undoMutation.isPending}
              onComplete={() => setCompleting(chore)}
              onUndo={() => confirmUndo(chore)}
            />
          )}
        </ChoreSection>
      )}

      {/* Leaderboard */}
      {leaderboard.length > 0 && <LeaderboardSection entries={leaderboard} />}

      {/* Complete modal */}
      {completing && (
        <CompleteModal
          chore={completing}
          onClose={() => setCompleting(null)}
          onDone={() => {
            setCompleting(null)
            invalidate()
          }}
        />
      )}
    </div>
  )
}

// ── stat chip ──────────────────────────────────────────────────────────────

function StatChip({ label, count, tone }: { label: string; count: number; tone: 'red' | 'amber' | 'blue' | 'neutral' }) {
  const styles: Record<string, string> = {
    red: 'bg-destructive/10 text-destructive',
    amber: 'bg-warning/10 text-warning',
    blue: 'bg-info/10 text-info',
    neutral: 'bg-accent text-muted-foreground',
  }
  return (
    <div className={cn('flex items-center gap-2 rounded-lg px-4 py-2', styles[tone])}>
      <span className="text-2xl font-semibold">{count}</span>
      <span className="text-sm">{label}</span>
    </div>
  )
}

// ── chore section ──────────────────────────────────────────────────────────

function ChoreSection({
  title,
  accentClass,
  chores,
  children,
}: {
  title: string
  accentClass: string
  chores: Chore[]
  children: (chore: Chore) => ReactNode
}) {
  if (chores.length === 0) {
    return (
      <section className="space-y-3">
        <h2 className={cn('text-base font-semibold', accentClass)}>{title}</h2>
        <p className="text-sm text-muted-foreground">Nothing due — all clear!</p>
      </section>
    )
  }

  return (
    <section className="space-y-3">
      <h2 className={cn('text-base font-semibold', accentClass)}>
        {title} <span className="ml-1 text-sm font-normal text-muted-foreground">({chores.length})</span>
      </h2>
      <div className="grid gap-2">{chores.map(children)}</div>
    </section>
  )
}

// ── dashboard chore card ───────────────────────────────────────────────────

function DashboardChoreCard({
  chore,
  undoPending,
  onComplete,
  onUndo,
}: {
  chore: Chore
  undoPending: boolean
  onComplete: () => void
  onUndo: () => void
}) {
  const status = choreDueStatus(chore)
  const dueLabel = dueLabelFor(chore)

  return (
    <Card className="px-4 py-3">
      <div className="flex items-center gap-3">
        {/* Emoji */}
        {chore.emoji ? (
          <span className="shrink-0 text-xl leading-none">{chore.emoji}</span>
        ) : (
          <span className="shrink-0 text-xl leading-none text-muted-foreground">📋</span>
        )}

        {/* Name + meta */}
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-baseline gap-x-2">
            <span className="font-semibold text-foreground">{chore.name}</span>
            {dueLabel && (
              <span
                className={cn(
                  'text-xs',
                  status === 'overdue' ? 'text-destructive' : 'text-muted-foreground',
                )}
              >
                {dueLabel}
              </span>
            )}
          </div>

          <div className="mt-1 flex flex-wrap items-center gap-1.5">
            {chore.customMode === 'Frequency' && chore.frequencyProgress != null && (
              <Badge tone="violet">
                {chore.frequencyProgress}/{chore.frequencyCount} this {(chore.frequencyPeriod ?? 'week').toLowerCase()}
              </Badge>
            )}
            {chore.tags.map((tag) => (
              <Badge key={tag} tone="neutral">
                {tag}
              </Badge>
            ))}
          </div>
        </div>

        {/* Assignee + actions */}
        <div className="flex shrink-0 items-center gap-2">
          {chore.currentAssignee && (
            <div className="flex items-center gap-1.5">
              <Avatar
                color={chore.currentAssignee.avatarColor}
                name={chore.currentAssignee.displayName}
                size={28}
              />
            </div>
          )}

          {chore.lastCompletion && (
            <button
              type="button"
              disabled={undoPending}
              onClick={onUndo}
              aria-label="Undo last completion"
              className="inline-flex h-8 w-8 items-center justify-center rounded-full text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:opacity-40"
            >
              <UndoIcon />
            </button>
          )}

          <button
            type="button"
            onClick={onComplete}
            aria-label="Mark complete"
            className="inline-flex h-9 w-9 items-center justify-center rounded-full bg-primary text-primary-foreground transition-colors hover:bg-primary/90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          >
            <CheckIcon />
          </button>
        </div>
      </div>
    </Card>
  )
}

// ── leaderboard ────────────────────────────────────────────────────────────

function LeaderboardSection({ entries }: { entries: LeaderboardEntry[] }) {
  const [showWeekly, setShowWeekly] = useState(false)
  const sorted = showWeekly
    ? [...entries].sort((a, b) => b.weeklyPoints - a.weeklyPoints)
    : entries // already sorted by all-time from the server

  return (
    <section className="space-y-3">
      <div className="flex items-center justify-between">
        <h2 className="text-base font-semibold text-foreground">Leaderboard</h2>
        <div className="flex rounded-lg border border-border text-sm">
          <button
            type="button"
            onClick={() => setShowWeekly(false)}
            className={cn(
              'rounded-l-lg px-3 py-1 transition-colors',
              !showWeekly ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:bg-accent',
            )}
          >
            All time
          </button>
          <button
            type="button"
            onClick={() => setShowWeekly(true)}
            className={cn(
              'rounded-r-lg px-3 py-1 transition-colors',
              showWeekly ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:bg-accent',
            )}
          >
            This week
          </button>
        </div>
      </div>

      <Card>
        <ul className="divide-y divide-border">
          {sorted.map((entry, index) => (
            <li key={entry.id} className="flex items-center gap-3 px-5 py-3">
              <span className="w-5 text-center text-sm font-medium text-muted-foreground">
                {index + 1}
              </span>
              <Avatar color={entry.avatarColor} name={entry.displayName} size={32} />
              <span className="flex-1 text-sm font-medium text-foreground">{entry.displayName}</span>
              <Badge tone="violet">
                {showWeekly ? entry.weeklyPoints : entry.points} pts
              </Badge>
            </li>
          ))}
        </ul>
      </Card>
    </section>
  )
}

// ── icons ──────────────────────────────────────────────────────────────────

function CheckIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M20 6 9 17l-5-5" />
    </svg>
  )
}

function UndoIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M3 7v6h6" />
      <path d="M3 13C5.33 8.67 9.5 6 14 6c4.42 0 8 3.58 8 8s-3.58 8-8 8c-2.42 0-4.6-1.08-6.1-2.8" />
    </svg>
  )
}

