import { useQuery } from '@tanstack/react-query'
import { choresApi, historyApi } from '@/lib/api'
import { Modal, Avatar } from '@/components/ui/Modal'
import { Badge } from '@/components/ui/Badge'
import { beatDeadline, choreHasDueTime, formatDateTime, relativeTimeLabel, timelinessClass, timelinessLabel } from '@/lib/chore-format'
import { t } from '@/lib/i18n'

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

  const { data: chores = [] } = useQuery({ queryKey: ['chores'], queryFn: choresApi.list })

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

        {!completions || completions.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('history.noCompletions')}</p>
        ) : (
          <ul className="divide-y divide-border">
            {completions.map((entry) => {
              const entryChore = chores.find((c) => c.id === entry.choreId)
              const onTime = entry.occurrenceDueAt
                ? beatDeadline(entry.at, entry.occurrenceDueAt, !!entryChore && choreHasDueTime(entryChore))
                : null
              return (
                <li key={entry.id} className="flex items-start gap-3 py-2.5">
                  <div className="min-w-0 flex-1">
                    <span className="text-sm text-foreground">
                      {entry.choreName}
                    </span>
                    {entry.notes && (
                      <p className="mt-0.5 text-xs text-muted-foreground">{entry.notes}</p>
                    )}
                  </div>
                  <div className="flex shrink-0 items-center gap-2 text-xs">
                    <time
                      dateTime={entry.at}
                      title={formatDateTime(entry.at)}
                      className="text-muted-foreground"
                    >
                      {relativeTimeLabel(entry.at)}
                    </time>
                    {onTime !== null && (
                      <span className={timelinessClass(entry, onTime)}>
                        {timelinessLabel(entry, onTime)}
                      </span>
                    )}
                    {entry.pointsAwarded > 0 && (
                      <span className="text-success">{t('common.ptsPlus', { count: entry.pointsAwarded })}</span>
                    )}
                  </div>
                </li>
              )
            })}
          </ul>
        )}
      </div>
    </Modal>
  )
}
