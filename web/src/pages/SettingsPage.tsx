import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { authApi, notificationsApi, settingsApi, tagsApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import { confirm } from '@/lib/confirm'
import { syncAppearanceFromServer } from '@/lib/appearance'
import { disablePush, enablePush, getCurrentEndpoint, isPushEnabled, pushPermission } from '@/lib/push'
import { isNative } from '@/lib/native'
import { clearServerOrigin, getServerOrigin } from '@/lib/server-config'
import { setRefreshToken } from '@/lib/native-auth'
import { unregisterNativePush } from '@/lib/native-push'
import { useAuthStore } from '@/store/auth'
import { Button } from '@/components/ui/Button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/Card'
import { Input, Label, Select } from '@/components/ui/Field'

// The browser's full IANA zone list (and the device's own zone), resolved once. Empty when the
// browser is too old to support `Intl.supportedValuesOf`, in which case we fall back to a text input.
const TIME_ZONES: string[] = (() => {
  try {
    const supported = (Intl as unknown as { supportedValuesOf?: (key: string) => string[] }).supportedValuesOf
    return supported ? supported('timeZone') : []
  } catch {
    return []
  }
})()
const DEVICE_TIME_ZONE: string = (() => {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone
  } catch {
    return ''
  }
})()

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

      {isNative() && <ServerCard />}

      <NotificationsCard />

      <QuietHoursCard />

      {isAdmin && <TimezoneCard />}

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

      {isAdmin && <DangerZoneCard />}
    </div>
  )
}

function AccountCard() {
  const user = useAuthStore((s) => s.user)

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
        <p>
          Choose your avatar color, frame, and app theme from the account menu (Customization). Unlock
          more in the Gacha.
        </p>
      </CardContent>
    </Card>
  )
}

function ServerCard() {
  const [busy, setBusy] = useState(false)
  const origin = getServerOrigin()

  async function onChange() {
    const ok = await confirm({
      title: 'Change server',
      message: 'This signs you out of this server and returns to the server picker. Continue?',
      confirmLabel: 'Change server',
    })
    if (!ok) return
    setBusy(true)
    try {
      await unregisterNativePush()
      await authApi.logout()
    } catch {
      // Switching servers regardless; a failed logout call shouldn't block it.
    }
    await setRefreshToken(null)
    await clearServerOrigin()
    useAuthStore.getState().clear()
    // Reload re-runs the bootstrap, which now finds no saved server and shows the picker.
    window.location.reload()
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Server</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <p className="text-sm text-muted-foreground">
          Connected to <span className="text-foreground">{origin}</span>.
        </p>
        <Button type="button" variant="secondary" disabled={busy} onClick={onChange}>
          {busy ? 'Switching…' : 'Change server'}
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

function QuietHoursCard() {
  const user = useAuthStore((s) => s.user)
  const setUser = useAuthStore((s) => s.setUser)
  const [enabled, setEnabled] = useState(Boolean(user?.quietHoursStart))
  const [start, setStart] = useState(user?.quietHoursStart ?? '22:00')
  const [end, setEnd] = useState(user?.quietHoursEnd ?? '07:00')

  const mutation = useMutation({
    mutationFn: () =>
      authApi.updateProfile({
        quietHoursStart: enabled ? start : null,
        quietHoursEnd: enabled ? end : null,
      }),
    onSuccess: (updated) => {
      setUser(updated)
      toast.success('Quiet hours updated.')
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : 'Something went wrong'),
  })

  // Already-saved state, to gate the Save button.
  const savedStart = user?.quietHoursStart ?? null
  const savedEnd = user?.quietHoursEnd ?? null
  const nextStart = enabled ? start : null
  const nextEnd = enabled ? end : null
  const dirty = nextStart !== savedStart || nextEnd !== savedEnd
  const invalid = enabled && start === end

  return (
    <Card>
      <CardHeader>
        <CardTitle>Quiet hours</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <p className="text-sm text-muted-foreground">
          Mute push notifications during these hours. They still arrive in your in-app inbox, so nothing
          is missed. You just won’t get buzzed. A window like 22:00 to 07:00 spans midnight.
        </p>
        <label className="flex items-center gap-2 text-sm text-foreground">
          <input
            type="checkbox"
            checked={enabled}
            onChange={(e) => setEnabled(e.target.checked)}
            className="h-4 w-4 accent-primary"
          />
          Enable quiet hours
        </label>
        {enabled && (
          <div className="flex items-end gap-3">
            <div>
              <Label htmlFor="quiet-start">From</Label>
              <Input id="quiet-start" type="time" value={start} onChange={(e) => setStart(e.target.value)} className="w-32" />
            </div>
            <div>
              <Label htmlFor="quiet-end">To</Label>
              <Input id="quiet-end" type="time" value={end} onChange={(e) => setEnd(e.target.value)} className="w-32" />
            </div>
          </div>
        )}
        {invalid && <p className="text-sm text-destructive">Start and end can’t be the same time.</p>}
        <Button onClick={() => mutation.mutate()} disabled={!dirty || invalid || mutation.isPending}>
          {mutation.isPending ? 'Saving…' : 'Save quiet hours'}
        </Button>
      </CardContent>
    </Card>
  )
}

function TimezoneCard() {
  const queryClient = useQueryClient()
  const { data } = useQuery({ queryKey: ['app-settings'], queryFn: settingsApi.get })
  const [selected, setSelected] = useState('')
  const [touched, setTouched] = useState(false)

  // Mirror the server value once loaded, unless the admin has started editing.
  useEffect(() => {
    if (data && !touched) setSelected(data.timeZone ?? '')
  }, [data, touched])

  const mutation = useMutation({
    mutationFn: () => settingsApi.update(selected || null),
    onSuccess: (updated) => {
      queryClient.setQueryData(['app-settings'], updated)
      setTouched(false)
      toast.success('Timezone updated.')
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : 'Something went wrong'),
  })

  const change = (value: string) => {
    setSelected(value)
    setTouched(true)
  }
  const serverTz = data?.serverTimeZone ?? ''
  const dirty = data ? selected !== (data.timeZone ?? '') : false

  return (
    <Card>
      <CardHeader>
        <CardTitle>Timezone</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <p className="text-sm text-muted-foreground">
          The timezone quiet hours are evaluated against. Set it to your family’s timezone so reminders
          respect quiet hours correctly, no matter where the server runs.
        </p>
        <div className="space-y-2">
          <Label htmlFor="tz">Family timezone</Label>
          {TIME_ZONES.length > 0 ? (
            <Select id="tz" value={selected} onChange={(e) => change(e.target.value)}>
              <option value="">Server default{serverTz ? ` (${serverTz})` : ''}</option>
              {TIME_ZONES.map((z) => (
                <option key={z} value={z}>{z}</option>
              ))}
            </Select>
          ) : (
            <Input id="tz" value={selected} onChange={(e) => change(e.target.value)} placeholder="e.g. Europe/Zagreb" />
          )}
          {DEVICE_TIME_ZONE && DEVICE_TIME_ZONE !== selected && (
            <button type="button" onClick={() => change(DEVICE_TIME_ZONE)} className="text-sm text-primary hover:underline">
              Use this device’s timezone ({DEVICE_TIME_ZONE})
            </button>
          )}
        </div>
        {!selected && serverTz && (
          <p className="text-xs text-muted-foreground">Currently using the server timezone: {serverTz}.</p>
        )}
        <Button onClick={() => mutation.mutate()} disabled={!dirty || mutation.isPending}>
          {mutation.isPending ? 'Saving…' : 'Save timezone'}
        </Button>
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

function DangerZoneCard() {
  const queryClient = useQueryClient()
  const [busy, setBusy] = useState(false)

  async function onFreshStart() {
    const ok = await confirm({
      title: 'Fresh start',
      message:
        'This permanently deletes all activity, point history, redemptions, achievements, and gacha progress, and resets everyone’s points to 0. Your chores and their schedules are kept. This cannot be undone.',
      confirmLabel: 'Reset everything',
    })
    if (!ok) return
    setBusy(true)
    try {
      await settingsApi.freshStart()
      // Refresh the signed-in user (points/cosmetics reset) and every balance/history surface.
      await syncAppearanceFromServer(queryClient)
      for (const key of [
        ['chores'], ['history'], ['points-log'], ['achievements'],
        ['redemptions'], ['awards'], ['inbox'],
      ]) {
        queryClient.invalidateQueries({ queryKey: key })
      }
      toast.success('Fresh start complete. Points and history cleared.')
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : 'Fresh start failed')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Danger zone</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <p className="text-sm text-muted-foreground">
          Clear all activity, point history, redemptions, achievements, and gacha progress, and reset
          everyone&apos;s points to 0. Your chores and their schedules stay exactly as they are. Use this
          for a clean slate, for example at the start of a new month. This cannot be undone.
        </p>
        <Button type="button" variant="danger" disabled={busy} onClick={onFreshStart}>
          {busy ? 'Resetting…' : 'Fresh start'}
        </Button>
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
