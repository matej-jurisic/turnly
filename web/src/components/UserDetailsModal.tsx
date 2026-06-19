import { useQuery } from '@tanstack/react-query'
import { usersApi } from '@/lib/api'
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

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
}

export function UserDetailsModal({
  userId,
  displayName,
  avatarColor,
  points,
  weeklyPoints,
  onClose,
}: UserDetailsModalProps) {
  const { data: log } = useQuery({
    queryKey: ['points-log', userId],
    queryFn: () => usersApi.pointsLog(userId),
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

        {!log || log.length === 0 ? (
          <p className="text-sm text-muted-foreground">No points activity yet.</p>
        ) : (
          <ul className="divide-y divide-border">
            {log.map((entry) => (
              <li key={entry.id} className="flex items-center justify-between py-2 text-sm">
                <span className="text-muted-foreground">
                  {entry.description ?? entry.type}
                  <span className="ml-2 text-xs">{formatDate(entry.createdAt)}</span>
                </span>
                <span className={entry.delta >= 0 ? 'text-success' : 'text-destructive'}>
                  {entry.delta >= 0 ? '+' : ''}{entry.delta}
                </span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </Modal>
  )
}
