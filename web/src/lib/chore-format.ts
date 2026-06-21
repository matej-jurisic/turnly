import type {
  AssignmentStrategy,
  Chore,
  ChoreNotification,
  ChoreTrack,
  CustomRecurrenceMode,
  RepeatType,
  SchedulingPreference,
  Weekday,
} from '@/lib/types'

// ── option lists (for <Select>) ──────────────────────────────────────────────

export const REPEAT_OPTIONS: { value: RepeatType; label: string }[] = [
  { value: 'OneTime', label: 'One-time' },
  { value: 'Daily', label: 'Daily' },
  { value: 'Weekly', label: 'Weekly' },
  { value: 'Monthly', label: 'Monthly' },
  { value: 'Yearly', label: 'Yearly' },
  { value: 'Custom', label: 'Custom' },
]

export const STRATEGY_OPTIONS: { value: AssignmentStrategy; label: string }[] = [
  { value: 'KeepLastAssigned', label: 'Keep last assigned' },
  { value: 'RoundRobin', label: 'Round robin' },
  { value: 'Random', label: 'Random' },
  { value: 'RandomExceptLastAssigned', label: 'Random (except last)' },
  { value: 'LeastAssigned', label: 'Least assigned' },
  { value: 'LeastCompleted', label: 'Least completed' },
  { value: 'Independent', label: 'Everyone (independent)' },
]

export const SCHEDULING_OPTIONS: { value: SchedulingPreference; label: string }[] = [
  { value: 'FromScheduledDate', label: 'From scheduled date' },
  { value: 'FromCompletionDate', label: 'From completion date' },
  { value: 'ToFirstNextRepeat', label: 'To first next repeat' },
  { value: 'SmartScheduling', label: 'Smart scheduling' },
]

// Grace-window units for Smart scheduling, stored as a flat minute count. Months are approximated as
// 30 days — fine for a tolerance window.
export const GRACE_UNITS: { value: string; label: string; minutes: number }[] = [
  { value: 'Hour', label: 'hours', minutes: 60 },
  { value: 'Day', label: 'days', minutes: 60 * 24 },
  { value: 'Week', label: 'weeks', minutes: 60 * 24 * 7 },
  { value: 'Month', label: 'months', minutes: 60 * 24 * 30 },
]

/** Decompose a stored grace-in-minutes into a {value, unit} pair, picking the largest unit that
 * divides evenly so it round-trips the way the user most likely entered it. */
export function splitGrace(minutes: number | null | undefined): { value: number; unit: string } {
  if (!minutes || minutes <= 0) return { value: 1, unit: 'Day' }
  for (const u of [...GRACE_UNITS].reverse()) {
    if (minutes % u.minutes === 0) return { value: minutes / u.minutes, unit: u.value }
  }
  return { value: minutes, unit: 'Hour' } // never hit (60 divides all), keeps TS happy
}

/** Human-readable grace window, e.g. 2880 → "2 days". Strips a trailing "s" for singular amounts. */
export function formatGrace(minutes: number): string {
  const { value, unit } = splitGrace(minutes)
  const label = GRACE_UNITS.find((u) => u.value === unit)?.label ?? 'days'
  return `${value} ${value === 1 ? label.replace(/s$/, '') : label}`
}

/** Whether a repeat type / custom mode is interval-style (the only case where Smart scheduling
 * applies — fixed-slot DaysOfWeek/DaysOfMonth recurrences always hold their grid). */
export function isIntervalStyle(repeatType: RepeatType, customMode?: CustomRecurrenceMode | null): boolean {
  if (repeatType === 'OneTime') return false
  if (repeatType !== 'Custom') return true
  return customMode === 'Interval'
}

export const STRATEGY_LABELS: Record<string, string> = Object.fromEntries(
  STRATEGY_OPTIONS.map((o) => [o.value, o.label]),
)

export const SCHEDULING_LABELS: Record<string, string> = Object.fromEntries(
  SCHEDULING_OPTIONS.map((o) => [o.value, o.label]),
)

export const WEEKDAY_SHORT: Record<Weekday, string> = {
  Sunday: 'Sun', Monday: 'Mon', Tuesday: 'Tue', Wednesday: 'Wed',
  Thursday: 'Thu', Friday: 'Fri', Saturday: 'Sat',
}
export const WEEKDAY_ORDER: Weekday[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']
/** Labels for DaysOfWeek week-of-month occurrences: 1–4 for the nth, -1 for last. */
export const WEEK_OCCURRENCE_SHORT: Record<number, string> = {
  1: '1st', 2: '2nd', 3: '3rd', 4: '4th', [-1]: 'Last',
}
export const MONTH_SHORT = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

// ── display helpers ──────────────────────────────────────────────────────────

export function repeatLabel(chore: Chore): string {
  return baseRepeatLabel(chore) + timesOfDaySuffix(chore)
}

/** " · 08:00, 20:00" for a multi-time-a-day chore; empty otherwise. */
function timesOfDaySuffix(chore: Chore): string {
  if (!chore.timesOfDay || chore.timesOfDay.length < 2) return ''
  return ` · ${chore.timesOfDay.map(formatDueTime).join(', ')}`
}

function baseRepeatLabel(chore: Chore): string {
  if (chore.repeatType !== 'Custom') {
    return REPEAT_OPTIONS.find((o) => o.value === chore.repeatType)?.label ?? chore.repeatType
  }

  switch (chore.customMode) {
    case 'Interval': {
      const unit = (chore.intervalUnit ?? 'Week').toLowerCase()
      const n = chore.intervalCount ?? 1
      return n === 1 ? `Every ${unit}` : `Every ${n} ${unit}s`
    }
    case 'DaysOfWeek': {
      const days =
        [...chore.weekdays]
          .sort((a, b) => WEEKDAY_ORDER.indexOf(a) - WEEKDAY_ORDER.indexOf(b))
          .map((d) => WEEKDAY_SHORT[d])
          .join(', ') || 'Days of week'
      if (!chore.weeksOfMonth?.length) return days
      const weeks = [...chore.weeksOfMonth]
        .sort((a, b) => (a === -1 ? 99 : a) - (b === -1 ? 99 : b))
        .map((w) => WEEK_OCCURRENCE_SHORT[w] ?? w)
        .join(', ')
      return `${weeks} · ${days}`
    }
    case 'DaysOfMonth': {
      const days = [...chore.daysOfMonth].sort((a, b) => a - b).join(', ')
      const months = [...chore.months].sort((a, b) => a - b).map((m) => MONTH_SHORT[m - 1]).join(', ')
      return `Days ${days}${months ? ` · ${months}` : ''}`
    }
    default:
      return 'Custom'
  }
}

/** The period word for a repeat type, used in the completion-progress pill (e.g. "this week"). */
const PERIOD_LABELS: Partial<Record<RepeatType, string>> = {
  Daily: 'today',
  Weekly: 'this week',
  Monthly: 'this month',
  Yearly: 'this year',
}

/**
 * Per-occurrence completion progress, e.g. "0/3 this week". Falls back to "0/3 done" for repeat
 * types without a natural period (OneTime). Only meaningful when `completionsRequired > 1`.
 */
export function completionProgressLabel(chore: Chore): string {
  const period = PERIOD_LABELS[chore.repeatType] ?? 'done'
  return `${chore.occurrenceProgress ?? 0}/${chore.completionsRequired} ${period}`
}

/** Whether a chore uses per-assignee independent schedules (track mode). */
export function isIndependent(chore: Pick<Chore, 'assignmentStrategy'>): boolean {
  return chore.assignmentStrategy === 'Independent'
}

/** A track's own progress pill, e.g. "1/2 this week" (or "Done" once the quota is met). */
export function trackProgressLabel(chore: Chore, track: ChoreTrack): string {
  const period = PERIOD_LABELS[chore.repeatType] ?? 'done'
  if (track.completionsRequired <= 1) return ''
  return `${track.progress}/${track.completionsRequired} ${period}`
}

const NOTIFICATION_TYPE_LABELS = {
  Reminder: 'Reminder', Due: 'Due', FollowUp: 'Follow-up',
} as const
const NOTIFICATION_UNIT_LABELS = {
  Minutes: 'minute', Hours: 'hour', Days: 'day',
} as const

/** Short type label for a notification entry (e.g. "Follow-up"), used as a badge. */
export function notificationTypeLabel(n: ChoreNotification): string {
  return NOTIFICATION_TYPE_LABELS[n.type]
}

/** Human-readable timing phrase, e.g. "1 hour before due", "at due time", "2 days after due". */
export function notificationTimingLabel(n: ChoreNotification): string {
  if (n.timing === 'AtDue') return 'at due time'
  const unit = NOTIFICATION_UNIT_LABELS[n.offsetUnit]
  const plural = n.offsetValue === 1 ? '' : 's'
  const dir = n.timing === 'Before' ? 'before' : 'after'
  return `${n.offsetValue} ${unit}${plural} ${dir} due`
}

/** Recipient phrase for a notification entry. */
export function notificationRecipientsLabel(n: ChoreNotification): string {
  return n.recipients === 'AllAssignees' ? 'all assignees' : 'current assignee'
}

export function formatDate(iso?: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('en-GB')
}

/** Formats an "HH:mm" due time for display in 24-hour format (e.g. "09:00"); empty for no time. */
export function formatDueTime(time?: string | null): string {
  if (!time) return ''
  const [hh, mm] = time.split(':').map(Number)
  return new Date(2000, 0, 1, hh, mm).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })
}

/**
 * The time-of-day to show next to a chore's due date. For "N times a day" chores the stored
 * `dueTime` is only the earliest slot, so the actual next slot is read off the `dueAt` instant;
 * single-slot chores keep using the stored local time (avoids any timezone drift on the instant).
 */
export function nextDueTimeLabel(chore: Pick<Chore, 'timesOfDay' | 'dueTime' | 'dueAt'>): string {
  if ((chore.timesOfDay?.length ?? 0) > 1 && chore.dueAt) {
    return new Date(chore.dueAt).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })
  }
  return formatDueTime(chore.dueTime)
}

/**
 * Builds an offset-preserving local ISO instant from a date (YYYY-MM-DD) + optional time (HH:mm).
 * Keeping the browser's UTC offset (rather than `toISOString()`, which collapses to UTC) is what
 * makes the chore land on the right calendar day in the user's timezone and keeps the recurrence
 * engine's time-of-day correct. No time → end of the local day, so it stays "due today" all day.
 */
export function toLocalDueInstant(dateStr: string, timeStr: string): string {
  const [y, m, d] = dateStr.split('-').map(Number)
  const [hh, mm] = timeStr ? timeStr.split(':').map(Number) : [23, 59]
  const dt = new Date(y, m - 1, d, hh, mm, timeStr ? 0 : 59, 0)
  const pad = (n: number) => String(Math.abs(n)).padStart(2, '0')
  const off = -dt.getTimezoneOffset()
  const sign = off >= 0 ? '+' : '-'
  return (
    `${dt.getFullYear()}-${pad(dt.getMonth() + 1)}-${pad(dt.getDate())}` +
    `T${pad(dt.getHours())}:${pad(dt.getMinutes())}:${pad(dt.getSeconds())}` +
    `${sign}${pad(Math.trunc(Math.abs(off) / 60))}:${pad(Math.abs(off) % 60)}`
  )
}

function startOfDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate())
}

/** Whole calendar days between `iso` and today (positive = in the past). */
export function calendarDaysAgo(iso: string): number {
  return Math.round((startOfDay(new Date()).getTime() - startOfDay(new Date(iso)).getTime()) / 86_400_000)
}

/** Relative day label: "today", "yesterday", "N days ago", or "in N days". */
export function relativeDayLabel(iso: string): string {
  const d = calendarDaysAgo(iso)
  if (d === 0) return 'today'
  if (d === 1) return 'yesterday'
  return d > 0 ? `${d} days ago` : `in ${-d} days`
}

export function dueStatus(dueAt?: string | null): 'overdue' | 'today' | 'upcoming' | 'later' {
  if (!dueAt) return 'later'
  const due = new Date(dueAt)
  const todayStart = startOfDay(new Date())
  const tomorrowStart = new Date(todayStart.getTime() + 86_400_000)
  const weekEnd = new Date(todayStart.getTime() + 7 * 86_400_000)
  if (due < todayStart) return 'overdue'
  if (due < tomorrowStart) return 'today'
  if (due <= weekEnd) return 'upcoming'
  return 'later'
}

export function choreDueStatus(chore: Chore): 'overdue' | 'today' | 'upcoming' | 'later' {
  return dueStatus(chore.dueAt)
}

/** Whether a track owner has completed their current obligation (next occurrence is in the future).
 * A not-yet-started track (future start date, no activity) is scheduled — not done. */
export function trackIsDone(track: ChoreTrack): boolean {
  if (!track.started) return false
  const s = dueStatus(track.dueAt)
  return s === 'upcoming' || s === 'later'
}

/** Short status phrase for one assignee's track, e.g. "done · next in 3 days", "due today", "overdue",
 * "due in 6 days" (a not-yet-started future occurrence). */
export function trackStatusText(chore: Chore, track: ChoreTrack): string {
  const progress = trackProgressLabel(chore, track)
  const s = dueStatus(track.dueAt)
  const base =
    s === 'overdue' ? `overdue${track.dueAt ? ` · was due ${relativeDayLabel(track.dueAt)}` : ''}`
    : s === 'today' ? 'due today'
    : !track.dueAt ? 'done'
    : track.started ? `done · next ${relativeDayLabel(track.dueAt)}`
    : `due ${relativeDayLabel(track.dueAt)}`
  return progress ? `${progress} · ${base}` : base
}
