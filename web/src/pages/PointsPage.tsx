import { useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { authApi, usersApi } from '@/lib/api'
import { useAuthStore } from '@/store/auth'
import type { LeaderboardEntry } from '@/lib/types'
import { Badge } from '@/components/ui/Badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/Card'
import { Avatar } from '@/components/ui/Modal'
import { UserDetailsModal } from '@/components/UserDetailsModal'
import { cn } from '@/lib/utils'
import { locale, t } from '@/lib/i18n'
import { pointsLogLabel } from '@/lib/points-format'

/** First day of a local month; null = all time. */
type Month = Date | null

function startOfMonth(d: Date) {
  return new Date(d.getFullYear(), d.getMonth(), 1)
}

function shiftMonth(m: Date, delta: number) {
  return new Date(m.getFullYear(), m.getMonth() + delta, 1)
}

function inMonth(iso: string, month: Month) {
  if (!month) return true
  const at = new Date(iso)
  return at >= month && at < shiftMonth(month, 1)
}

function monthLabel(m: Date) {
  return m.toLocaleDateString(locale(), { month: 'long', year: 'numeric' })
}

export function PointsPage() {
  const user = useAuthStore((s) => s.user)
  const [selectedUser, setSelectedUser] = useState<LeaderboardEntry | null>(null)
  // The stepper's month is kept apart from whether it is applied, so "All time" doesn't lose it.
  const [shownMonth, setShownMonth] = useState(() => startOfMonth(new Date()))
  const [showAllTime, setShowAllTime] = useState(true)
  const month: Month = showAllTime ? null : shownMonth
  const range = month
    ? { from: month.toISOString(), to: shiftMonth(month, 1).toISOString() }
    : undefined
  // All-time totals stay loaded for the details modal, whatever month the board shows.
  const { data: allTime = [] } = useQuery({
    queryKey: ['leaderboard'],
    queryFn: () => usersApi.leaderboard(),
  })
  const { data: monthly = [] } = useQuery({
    queryKey: ['leaderboard', range?.from],
    queryFn: () => usersApi.leaderboard(range),
    enabled: !!range,
    placeholderData: keepPreviousData,
  })
  const leaderboard = month ? monthly : allTime
  const selectedTotal = allTime.find((e) => e.id === selectedUser?.id)

  if (!user) return null
  return (
    <div className="space-y-8">
      <div className="flex items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold text-foreground">{t('nav.points')}</h1>
        <MonthFilter
          month={shownMonth}
          allTime={showAllTime}
          onAllTime={() => setShowAllTime(true)}
          onMonth={(m) => {
            setShownMonth(m)
            setShowAllTime(false)
          }}
        />
      </div>
      {leaderboard.length > 0 && (
        <LeaderboardSection entries={leaderboard} onSelect={setSelectedUser} />
      )}
      <PointsCard userId={user.id} month={month} />

      {selectedUser && (
        <UserDetailsModal
          userId={selectedUser.id}
          displayName={selectedUser.displayName}
          avatarColor={selectedUser.avatarColor}
          avatarEmoji={selectedUser.avatarEmoji}
          equippedFrameKey={selectedUser.equippedFrameKey}
          points={selectedTotal?.earnedPoints ?? selectedUser.earnedPoints}
          weeklyPoints={selectedUser.weeklyPoints}
          onClose={() => setSelectedUser(null)}
        />
      )}
    </div>
  )
}

/** [All time | ‹ Oct 2026 ›]: the month segment is always shown at a fixed width, so the control
 * never changes size. Picking or stepping a month applies it. */
function MonthFilter({
  month,
  allTime,
  onAllTime,
  onMonth,
}: {
  month: Date
  allTime: boolean
  onAllTime: () => void
  onMonth: (m: Date) => void
}) {
  const current = startOfMonth(new Date())
  const arrowClass = cn(
    'flex w-6 shrink-0 items-center justify-center transition-colors disabled:pointer-events-none disabled:opacity-40',
    allTime ? 'hover:bg-accent' : 'hover:bg-primary-foreground/15',
  )

  return (
    <div className="flex shrink-0 rounded-lg border border-border text-sm">
      <button
        type="button"
        onClick={onAllTime}
        className={cn(
          'whitespace-nowrap rounded-l-lg px-3 py-1 transition-colors',
          allTime ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:bg-accent',
        )}
      >
        {t('history.allTime')}
      </button>
      <div
        className={cn(
          'flex w-32 rounded-r-lg transition-colors',
          allTime ? 'text-muted-foreground' : 'bg-primary text-primary-foreground',
        )}
      >
        <button type="button" onClick={() => onMonth(shiftMonth(month, -1))} aria-label={t('calendar.prev')} className={arrowClass}>‹</button>
        <button
          type="button"
          onClick={() => onMonth(month)}
          className={cn('min-w-0 flex-1 truncate whitespace-nowrap py-1 transition-colors', allTime && 'hover:bg-accent')}
        >
          {month.toLocaleDateString(locale(), { month: 'short', year: 'numeric' })}
        </button>
        <button
          type="button"
          onClick={() => onMonth(shiftMonth(month, 1))}
          disabled={month >= current}
          aria-label={t('calendar.next')}
          className={cn(arrowClass, 'rounded-r-lg')}
        >
          ›
        </button>
      </div>
    </div>
  )
}

function PointsCard({ userId, month }: { userId: string; month: Month }) {
  const { data: me } = useQuery({ queryKey: ['me'], queryFn: authApi.me })
  const { data: log } = useQuery({ queryKey: ['points-log', userId], queryFn: () => usersApi.pointsLog(userId) })
  const entries = log?.filter((entry) => inMonth(entry.createdAt, month)) ?? []

  return (
    <Card>
      <CardHeader className="flex items-center justify-between">
        <CardTitle>{t('points.myLog')}</CardTitle>
        <Badge tone="violet">{t('common.pts', { count: me?.points ?? 0 })}</Badge>
      </CardHeader>
      <CardContent>
        {entries.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            {month ? t('points.emptyMonth', { month: monthLabel(month) }) : t('points.empty')}
          </p>
        ) : (
          <ul className="divide-y divide-border">
            {entries.map((entry) => (
              <li key={entry.id} className="flex items-center justify-between py-2 text-sm">
                <span className="text-muted-foreground">
                  {pointsLogLabel(entry)}
                  <span className="ml-2 text-xs">
                    {new Date(entry.createdAt).toLocaleDateString(locale())}
                  </span>
                </span>
                <span className={entry.delta >= 0 ? 'text-success' : 'text-destructive'}>
                  {entry.delta >= 0 ? '+' : ''}{entry.delta}
                </span>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}

function LeaderboardSection({
  entries,
  onSelect,
}: {
  entries: LeaderboardEntry[]
  onSelect: (entry: LeaderboardEntry) => void
}) {
  return (
    <section className="space-y-3">
      <h2 className="text-base font-semibold text-foreground">{t('points.leaderboard')}</h2>

      <Card>
        <ul className="divide-y divide-border">
          {entries.map((entry, index) => (
            <li
              key={entry.id}
              onClick={() => onSelect(entry)}
              className="flex cursor-pointer items-center gap-3 px-5 py-3 transition-colors hover:bg-accent"
            >
              <span className="w-5 text-center text-sm font-medium text-muted-foreground">
                {index + 1}
              </span>
              <Avatar color={entry.avatarColor} name={entry.displayName} size={32} frame={entry.equippedFrameKey} emoji={entry.avatarEmoji} />
              <span className="flex-1 text-sm font-medium text-foreground">{entry.displayName}</span>
              <Badge tone="violet">
                {t('common.pts', { count: entry.earnedPoints })}
              </Badge>
            </li>
          ))}
        </ul>
      </Card>
    </section>
  )
}
