import { useQuery } from '@tanstack/react-query'
import { historyApi } from '@/lib/api'
import { Modal, Avatar } from '@/components/ui/Modal'
import { Badge } from '@/components/ui/Badge'

interface UserDetailsModalProps {
  userId: string
  displayName: string
  avatarColor: string
  points: number
  weeklyPoints: number
  onClose: () => void
}

export function UserDetailsModal({
  userId,
  displayName,
  avatarColor,
  points,
  weeklyPoints,
  onClose,
}: UserDetailsModalProps) {
  const { data: completions } = useQuery({
    queryKey: ['history', { userId }],
    queryFn: () => historyApi.list({ userId }),
  })

  const title = (
    <span className="flex items-center gap-2">
      <Avatar color={avatarColor} name={displayName} size={28} />
      <span>{displayName}</span>
    </span>
  )

  return (
    <Modal title={title} onClose={onClose}>
      <div className="max-h-[65vh] space-y-4 overflow-y-auto pr-4 -mr-4">
        <div className="flex gap-2">
          <Badge tone="violet">{points} pts all time</Badge>
          <Badge tone="blue">{weeklyPoints} pts this week</Badge>
        </div>

        {!completions || completions.length === 0 ? (
          <p className="text-sm text-muted-foreground">No completions yet.</p>
        ) : (
          <ul className="divide-y divide-border">
            {completions.map((entry) => {
              const onTime = entry.occurrenceDueAt
                ? new Date(entry.at) <= new Date(entry.occurrenceDueAt)
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
                      title={new Date(entry.at).toLocaleString()}
                      className="text-muted-foreground"
                    >
                      {formatRelative(entry.at)}
                    </time>
                    {onTime !== null && (
                      <span className={onTime ? 'text-success' : 'text-destructive'}>
                        {onTime ? 'on time' : 'late'}
                      </span>
                    )}
                    {entry.pointsAwarded > 0 && (
                      <span className="text-success">+{entry.pointsAwarded} pts</span>
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

function formatRelative(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime()
  const mins = Math.floor(diff / 60_000)
  if (mins < 1) return 'just now'
  if (mins < 60) return `${mins}m ago`
  const hours = Math.floor(mins / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)
  if (days < 7) return `${days}d ago`
  return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
}
