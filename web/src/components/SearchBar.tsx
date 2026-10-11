import { useEffect, useRef, useState } from 'react'
import type { KeyboardEvent as ReactKeyboardEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { choresApi, usersApi } from '@/lib/api'
import type { Chore, LeaderboardEntry } from '@/lib/types'
import { Avatar } from '@/components/ui/Modal'
import { cn } from '@/lib/utils'
import { pairLabel } from '@/lib/chore-format'
import { t } from '@/lib/i18n'

interface SearchBarProps {
  open: boolean
  onClose: () => void
  onSelectChore: (chore: Chore) => void
  onSelectUser: (user: LeaderboardEntry) => void
}

type Result =
  | { type: 'chore'; item: Chore }
  | { type: 'user'; item: LeaderboardEntry }

export function SearchBar({ open, onClose, onSelectChore, onSelectUser }: SearchBarProps) {
  const [query, setQuery] = useState('')
  const [activeIndex, setActiveIndex] = useState(-1)
  const inputRef = useRef<HTMLInputElement>(null)

  // Re-use cached data so no extra fetches when search opens.
  const { data: chores = [] } = useQuery({ queryKey: ['chores'], queryFn: choresApi.list })
  const { data: leaderboard = [] } = useQuery({ queryKey: ['leaderboard'], queryFn: usersApi.leaderboard })

  // Focus input when opened; reset when closed.
  useEffect(() => {
    if (open) {
      setTimeout(() => inputRef.current?.focus(), 30)
    } else {
      setQuery('')
      setActiveIndex(-1)
    }
  }, [open])

  // Prevent background scroll while open.
  useEffect(() => {
    document.body.style.overflow = open ? 'hidden' : ''
    return () => { document.body.style.overflow = '' }
  }, [open])

  // Escape to close.
  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [open, onClose])

  const q = query.trim().toLowerCase()

  const choreResults: Chore[] = q
    ? chores.filter(
        (c) =>
          c.name.toLowerCase().includes(q) ||
          (c.description ?? '').toLowerCase().includes(q) ||
          c.tags.some((t) => t.toLowerCase().includes(q)),
      ).slice(0, 5)
    : []

  const userResults: LeaderboardEntry[] = q
    ? leaderboard.filter((u) => u.displayName.toLowerCase().includes(q)).slice(0, 3)
    : []

  const results: Result[] = [
    ...choreResults.map((c): Result => ({ type: 'chore', item: c })),
    ...userResults.map((u): Result => ({ type: 'user', item: u })),
  ]

  // Reset active index on query change.
  useEffect(() => { setActiveIndex(-1) }, [query])

  function select(r: Result) {
    onClose()
    if (r.type === 'chore') onSelectChore(r.item)
    else onSelectUser(r.item)
  }

  function handleKeyDown(e: ReactKeyboardEvent<HTMLInputElement>) {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setActiveIndex((i) => Math.min(i + 1, results.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActiveIndex((i) => Math.max(i - 1, -1))
    } else if (e.key === 'Enter' && activeIndex >= 0 && results[activeIndex]) {
      e.preventDefault()
      select(results[activeIndex])
    }
  }

  if (!open) return null

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center bg-black/50 px-4 pt-14"
      onClick={onClose}
    >
      <div
        className="w-full max-w-lg overflow-hidden rounded-xl border border-border bg-card shadow-pop"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Input row */}
        <div className="flex items-center gap-2 border-b border-border px-4 py-3">
          <SearchIcon />
          <input
            ref={inputRef}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder={t('search.placeholder')}
            className="flex-1 bg-transparent text-sm text-foreground placeholder:text-muted-foreground focus:outline-none"
          />
          {query && (
            <button
              type="button"
              onClick={() => setQuery('')}
              className="text-muted-foreground hover:text-foreground"
              aria-label={t('history.clear')}
            >
              <CloseIcon />
            </button>
          )}
          <kbd className="hidden rounded border border-border px-1.5 py-0.5 text-xs text-muted-foreground sm:inline">
            esc
          </kbd>
        </div>

        {/* Results */}
        {q ? (
          results.length > 0 ? (
            <ul className="max-h-80 overflow-y-auto py-1" role="listbox">
              {choreResults.length > 0 && (
                <li>
                  <p className="px-4 py-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">
                    {t('nav.chores')}
                  </p>
                  <ul>
                    {choreResults.map((chore, i) => {
                      const idx = i
                      return (
                        <li key={chore.id} role="option" aria-selected={activeIndex === idx}>
                          <button
                            type="button"
                            onClick={() => select({ type: 'chore', item: chore })}
                            onMouseEnter={() => setActiveIndex(idx)}
                            className={cn(
                              'flex w-full items-center gap-3 px-4 py-2.5 text-left text-sm transition-colors',
                              activeIndex === idx ? 'bg-accent' : 'hover:bg-accent',
                            )}
                          >
                            <span className="shrink-0 text-base leading-none">
                              {chore.emoji ?? '📋'}
                            </span>
                            <span className="flex-1 font-medium text-foreground">{chore.name}</span>
                            {chore.currentAssignee && (
                              <span className="text-xs text-muted-foreground">
                                {pairLabel(chore.currentAssignee, chore.currentPartner)}
                              </span>
                            )}
                          </button>
                        </li>
                      )
                    })}
                  </ul>
                </li>
              )}

              {userResults.length > 0 && (
                <li>
                  <p className="px-4 py-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">
                    {t('search.people')}
                  </p>
                  <ul>
                    {userResults.map((user, i) => {
                      const idx = choreResults.length + i
                      return (
                        <li key={user.id} role="option" aria-selected={activeIndex === idx}>
                          <button
                            type="button"
                            onClick={() => select({ type: 'user', item: user })}
                            onMouseEnter={() => setActiveIndex(idx)}
                            className={cn(
                              'flex w-full items-center gap-3 px-4 py-2.5 text-left text-sm transition-colors',
                              activeIndex === idx ? 'bg-accent' : 'hover:bg-accent',
                            )}
                          >
                            <Avatar color={user.avatarColor} name={user.displayName} size={24} frame={user.equippedFrameKey} emoji={user.avatarEmoji} />
                            <span className="flex-1 font-medium text-foreground">{user.displayName}</span>
                            <span className="text-xs text-muted-foreground">{t('common.pts', { count: user.points })}</span>
                          </button>
                        </li>
                      )
                    })}
                  </ul>
                </li>
              )}
            </ul>
          ) : (
            <p className="px-4 py-6 text-center text-sm text-muted-foreground">
              {t('search.noResults', { query })}
            </p>
          )
        ) : (
          <p className="px-4 py-6 text-center text-sm text-muted-foreground">
            {t('search.hint')}
          </p>
        )}

        {/* Footer hints */}
        <div className="flex items-center gap-4 border-t border-border px-4 py-2 text-xs text-muted-foreground">
          <span><kbd className="font-sans">↑↓</kbd> {t('search.navigate')}</span>
          <span><kbd className="font-sans">↵</kbd> {t('search.open')}</span>
          <span><kbd className="font-sans">esc</kbd> {t('search.close')}</span>
        </div>
      </div>
    </div>
  )
}

function SearchIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="shrink-0 text-muted-foreground" aria-hidden="true">
      <circle cx="11" cy="11" r="8" />
      <path d="m21 21-4.35-4.35" />
    </svg>
  )
}

function CloseIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
      <path d="M18 6 6 18M6 6l12 12" />
    </svg>
  )
}

// Re-export for use in Layout
export { SearchIcon }
