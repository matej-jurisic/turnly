import type {
  ChoreHistoryEntry,
  AssignmentStrategy,
  Chore,
  ChoreNotification,
  ChoreOffer,
  ChoreTrack,
  CustomRecurrenceMode,
  RepeatType,
  RotationPeriod,
  SchedulingPreference,
  User,
  Weekday,
} from '@/lib/types'
import { locale, localized, plural, t } from '@/lib/i18n'
import type { TKey } from '@/lib/i18n'
import { dayStartMs } from '@/lib/day-start'

// ── option lists (for <Select>) ──────────────────────────────────────────────

// Labels are getters (see `localized`), so these module-level tables follow the active language.
const option = <V extends string>(value: V, label: TKey) => localized({ value }, { label })

export const REPEAT_OPTIONS: { value: RepeatType; label: string }[] = [
  option('OneTime', 'repeat.OneTime'),
  option('OnDemand', 'repeat.OnDemand'),
  option('Daily', 'repeat.Daily'),
  option('Weekly', 'repeat.Weekly'),
  option('Monthly', 'repeat.Monthly'),
  option('Yearly', 'repeat.Yearly'),
  option('Custom', 'repeat.Custom'),
]

export const STRATEGY_OPTIONS: { value: AssignmentStrategy; label: string }[] = [
  option('KeepLastAssigned', 'strategy.KeepLastAssigned'),
  option('RoundRobin', 'strategy.RoundRobin'),
  option('Random', 'strategy.Random'),
  option('RandomExceptLastAssigned', 'strategy.RandomExceptLastAssigned'),
  option('LeastAssigned', 'strategy.LeastAssigned'),
  option('LeastCompleted', 'strategy.LeastCompleted'),
  option('Independent', 'strategy.Independent'),
  option('Manual', 'strategy.Manual'),
  option('Anyone', 'strategy.Anyone'),
]

interface RotationPeriodOption {
  value: RotationPeriod
  label: string
  span?: string
  repeats: RepeatType[]
}

function rotationOption(value: RotationPeriod, repeats: RepeatType[], label: TKey, span?: TKey): RotationPeriodOption {
  const item: RotationPeriodOption = localized({ value, repeats }, { label })
  return span ? localized(item, { span }) : item
}

/** `span` is the sentence explaining the stretch one person holds the chore for; `repeats` lists the
 *  repeat types that can come due more than once inside it (mirrors `RotationBoundary.AppliesTo` on
 *  the backend). */
export const ROTATION_PERIOD_OPTIONS: RotationPeriodOption[] = [
  rotationOption('EveryOccurrence', ['Daily', 'Weekly', 'Monthly', 'Custom'], 'rotation.EveryOccurrence'),
  rotationOption('Daily', ['Daily', 'Custom'], 'rotation.Daily', 'rotation.span.Daily'),
  rotationOption('Weekly', ['Daily', 'Custom'], 'rotation.Weekly', 'rotation.span.Weekly'),
  rotationOption('Monthly', ['Daily', 'Weekly', 'Custom'], 'rotation.Monthly', 'rotation.span.Monthly'),
  rotationOption('Quarterly', ['Daily', 'Weekly', 'Monthly', 'Custom'], 'rotation.Quarterly', 'rotation.span.Quarterly'),
  rotationOption('Yearly', ['Daily', 'Weekly', 'Monthly', 'Custom'], 'rotation.Yearly', 'rotation.span.Yearly'),
]

/** The rotation periods that can apply: the chore has to rotate, and come due more often than the
 *  period. Empty when there's nothing to choose (only "every occurrence" would be left). */
export function rotationPeriodOptions(repeatType: RepeatType, strategy: AssignmentStrategy) {
  if (strategy === 'Independent' || strategy === 'KeepLastAssigned' || strategy === 'Anyone') return []
  const options = ROTATION_PERIOD_OPTIONS.filter((o) => o.repeats.includes(repeatType))
  return options.length > 1 ? options : []
}

export const SCHEDULING_OPTIONS: { value: SchedulingPreference; label: string }[] = [
  option('FromScheduledDate', 'scheduling.FromScheduledDate'),
  option('FromCompletionDate', 'scheduling.FromCompletionDate'),
  option('ToFirstNextRepeat', 'scheduling.ToFirstNextRepeat'),
  option('SmartScheduling', 'scheduling.SmartScheduling'),
]

// Grace-window units for Smart scheduling, stored as a flat minute count. Months are approximated as
// 30 days — fine for a tolerance window.
export const GRACE_UNITS: { value: string; label: string; minutes: number }[] = [
  localized({ value: 'Hour', minutes: 60 }, { label: 'unit.hours' }),
  localized({ value: 'Day', minutes: 60 * 24 }, { label: 'unit.days' }),
  localized({ value: 'Week', minutes: 60 * 24 * 7 }, { label: 'unit.weeks' }),
  localized({ value: 'Month', minutes: 60 * 24 * 30 }, { label: 'unit.months' }),
]

const GRACE_DURATION_KEYS = {
  Hour: 'duration.hour', Day: 'duration.day', Week: 'duration.week', Month: 'duration.month',
} as const

/** Decompose a stored grace-in-minutes into a {value, unit} pair, picking the largest unit that
 * divides evenly so it round-trips the way the user most likely entered it. */
export function splitGrace(minutes: number | null | undefined): { value: number; unit: string } {
  if (!minutes || minutes <= 0) return { value: 1, unit: 'Day' }
  for (const u of [...GRACE_UNITS].reverse()) {
    if (minutes % u.minutes === 0) return { value: minutes / u.minutes, unit: u.value }
  }
  return { value: minutes, unit: 'Hour' } // never hit (60 divides all), keeps TS happy
}

/** Human-readable grace window, e.g. 2880 → "2 days". */
export function formatGrace(minutes: number): string {
  const { value, unit } = splitGrace(minutes)
  return plural(GRACE_DURATION_KEYS[unit as keyof typeof GRACE_DURATION_KEYS] ?? 'duration.day', value)
}

/** Whether a repeat type / custom mode is interval-style (the only case where Smart scheduling
 * applies — fixed-slot DaysOfWeek/DaysOfMonth recurrences always hold their grid). */
export function isIntervalStyle(repeatType: RepeatType, customMode?: CustomRecurrenceMode | null): boolean {
  if (repeatType === 'OneTime' || repeatType === 'OnDemand') return false
  if (repeatType !== 'Custom') return true
  return customMode === 'Interval'
}

/** Display name of an enum value, falling back to the raw value for one this build doesn't know. */
export function strategyLabel(value: string): string {
  return STRATEGY_OPTIONS.find((o) => o.value === value)?.label ?? value
}

export function rotationPeriodLabel(value: string): string {
  return ROTATION_PERIOD_OPTIONS.find((o) => o.value === value)?.label ?? value
}

export function schedulingLabel(value: string): string {
  return SCHEDULING_OPTIONS.find((o) => o.value === value)?.label ?? value
}

export function weekdayShort(day: Weekday): string {
  return t(`weekday.short.${day}`)
}
export const WEEKDAY_ORDER: Weekday[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']
/** Label for a DaysOfWeek week-of-month occurrence: 1–4 for the nth, -1 for last. */
export function weekOccurrenceShort(week: number): string {
  if (week === -1) return t('weekOccurrence.last')
  return week >= 1 && week <= 4 ? t(`weekOccurrence.${week as 1 | 2 | 3 | 4}`) : String(week)
}
/** Short month name for a 1-based month number. */
export function monthShort(month: number): string {
  return month >= 1 && month <= 12 ? t(`month.short.${month}` as TKey) : String(month)
}

// ── display helpers ──────────────────────────────────────────────────────────

export function repeatLabel(chore: Chore): string {
  return baseRepeatLabel(chore) + timesOfDaySuffix(chore)
}

/** `repeatLabel` for tight spots (the list pill): labelled slots collapse to a count ("Daily · 2×")
 * since slot names are free text and the due pill already names the current one. */
export function compactRepeatLabel(chore: Chore): string {
  const times = chore.timesOfDay ?? []
  if (times.length === 2 && times.some((t) => chore.timeLabels?.[t])) {
    return `${baseRepeatLabel(chore)} · ${times.length}×`
  }
  return repeatLabel(chore)
}

/** " · 08:00, 20:00" for two times (a labelled slot shows its name instead: " · Morning, Evening");
 * " · 3×" for three or more. */
function timesOfDaySuffix(chore: Chore): string {
  if (!chore.timesOfDay || chore.timesOfDay.length < 2) return ''
  if (chore.timesOfDay.length === 2) {
    return ` · ${chore.timesOfDay.map((t) => chore.timeLabels?.[t] || formatDueTime(t)).join(', ')}`
  }
  return ` · ${chore.timesOfDay.length}×`
}

function baseRepeatLabel(chore: Chore): string {
  if (chore.repeatType !== 'Custom') {
    return REPEAT_OPTIONS.find((o) => o.value === chore.repeatType)?.label ?? chore.repeatType
  }

  switch (chore.customMode) {
    case 'Interval': {
      const unit = chore.intervalUnit ?? 'Week'
      const n = chore.intervalCount ?? 1
      return n === 1 ? t(`repeat.every.${unit}`) : plural(`repeat.everyN.${unit}`, n)
    }
    case 'DaysOfWeek': {
      const days =
        [...chore.weekdays]
          .sort((a, b) => WEEKDAY_ORDER.indexOf(a) - WEEKDAY_ORDER.indexOf(b))
          .map(weekdayShort)
          .join(', ') || t('repeat.daysOfWeek')
      if (!chore.weeksOfMonth?.length) return days
      const weeks = [...chore.weeksOfMonth]
        .sort((a, b) => (a === -1 ? 99 : a) - (b === -1 ? 99 : b))
        .map(weekOccurrenceShort)
        .join(', ')
      return `${weeks} · ${days}`
    }
    case 'DaysOfMonth': {
      const days = [...chore.daysOfMonth].sort((a, b) => a - b).join(', ')
      const months = [...chore.months].sort((a, b) => a - b).map(monthShort).join(', ')
      return `${t('repeat.daysOfMonth', { days })}${months ? ` · ${months}` : ''}`
    }
    default:
      return t('repeat.Custom')
  }
}

/** The period word for a repeat type, used in the completion-progress pill (e.g. "this week"). */
function periodLabel(repeatType: RepeatType): string {
  switch (repeatType) {
    case 'Daily':
    case 'Weekly':
    case 'Monthly':
    case 'Yearly':
      return t(`period.${repeatType}`)
    default:
      return t('period.done')
  }
}

/**
 * Per-occurrence completion progress, e.g. "0/3 this week". Falls back to "0/3 done" for repeat
 * types without a natural period (OneTime). Only meaningful when `completionsRequired > 1`.
 */
export function completionProgressLabel(chore: Chore): string {
  const period = periodLabel(chore.repeatType)
  return `${chore.occurrenceProgress ?? 0}/${chore.completionsRequired} ${period}`
}

/** Whether a chore has no schedule at all: never due, done whenever it's needed. */
export function isOnDemand(chore: Pick<Chore, 'repeatType'>): boolean {
  return chore.repeatType === 'OnDemand'
}

/** Whether a chore has something to complete right now: a due occurrence, or an on-demand chore,
 * which always does. */
export function canComplete(chore: Pick<Chore, 'repeatType' | 'dueAt' | 'isFrozen'>): boolean {
  return (Boolean(chore.dueAt) || isOnDemand(chore)) && !chore.isFrozen
}

/** Whether a chore uses per-assignee independent schedules (track mode). */
export function isIndependent(chore: Pick<Chore, 'assignmentStrategy'>): boolean {
  return chore.assignmentStrategy === 'Independent'
}

/** Whether nobody holds a chore and any assignee can do it. */
export function isAnyone(chore: Pick<Chore, 'assignmentStrategy'>): boolean {
  return chore.assignmentStrategy === 'Anyone'
}

/** Whoever can take a turn on a chore: each fixed pair, then each assignee outside every pair,
 * alone. A pair with a frozen member is away, so it doesn't count. */
export function turnUnits(chore: Pick<Chore, 'pairs' | 'assignees'>): { user: User; partner?: User }[] {
  const active = (id: string) => chore.assignees.find((u) => u.id === id && !u.isFrozen)
  const pairs = chore.pairs.flatMap(([a, b]) => {
    const user = active(a)
    const partner = active(b)
    return user && partner ? [{ user, partner }] : []
  })
  const alone = chore.assignees.filter((u) => !pairs.some((p) => p.user.id === u.id || p.partner.id === u.id))
  return [...pairs, ...alone.map((user) => ({ user }))]
}

/** Who shares the credit when `userId` completes the chore. Mirrors `Chore.CreditedPartner`: a holder
 * shares it with their co-holder, anyone else brings their partner only when they are in exactly one
 * live pair. Null when they are credited alone. */
export function creditedPartner(
  chore: Pick<Chore, 'pairs' | 'assignees' | 'currentAssignee' | 'currentPartner'>,
  userId: string,
): User | null {
  const active = (id?: string | null) => chore.assignees.find((u) => u.id === id && !u.isFrozen) ?? null
  if (userId === chore.currentAssignee?.id) return active(chore.currentPartner?.id)
  if (userId === chore.currentPartner?.id) return active(chore.currentAssignee?.id)
  const mine = chore.pairs.filter((p) => p.includes(userId) && active(p[0]) && active(p[1]))
  return mine.length === 1 ? active(mine[0].find((id) => id !== userId)) : null
}

/** "Ana & Ben" for a pair, or just the one name. */
export function pairLabel(user: Pick<User, 'displayName'>, partner?: Pick<User, 'displayName'> | null): string {
  return peopleLabel(partner ? [user, partner] : [user])
}

/** "Ana, Ben & Cy": everyone credited with something, in the same style as `pairLabel`. */
export function peopleLabel(people: Pick<User, 'displayName'>[]): string {
  const names = people.map((u) => u.displayName)
  return names.length > 1 ? `${names.slice(0, -1).join(', ')} & ${names[names.length - 1]}` : names[0] ?? ''
}

/** Everyone a logged completion credits: the completer, their partner and any helpers. */
export function creditedPeople<T extends User>(entry: { actor?: T | null; partner?: T | null; helpers?: T[] }): T[] {
  return [entry.actor, entry.partner, ...(entry.helpers ?? [])].filter((u): u is T => !!u)
}

/** Whether this person holds the current occurrence: the current assignee, or their partner on a
 * paired chore. */
export function holdsChore(chore: Pick<Chore, 'currentAssignee' | 'currentPartner'>, userId?: string | null): boolean {
  return Boolean(userId) && (chore.currentAssignee?.id === userId || chore.currentPartner?.id === userId)
}

/** Who the viewer can nudge about this chore: whoever owes the current occurrence (the assignee and
 * their partner, every assignee on an Anyone chore, or everyone with an open track on a per-person
 * chore), minus the viewer and anyone
 * on leave. Mirrors `ChoreService.NudgeAsync`; the server still has the final say. */
export function nudgeCandidates(chore: Chore, meId?: string | null): User[] {
  if (chore.isFrozen) return []
  const holders = isIndependent(chore)
    ? chore.tracks.filter((tr) => tr.dueAt).map((tr) => tr.user)
    : !chore.dueAt && !isOnDemand(chore)
      ? []
      : isAnyone(chore)
        ? chore.assignees
        : [chore.currentAssignee, chore.currentPartner].filter((u): u is User => Boolean(u))
  return holders.filter((u) => u.id !== meId && !u.isFrozen)
}

/** Whether to surface a chore's on-time streak: only for recurring chores with a streak worth
 * showing (a streak of 1 isn't). For track-mode chores this reflects the viewer's own streak. */
export function showStreak(chore: Pick<Chore, 'repeatType' | 'currentStreak'>): boolean {
  return chore.repeatType !== 'OneTime' && chore.repeatType !== 'OnDemand' && chore.currentStreak >= 2
}

/** A track's own progress pill, e.g. "1/2 this week" (or "Done" once the quota is met). */
export function trackProgressLabel(chore: Chore, track: ChoreTrack): string {
  const period = periodLabel(chore.repeatType)
  if (track.completionsRequired <= 1) return ''
  return `${track.progress}/${track.completionsRequired} ${period}`
}

/** Full sentence describing a live offer, used as the badge tooltip / accessible label. */
export function offerSentence(offer: ChoreOffer, meId?: string): string {
  return offer.fromUser.id === meId
    ? t('offer.sentence.youOpen')
    : t('offer.sentence.nameOpen', { from: offer.fromUser.displayName })
}

const NOTIFICATION_UNIT_KEYS = {
  Minutes: 'duration.minute', Hours: 'duration.hour', Days: 'duration.day',
} as const

/** Short type label for a notification entry (e.g. "Follow-up"), used as a badge. */
export function notificationTypeLabel(n: ChoreNotification): string {
  return t(`notifType.${n.type}`)
}

/** Human-readable timing phrase, e.g. "1 hour before due", "at due time", "2 days after due". */
export function notificationTimingLabel(n: ChoreNotification): string {
  if (n.timing === 'AtDue') return t('notifTiming.atDue')
  const duration = plural(NOTIFICATION_UNIT_KEYS[n.offsetUnit], n.offsetValue)
  return t(n.timing === 'Before' ? 'notifTiming.before' : 'notifTiming.after', { duration })
}

/** Recipient phrase for a notification entry. */
export function notificationRecipientsLabel(n: ChoreNotification): string {
  return n.recipients === 'AllAssignees' ? t('notifRecipients.everyone') : t('notifRecipients.assignee')
}

export function formatDate(iso?: string | null): string {
  if (!iso) return '-'
  return new Date(iso).toLocaleDateString(locale())
}

/** `formatDate` without the year when it's the current one ("10/10"), for the due pills. */
export function formatDueDate(iso?: string | null): string {
  if (!iso) return '-'
  const d = new Date(iso)
  if (d.getFullYear() !== new Date().getFullYear()) return formatDate(iso)
  return d.toLocaleDateString(locale(), { day: '2-digit', month: '2-digit' })
}

/** Short weekday of a due date in the current language, capitalized ("Mon", "Pon"). */
export function formatDueWeekday(iso: string): string {
  const day = new Date(iso).toLocaleDateString(locale(), { weekday: 'short' }).replace(/\.$/, '')
  return day.charAt(0).toLocaleUpperCase(locale()) + day.slice(1)
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
 * A labelled slot leads with its name ("Morning 08:00").
 */
export function nextDueTimeLabel(chore: Pick<Chore, 'timesOfDay' | 'dueTime' | 'dueAt' | 'dueLabel'>): string {
  if ((chore.timesOfDay?.length ?? 0) > 1 && chore.dueAt) {
    const time = new Date(chore.dueAt).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })
    return chore.dueLabel ? `${chore.dueLabel} ${time}` : time
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

/** Formats a Date as a local `YYYY-MM-DD` string for a `<input type="date">` (not UTC). */
export function toLocalDateInput(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

function startOfDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate())
}

/** The calendar day an instant belongs to on the household clock (see `householdNow`). */
function householdDay(d: Date): Date {
  return startOfDay(new Date(d.getTime() - dayStartMs()))
}

/** Whole household days between `iso` and today (positive = in the past). */
export function calendarDaysAgo(iso: string): number {
  return Math.round((householdDay(new Date()).getTime() - householdDay(new Date(iso)).getTime()) / 86_400_000)
}

/** Relative day label: "today", "yesterday", "N days ago", or "in N days". */
export function relativeDayLabel(iso: string): string {
  const d = calendarDaysAgo(iso)
  if (d === 0) return t('relative.today')
  if (d === 1) return t('relative.yesterday')
  return d > 0 ? plural('relative.daysAgo', d) : plural('relative.inDays', -d)
}

/** Compact "how long ago" for activity feeds: minutes, hours, then days, then the plain date. */
export function relativeTimeLabel(iso: string): string {
  const mins = Math.floor((Date.now() - new Date(iso).getTime()) / 60_000)
  if (mins < 1) return t('time.justNow')
  if (mins < 60) return t('time.minutesAgo', { count: mins })
  const hours = Math.floor(mins / 60)
  if (hours < 24) return t('time.hoursAgo', { count: hours })
  const days = Math.floor(hours / 24)
  if (days < 7) return t('time.daysAgo', { count: days })
  return formatDate(iso)
}

/** The occurrence a history entry was for: its due date, the slot's time on timed chores, and the
 * slot label when it has one ("18/10/2026 08:00 · Morning"). Empty for entries with no occurrence. */
export function occurrenceLabel(
  entry: Pick<ChoreHistoryEntry, 'occurrenceDueAt' | 'occurrenceLabel'>,
  hasDueTime: boolean,
): string {
  if (!entry.occurrenceDueAt) return ''
  let label = formatDate(entry.occurrenceDueAt)
  if (hasDueTime) {
    label += ` ${new Date(entry.occurrenceDueAt).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })}`
  }
  return entry.occurrenceLabel ? `${label} · ${entry.occurrenceLabel}` : label
}

/** Full date and time, for tooltips. */
export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString(locale())
}

/** Whether a chore's due instant carries a concrete time-of-day (vs an all-day end-of-day due). */
export function choreHasDueTime(chore: Pick<Chore, 'dueTime' | 'timesOfDay'>): boolean {
  return !!chore.dueTime || (chore.timesOfDay?.length ?? 0) > 0
}

/**
 * Buckets a due instant. By default the overdue boundary is day-granular (a chore due today stays
 * "today" until the household's day start, midnight unless the admin moved it). Pass `hasTime` for
 * chores with a concrete due time, so they flip to "overdue" the moment that time passes instead of
 * waiting for the next day. "Today" and the week ahead are household days for both kinds.
 */
export function dueStatus(
  dueAt?: string | null,
  hasTime = false,
): 'overdue' | 'today' | 'upcoming' | 'later' {
  if (!dueAt) return 'later'
  const due = new Date(dueAt)
  const now = new Date()
  const todayStart = householdDay(now)
  const tomorrowStart = new Date(todayStart.getTime() + 86_400_000)
  const weekEnd = new Date(todayStart.getTime() + 7 * 86_400_000)
  // An all-day due is stamped at the end of its own calendar day, so it already names its day; a
  // timed one belongs to whichever household day its instant falls in.
  const dueDay = hasTime ? new Date(due.getTime() - dayStartMs()) : due
  if (hasTime ? due < now : dueDay < todayStart) return 'overdue'
  if (dueDay < tomorrowStart) return 'today'
  if (dueDay <= weekEnd) return 'upcoming'
  return 'later'
}

export function choreDueStatus(chore: Chore): 'overdue' | 'today' | 'upcoming' | 'later' {
  return dueStatus(chore.dueAt, choreHasDueTime(chore))
}

/** Whether a track owner has completed their current obligation (next occurrence is in the future).
 * A not-yet-started track (future start date, no activity) is scheduled — not done. */
export function trackIsDone(chore: Chore, track: ChoreTrack): boolean {
  if (!track.started) return false
  const s = dueStatus(track.dueAt, choreHasDueTime(chore))
  return s === 'upcoming' || s === 'later'
}

/** Short status phrase for one assignee's track, e.g. "done · next in 3 days", "due today", "overdue",
 * "due in 6 days" (a not-yet-started future occurrence). */
export function trackStatusText(chore: Chore, track: ChoreTrack): string {
  const progress = trackProgressLabel(chore, track)
  const s = dueStatus(track.dueAt, choreHasDueTime(chore))
  const base =
    s === 'overdue'
      ? (track.dueAt ? t('track.overdueSince', { when: relativeDayLabel(track.dueAt) }) : t('track.overdue'))
    : s === 'today' ? t('track.dueToday')
    : !track.dueAt ? t('track.done')
    : track.started ? t('track.doneNext', { when: relativeDayLabel(track.dueAt) })
    : t('track.due', { when: relativeDayLabel(track.dueAt) })
  return progress ? `${progress} · ${base}` : base
}

/** Whether a logged entry beat its occurrence's deadline. An all-day chore (`hasDueTime` false) stays
 * on time until the household's day start, mirroring the server's `Chore.LateGrace`. */
export function beatDeadline(at: string, occurrenceDueAt: string, hasDueTime: boolean): boolean {
  const grace = hasDueTime ? 0 : dayStartMs()
  return new Date(at).getTime() <= new Date(occurrenceDueAt).getTime() + grace
}

/** History rows label a completion "on time" / "late", or "rescue" when someone stepped in for a
 * chore that was overdue in another person's hands (decided by the server). */
export function timelinessLabel(entry: ChoreHistoryEntry, onTime: boolean): string {
  if (entry.isRescue) return t('history.rescueLower')
  return onTime ? t('history.onTimeLower') : t('history.lateLower')
}

export function timelinessClass(entry: ChoreHistoryEntry, onTime: boolean): string {
  if (entry.isRescue) return 'text-info'
  return onTime ? 'text-success' : 'text-destructive'
}
