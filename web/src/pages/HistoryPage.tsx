import { Fragment, useLayoutEffect, useRef, useState } from 'react'
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
import { beatDeadline, choreHasDueTime, dueStatus, formatDateTime, pairLabel, relativeTimeLabel, timelinessClass, timelinessLabel } from '@/lib/chore-format'
import { locale, localized, plural, t } from '@/lib/i18n'

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
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('chores.deleteFailed')),
  })

  const onDeleteEntry = async (entry: ChoreHistoryEntry) => {
    const { title, message } =
      entry.kind === 'skip'
        ? { title: t('activity.deleteSkipTitle'), message: t('activity.deleteSkipMessage') }
        : entry.kind === 'expired'
          ? { title: t('activity.deleteExpiredTitle'), message: t('activity.deleteExpiredMessage') }
          : { title: t('activity.deleteCompletionTitle'), message: plural('activity.deleteCompletionMessage', entry.pointsAwarded) }
    if (await confirm({ title, message, confirmLabel: t('common.delete') })) {
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
    // A paired chore is on both partners' plates.
    for (const holder of [chore.currentAssignee, chore.currentPartner]) {
      if (!holder) continue
      const { id, displayName, avatarColor } = holder
      const entry = assigneeMap.get(id) ?? { label: displayName, count: 0, color: avatarColor }
      assigneeMap.set(id, { ...entry, count: entry.count + 1 })
    }
  }
  const assigneeSegments = [...assigneeMap.values()].sort((a, b) => b.count - a.count)

  // Donut chart 3: active task status breakdown
  let overdueTasks = 0, todayTasks = 0, upcomingTasks = 0, pausedTasks = 0
  for (const chore of chores) {
    if (chore.isFrozen) { pausedTasks++; continue }
    if (!chore.dueAt) continue
    const status = dueStatus(chore.dueAt)
    if (status === 'overdue') overdueTasks++
    else if (status === 'today') todayTasks++
    else upcomingTasks++
  }

  return (
    <div className="space-y-8">
      <h1 className="text-2xl font-semibold text-foreground">{t('nav.history')}</h1>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <DonutChart
          title={t('history.timing')}
          segments={[
            { label: t('history.onTime'), count: totalOnTime, color: 'var(--success)' },
            { label: t('history.late'), count: totalLate, color: 'var(--destructive)' },
            { label: t('details.expired'), count: totalMissed, color: 'var(--warning)' },
          ]}
        />
        <DonutChart
          title={t('history.byAssignee')}
          segments={assigneeSegments}
        />
        <DonutChart
          title={t('history.status')}
          segments={[
            { label: t('due.overdue'), count: overdueTasks, color: 'var(--destructive)' },
            { label: t('rail.dueToday'), count: todayTasks, color: 'var(--warning)' },
            { label: t('history.upcoming'), count: upcomingTasks, color: 'var(--primary)' },
            { label: t('chores.paused'), count: pausedTasks, color: 'var(--muted-foreground)' },
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
          <h2 className="text-base font-semibold text-foreground">{t('history.activityLog')}</h2>
          <div className="flex flex-1 gap-2">
            <select
              value={filterTag}
              onChange={(e) => setFilterTag(e.target.value)}
              className="min-w-0 flex-1 rounded-lg border border-border bg-card px-3 py-1.5 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring sm:flex-none"
            >
              <option value="">{t('history.allTags')}</option>
              {tags.map((tag) => (
                <option key={tag.id} value={tag.name}>{tag.name}</option>
              ))}
            </select>
            <select
              value={filterUserId}
              onChange={(e) => setFilterUserId(e.target.value)}
              className="min-w-0 flex-1 rounded-lg border border-border bg-card px-3 py-1.5 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring sm:flex-none"
            >
              <option value="">{t('history.allMembers')}</option>
              {members.map((u) => (
                <option key={u.id} value={u.id}>{u.displayName}</option>
              ))}
            </select>
            <select
              value={filterChoreId}
              onChange={(e) => setFilterChoreId(e.target.value)}
              className="min-w-0 flex-1 rounded-lg border border-border bg-card px-3 py-1.5 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring sm:flex-none"
            >
              <option value="">{t('history.allChores')}</option>
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
                {t('history.clear')}
              </button>
            )}
          </div>
        </div>

        {history.length === 0 ? (
          <Card>
            <CardContent>
              <p className="py-4 text-sm text-muted-foreground">{t('history.empty')}</p>
            </CardContent>
          </Card>
        ) : (
          <Card>
            <ul className="divide-y divide-border">
              {history.map((entry) => {
                const subject = entry.actor ?? entry.toAssignee ?? entry.fromAssignee
                const entryChore = chores.find((c) => c.id === entry.choreId)
                const onTime = entry.occurrenceDueAt
                  ? beatDeadline(entry.at, entry.occurrenceDueAt, !!entryChore && choreHasDueTime(entryChore))
                  : null
                // Rendered twice: beside the time on phones (so the detail row keeps the full width),
                // as its own trailing column from `sm` up.
                const deleteButton = (className: string) =>
                  isAdmin && entry.kind !== 'reassignment' && (
                    <button
                      type="button"
                      onClick={() => onDeleteEntry(entry)}
                      disabled={deleteMutation.isPending}
                      aria-label={t('activity.deleteEntry')}
                      className={cn(
                        'h-7 w-7 shrink-0 items-center justify-center rounded-full text-muted-foreground transition-colors hover:bg-destructive/10 hover:text-destructive focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50',
                        className,
                      )}
                    >
                      <TrashIcon />
                    </button>
                  )
                return (
                  <li key={entry.id} className="flex items-start gap-3 px-5 py-3">
                    <Avatar
                      color={subject?.avatarColor ?? 'var(--muted)'}
                      name={subject?.displayName ?? '?'}
                      size={32}
                      frame={subject?.equippedFrameKey}
                      emoji={subject?.avatarEmoji}
                    />
                    <div className="min-w-0 flex-1">
                      {/* Phones: name + time + delete on one row, then the pill and who did it on a full-width row. */}
                      <div className="flex items-center gap-x-2 gap-y-1 sm:flex-wrap">
                        <span className="min-w-0 flex-1 truncate text-sm font-medium text-foreground sm:flex-none sm:whitespace-normal">
                          {entry.choreName}
                        </span>
                        <time
                          dateTime={entry.at}
                          title={formatDateTime(entry.at)}
                          className="shrink-0 text-xs text-muted-foreground sm:hidden"
                        >
                          {relativeTimeLabel(entry.at)}
                        </time>
                        {deleteButton('-my-1 -mr-1.5 inline-flex sm:hidden')}
                        <Badge tone={ACTIVITY_BADGE[entry.kind].tone} className="hidden sm:inline-flex">
                          {ACTIVITY_BADGE[entry.kind].label}
                        </Badge>
                      </div>
                      <div className="mt-1 flex flex-wrap items-center gap-x-1.5 gap-y-1 text-xs text-muted-foreground sm:mt-0.5">
                        <Badge tone={ACTIVITY_BADGE[entry.kind].tone} className="sm:hidden">
                          {ACTIVITY_BADGE[entry.kind].label}
                        </Badge>
                        <span>{activityDetail(entry)}</span>
                      </div>
                      {/* Phones: status and points always get their own row, so they never land wherever the wrap puts them. */}
                      {(onTime !== null || entry.pointsAwarded > 0) && (
                        <div className="mt-0.5 flex flex-wrap items-center gap-x-1.5 text-xs text-muted-foreground sm:hidden">
                          {onTime !== null && (
                            <span className={timelinessClass(entry, onTime)}>{timelinessLabel(entry, onTime)}</span>
                          )}
                          {onTime !== null && entry.pointsAwarded > 0 && <span aria-hidden>·</span>}
                          {entry.pointsAwarded > 0 && (
                            <span className="text-success">{t('common.ptsPlus', { count: entry.pointsAwarded })}</span>
                          )}
                        </div>
                      )}
                      {entry.notes && (
                        <p className="mt-0.5 text-xs italic text-muted-foreground">{entry.notes}</p>
                      )}
                    </div>
                    <div className="hidden shrink-0 flex-col items-end gap-1 text-xs sm:flex">
                      <time
                        dateTime={entry.at}
                        title={formatDateTime(entry.at)}
                        className="text-muted-foreground"
                      >
                        {relativeTimeLabel(entry.at)}
                      </time>
                      <div className="flex items-center gap-2">
                        {onTime !== null && (
                          <span className={timelinessClass(entry, onTime)}>
                            {timelinessLabel(entry, onTime)}
                          </span>
                        )}
                        {entry.pointsAwarded > 0 && (
                          <span className="text-success">{t('common.ptsPlus', { count: entry.pointsAwarded })}</span>
                        )}
                      </div>
                    </div>
                    {deleteButton('hidden sm:inline-flex')}
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

function CompletionChart({ chart: allWeeks }: { chart: ChartWeek[] }) {
  // Weeks before the first completion are just padding that pushes the data off-screen.
  const firstActive = allWeeks.findIndex((w) => w.userCounts.some((u) => u.count > 0))
  const chart = firstActive < 0 ? [] : allWeeks.slice(firstActive)
  const maxCount = Math.max(
    1,
    ...chart.flatMap((w) => w.userCounts.map((u) => u.count)),
  )
  const hasAnyData = chart.length > 0

  // Open on the newest week when the chart is wider than the card.
  const scrollRef = useRef<HTMLDivElement>(null)
  useLayoutEffect(() => {
    const el = scrollRef.current
    if (el) el.scrollLeft = el.scrollWidth
  }, [chart.length])

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('history.perWeek')}</CardTitle>
      </CardHeader>
      <CardContent>
        {!hasAnyData ? (
          <p className="text-sm text-muted-foreground">{t('history.noCompletions')}</p>
        ) : (
          <div ref={scrollRef} className="overflow-x-auto">
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
                    {weekRangeLabel(week)}
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
    weekly: t('due.upcoming'),
    monthly: t('history.thisMonth'),
    allTime: t('history.allTime'),
  }

  return (
    <Card>
      <CardHeader className="flex flex-col items-start gap-3 sm:flex-row sm:items-center sm:justify-between">
        <CardTitle>{t('history.byMember')}</CardTitle>
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
          <p className="text-sm text-muted-foreground">{t('history.noMembers')}</p>
        ) : (
          <ul className="divide-y divide-border">
            {sorted.map((u, index) => {
              const count =
                period === 'weekly' ? u.weeklyCount
                : period === 'monthly' ? u.monthlyCount
                : u.allTimeCount
              const counts =
                period === 'weekly' ? u.weeklyTimeliness
                : period === 'monthly' ? u.monthlyTimeliness
                : { onTime: u.onTimeCount, overdue: u.overdueCount, rescue: u.rescueCount, missed: u.missedCount }
              const timeliness = [
                { key: 'onTime', n: counts.onTime, className: 'text-success', label: t('history.nOnTime', { count: counts.onTime }) },
                { key: 'late', n: counts.overdue, className: 'text-destructive', label: t('history.nLate', { count: counts.overdue }) },
                { key: 'rescue', n: counts.rescue, className: 'text-info', label: plural('history.nRescue', counts.rescue) },
                { key: 'missed', n: counts.missed, className: 'text-warning', label: t('history.nMissed', { count: counts.missed }) },
              ].filter((part) => part.n > 0)
              return (
                <li key={u.userId} className="flex items-center gap-3 py-2.5">
                  <Avatar color={u.avatarColor} name={u.displayName} size={28} frame={u.equippedFrameKey} emoji={u.avatarEmoji} />
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm text-foreground">
                      <span className="text-muted-foreground">{index + 1}.</span> {u.displayName}
                    </p>
                    {timeliness.length > 0 && (
                      <p title={t('history.statsTitle')} className="flex flex-wrap items-center gap-x-1 text-xs text-muted-foreground">
                        {timeliness.map((part, i) => (
                          <Fragment key={part.key}>
                            {i > 0 && <span aria-hidden>·</span>}
                            <span className={cn('whitespace-nowrap', part.className)}>{part.label}</span>
                          </Fragment>
                        ))}
                      </p>
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
  completion: localized({ tone: 'green' as const }, { label: 'details.completed' }),
  skip: localized({ tone: 'neutral' as const }, { label: 'details.skipped' }),
  expired: localized({ tone: 'amber' as const }, { label: 'details.expired' }),
  reassignment: localized({ tone: 'blue' as const }, { label: 'history.reassigned' }),
}

/** One consistent "who did it" line per entry; the badge already names the action. */
function activityDetail(entry: ChoreHistoryEntry): string {
  if (entry.kind === 'reassignment') {
    const detail = `${entry.fromAssignee?.displayName ?? t('history.nobody')} → ${entry.toAssignee?.displayName ?? t('history.nobody')}`
    return entry.actor ? t('history.reassignBy', { detail, name: entry.actor.displayName }) : detail
  }
  if (entry.kind === 'expired') {
    return entry.actor
      ? t('history.assignedTo', { name: pairLabel(entry.actor, entry.partner) })
      : t('history.autoAdvanced')
  }
  return t('history.by', { name: entry.actor ? pairLabel(entry.actor, entry.partner) : t('history.someone') })
}

/** "5–11 Oct" for a chart week, in the UI language. Falls back to the server's own label where the
 * browser can't format a date range. */
function weekRangeLabel(week: ChartWeek): string {
  const start = new Date(week.weekStart)
  const end = new Date(start.getFullYear(), start.getMonth(), start.getDate() + 6)
  const format = new Intl.DateTimeFormat(locale(), { day: 'numeric', month: 'short' })
  return typeof format.formatRange === 'function' ? format.formatRange(start, end) : week.label
}
