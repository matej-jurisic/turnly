import { useState } from 'react'
import type { FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { authApi, usersApi, ApiError } from '@/lib/api'
import { useAuthStore } from '@/store/auth'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/Card'
import { Input, Label } from '@/components/ui/Field'

export function SettingsPage() {
  const user = useAuthStore((s) => s.user)
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [message, setMessage] = useState<{ kind: 'ok' | 'error'; text: string } | null>(null)
  const [submitting, setSubmitting] = useState(false)

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setMessage(null)
    setSubmitting(true)
    try {
      await authApi.changePassword(currentPassword, newPassword)
      setMessage({ kind: 'ok', text: 'Password updated.' })
      setCurrentPassword('')
      setNewPassword('')
    } catch (err) {
      setMessage({ kind: 'error', text: err instanceof ApiError ? err.message : 'Something went wrong' })
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="mx-auto max-w-lg space-y-6">
      <h1 className="text-2xl font-semibold text-foreground">Settings</h1>

      <Card>
        <CardHeader>
          <CardTitle>Account</CardTitle>
        </CardHeader>
        <CardContent className="space-y-1 text-sm text-muted-foreground">
          <p><span className="text-foreground">{user?.displayName}</span> (@{user?.username})</p>
          <p>Role: {user?.role}</p>
        </CardContent>
      </Card>

      {user && <PointsCard userId={user.id} />}

      <Card>
        <CardHeader>
          <CardTitle>Change password</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={onSubmit} className="space-y-4">
            <div>
              <Label htmlFor="current">Current password</Label>
              <Input id="current" type="password" value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} autoComplete="current-password" required />
            </div>
            <div>
              <Label htmlFor="new">New password</Label>
              <Input id="new" type="password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} autoComplete="new-password" required />
            </div>
            {message && (
              <p className={message.kind === 'ok' ? 'text-sm text-success' : 'text-sm text-destructive'}>
                {message.text}
              </p>
            )}
            <Button type="submit" disabled={submitting}>
              {submitting ? 'Saving…' : 'Update password'}
            </Button>
          </form>
        </CardContent>
      </Card>
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
