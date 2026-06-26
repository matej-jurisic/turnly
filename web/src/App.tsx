import { useEffect, useState } from 'react'
import { Navigate, Route, Routes, useNavigate } from 'react-router-dom'
import { authApi, tryRefresh } from '@/lib/api'
import { useAuthStore } from '@/store/auth'
import { applyPalette } from '@/lib/palette'
import { isNative } from '@/lib/native'
import { loadServerOrigin } from '@/lib/server-config'
import { loadRefreshToken } from '@/lib/native-auth'
import { registerNativePush } from '@/lib/native-push'
import { ServerSetupPage } from '@/pages/ServerSetupPage'
import { Layout } from '@/components/Layout'
import { Toaster } from '@/components/ui/Toaster'
import { ConfirmHost } from '@/components/ui/ConfirmHost'
import { AchievementCelebration } from '@/components/AchievementCelebration'
import { SetupPage } from '@/pages/SetupPage'
import { LoginPage } from '@/pages/LoginPage'
import { UsersPage } from '@/pages/UsersPage'
import { ChoresPage } from '@/pages/ChoresPage'
import { SettingsPage } from '@/pages/SettingsPage'
import { PointsPage } from '@/pages/PointsPage'
import { HistoryPage } from '@/pages/HistoryPage'
import { AwardsPage } from '@/pages/AwardsPage'
import { AchievementsPage } from '@/pages/AchievementsPage'
import { GachaPage } from '@/pages/GachaPage'

export default function App() {
  return (
    <>
      <AppRoutes />
      <Toaster />
      <ConfirmHost />
      <AchievementCelebration />
    </>
  )
}

function AppRoutes() {
  const status = useAuthStore((s) => s.status)
  const user = useAuthStore((s) => s.user)
  const setStatus = useAuthStore((s) => s.setStatus)
  const navigate = useNavigate()
  const [needsSetup, setNeedsSetup] = useState(false)
  const [needsServer, setNeedsServer] = useState(false)
  // Bumped after the native server picker connects, to re-run the bootstrap below.
  const [bootKey, setBootKey] = useState(0)

  // Keep the equipped app theme palette in sync with the signed-in user (gacha cosmetic).
  const equippedThemeKey = user?.equippedThemeKey
  useEffect(() => {
    applyPalette(equippedThemeKey)
  }, [equippedThemeKey])

  // Native app: once signed in, register for FCM push and deep-link notification taps.
  useEffect(() => {
    if (!isNative() || status !== 'authenticated') return
    void registerNativePush((url) => navigate(url))
  }, [status, navigate])

  useEffect(() => {
    let cancelled = false
    void (async () => {
      setStatus('loading')
      // Native: hydrate the persisted server + refresh token before anything calls the API.
      if (isNative()) {
        const origin = await loadServerOrigin()
        if (cancelled) return
        if (!origin) {
          setNeedsServer(true)
          setStatus('unauthenticated')
          return
        }
        setNeedsServer(false)
        await loadRefreshToken()
        if (cancelled) return
      }
      try {
        const { needsSetup } = await authApi.status()
        if (cancelled) return
        setNeedsSetup(needsSetup)
        if (needsSetup) {
          setStatus('unauthenticated')
          return
        }
        // Restore a session: web exchanges the httpOnly cookie, native its stored refresh token.
        const restored = await tryRefresh()
        if (!cancelled && !restored) setStatus('unauthenticated')
      } catch {
        if (!cancelled) {
          setNeedsSetup(false)
          setStatus('unauthenticated')
        }
      }
    })()
    return () => {
      cancelled = true
    }
  }, [setStatus, bootKey])

  if (status === 'loading') {
    return (
      <div className="flex min-h-screen items-center justify-center text-muted-foreground">Loading…</div>
    )
  }

  // Native app with no server chosen yet: gate everything behind the server picker.
  if (needsServer) {
    return <ServerSetupPage onConnected={() => setBootKey((k) => k + 1)} />
  }

  if (status === 'authenticated') {
    return (
      <Routes>
        <Route element={<Layout />}>
          <Route index element={<Navigate to="/chores" replace />} />
          <Route path="/dashboard" element={<Navigate to="/chores" replace />} />
          <Route path="/chores" element={<ChoresPage />} />
          <Route
            path="/users"
            element={user?.role === 'Admin' ? <UsersPage /> : <Navigate to="/chores" replace />}
          />
          <Route path="/points" element={<PointsPage />} />
          <Route path="/awards" element={<AwardsPage />} />
          <Route path="/achievements" element={<AchievementsPage />} />
          <Route path="/gacha" element={<GachaPage />} />
          <Route path="/history" element={<HistoryPage />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Route>
      </Routes>
    )
  }

  if (needsSetup) {
    return (
      <Routes>
        <Route path="/setup" element={<SetupPage />} />
        <Route path="*" element={<Navigate to="/setup" replace />} />
      </Routes>
    )
  }

  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="*" element={<Navigate to="/login" replace />} />
    </Routes>
  )
}
