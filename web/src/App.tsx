import { useEffect, useState } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { authApi, tryRefresh } from '@/lib/api'
import { useAuthStore } from '@/store/auth'
import { Layout } from '@/components/Layout'
import { Toaster } from '@/components/ui/Toaster'
import { ConfirmHost } from '@/components/ui/ConfirmHost'
import { SetupPage } from '@/pages/SetupPage'
import { LoginPage } from '@/pages/LoginPage'
import { UsersPage } from '@/pages/UsersPage'
import { ChoresPage } from '@/pages/ChoresPage'
import { SettingsPage } from '@/pages/SettingsPage'
import { PointsPage } from '@/pages/PointsPage'
import { HistoryPage } from '@/pages/HistoryPage'
import { AwardsPage } from '@/pages/AwardsPage'
import { AchievementsPage } from '@/pages/AchievementsPage'

export default function App() {
  return (
    <>
      <AppRoutes />
      <Toaster />
      <ConfirmHost />
    </>
  )
}

function AppRoutes() {
  const status = useAuthStore((s) => s.status)
  const user = useAuthStore((s) => s.user)
  const setStatus = useAuthStore((s) => s.setStatus)
  const [needsSetup, setNeedsSetup] = useState(false)

  useEffect(() => {
    let cancelled = false
    void (async () => {
      try {
        const { needsSetup } = await authApi.status()
        if (cancelled) return
        setNeedsSetup(needsSetup)
        if (needsSetup) {
          setStatus('unauthenticated')
          return
        }
        // Exchange the httpOnly refresh cookie for a session, if one exists.
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
  }, [setStatus])

  if (status === 'loading') {
    return (
      <div className="flex min-h-screen items-center justify-center text-muted-foreground">Loading…</div>
    )
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
