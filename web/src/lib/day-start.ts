import { useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import { create } from 'zustand'
import { settingsApi } from '@/lib/api'

const STORAGE_KEY = 'turnly-day-start'
const MIDNIGHT = '00:00'

function stored(): string {
  try {
    return localStorage.getItem(STORAGE_KEY) ?? MIDNIGHT
  } catch {
    return MIDNIGHT
  }
}

interface DayStartState {
  /** "HH:mm": when the household's day rolls over. "00:00" is plain midnight. */
  dayStart: string
  setDayStart: (next: string) => void
}

/**
 * The household's day start (an app setting, mirrored here so the pure date helpers can read it).
 * Like the language it is read imperatively, so `App` keys its tree on it and remounts on a change.
 * The last known value is kept in localStorage so the first paint already buckets chores correctly.
 */
export const useDayStartStore = create<DayStartState>((set) => ({
  dayStart: stored(),
  setDayStart: (next) => {
    try {
      localStorage.setItem(STORAGE_KEY, next)
    } catch {
      // Not persisted, but still applies for this session.
    }
    set({ dayStart: next })
  },
}))

/** The day start as milliseconds past midnight. */
export function dayStartMs(): number {
  const [hh, mm] = useDayStartStore.getState().dayStart.split(':').map(Number)
  return ((hh || 0) * 60 + (mm || 0)) * 60_000
}

/**
 * "Now" on the household clock: with a 05:00 day start, 01:00 on Tuesday is still Monday. Take the
 * calendar day off this instead of `new Date()` wherever "today" decides what is due or overdue.
 */
export function householdNow(): Date {
  return new Date(Date.now() - dayStartMs())
}

/** Keeps the store in step with the server setting. Mounted once, in `Layout`. */
export function useDayStartSync(): void {
  const { data } = useQuery({ queryKey: ['app-settings'], queryFn: settingsApi.get })
  const dayStart = data?.dayStart
  useEffect(() => {
    if (dayStart && dayStart !== useDayStartStore.getState().dayStart)
      useDayStartStore.getState().setDayStart(dayStart)
  }, [dayStart])
}
