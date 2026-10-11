import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { authApi, notificationsApi, settingsApi, tagsApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import { confirm } from '@/lib/confirm'
import { syncAppearanceFromServer } from '@/lib/appearance'
import { disablePush, enablePush, getCurrentEndpoint, isPushEnabled, pushPermission } from '@/lib/push'
import { isNative } from '@/lib/native'
import { clearServerOrigin, getServerOrigin } from '@/lib/server-config'
import { setRefreshToken } from '@/lib/native-auth'
import { getNativePushToken, nativePushPermission, registerNativePush, unregisterNativePush } from '@/lib/native-push'
import { useAuthStore } from '@/store/auth'
import type { DeliveryChannel, NotificationCategory, NotificationPreference } from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/Card'
import { Input, Label, Select } from '@/components/ui/Field'
import { LANGUAGES, localized, plural, t, tRich, useLanguageStore } from '@/lib/i18n'

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
      setMessage({ kind: 'ok', text: t('settings.passwordUpdated') })
      setCurrentPassword('')
      setNewPassword('')
    } catch (err) {
      setMessage({ kind: 'error', text: err instanceof ApiError ? err.message : t('common.error') })
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="mx-auto max-w-lg space-y-6">
      <h1 className="text-2xl font-semibold text-foreground">{t('nav.settings')}</h1>

      <AccountCard />

      <LanguageCard />

      {isNative() && <ServerCard />}

      {isNative() ? <NativeNotificationsCard /> : <NotificationsCard />}

      <NotificationPreferencesCard />

      <QuietHoursCard />

      {isAdmin && <TimezoneCard />}

      {isAdmin && <TagsCard />}

      <Card>
        <CardHeader>
          <CardTitle>{t('settings.changePassword')}</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={onSubmit} className="space-y-4">
            <div>
              <Label htmlFor="current">{t('settings.currentPassword')}</Label>
              <Input id="current" type="password" value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} autoComplete="current-password" required />
            </div>
            <div>
              <Label htmlFor="new">{t('settings.newPassword')}</Label>
              <Input id="new" type="password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} autoComplete="new-password" required />
            </div>
            {message && (
              <p className={message.kind === 'ok' ? 'text-sm text-success' : 'text-sm text-destructive'}>
                {message.text}
              </p>
            )}
            <Button type="submit" disabled={submitting}>
              {submitting ? t('common.saving') : t('settings.updatePassword')}
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
        <CardTitle>{t('settings.account')}</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4 text-sm text-muted-foreground">
        <div className="space-y-1">
          <p><span className="text-foreground">{user?.displayName}</span> (@{user?.username})</p>
          <p>{t('settings.role', { role: user ? t(`role.${user.role}`) : '' })}</p>
        </div>
        <p>{t('settings.accountHint')}</p>
      </CardContent>
    </Card>
  )
}

function LanguageCard() {
  const language = useLanguageStore((s) => s.language)
  const setLanguage = useLanguageStore((s) => s.setLanguage)

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('settings.language')}</CardTitle>
      </CardHeader>
      <CardContent>
        <div className="inline-flex rounded-lg border border-border p-0.5" role="group" aria-label={t('settings.language')}>
          {LANGUAGES.map((option) => {
            const selected = option.value === language
            return (
              <button
                key={option.value}
                type="button"
                lang={option.value}
                aria-pressed={selected}
                onClick={() => setLanguage(option.value)}
                className={`rounded-md px-3 py-1.5 text-sm transition-colors ${
                  selected
                    ? 'bg-primary/10 font-semibold text-primary'
                    : 'text-muted-foreground hover:bg-accent hover:text-foreground'
                }`}
              >
                {option.label}
              </button>
            )
          })}
        </div>
      </CardContent>
    </Card>
  )
}

function ServerCard() {
  const [busy, setBusy] = useState(false)
  const origin = getServerOrigin()

  async function onChange() {
    const ok = await confirm({
      title: t('settings.changeServer'),
      message: t('settings.changeServerConfirm'),
      confirmLabel: t('settings.changeServer'),
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
        <CardTitle>{t('settings.server')}</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <p className="text-sm text-muted-foreground">
          {tRich('settings.connectedTo', { origin: <span className="text-foreground">{origin}</span> })}
        </p>
        <Button type="button" variant="secondary" disabled={busy} onClick={onChange}>
          {busy ? t('settings.switching') : t('settings.changeServer')}
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
        setMessage({ kind: 'ok', text: t('settings.pushEnabled') })
      } else {
        await disablePush()
        setMessage({ kind: 'ok', text: t('settings.pushDisabled') })
      }
      await refreshLocalState()
      queryClient.invalidateQueries({ queryKey: ['push-devices'] })
    } catch (err) {
      setMessage({ kind: 'error', text: err instanceof Error ? err.message : t('common.error') })
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
          ? { kind: 'ok', text: plural('settings.testSent', sent) }
          : { kind: 'error', text: t('settings.testNoDevice') },
      )
    } catch (err) {
      setMessage({ kind: 'error', text: err instanceof ApiError ? err.message : t('settings.testFailed') })
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
    onError: (err) => setMessage({ kind: 'error', text: err instanceof ApiError ? err.message : t('settings.removeDeviceFailed') }),
  })

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('notifEdit.title')}</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <p className="text-sm text-muted-foreground">
          {t('settings.pushIntro')}
        </p>
        {unsupported ? (
          <p className="text-sm text-muted-foreground">{t('settings.pushUnsupported')}</p>
        ) : blocked ? (
          <p className="text-sm text-destructive">
            {t('settings.pushBlockedWeb')}
          </p>
        ) : enabled === null ? (
          <p className="text-sm text-muted-foreground">{t('settings.checking')}</p>
        ) : enabled ? (
          <Button type="button" variant="secondary" disabled={busy} onClick={() => toggle(false)}>
            {busy ? t('settings.working') : t('settings.disableDevice')}
          </Button>
        ) : (
          <Button type="button" disabled={busy} onClick={() => toggle(true)}>
            {busy ? t('settings.working') : t('settings.enableDevice')}
          </Button>
        )}

        {devices && devices.length > 0 && (
          <div className="space-y-2 border-t border-border pt-3">
            <p className="text-sm text-foreground">{t('settings.yourDevices')}</p>
            <ul className="space-y-1">
              {devices.map((d) => {
                const isThis = currentEndpoint != null && d.endpoint === currentEndpoint
                return (
                  <li key={d.id} className="flex items-center justify-between gap-2 rounded-md bg-accent px-3 py-2 text-sm">
                    <span className="flex items-center gap-2 text-foreground">
                      {d.label}
                      {isThis && <span className="rounded bg-primary/10 px-1.5 py-0.5 text-xs text-primary">{t('settings.thisDevice')}</span>}
                    </span>
                    <button
                      type="button"
                      onClick={() => removeMutation.mutate(d.id)}
                      disabled={removeMutation.isPending}
                      aria-label={t('settings.removeItem', { name: d.label })}
                      className="text-muted-foreground transition-colors hover:text-destructive disabled:opacity-50"
                    >
                      <XIcon />
                    </button>
                  </li>
                )
              })}
            </ul>
            <p className="text-xs text-muted-foreground">
              {t('settings.removeDeviceNote')}
            </p>
          </div>
        )}

        {isAdmin && enabled && (
          <div className="border-t border-border pt-3">
            <Button type="button" variant="secondary" disabled={testing} onClick={sendTest}>
              {testing ? t('settings.sending') : t('settings.sendTest')}
            </Button>
            <p className="mt-1 text-xs text-muted-foreground">{t('settings.sendTestHint')}</p>
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

function NativeNotificationsCard() {
  const isAdmin = useAuthStore((s) => s.user?.role === 'Admin')
  const navigate = useNavigate()
  const [permission, setPermission] = useState<'granted' | 'denied' | 'prompt' | 'unsupported' | null>(null)
  const [enabled, setEnabled] = useState(getNativePushToken() != null)
  const [busy, setBusy] = useState(false)
  const [testing, setTesting] = useState(false)
  const [message, setMessage] = useState<{ kind: 'ok' | 'error'; text: string } | null>(null)

  useEffect(() => {
    void nativePushPermission().then(setPermission)
  }, [])

  async function enable() {
    setBusy(true)
    setMessage(null)
    try {
      const granted = await registerNativePush((url) => navigate(url))
      const perm = await nativePushPermission()
      setPermission(perm)
      if (granted) {
        setEnabled(true)
        setMessage({ kind: 'ok', text: t('settings.pushEnabled') })
      } else {
        setMessage({ kind: 'error', text: t('push.denied') })
      }
    } catch (err) {
      setMessage({ kind: 'error', text: err instanceof Error ? err.message : t('common.error') })
    } finally {
      setBusy(false)
    }
  }

  async function disable() {
    setBusy(true)
    setMessage(null)
    try {
      await unregisterNativePush()
      setEnabled(false)
      setMessage({ kind: 'ok', text: t('settings.pushDisabled') })
    } catch (err) {
      setMessage({ kind: 'error', text: err instanceof Error ? err.message : t('common.error') })
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
          ? { kind: 'ok', text: plural('settings.testSent', sent) }
          : { kind: 'error', text: t('settings.testNoDevice') },
      )
    } catch (err) {
      setMessage({ kind: 'error', text: err instanceof ApiError ? err.message : t('settings.testFailed') })
    } finally {
      setTesting(false)
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('notifEdit.title')}</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <p className="text-sm text-muted-foreground">
          {t('settings.pushIntro')}
        </p>
        {permission === null ? (
          <p className="text-sm text-muted-foreground">{t('settings.checking')}</p>
        ) : permission === 'denied' ? (
          <p className="text-sm text-destructive">
            {t('settings.pushBlockedNative')}
          </p>
        ) : enabled ? (
          <Button type="button" variant="secondary" disabled={busy} onClick={disable}>
            {busy ? t('settings.working') : t('settings.disableDevice')}
          </Button>
        ) : (
          <Button type="button" disabled={busy} onClick={enable}>
            {busy ? t('settings.working') : t('settings.enableDevice')}
          </Button>
        )}

        {isAdmin && enabled && (
          <div className="border-t border-border pt-3">
            <Button type="button" variant="secondary" disabled={testing} onClick={sendTest}>
              {testing ? t('settings.sending') : t('settings.sendTest')}
            </Button>
            <p className="mt-1 text-xs text-muted-foreground">{t('settings.sendTestHint')}</p>
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

/** Label and one-line explanation per category, in the order the card shows them. Categories the
 * server marks non-configurable are rendered read-only rather than hidden, so the list still
 * describes everything Turnly can send you. */
const CATEGORY_COPY: Record<NotificationCategory, { label: string; hint: string }> = {
  ChoreReminder: localized({}, { label: 'category.ChoreReminder.label', hint: 'category.ChoreReminder.hint' }),
  ChoreDue: localized({}, { label: 'category.ChoreDue.label', hint: 'category.ChoreDue.hint' }),
  ChoreOverdue: localized({}, { label: 'category.ChoreOverdue.label', hint: 'category.ChoreOverdue.hint' }),
  OfferReceived: localized({}, { label: 'category.OfferReceived.label', hint: 'category.OfferReceived.hint' }),
  OfferResponse: localized({}, { label: 'category.OfferResponse.label', hint: 'category.OfferResponse.hint' }),
  System: localized({}, { label: 'category.System.label', hint: 'category.System.hint' }),
}

const CHANNEL_COPY: Record<DeliveryChannel, { label: string; title: string }> = {
  PushAndInbox: localized({}, { label: 'channel.PushAndInbox.label', title: 'channel.PushAndInbox.title' }),
  InboxOnly: localized({}, { label: 'channel.InboxOnly.label', title: 'channel.InboxOnly.title' }),
  Off: localized({}, { label: 'channel.Off.label', title: 'channel.Off.title' }),
}

const CHANNELS: DeliveryChannel[] = ['PushAndInbox', 'InboxOnly', 'Off']

function NotificationPreferencesCard() {
  const queryClient = useQueryClient()
  const { data: preferences, isLoading } = useQuery({
    queryKey: ['notification-preferences'],
    queryFn: notificationsApi.preferences,
  })

  const mutation = useMutation({
    mutationKey: ['notification-preferences', 'set'],
    mutationFn: ({ category, channel }: { category: NotificationCategory; channel: DeliveryChannel }) =>
      notificationsApi.setPreference(category, channel),
    // Move the pill immediately; the round trip is invisible unless it fails.
    onMutate: async ({ category, channel }) => {
      await queryClient.cancelQueries({ queryKey: ['notification-preferences'] })
      const previous = queryClient.getQueryData<NotificationPreference[]>(['notification-preferences'])
      queryClient.setQueryData<NotificationPreference[]>(['notification-preferences'], (current) =>
        current?.map((pref) => (pref.category === category ? { ...pref, channel } : pref)),
      )
      return { previous }
    },
    // The server hands back the full matrix, so seed the cache with it instead of refetching.
    // Skip it while another click is still in flight, otherwise a slower earlier
    // response would overwrite the newer optimistic state.
    onSuccess: (updated) => {
      if (queryClient.isMutating({ mutationKey: ['notification-preferences', 'set'] }) === 1) {
        queryClient.setQueryData(['notification-preferences'], updated)
      }
    },
    onError: (err, _vars, context) => {
      if (context?.previous) queryClient.setQueryData(['notification-preferences'], context.previous)
      toast.error(err instanceof ApiError ? err.message : t('common.error'))
    },
  })

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('settings.prefsTitle')}</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <p className="text-sm text-muted-foreground">
          {tRich('settings.prefsIntro', {
            push: <span className="text-foreground">{CHANNEL_COPY.PushAndInbox.label}</span>,
            inbox: <span className="text-foreground">{CHANNEL_COPY.InboxOnly.label}</span>,
            off: <span className="text-foreground">{CHANNEL_COPY.Off.label}</span>,
          })}
        </p>
        {isLoading && <p className="text-sm text-muted-foreground">{t('common.loading')}</p>}
        <div className="divide-y divide-border">
          {preferences?.map((pref) => (
            <div key={pref.category} className="flex items-center justify-between gap-4 py-3">
              <div className="min-w-0">
                <p className="text-sm text-foreground">{CATEGORY_COPY[pref.category].label}</p>
                <p className="text-xs text-muted-foreground">{CATEGORY_COPY[pref.category].hint}</p>
              </div>
              <div className="flex shrink-0 rounded-lg border border-border p-0.5" role="group">
                {CHANNELS.map((channel) => {
                  const selected = pref.channel === channel
                  return (
                    <button
                      key={channel}
                      type="button"
                      title={CHANNEL_COPY[channel].title}
                      aria-pressed={selected}
                      disabled={!pref.configurable}
                      onClick={() => mutation.mutate({ category: pref.category, channel })}
                      className={`rounded-md px-2.5 py-1 text-xs transition-colors disabled:cursor-not-allowed disabled:opacity-40 ${
                        selected
                          ? 'bg-primary/10 font-semibold text-primary'
                          : 'text-muted-foreground hover:bg-accent hover:text-foreground'
                      }`}
                    >
                      {CHANNEL_COPY[channel].label}
                    </button>
                  )
                })}
              </div>
            </div>
          ))}
        </div>
        <p className="text-xs text-muted-foreground">
          {t('settings.muteHint')}
        </p>
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
      toast.success(t('settings.quietUpdated'))
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('common.error')),
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
        <CardTitle>{t('settings.quietTitle')}</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <p className="text-sm text-muted-foreground">
          {t('settings.quietIntro')}
        </p>
        <label className="flex items-center gap-2 text-sm text-foreground">
          <input
            type="checkbox"
            checked={enabled}
            onChange={(e) => setEnabled(e.target.checked)}
            className="h-4 w-4 accent-primary"
          />
          {t('settings.quietEnable')}
        </label>
        {enabled && (
          <div className="flex items-end gap-3">
            <div>
              <Label htmlFor="quiet-start">{t('settings.from')}</Label>
              <Input id="quiet-start" type="time" value={start} onChange={(e) => setStart(e.target.value)} className="w-32" />
            </div>
            <div>
              <Label htmlFor="quiet-end">{t('settings.to')}</Label>
              <Input id="quiet-end" type="time" value={end} onChange={(e) => setEnd(e.target.value)} className="w-32" />
            </div>
          </div>
        )}
        {invalid && <p className="text-sm text-destructive">{t('settings.quietSame')}</p>}
        <Button onClick={() => mutation.mutate()} disabled={!dirty || invalid || mutation.isPending}>
          {mutation.isPending ? t('common.saving') : t('settings.quietSave')}
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
      toast.success(t('settings.tzUpdated'))
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('common.error')),
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
        <CardTitle>{t('settings.tzTitle')}</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <p className="text-sm text-muted-foreground">
          {t('settings.tzIntro')}
        </p>
        <div className="space-y-2">
          <Label htmlFor="tz">{t('settings.tzFamily')}</Label>
          {TIME_ZONES.length > 0 ? (
            <Select id="tz" value={selected} onChange={(e) => change(e.target.value)}>
              <option value="">{serverTz ? t('settings.tzServerDefaultZone', { zone: serverTz }) : t('settings.tzServerDefault')}</option>
              {TIME_ZONES.map((z) => (
                <option key={z} value={z}>{z}</option>
              ))}
            </Select>
          ) : (
            <Input id="tz" value={selected} onChange={(e) => change(e.target.value)} placeholder={t('settings.tzPlaceholder')} />
          )}
          {DEVICE_TIME_ZONE && DEVICE_TIME_ZONE !== selected && (
            <button type="button" onClick={() => change(DEVICE_TIME_ZONE)} className="text-sm text-primary hover:underline">
              {t('settings.tzUseDevice', { zone: DEVICE_TIME_ZONE })}
            </button>
          )}
        </div>
        <Button onClick={() => mutation.mutate()} disabled={!dirty || mutation.isPending}>
          {mutation.isPending ? t('common.saving') : t('settings.tzSave')}
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
    onError: (err) => setError(err instanceof ApiError ? err.message : t('settings.tagCreateFailed')),
  })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => tagsApi.remove(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['tags'] }),
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('settings.tagDeleteFailed')),
  })

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('filters.tags')}</CardTitle>
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
                  aria-label={t('settings.deleteItem', { name: tag.name })}
                  className="ml-0.5 text-muted-foreground transition-colors hover:text-destructive disabled:opacity-50"
                >
                  <XIcon />
                </button>
              </span>
            ))}
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">{t('settings.noTags')}</p>
        )}
        <form
          onSubmit={(e) => { e.preventDefault(); if (name.trim()) createMutation.mutate(name.trim()) }}
          className="flex gap-2"
        >
          <Input value={name} onChange={(e) => setName(e.target.value)} placeholder={t('settings.newTag')} required />
          <Button type="submit" variant="secondary" disabled={createMutation.isPending}>{t('common.add')}</Button>
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
      title: t('settings.freshStart'),
      message: t('settings.freshStartConfirm'),
      confirmLabel: t('settings.resetEverything'),
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
      toast.success(t('settings.freshStartDone'))
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : t('settings.freshStartFailed'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('settings.dangerZone')}</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <p className="text-sm text-muted-foreground">
          {t('settings.dangerIntro')}
        </p>
        <Button type="button" variant="danger" disabled={busy} onClick={onFreshStart}>
          {busy ? t('settings.resetting') : t('settings.freshStart')}
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
