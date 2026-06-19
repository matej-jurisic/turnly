import { useQuery } from '@tanstack/react-query'
import { authApi, usersApi } from '@/lib/api'
import { useAuthStore } from '@/store/auth'
import { Badge } from '@/components/ui/Badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/Card'

export function PointsPage() {
  const user = useAuthStore((s) => s.user)

  if (!user) return null
  return (
    <div className="mx-auto max-w-lg space-y-6">
      <h1 className="text-2xl font-semibold text-foreground">Points</h1>
      <PointsCard userId={user.id} />
    </div>
  )
}

function PointsCard({ userId }: { userId: string }) {
  const { data: me } = useQuery({ queryKey: ['me'], queryFn: authApi.me })
  const { data: log } = useQuery({ queryKey: ['points-log', userId], queryFn: () => usersApi.pointsLog(userId) })

  return (
    <Card>
      <CardHeader className="flex items-center justify-between">
        <CardTitle>Points</CardTitle>
        <Badge tone="violet">{me?.points ?? 0} pts</Badge>
      </CardHeader>
      <CardContent>
        {!log || log.length === 0 ? (
          <p className="text-sm text-muted-foreground">No points activity yet.</p>
        ) : (
          <ul className="divide-y divide-border">
            {log.map((entry) => (
              <li key={entry.id} className="flex items-center justify-between py-2 text-sm">
                <span className="text-muted-foreground">
                  {entry.description ?? entry.type}
                  <span className="ml-2 text-xs">
                    {new Date(entry.createdAt).toLocaleDateString()}
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
