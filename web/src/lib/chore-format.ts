import type {
  AssignmentStrategy,
  Chore,
  ChoreNotification,
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
export const MONTH_SHORT = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

// ── display helpers ──────────────────────────────────────────────────────────

export function repeatLabel(chore: Chore): string {
  if (chore.repeatType !== 'Custom') {
    return REPEAT_OPTIONS.find((o) => o.value === chore.repeatType)?.label ?? chore.repeatType
  }

  switch (chore.customMode) {
    case 'Interval': {
      const unit = (chore.intervalUnit ?? 'Week').toLowerCase()
      const n = chore.intervalCount ?? 1
      return n === 1 ? `Every ${unit}` : `Every ${n} ${unit}s`
    }
    case 'DaysOfWeek':
      return [...chore.weekdays]
        .sort((a, b) => WEEKDAY_ORDER.indexOf(a) - WEEKDAY_ORDER.indexOf(b))
        .map((d) => WEEKDAY_SHORT[d])
        .join(', ') || 'Days of week'
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

export function choreDueStatus(chore: Chore): 'overdue' | 'today' | 'upcoming' | 'later' {
  if (!chore.dueAt) return 'later'
  const due = new Date(chore.dueAt)
  const todayStart = startOfDay(new Date())
  const tomorrowStart = new Date(todayStart.getTime() + 86_400_000)
  const weekEnd = new Date(todayStart.getTime() + 7 * 86_400_000)
  if (due < todayStart) return 'overdue'
  if (due < tomorrowStart) return 'today'
  if (due <= weekEnd) return 'upcoming'
  return 'later'
}
