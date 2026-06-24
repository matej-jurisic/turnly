import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, achievementsApi, usersApi } from '@/lib/api'
import { toast } from '@/lib/toast'
import { confirm } from '@/lib/confirm'
import { useAuthStore } from '@/store/auth'
import type { Achievement } from '@/lib/types'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Card, CardContent } from '@/components/ui/Card'
import { Label, Select } from '@/components/ui/Field'
import { cn } from '@/lib/utils'

// Order categories deterministically; anything unknown falls to the end.
const CATEGORY_ORDER = ['Completions', 'Streaks', 'Points', 'Rewards', 'Variety']

export function AchievementsPage() {
  const currentUser = useAuthStore((s) => s.user)
  const isAdmin = currentUser?.role === 'Admin'
  const queryClient = useQueryClient()

  // Admins can inspect (and revoke from) any user; everyone else sees their own.
  const [viewUserId, setViewUserId] = useState(currentUser?.id ?? '')
  const targetUserId = isAdmin ? viewUserId : (currentUser?.id ?? '')
  const isOwnView = targetUserId === currentUser?.id

  const { data: users } = useQuery({ queryKey: ['users'], queryFn: usersApi.list, enabled: isAdmin })

  const { data: achievements, isLoading, error } = useQuery({
    queryKey: ['achievements', targetUserId],
    queryFn: () => achievementsApi.list(isOwnView ? undefined : targetUserId),
    enabled: Boolean(targetUserId),
  })

  const revokeMutation = useMutation({
    mutationFn: (key: string) => achievementsApi.revoke(targetUserId, key),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['achievements', targetUserId] }),
    onError: (err) => toast.error(err instanceof ApiError ? err.message : 'Revoke failed'),
  })

  const earnedCount = achievements?.filter((a) => a.earned).length ?? 0
  const total = achievements?.length ?? 0

  const groups = groupByCategory(achievements ?? [])

  return (
    <div className="space-y-8">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold text-foreground">Achievements</h1>
        <div className="flex items-center gap-3">
          {isAdmin && users && users.length > 0 && (
            <div className="flex items-center gap-2">
              <Label htmlFor="achievement-user" className="mb-0 shrink-0">User</Label>
              <Select
                id="achievement-user"
                value={viewUserId}
                onChange={(e) => setViewUserId(e.target.value)}
              >
                {users.map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.displayName}
                    {u.id === currentUser?.id ? ' (you)' : ''}
                  </option>
                ))}
              </Select>
            </div>
          )}
          {total > 0 && (
            <Badge tone="violet">
              {earnedCount} / {total} unlocked
            </Badge>
          )}
        </div>
      </div>

      {isLoading && <p className="text-muted-foreground">Loading…</p>}
      {error && <p className="text-destructive">{(error as ApiError).message}</p>}

      {groups.map(([category, items]) => (
        <section key={category} className="space-y-3">
          <h2 className="text-sm font-semibold text-muted-foreground">{category}</h2>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {items.map((a) => (
              <AchievementCard
                key={a.key}
                achievement={a}
                canRevoke={isAdmin && a.earned}
                revoking={revokeMutation.isPending}
                onRevoke={async () => {
                  if (
                    await confirm({
                      title: 'Revoke achievement',
                      message: `Revoke "${a.name}"? It can be re-earned if the threshold is met again.`,
                      confirmLabel: 'Revoke',
                    })
                  ) {
                    revokeMutation.mutate(a.key)
                  }
                }}
              />
            ))}
          </div>
        </section>
      ))}
    </div>
  )
}

function AchievementCard({
  achievement: a,
  canRevoke,
  revoking,
  onRevoke,
}: {
  achievement: Achievement
  canRevoke: boolean
  revoking: boolean
  onRevoke: () => void
}) {
  const pct = a.threshold > 0 ? Math.min(100, Math.round((a.progress / a.threshold) * 100)) : 0

  return (
    <Card className={cn('flex flex-col', !a.earned && 'opacity-90')}>
      <CardContent className="flex flex-1 flex-col gap-3">
        <div className="flex items-start gap-3">
          {/* Locked achievements are desaturated so earned ones pop. */}
          <span className={cn('text-3xl leading-none', !a.earned && 'opacity-40 grayscale')}>
            {a.emoji}
          </span>
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2">
              <p className="truncate font-semibold text-foreground">{a.name}</p>
              {a.earned && <Badge tone="green">Earned</Badge>}
            </div>
            <p className="mt-0.5 text-sm text-muted-foreground">{a.description}</p>
          </div>
        </div>

        <div className="mt-auto space-y-1.5 pt-1">
          {a.earned ? (
            <div className="flex items-center justify-between gap-2">
              <p className="text-xs text-muted-foreground">
                Unlocked{a.earnedAt ? ` ${new Date(a.earnedAt).toLocaleDateString()}` : ''}
              </p>
              {canRevoke && (
                <Button
                  size="sm"
                  variant="ghost"
                  className="text-destructive hover:bg-destructive/10"
                  disabled={revoking}
                  onClick={onRevoke}
                >
                  Revoke
                </Button>
              )}
            </div>
          ) : (
            <>
              <div className="h-2 overflow-hidden rounded-full bg-accent">
                <div className="h-full rounded-full bg-primary transition-all" style={{ width: `${pct}%` }} />
              </div>
              <p className="text-xs text-muted-foreground">
                {a.progress} / {a.threshold}
              </p>
            </>
          )}
        </div>
      </CardContent>
    </Card>
  )
}

/** Groups achievements by category in a stable, curated order. */
function groupByCategory(achievements: Achievement[]): [string, Achievement[]][] {
  const byCategory = new Map<string, Achievement[]>()
  for (const a of achievements) {
    const list = byCategory.get(a.category)
    if (list) list.push(a)
    else byCategory.set(a.category, [a])
  }
  return [...byCategory.entries()].sort(
    ([a], [b]) => orderIndex(a) - orderIndex(b) || a.localeCompare(b),
  )
}

function orderIndex(category: string): number {
  const i = CATEGORY_ORDER.indexOf(category)
  return i === -1 ? CATEGORY_ORDER.length : i
}
