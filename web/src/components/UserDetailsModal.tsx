import { useQuery } from '@tanstack/react-query'
import { choresApi, historyApi, usersApi } from '@/lib/api'
import { Modal, Avatar } from '@/components/ui/Modal'
import { Badge } from '@/components/ui/Badge'
import { beatDeadline, choreHasDueTime, formatDateTime, relativeTimeLabel, timelinessClass, timelinessLabel } from '@/lib/chore-format'
import { t } from '@/lib/i18n'
import { pointsLogLabel } from '@/lib/points-format'
import type { ChoreHistoryEntry, PointsLogEntry } from '@/lib/types'

// Completions come from chore history (for the on-time label); everything else that moved points
// (spending, bounties, adjustments) comes from the points log. Misses and skips live on the History page.
type Row =
  | { kind: 'completion'; at: string; entry: ChoreHistoryEntry }
  | { kind: 'points'; at: string; entry: PointsLogEntry }

interface UserDetailsModalProps {
  userId: string
  displayName: string
  avatarColor: string
  avatarEmoji?: string | null
  equippedFrameKey?: string | null
  points: number
  weeklyPoints: number
  onClose: () => void
}

export function UserDetailsModal({
  userId,
  displayName,
  avatarColor,
  avatarEmoji,
  equippedFrameKey,
  points,
  weeklyPoints,
  onClose,
}: UserDetailsModalProps) {
  const { data: completions } = useQuery({
    queryKey: ['history', { userId }],
    queryFn: () => historyApi.list({ userId }),
  })

  const { data: log } = useQuery({
    queryKey: ['points-log', userId],
    queryFn: () => usersApi.pointsLog(userId),
  })

  const { data: chores = [] } = useQuery({ queryKey: ['chores'], queryFn: choresApi.list })

  const rows: Row[] = [
    ...(completions ?? [])
      .filter((entry) => entry.kind === 'completion')
      .map((entry): Row => ({ kind: 'completion', at: entry.at, entry })),
    ...(log ?? [])
      .filter((entry) => entry.type !== 'Completion')
      .map((entry): Row => ({ kind: 'points', at: entry.createdAt, entry })),
  ].sort((a, b) => new Date(b.at).getTime() - new Date(a.at).getTime())

  const title = (
    <span className="flex items-center gap-2">
      <Avatar color={avatarColor} name={displayName} size={28} frame={equippedFrameKey} emoji={avatarEmoji} />
      <span>{displayName}</span>
    </span>
  )

  return (
    <Modal title={title} onClose={onClose}>
      <div className="max-h-[65vh] space-y-4 overflow-y-auto pr-4 -mr-4">
        <div className="flex gap-2">
          <Badge tone="violet">{t('userDetails.allTime', { count: points })}</Badge>
          <Badge tone="blue">{t('userDetails.thisWeek', { count: weeklyPoints })}</Badge>
        </div>

        {rows.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('points.empty')}</p>
        ) : (
          <ul className="divide-y divide-border">
            {rows.map((row) => {
              if (row.kind === 'points') {
                const entry = row.entry
                return (
                  <li key={`p-${entry.id}`} className="flex items-start gap-3 py-2.5">
                    <div className="min-w-0 flex-1">
                      <p className="text-sm text-foreground">{pointsLogLabel(entry)}</p>
                      <time dateTime={row.at} title={formatDateTime(row.at)} className="mt-0.5 block text-xs text-muted-foreground">
                        {relativeTimeLabel(row.at)}
                      </time>
                    </div>
                    <span className={`shrink-0 text-sm ${entry.delta >= 0 ? 'text-success' : 'text-destructive'}`}>
                      {entry.delta >= 0 ? '+' : ''}{entry.delta}
                    </span>
                  </li>
                )
              }
              const entry = row.entry
              const entryChore = chores.find((c) => c.id === entry.choreId)
              const onTime = entry.occurrenceDueAt
                ? beatDeadline(entry.at, entry.occurrenceDueAt, !!entryChore && choreHasDueTime(entryChore))
                : null
              return (
                <li key={`c-${entry.id}`} className="flex items-start gap-3 py-2.5">
                  <div className="min-w-0 flex-1">
                    <p className="text-sm text-foreground">{entry.choreName}</p>
                    <p className="mt-0.5 flex flex-wrap items-center gap-x-1.5 text-xs text-muted-foreground">
                      <time dateTime={entry.at} title={formatDateTime(entry.at)}>
                        {relativeTimeLabel(entry.at)}
                      </time>
                      {onTime !== null && (
                        <>
                          <span aria-hidden>·</span>
                          <span className={timelinessClass(entry, onTime)}>
                            {timelinessLabel(entry, onTime)}
                          </span>
                        </>
                      )}
                    </p>
                    {entry.notes && (
                      <p className="mt-0.5 text-xs text-muted-foreground">{entry.notes}</p>
                    )}
                  </div>
                  {entry.pointsAwarded > 0 && (
                    <span className="shrink-0 text-sm text-success">{t('common.ptsPlus', { count: entry.pointsAwarded })}</span>
                  )}
                </li>
              )
            })}
          </ul>
        )}
      </div>
    </Modal>
  )
}
