import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { authApi, usersApi } from '@/lib/api'
import type { Chore, LeaderboardEntry, User } from '@/lib/types'
import { Card } from '@/components/ui/Card'
import { Avatar } from '@/components/ui/Modal'
import { choreDueStatus } from '@/lib/chore-format'
import { cn } from '@/lib/utils'

/**
 * Right-hand info rail for the chore list (desktop `xl`+ only). Surfaces two glanceable widgets that
 * use data already on the page or cheap existing queries: a personal snapshot and the weekly
 * leaderboard. Styling stays deliberately understated (small label/value rows, no big numbers) to
 * match the rest of the app. Mobile/tablet never render this.
 */
export function ChoreRail({
  chores,
  currentUser,
  onSelectUser,
}: {
  chores: Chore[]
  currentUser: User
  onSelectUser: (entry: LeaderboardEntry) => void
}) {
  return (
    <div className="space-y-4">
      <SnapshotCard chores={chores} currentUser={currentUser} />
      <LeaderboardCard onSelectUser={onSelectUser} />
    </div>
  )
}

/** "Your snapshot": fresh points + this week's points, plus how many of *your* chores are actionable
 * (overdue / due today). Counts are derived from the same chore list the page already loaded. */
function SnapshotCard({ chores, currentUser }: { chores: Chore[]; currentUser: User }) {
  // Fresh points/weeklyPoints (the store user can lag after a completion); fall back to the store.
  const { data: me } = useQuery({ queryKey: ['me'], queryFn: authApi.me })
  const points = me?.points ?? currentUser.points
  const weeklyPoints = me?.weeklyPoints ?? currentUser.weeklyPoints

  const { overdue, today } = useMemo(() => {
    const mine = chores.filter(
      (c) =>
        !c.isFrozen &&
        c.dueAt &&
        (c.currentAssignee?.id === currentUser.id || c.tracks.some((t) => t.user.id === currentUser.id)),
    )
    let overdue = 0
    let today = 0
    for (const c of mine) {
      const status = choreDueStatus(c)
      if (status === 'overdue') overdue++
      else if (status === 'today') today++
    }
    return { overdue, today }
  }, [chores, currentUser.id])

  return (
    <Card className="p-4">
      <h3 className="text-sm font-semibold text-foreground">Your snapshot</h3>
      <dl className="mt-2 space-y-1 text-sm">
        <Row label="Points" value={points} />
        <Row label="This week" value={weeklyPoints} />
        <Row label="Due today" value={today} valueClass={today > 0 ? 'text-info' : undefined} />
        <Row label="Overdue" value={overdue} valueClass={overdue > 0 ? 'text-destructive' : undefined} />
      </dl>
    </Card>
  )
}

function Row({ label, value, valueClass }: { label: string; value: number; valueClass?: string }) {
  return (
    <div className="flex items-center justify-between py-0.5">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className={cn('font-medium', valueClass ?? 'text-foreground')}>{value}</dd>
    </div>
  )
}

/** Weekly leaderboard, top 5 by this week's points. Mirrors the Points page list; rows open the
 * user details modal. Hidden entirely when there's no scored activity. */
function LeaderboardCard({ onSelectUser }: { onSelectUser: (entry: LeaderboardEntry) => void }) {
  const { data: leaderboard = [] } = useQuery({
    queryKey: ['leaderboard'],
    queryFn: usersApi.leaderboard,
  })

  const top = useMemo(
    () => [...leaderboard].sort((a, b) => b.weeklyPoints - a.weeklyPoints).slice(0, 5),
    [leaderboard],
  )

  if (top.length === 0 || top.every((e) => e.weeklyPoints === 0)) return null

  return (
    <Card className="overflow-hidden">
      <div className="border-b border-border px-4 py-3">
        <h3 className="text-sm font-semibold text-foreground">Leaderboard · This week</h3>
      </div>
      <ul className="divide-y divide-border">
        {top.map((entry, index) => (
          <li
            key={entry.id}
            onClick={() => onSelectUser(entry)}
            className="flex cursor-pointer items-center gap-3 px-4 py-2 transition-colors hover:bg-accent"
          >
            <span className="w-4 text-center text-xs text-muted-foreground">{index + 1}</span>
            <Avatar color={entry.avatarColor} name={entry.displayName} size={24} frame={entry.equippedFrameKey} emoji={entry.avatarEmoji} />
            <span className="flex-1 truncate text-sm text-foreground">{entry.displayName}</span>
            <span className="text-sm font-medium text-muted-foreground">{entry.weeklyPoints}</span>
          </li>
        ))}
      </ul>
    </Card>
  )
}
