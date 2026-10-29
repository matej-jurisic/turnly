import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
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

export function PointsPage() {
  const user = useAuthStore((s) => s.user)
  const [selectedUser, setSelectedUser] = useState<LeaderboardEntry | null>(null)
  const { data: leaderboard = [] } = useQuery({
    queryKey: ['leaderboard'],
    queryFn: usersApi.leaderboard,
  })

  if (!user) return null
  return (
    <div className="space-y-8">
      <h1 className="text-2xl font-semibold text-foreground">{t('nav.points')}</h1>
      {leaderboard.length > 0 && (
        <LeaderboardSection entries={leaderboard} onSelect={setSelectedUser} />
      )}
      <PointsCard userId={user.id} />

      {selectedUser && (
        <UserDetailsModal
          userId={selectedUser.id}
          displayName={selectedUser.displayName}
          avatarColor={selectedUser.avatarColor}
          avatarEmoji={selectedUser.avatarEmoji}
          equippedFrameKey={selectedUser.equippedFrameKey}
          points={selectedUser.points}
          weeklyPoints={selectedUser.weeklyPoints}
          onClose={() => setSelectedUser(null)}
        />
      )}
    </div>
  )
}

function PointsCard({ userId }: { userId: string }) {
  const { data: me } = useQuery({ queryKey: ['me'], queryFn: authApi.me })
  const { data: log } = useQuery({ queryKey: ['points-log', userId], queryFn: () => usersApi.pointsLog(userId) })

  return (
    <Card>
      <CardHeader className="flex items-center justify-between">
        <CardTitle>{t('points.myLog')}</CardTitle>
        <Badge tone="violet">{t('common.pts', { count: me?.points ?? 0 })}</Badge>
      </CardHeader>
      <CardContent>
        {!log || log.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('points.empty')}</p>
        ) : (
          <ul className="divide-y divide-border">
            {log.map((entry) => (
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
  const [showWeekly, setShowWeekly] = useState(false)
  const sorted = showWeekly
    ? [...entries].sort((a, b) => b.weeklyPoints - a.weeklyPoints)
    : entries

  return (
    <section className="space-y-3">
      <div className="flex items-center justify-between">
        <h2 className="text-base font-semibold text-foreground">{t('points.leaderboard')}</h2>
        <div className="flex rounded-lg border border-border text-sm">
          <button
            type="button"
            onClick={() => setShowWeekly(false)}
            className={cn(
              'rounded-l-lg px-3 py-1 transition-colors',
              !showWeekly ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:bg-accent',
            )}
          >
            {t('history.allTime')}
          </button>
          <button
            type="button"
            onClick={() => setShowWeekly(true)}
            className={cn(
              'rounded-r-lg px-3 py-1 transition-colors',
              showWeekly ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:bg-accent',
            )}
          >
            {t('due.upcoming')}
          </button>
        </div>
      </div>

      <Card>
        <ul className="divide-y divide-border">
          {sorted.map((entry, index) => (
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
                {t('common.pts', { count: showWeekly ? entry.weeklyPoints : entry.points })}
              </Badge>
            </li>
          ))}
        </ul>
      </Card>
    </section>
  )
}
