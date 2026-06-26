import { useCallback, useEffect, useState } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { authApi, choresApi } from '@/lib/api'
import { confirm } from '@/lib/confirm'
import { useAuthStore } from '@/store/auth'
import { UserMenu } from '@/components/UserMenu'
import { NotificationsBell } from '@/components/NotificationsBell'
import { SearchBar, SearchIcon } from '@/components/SearchBar'
import { ChoreDetailsModal } from '@/components/ChoreDetailsModal'
import { UserDetailsModal } from '@/components/UserDetailsModal'
import { CompleteModal } from '@/components/CompleteModal'
import type { Chore, LeaderboardEntry } from '@/lib/types'
import { cn } from '@/lib/utils'

export function Layout() {
  const user = useAuthStore((s) => s.user)
  const clear = useAuthStore((s) => s.clear)
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [menuOpen, setMenuOpen] = useState(false)
  const [searchOpen, setSearchOpen] = useState(false)
  const [detailChore, setDetailChore] = useState<Chore | null>(null)
  const [completeChore, setCompleteChore] = useState<Chore | null>(null)
  const [detailUser, setDetailUser] = useState<LeaderboardEntry | null>(null)

  async function logout() {
    if (
      !(await confirm({
        title: 'Sign out',
        message: 'Are you sure you want to sign out?',
        confirmLabel: 'Sign out',
        variant: 'primary',
      }))
    )
      return
    setMenuOpen(false)
    try {
      await authApi.logout()
    } finally {
      clear()
      navigate('/')
    }
  }

  // Ctrl+K / Cmd+K opens search.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
        e.preventDefault()
        setSearchOpen((v) => !v)
      }
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [])

  const closeSearch = useCallback(() => setSearchOpen(false), [])

  // Open a chore's details from a notification (inbox click). Fetch fresh so it reflects the
  // current occurrence; silently ignore a chore that was since deleted.
  const openChoreById = useCallback(async (choreId: string) => {
    try {
      setDetailChore(await choresApi.get(choreId))
    } catch {
      /* chore no longer exists — nothing to open */
    }
  }, [])

  // Close the drawer on Escape; lock background scroll while it's open.
  useEffect(() => {
    if (!menuOpen) return
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setMenuOpen(false)
    document.addEventListener('keydown', onKey)
    document.body.style.overflow = 'hidden'
    return () => {
      document.removeEventListener('keydown', onKey)
      document.body.style.overflow = ''
    }
  }, [menuOpen])

  const isAdmin = user?.role === 'Admin'

  const tabs = [
    { to: '/chores', label: 'Chores', Icon: ChoresIcon },
    ...(isAdmin ? [{ to: '/users', label: 'Users', Icon: UsersIcon }] : []),
    { to: '/points', label: 'Points', Icon: PointsIcon },
    { to: '/achievements', label: 'Achievements', Icon: AchievementsIcon },
    { to: '/awards', label: 'Awards', Icon: AwardsIcon },
    { to: '/gacha', label: 'Gacha', Icon: GachaIcon },
    { to: '/history', label: 'History', Icon: HistoryIcon },
    { to: '/settings', label: 'Settings', Icon: SettingsIcon },
  ]

  const navItemClass = ({ isActive }: { isActive: boolean }) =>
    cn(
      'flex items-center gap-3 rounded-lg px-3 py-2 text-sm transition-colors',
      isActive
        ? 'bg-primary/10 font-semibold text-primary'
        : 'text-muted-foreground hover:bg-accent hover:text-foreground',
    )

  const navLinks = (onNavigate?: () => void) =>
    tabs.map(({ to, label, Icon }) => (
      <NavLink key={to} to={to} end onClick={onNavigate} className={navItemClass}>
        <Icon />
        {label}
      </NavLink>
    ))

  return (
    <div className="min-h-screen">
      {/* Sidebar (desktop) */}
      <aside className="fixed inset-y-0 left-0 z-30 hidden w-60 flex-col border-r border-border bg-sidebar md:flex">
        <div className="flex h-16 items-center px-5">
          <span className="text-xl font-semibold text-primary">Turnly</span>
        </div>
        <nav className="flex flex-1 flex-col gap-1 px-3 py-2">{navLinks()}</nav>
      </aside>

      {/* Main column */}
      <div className="flex min-h-screen flex-col md:pl-60">
        <header className="sticky top-0 z-20 flex h-16 items-center border-b border-border bg-card px-4 md:px-6">
          {/* Left: mobile menu + logo */}
          <div className="flex items-center gap-2 md:hidden">
            <button
              type="button"
              onClick={() => setMenuOpen(true)}
              aria-label="Open menu"
              className="-ml-1 rounded-md p-2 text-foreground hover:bg-accent"
            >
              <MenuIcon />
            </button>
            <span className="text-lg font-semibold text-primary">Turnly</span>
          </div>

          {/* Center: search */}
          <div className="flex flex-1 justify-center px-4">
            <button
              type="button"
              onClick={() => setSearchOpen(true)}
              aria-label="Search"
              className="flex w-full max-w-sm items-center gap-2 rounded-lg border border-border bg-card px-3 py-1.5 text-sm text-muted-foreground transition-colors hover:border-ring hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            >
              <SearchIcon />
              <span className="flex-1 text-left">Search…</span>
              <kbd className="hidden rounded border border-border px-1 py-0.5 text-xs sm:inline">⌘K</kbd>
            </button>
          </div>

          {/* Right: notifications + user menu */}
          <div className="flex items-center gap-1">
            <NotificationsBell onOpenChore={openChoreById} />
            <UserMenu onLogout={logout} />
          </div>
        </header>

        <main className="mx-auto w-full max-w-5xl flex-1 px-4 py-4 md:px-8 md:py-8">
          <Outlet />
        </main>
      </div>

      {/* Mobile navigation drawer (kept mounted so it can animate in and out) */}
      <div
        className={cn(
          'fixed inset-0 z-50 overflow-hidden md:hidden',
          menuOpen ? 'pointer-events-auto' : 'pointer-events-none',
        )}
        role="dialog"
        aria-modal="true"
        aria-hidden={!menuOpen}
      >
        <div
          className={cn(
            'absolute inset-0 bg-black/50 transition-opacity duration-300 motion-reduce:transition-none',
            menuOpen ? 'opacity-100' : 'opacity-0',
          )}
          onClick={() => setMenuOpen(false)}
        />
        <div
          className={cn(
            'absolute left-0 top-0 flex h-full w-72 max-w-[80%] flex-col border-r border-border bg-sidebar shadow-pop transition-transform duration-300 ease-in-out motion-reduce:transition-none',
            menuOpen ? 'translate-x-0' : '-translate-x-full',
          )}
        >
          <div className="flex h-16 items-center justify-between px-5">
            <span className="text-xl font-semibold text-primary">Turnly</span>
            <button
              type="button"
              onClick={() => setMenuOpen(false)}
              aria-label="Close menu"
              className="rounded-md p-2 text-muted-foreground hover:bg-accent hover:text-foreground"
            >
              <CloseIcon />
            </button>
          </div>
          <nav className="flex flex-col gap-1 px-3 py-2">{navLinks(() => setMenuOpen(false))}</nav>
        </div>
      </div>

      {/* Global search overlay */}
      <SearchBar
        open={searchOpen}
        onClose={closeSearch}
        onSelectChore={setDetailChore}
        onSelectUser={setDetailUser}
      />

      {detailChore && (
        <ChoreDetailsModal
          chore={detailChore}
          onClose={() => setDetailChore(null)}
          onComplete={() => { setCompleteChore(detailChore); setDetailChore(null) }}
        />
      )}

      {completeChore && (
        <CompleteModal
          chore={completeChore}
          onClose={() => setCompleteChore(null)}
          onDone={() => {
            setCompleteChore(null)
            void queryClient.invalidateQueries({ queryKey: ['chores'] })
          }}
        />
      )}

      {detailUser && (
        <UserDetailsModal
          userId={detailUser.id}
          displayName={detailUser.displayName}
          avatarColor={detailUser.avatarColor}
          points={detailUser.points}
          weeklyPoints={detailUser.weeklyPoints}
          onClose={() => setDetailUser(null)}
        />
      )}
    </div>
  )
}

function MenuIcon() {
  return (
    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
      <path d="M3 6h18M3 12h18M3 18h18" />
    </svg>
  )
}

function CloseIcon() {
  return (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
      <path d="M18 6 6 18M6 6l12 12" />
    </svg>
  )
}

function ChoresIcon() {
  return (
    <svg className="shrink-0" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M9 11l3 3L22 4" />
      <path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11" />
    </svg>
  )
}

function UsersIcon() {
  return (
    <svg className="shrink-0" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" />
      <circle cx="9" cy="7" r="4" />
      <path d="M22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75" />
    </svg>
  )
}

function PointsIcon() {
  return (
    <svg className="shrink-0" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z" />
    </svg>
  )
}

function SettingsIcon() {
  return (
    <svg className="shrink-0" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <circle cx="12" cy="12" r="3" />
      <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" />
    </svg>
  )
}

function AwardsIcon() {
  return (
    <svg className="shrink-0" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M6 9H4.5a2.5 2.5 0 0 1 0-5H6" />
      <path d="M18 9h1.5a2.5 2.5 0 0 0 0-5H18" />
      <path d="M4 22h16" />
      <path d="M10 14.66V17c0 .55-.47.98-.97 1.21C7.85 18.75 7 20.24 7 22" />
      <path d="M14 14.66V17c0 .55.47.98.97 1.21C16.15 18.75 17 20.24 17 22" />
      <path d="M18 2H6v7a6 6 0 0 0 12 0V2Z" />
    </svg>
  )
}

function AchievementsIcon() {
  return (
    <svg className="shrink-0" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <circle cx="12" cy="8" r="6" />
      <path d="M15.477 12.89 17 22l-5-3-5 3 1.523-9.11" />
    </svg>
  )
}

function HistoryIcon() {
  return (
    <svg className="shrink-0" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <circle cx="12" cy="12" r="10" />
      <polyline points="12 6 12 12 16 14" />
    </svg>
  )
}

function GachaIcon() {
  return (
    <svg className="shrink-0" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M20 12v9H4v-9" />
      <rect x="2" y="7" width="20" height="5" rx="1" />
      <path d="M12 22V7" />
      <path d="M12 7S10.5 2 7.5 2a2.5 2.5 0 0 0 0 5M12 7s1.5-5 4.5-5a2.5 2.5 0 0 1 0 5" />
    </svg>
  )
}
