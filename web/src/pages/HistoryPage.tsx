import { useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, choresApi, historyApi, tagsApi, usersApi } from '@/lib/api'
import { toast } from '@/lib/toast'
import { confirm } from '@/lib/confirm'
import { useAuthStore } from '@/store/auth'
import type { ChartWeek, ChoreHistoryEntry, UserStats } from '@/lib/types'
import type { BadgeTone } from '@/components/ui/Badge'
import { Badge } from '@/components/ui/Badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/Card'
import { Avatar } from '@/components/ui/Modal'
import { TrashIcon } from '@/components/chores/icons'
import { cn } from '@/lib/utils'

interface Segment {
  label: string
  count: number
  color: string
}

export function HistoryPage() {
  const isAdmin = useAuthStore((s) => s.user?.role === 'Admin')
  const queryClient = useQueryClient()
  const [filterTag, setFilterTag] = useState('')
  const [filterUserId, setFilterUserId] = useState('')
  const [filterChoreId, setFilterChoreId] = useState('')

  const { data: tags = [] } = useQuery({ queryKey: ['tags'], queryFn: tagsApi.list })
  const { data: members = [] } = useQuery({ queryKey: ['leaderboard'], queryFn: usersApi.leaderboard })
  const { data: chores = [] } = useQuery({ queryKey: ['chores'], queryFn: choresApi.list })
  const { data: stats } = useQuery({ queryKey: ['stats'], queryFn: historyApi.stats })

  const filters = {
    ...(filterTag ? { tag: filterTag } : {}),
    ...(filterUserId ? { userId: filterUserId } : {}),
    ...(filterChoreId ? { choreId: filterChoreId } : {}),
  }
  const { data: history = [] } = useQuery({
    queryKey: ['history', { ...filters, includeReassignments: true }],
    queryFn: () => historyApi.list({ ...filters, includeReassignments: true }),
    placeholderData: keepPreviousData,
  })

  const hasFilter = filterTag || filterUserId || filterChoreId

  // Admins can delete completions/skips/expired entries to fix up history; deletion reverses the
  // entry's points but never reschedules the chore. Reassignments aren't completion rows, so they
  // can't be deleted through this path.
  const deleteMutation = useMutation({
    mutationFn: (id: string) => choresApi.deleteActivity(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['history'] })
      void queryClient.invalidateQueries({ queryKey: ['stats'] })
      void queryClient.invalidateQueries({ queryKey: ['chores'] })
      void queryClient.invalidateQueries({ queryKey: ['me'] })
      void queryClient.invalidateQueries({ queryKey: ['leaderboard'] })
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : 'Delete failed'),
  })

  const onDeleteEntry = async (entry: ChoreHistoryEntry) => {
    const { title, message } =
      entry.kind === 'skip'
        ? { title: 'Delete skip', message: 'Delete this skip from the log? The chore schedule is not changed.' }
        : entry.kind === 'expired'
          ? { title: 'Delete expired entry', message: 'Delete this auto-expired entry from the log? The chore schedule is not changed.' }
          : { title: 'Delete completion', message: `Delete this completion? ${entry.pointsAwarded} points will be reversed and the chore schedule is not changed.` }
    if (await confirm({ title, message, confirmLabel: 'Delete' })) {
      deleteMutation.mutate(entry.id)
    }
  }

  // Donut chart 1: completion timing (all-time, from stats)
  const totalOnTime = stats?.userStats.reduce((s, u) => s + u.onTimeCount, 0) ?? 0
  const totalLate = stats?.userStats.reduce((s, u) => s + u.overdueCount, 0) ?? 0
  const totalMissed = stats?.totalMissedCount ?? 0

  // Donut chart 2: currently assigned tasks per member
  const assigneeMap = new Map<string, Segment>()
  for (const chore of chores) {
    if (!chore.currentAssignee) continue
    const { id, displayName, avatarColor } = chore.currentAssignee
    const entry = assigneeMap.get(id) ?? { label: displayName, count: 0, color: avatarColor }
    assigneeMap.set(id, { ...entry, count: entry.count + 1 })
  }
  const assigneeSegments = [...assigneeMap.values()].sort((a, b) => b.count - a.count)

  // Donut chart 3: active task status breakdown
  const now = new Date()
  const todayStart = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  const tomorrowStart = new Date(todayStart.getTime() + 86_400_000)
  let overdueTasks = 0, todayTasks = 0, upcomingTasks = 0, pausedTasks = 0
  for (const chore of chores) {
    if (chore.isFrozen) { pausedTasks++; continue }
    if (!chore.dueAt) continue
    const due = new Date(chore.dueAt)
    if (due < todayStart) overdueTasks++
    else if (due < tomorrowStart) todayTasks++
    else upcomingTasks++
  }

  return (
    <div className="space-y-8">
      <h1 className="text-2xl font-semibold text-foreground">History</h1>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <DonutChart
          title="Completion timing"
          segments={[
            { label: 'On time', count: totalOnTime, color: 'var(--success)' },
            { label: 'Late', count: totalLate, color: 'var(--destructive)' },
            { label: 'Expired', count: totalMissed, color: 'var(--warning)' },
          ]}
        />
        <DonutChart
          title="Tasks by assignee"
          segments={assigneeSegments}
        />
        <DonutChart
          title="Task status"
          segments={[
            { label: 'Overdue', count: overdueTasks, color: 'var(--destructive)' },
            { label: 'Due today', count: todayTasks, color: 'var(--warning)' },
            { label: 'Upcoming', count: upcomingTasks, color: 'var(--primary)' },
            { label: 'Paused', count: pausedTasks, color: 'var(--muted-foreground)' },
          ]}
        />
      </div>

      {stats && (
        <div className="space-y-6">
          <CompletionChart chart={stats.chart} />
          <UserStatsTable userStats={stats.userStats} />
        </div>
      )}

      <section className="space-y-4">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
          <h2 className="text-base font-semibold text-foreground">Activity log</h2>
          <div className="flex flex-1 gap-2">
            <select
              value={filterTag}
              onChange={(e) => setFilterTag(e.target.value)}
              className="min-w-0 flex-1 rounded-lg border border-border bg-card px-3 py-1.5 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring sm:flex-none"
            >
              <option value="">All tags</option>
              {tags.map((t) => (
                <option key={t.id} value={t.name}>{t.name}</option>
              ))}
            </select>
            <select
              value={filterUserId}
              onChange={(e) => setFilterUserId(e.target.value)}
              className="min-w-0 flex-1 rounded-lg border border-border bg-card px-3 py-1.5 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring sm:flex-none"
            >
              <option value="">All members</option>
              {members.map((u) => (
                <option key={u.id} value={u.id}>{u.displayName}</option>
              ))}
            </select>
            <select
              value={filterChoreId}
              onChange={(e) => setFilterChoreId(e.target.value)}
              className="min-w-0 flex-1 rounded-lg border border-border bg-card px-3 py-1.5 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring sm:flex-none"
            >
              <option value="">All chores</option>
              {chores.map((c) => (
                <option key={c.id} value={c.id}>{c.emoji ? `${c.emoji} ${c.name}` : c.name}</option>
              ))}
            </select>
            {hasFilter && (
              <button
                type="button"
                onClick={() => { setFilterTag(''); setFilterUserId(''); setFilterChoreId('') }}
                className="shrink-0 rounded-lg px-3 py-1.5 text-sm text-muted-foreground hover:bg-accent hover:text-foreground"
              >
                Clear
              </button>
            )}
          </div>
        </div>

        {history.length === 0 ? (
          <Card>
            <CardContent>
              <p className="py-4 text-sm text-muted-foreground">No activity found.</p>
            </CardContent>
          </Card>
        ) : (
          <Card>
            <ul className="divide-y divide-border">
              {history.map((entry) => {
                const subject = entry.actor ?? entry.toAssignee ?? entry.fromAssignee
                const onTime = entry.occurrenceDueAt
                  ? new Date(entry.at) <= new Date(entry.occurrenceDueAt)
                  : null
                return (
                  <li key={entry.id} className="flex items-start gap-3 px-5 py-3">
                    <Avatar
                      color={subject?.avatarColor ?? 'var(--muted)'}
                      name={subject?.displayName ?? '?'}
                      size={32}
                    />
                    <div className="min-w-0 flex-1">
                      <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                        <span className="text-sm font-medium text-foreground">
                          {entry.choreName}
                        </span>
                        <Badge tone={ACTIVITY_BADGE[entry.kind].tone}>
                          {ACTIVITY_BADGE[entry.kind].label}
                        </Badge>
                      </div>
                      <p className="mt-0.5 text-xs text-muted-foreground">
                        {activityDetail(entry)}
                      </p>
                      {entry.notes && (
                        <p className="mt-0.5 text-xs italic text-muted-foreground">{entry.notes}</p>
                      )}
                    </div>
                    <div className="flex shrink-0 flex-col items-end gap-1 text-xs">
                      <time
                        dateTime={entry.at}
                        title={new Date(entry.at).toLocaleString()}
                        className="text-muted-foreground"
                      >
                        {formatRelative(entry.at)}
                      </time>
                      <div className="flex items-center gap-2">
                        {onTime !== null && (
                          <span className={cn(onTime ? 'text-success' : 'text-destructive')}>
                            {onTime ? 'on time' : 'late'}
                          </span>
                        )}
                        {entry.pointsAwarded > 0 && (
                          <span className="text-success">+{entry.pointsAwarded} pts</span>
                        )}
                      </div>
                    </div>
                    {isAdmin && entry.kind !== 'reassignment' && (
                      <button
                        type="button"
                        onClick={() => onDeleteEntry(entry)}
                        disabled={deleteMutation.isPending}
                        aria-label="Delete entry"
                        className="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-muted-foreground transition-colors hover:bg-destructive/10 hover:text-destructive focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50"
                      >
                        <TrashIcon />
                      </button>
                    )}
                  </li>
                )
              })}
            </ul>
          </Card>
        )}
      </section>
    </div>
  )
}

function DonutChart({ title, segments }: { title: string; segments: Segment[] }) {
  const r = 38
  const strokeWidth = 14
  const circumference = 2 * Math.PI * r
  const total = segments.reduce((s, seg) => s + seg.count, 0)

  // Pre-compute arc geometry so we don't mutate inside JSX.
  let cumulative = 0
  const arcs = segments
    .filter((seg) => seg.count > 0)
    .map((seg) => {
      const dashLen = (seg.count / total) * circumference
      const offset = circumference - cumulative
      cumulative += dashLen
      return { ...seg, dashLen, offset }
    })

  return (
    <Card>
      <CardHeader>
        <CardTitle>{title}</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col items-center gap-4">
        {/* SVG is rotated so arc rendering starts at 12 o'clock; the overlay is not rotated. */}
        <div className="relative">
          <svg viewBox="0 0 100 100" className="-rotate-90 h-28 w-28">
            {arcs.length === 0 ? (
              <circle
                cx="50" cy="50" r={r}
                fill="none"
                stroke="var(--border)"
                strokeWidth={strokeWidth}
              />
            ) : arcs.map((arc) => (
              <circle
                key={arc.label}
                cx="50" cy="50" r={r}
                fill="none"
                stroke={arc.color}
                strokeWidth={strokeWidth}
                strokeDasharray={`${arc.dashLen} ${circumference - arc.dashLen}`}
                strokeDashoffset={arc.offset}
              />
            ))}
          </svg>
          <div className="absolute inset-0 flex flex-col items-center justify-center">
            <span className="text-xl font-semibold text-foreground">{total}</span>
          </div>
        </div>

        <ul className="w-full space-y-1.5">
          {segments.map((seg) => (
            <li key={seg.label} className="flex items-center gap-2 text-sm">
              <span
                className="h-2.5 w-2.5 shrink-0 rounded-full"
                style={{ backgroundColor: seg.color, opacity: seg.count === 0 ? 0.25 : 1 }}
              />
              <span className="flex-1 text-muted-foreground">{seg.label}</span>
              <span className="font-medium text-foreground">{seg.count}</span>
            </li>
          ))}
        </ul>
      </CardContent>
    </Card>
  )
}

function CompletionChart({ chart }: { chart: ChartWeek[] }) {
  const maxCount = Math.max(
    1,
    ...chart.flatMap((w) => w.userCounts.map((u) => u.count)),
  )
  const hasAnyData = chart.some((w) => w.userCounts.some((u) => u.count > 0))

  return (
    <Card>
      <CardHeader>
        <CardTitle>Completions per week</CardTitle>
      </CardHeader>
      <CardContent>
        {!hasAnyData ? (
          <p className="text-sm text-muted-foreground">No completions yet.</p>
        ) : (
          <div className="overflow-x-auto">
            <div className="flex min-w-max items-end gap-4 pb-2 pt-2">
              {chart.map((week) => (
                <div key={week.weekStart} className="flex flex-col items-center gap-1">
                  <div className="flex items-end gap-0.5" style={{ height: 80 }}>
                    {week.userCounts
                      .filter((u) => u.count > 0 || week.userCounts.every((u2) => u2.count === 0))
                      .map((u) => (
                        <div
                          key={u.userId}
                          title={`${u.displayName}: ${u.count}`}
                          className="w-5 rounded-t transition-all"
                          style={{
                            height: `${Math.max(2, (u.count / maxCount) * 76)}px`,
                            backgroundColor: u.count > 0 ? u.avatarColor : 'transparent',
                            opacity: u.count > 0 ? 0.85 : 0,
                          }}
                        />
                      ))}
                  </div>
                  <span className="w-20 text-center text-[10px] leading-tight text-muted-foreground">
                    {week.label}
                  </span>
                </div>
              ))}
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  )
}

function UserStatsTable({ userStats }: { userStats: UserStats[] }) {
  type Period = 'weekly' | 'monthly' | 'allTime'
  const [period, setPeriod] = useState<Period>('weekly')

  const sorted = [...userStats].sort((a, b) => {
    const va = period === 'weekly' ? a.weeklyCount : period === 'monthly' ? a.monthlyCount : a.allTimeCount
    const vb = period === 'weekly' ? b.weeklyCount : period === 'monthly' ? b.monthlyCount : b.allTimeCount
    return vb - va
  })

  const periodLabel: Record<Period, string> = {
    weekly: 'This week',
    monthly: 'This month',
    allTime: 'All time',
  }

  return (
    <Card>
      <CardHeader className="flex flex-col items-start gap-3 sm:flex-row sm:items-center sm:justify-between">
        <CardTitle>Completions by member</CardTitle>
        <div className="flex w-full rounded-lg border border-border text-sm sm:w-auto">
          {(['weekly', 'monthly', 'allTime'] as Period[]).map((p, i) => (
            <button
              key={p}
              type="button"
              onClick={() => setPeriod(p)}
              className={cn(
                'flex-1 whitespace-nowrap px-3 py-1 transition-colors sm:flex-none',
                i === 0 && 'rounded-l-lg',
                i === 2 && 'rounded-r-lg',
                period === p
                  ? 'bg-primary text-primary-foreground'
                  : 'text-muted-foreground hover:bg-accent',
              )}
            >
              {periodLabel[p]}
            </button>
          ))}
        </div>
      </CardHeader>
      <CardContent>
        {sorted.length === 0 ? (
          <p className="text-sm text-muted-foreground">No members yet.</p>
        ) : (
          <ul className="divide-y divide-border">
            {sorted.map((u, index) => {
              const count =
                period === 'weekly' ? u.weeklyCount
                : period === 'monthly' ? u.monthlyCount
                : u.allTimeCount
              return (
                <li key={u.userId} className="flex items-center gap-3 py-2.5">
                  <span className="w-5 text-center text-xs text-muted-foreground">{index + 1}</span>
                  <Avatar color={u.avatarColor} name={u.displayName} size={28} />
                  <span className="flex-1 text-sm text-foreground">{u.displayName}</span>
                  <div className="flex items-center gap-3 text-xs text-muted-foreground">
                    {(u.onTimeCount + u.overdueCount + u.missedCount > 0) && (
                      <span title="On time / late / expired (all time)" className="flex items-center gap-1">
                        {u.onTimeCount > 0 && <span className="text-success">{u.onTimeCount} on time</span>}
                        {u.onTimeCount > 0 && u.overdueCount > 0 && <span>·</span>}
                        {u.overdueCount > 0 && <span className="text-destructive">{u.overdueCount} late</span>}
                        {u.missedCount > 0 && (u.onTimeCount > 0 || u.overdueCount > 0) && <span>·</span>}
                        {u.missedCount > 0 && <span className="text-warning">{u.missedCount} missed</span>}
                      </span>
                    )}
                  </div>
                  <span className="w-6 text-right text-sm font-medium text-foreground">{count}</span>
                </li>
              )
            })}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}

const ACTIVITY_BADGE: Record<ChoreHistoryEntry['kind'], { tone: BadgeTone; label: string }> = {
  completion: { tone: 'green', label: 'Completed' },
  skip: { tone: 'neutral', label: 'Skipped' },
  expired: { tone: 'amber', label: 'Expired' },
  reassignment: { tone: 'blue', label: 'Reassigned' },
}

/** One consistent "who did it" line per entry; the badge already names the action. */
function activityDetail(entry: ChoreHistoryEntry): string {
  if (entry.kind === 'reassignment') {
    const detail = `${entry.fromAssignee?.displayName ?? 'nobody'} → ${entry.toAssignee?.displayName ?? 'nobody'}`
    return entry.actor ? `${detail} · by ${entry.actor.displayName}` : detail
  }
  if (entry.kind === 'expired') return entry.actor ? `assigned to ${entry.actor.displayName}` : 'auto-advanced'
  return `by ${entry.actor?.displayName ?? 'someone'}`
}

function formatRelative(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime()
  const mins = Math.floor(diff / 60_000)
  if (mins < 1) return 'just now'
  if (mins < 60) return `${mins}m ago`
  const hours = Math.floor(mins / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)
  if (days < 7) return `${days}d ago`
  return new Date(iso).toLocaleDateString()
}
