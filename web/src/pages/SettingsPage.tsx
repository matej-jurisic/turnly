import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { authApi, notificationsApi, tagsApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import { disablePush, enablePush, getCurrentEndpoint, isPushEnabled, pushPermission } from '@/lib/push'
import { useAuthStore } from '@/store/auth'
import { Button } from '@/components/ui/Button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/Card'
import { Input, Label } from '@/components/ui/Field'
import { ColorPicker } from '@/components/ui/ColorPicker'

export function SettingsPage() {
  const user = useAuthStore((s) => s.user)
  const isAdmin = user?.role === 'Admin'
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

      <AccountCard />

      <NotificationsCard />

      {isAdmin && <TagsCard />}

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

function AccountCard() {
  const user = useAuthStore((s) => s.user)
  const setUser = useAuthStore((s) => s.setUser)
  const queryClient = useQueryClient()
  const [color, setColor] = useState(user?.avatarColor ?? '')

  const mutation = useMutation({
    mutationFn: (avatarColor: string) => authApi.updateProfile(avatarColor),
    onSuccess: (updated) => {
      setUser(updated)
      queryClient.invalidateQueries({ queryKey: ['leaderboard'] })
      queryClient.invalidateQueries({ queryKey: ['users'] })
      toast.success('Profile color updated.')
    },
    onError: (err) => {
      toast.error(err instanceof ApiError ? err.message : 'Something went wrong')
    },
  })

  const changed = !!user && color.toLowerCase() !== user.avatarColor.toLowerCase()

  return (
    <Card>
      <CardHeader>
        <CardTitle>Account</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4 text-sm text-muted-foreground">
        <div className="space-y-1">
          <p><span className="text-foreground">{user?.displayName}</span> (@{user?.username})</p>
          <p>Role: {user?.role}</p>
        </div>
        <div className="space-y-2">
          <Label>Profile color</Label>
          <ColorPicker value={color} onChange={setColor} />
        </div>
        <Button onClick={() => mutation.mutate(color)} disabled={!changed || mutation.isPending}>
          {mutation.isPending ? 'Saving…' : 'Save color'}
        </Button>
      </CardContent>
    </Card>
  )
}

function NotificationsCard() {
  const isAdmin = useAuthStore((s) => s.user?.role === 'Admin')
  const queryClient = useQueryClient()
  const [permission] = useState(pushPermission())
  const [enabled, setEnabled] = useState<boolean | null>(null)
  const [currentEndpoint, setCurrentEndpoint] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [testing, setTesting] = useState(false)
  const [message, setMessage] = useState<{ kind: 'ok' | 'error'; text: string } | null>(null)

  const supported = permission !== 'unsupported'
  const { data: devices } = useQuery({
    queryKey: ['push-devices'],
    queryFn: notificationsApi.devices,
    enabled: supported,
  })

  const refreshLocalState = async () => {
    setEnabled(await isPushEnabled())
    setCurrentEndpoint(await getCurrentEndpoint())
  }

  useEffect(() => {
    refreshLocalState()
  }, [])

  const unsupported = permission === 'unsupported'
  const blocked = permission === 'denied'

  async function toggle(turnOn: boolean) {
    setBusy(true)
    setMessage(null)
    try {
      if (turnOn) {
        await enablePush()
        setMessage({ kind: 'ok', text: 'Notifications enabled on this device.' })
      } else {
        await disablePush()
        setMessage({ kind: 'ok', text: 'Notifications disabled on this device.' })
      }
      await refreshLocalState()
      queryClient.invalidateQueries({ queryKey: ['push-devices'] })
    } catch (err) {
      setMessage({ kind: 'error', text: err instanceof Error ? err.message : 'Something went wrong' })
    } finally {
      setBusy(false)
    }
  }

  async function sendTest() {
    setTesting(true)
    setMessage(null)
    try {
      const { sent } = await notificationsApi.test()
      setMessage(
        sent > 0
          ? { kind: 'ok', text: `Test notification sent to ${sent} device${sent === 1 ? '' : 's'}.` }
          : { kind: 'error', text: 'No device accepted the push. Make sure notifications are enabled on this device.' },
      )
    } catch (err) {
      setMessage({ kind: 'error', text: err instanceof ApiError ? err.message : 'Failed to send test' })
    } finally {
      setTesting(false)
    }
  }

  const removeMutation = useMutation({
    mutationFn: (id: string) => notificationsApi.removeDevice(id),
    onSuccess: async () => {
      await refreshLocalState()
      queryClient.invalidateQueries({ queryKey: ['push-devices'] })
    },
    onError: (err) => setMessage({ kind: 'error', text: err instanceof ApiError ? err.message : 'Failed to remove device' }),
  })

  return (
    <Card>
      <CardHeader>
        <CardTitle>Notifications</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <p className="text-sm text-muted-foreground">
          Get push reminders for your chores on this device. Notifications stop once a chore is marked complete.
        </p>
        {unsupported ? (
          <p className="text-sm text-muted-foreground">This browser doesn’t support push notifications.</p>
        ) : blocked ? (
          <p className="text-sm text-destructive">
            Notifications are blocked. Enable them for this site in your browser settings, then reload.
          </p>
        ) : enabled === null ? (
          <p className="text-sm text-muted-foreground">Checking…</p>
        ) : enabled ? (
          <Button type="button" variant="secondary" disabled={busy} onClick={() => toggle(false)}>
            {busy ? 'Working…' : 'Disable on this device'}
          </Button>
        ) : (
          <Button type="button" disabled={busy} onClick={() => toggle(true)}>
            {busy ? 'Working…' : 'Enable on this device'}
          </Button>
        )}

        {devices && devices.length > 0 && (
          <div className="space-y-2 border-t border-border pt-3">
            <p className="text-sm text-foreground">Your devices</p>
            <ul className="space-y-1">
              {devices.map((d) => {
                const isThis = currentEndpoint != null && d.endpoint === currentEndpoint
                return (
                  <li key={d.id} className="flex items-center justify-between gap-2 rounded-md bg-accent px-3 py-2 text-sm">
                    <span className="flex items-center gap-2 text-foreground">
                      {d.label}
                      {isThis && <span className="rounded bg-primary/10 px-1.5 py-0.5 text-xs text-primary">This device</span>}
                    </span>
                    <button
                      type="button"
                      onClick={() => removeMutation.mutate(d.id)}
                      disabled={removeMutation.isPending}
                      aria-label={`Remove ${d.label}`}
                      className="text-muted-foreground transition-colors hover:text-destructive disabled:opacity-50"
                    >
                      <XIcon />
                    </button>
                  </li>
                )
              })}
            </ul>
            <p className="text-xs text-muted-foreground">
              Removing a device stops notifications there. To re-enable, open Turnly on that device and turn notifications on.
            </p>
          </div>
        )}

        {isAdmin && enabled && (
          <div className="border-t border-border pt-3">
            <Button type="button" variant="secondary" disabled={testing} onClick={sendTest}>
              {testing ? 'Sending…' : 'Send test notification'}
            </Button>
            <p className="mt-1 text-xs text-muted-foreground">Dev: pushes an immediate notification to your devices.</p>
          </div>
        )}
        {message && (
          <p className={message.kind === 'ok' ? 'text-sm text-success' : 'text-sm text-destructive'}>
            {message.text}
          </p>
        )}
      </CardContent>
    </Card>
  )
}

function TagsCard() {
  const queryClient = useQueryClient()
  const { data: tags } = useQuery({ queryKey: ['tags'], queryFn: tagsApi.list })
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)

  const createMutation = useMutation({
    mutationFn: (n: string) => tagsApi.create(n),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tags'] })
      setName('')
      setError(null)
    },
    onError: (err) => setError(err instanceof ApiError ? err.message : 'Failed to create tag'),
  })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => tagsApi.remove(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['tags'] }),
    onError: (err) => toast.error(err instanceof ApiError ? err.message : 'Failed to delete tag'),
  })

  return (
    <Card>
      <CardHeader>
        <CardTitle>Tags</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {tags && tags.length > 0 ? (
          <div className="flex flex-wrap gap-2">
            {tags.map((tag) => (
              <span key={tag.id} className="flex items-center gap-1 rounded-md bg-accent px-2 py-1 text-sm text-foreground">
                {tag.name}
                <button
                  type="button"
                  onClick={() => deleteMutation.mutate(tag.id)}
                  disabled={deleteMutation.isPending}
                  aria-label={`Delete ${tag.name}`}
                  className="ml-0.5 text-muted-foreground transition-colors hover:text-destructive disabled:opacity-50"
                >
                  <XIcon />
                </button>
              </span>
            ))}
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">No tags yet.</p>
        )}
        <form
          onSubmit={(e) => { e.preventDefault(); if (name.trim()) createMutation.mutate(name.trim()) }}
          className="flex gap-2"
        >
          <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="New tag name" required />
          <Button type="submit" variant="secondary" disabled={createMutation.isPending}>Add</Button>
        </form>
        {error && <p className="text-sm text-destructive">{error}</p>}
      </CardContent>
    </Card>
  )
}

function XIcon() {
  return (
    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" aria-hidden="true">
      <path d="M18 6 6 18M6 6l12 12" />
    </svg>
  )
}
