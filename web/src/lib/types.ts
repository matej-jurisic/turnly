export type UserRole = 'Admin' | 'Member'

export interface User {
  id: string
  username: string
  displayName: string
  avatarColor: string
  role: UserRole
  points: number
  weeklyPoints: number
  createdAt: string
  /** Quiet-hours window "HH:mm" (server-local). Both set together, or both null = off. During quiet
   * hours push notifications are suppressed (the in-app inbox still receives them). */
  quietHoursStart?: string | null
  quietHoursEnd?: string | null
}

/** Self-service profile update (avatar color + optional quiet hours). */
export interface ProfileUpdate {
  avatarColor: string
  quietHoursStart?: string | null
  quietHoursEnd?: string | null
}

/** App-wide settings (admin). `timeZone` is the configured family zone (null = unset, falls back to
 * `serverTimeZone`); it's the zone quiet hours are evaluated against server-side. */
export interface AppSettings {
  timeZone: string | null
  serverTimeZone: string
}

export interface LeaderboardEntry {
  id: string
  displayName: string
  avatarColor: string
  points: number
  weeklyPoints: number
}

export interface AuthResponse {
  accessToken: string
  accessTokenExpiresAt: string
  user: User
}

export interface SetupRequest {
  username: string
  displayName: string
  password: string
  avatarColor?: string
}

export interface CreateUserRequest {
  username: string
  displayName: string
  password: string
  role: UserRole
  avatarColor?: string
}

export interface UpdateUserRequest {
  displayName: string
  avatarColor: string
  role: UserRole
}

export type RepeatType = 'OneTime' | 'Daily' | 'Weekly' | 'Monthly' | 'Yearly' | 'Custom'

export type CustomRecurrenceMode = 'Interval' | 'DaysOfWeek' | 'DaysOfMonth'

export type RecurrenceUnit = 'Day' | 'Week' | 'Month' | 'Year'

export type AssignmentStrategy =
  | 'Random'
  | 'LeastAssigned'
  | 'LeastCompleted'
  | 'KeepLastAssigned'
  | 'RandomExceptLastAssigned'
  | 'RoundRobin'
  /** Everyone independently: each assignee has their own schedule (a track) and quota; no rotation. */
  | 'Independent'

export type SchedulingPreference =
  | 'FromScheduledDate'
  | 'FromCompletionDate'
  | 'ToFirstNextRepeat'
  | 'SmartScheduling'

export type Weekday =
  | 'Sunday'
  | 'Monday'
  | 'Tuesday'
  | 'Wednesday'
  | 'Thursday'
  | 'Friday'
  | 'Saturday'

export type NotificationType = 'Reminder' | 'Due' | 'FollowUp'

export type NotificationTiming = 'Before' | 'AtDue' | 'After'

export type NotificationOffsetUnit = 'Minutes' | 'Hours' | 'Days'

export type NotificationRecipients = 'CurrentAssignee' | 'AllAssignees'

/** A chore notification schedule entry. `id` is present on responses, absent on requests. */
export interface ChoreNotificationInput {
  type: NotificationType
  timing: NotificationTiming
  offsetValue: number
  offsetUnit: NotificationOffsetUnit
  recipients: NotificationRecipients
}

export interface ChoreNotification extends ChoreNotificationInput {
  id: string
}

export interface ChoreCompletion {
  id: string
  choreId: string
  choreName: string
  completedBy?: User | null
  completedAt: string
  occurrenceDueAt?: string | null
  notes?: string | null
  pointsAwarded: number
  isSkip: boolean
  isExpired: boolean
}

/** A row in the chore history feed: a completion, a skip, an auto-expiry, or a manual reassignment. */
export interface ChoreHistoryEntry {
  id: string
  kind: 'completion' | 'skip' | 'expired' | 'reassignment'
  choreId: string
  choreName: string
  /** Completer, or the user who performed the reassignment (null if since deleted). */
  actor?: User | null
  at: string
  occurrenceDueAt?: string | null
  notes?: string | null
  pointsAwarded: number
  /** Reassignment only: previous assignee. */
  fromAssignee?: User | null
  /** Reassignment only: new assignee. */
  toAssignee?: User | null
}

/** One assignee's independent schedule on a track-mode (`Independent`) chore. */
export interface ChoreTrack {
  user: User
  dueAt?: string | null
  completionsRequired: number
  /** Completions/skips this assignee has logged toward their current occurrence. */
  progress: number
  /** False until the assignee has logged any completion/skip — distinguishes a future due date that
   * is just the not-yet-reached first occurrence (start date in the future) from one reached by
   * completing the prior occurrence. */
  started: boolean
  /** This assignee's current on-time streak (consecutive occurrences completed on/before due). */
  streak: number
}

/** Custom-recurrence parameters; which fields apply depends on `customMode`. */
export interface RecurrenceFields {
  customMode?: CustomRecurrenceMode | null
  intervalCount?: number | null
  intervalUnit?: RecurrenceUnit | null
  weekdays: Weekday[]
  /** Restricts DaysOfWeek to specific monthly occurrences: 1–4 for the nth, -1 for last. Empty = every week. */
  weeksOfMonth: number[]
  daysOfMonth: number[]
  months: number[]
}

export interface Chore extends RecurrenceFields {
  id: string
  name: string
  description?: string | null
  emoji?: string | null
  points: number
  repeatType: RepeatType
  /** Completions (skips included) needed to close one occurrence before it advances. 1 for the
   * usual chore; >1 = "complete N times" (only on the non-custom repeat types). */
  completionsRequired: number
  /** For multi-completion chores: rotate the assignee after each completion rather than only when
   * the occurrence is fully complete. */
  rotateOnEachCompletion: boolean
  assignmentStrategy: AssignmentStrategy
  schedulingPreference: SchedulingPreference
  /** Grace window (minutes) for SmartScheduling; null = no grace. */
  graceMinutes?: number | null
  /** When true, the background service auto-expires unfilled slots once the window closes. Only
   * meaningful for multi-completion non-custom non-independent chores. */
  autoAdvanceIncomplete: boolean
  /** Minutes after dueAt before auto-advance fires; null = immediately when overdue. */
  completionWindowMinutes?: number | null
  startDate: string
  /** Local due time "HH:mm", or null for "no specific time" (due end of day). */
  dueTime?: string | null
  /** Fixed times-of-day ("HH:mm") for "N times a day"; empty for a single daily slot. Only set for
   * Daily / DaysOfWeek / DaysOfMonth schedules. */
  timesOfDay: string[]
  dueAt?: string | null
  currentAssignee?: User | null
  /** Who the chore will rotate to next, for strategies whose outcome is fixed by current state
   * (null for the random strategies, one-time chores, and single-assignee chores). */
  nextAssignee?: User | null
  assignees: User[]
  tags: string[]
  notifications: ChoreNotification[]
  lastCompletion?: ChoreCompletion | null
  /** Completions logged against the current occurrence (only for multi-completion chores). For
   * track-mode chores this is the *viewing user's* track progress. */
  occurrenceProgress?: number | null
  /** Consecutive occurrences completed on/before their due date. For track-mode chores this is the
   * *viewing user's* own streak (per-assignee streaks live on `tracks`). */
  currentStreak: number
  /** Per-assignee schedules, present only for `Independent` chores. `dueAt`/`occurrenceProgress`
   * above are personalised to the viewing user's own track. */
  tracks: ChoreTrack[]
  createdAt: string
  /** Achievements the completing user just unlocked, returned only on a self-completion response so
   * the client can show a celebration popup. Absent/empty otherwise. */
  unlockedAchievements?: Achievement[]
}

export interface ChoreRequest extends RecurrenceFields {
  name: string
  description?: string | null
  emoji?: string | null
  points: number
  repeatType: RepeatType
  completionsRequired: number
  rotateOnEachCompletion: boolean
  assignmentStrategy: AssignmentStrategy
  schedulingPreference: SchedulingPreference
  graceMinutes?: number | null
  autoAdvanceIncomplete: boolean
  completionWindowMinutes?: number | null
  startDate: string
  dueTime?: string | null
  /** Fixed times-of-day ("HH:mm") for "N times a day"; omit/empty for a single daily slot. */
  timesOfDay?: string[] | null
  assigneeIds: string[]
  currentAssigneeId: string | null
  tagNames: string[]
  notifications: ChoreNotificationInput[]
  /** Per-assignee quotas for `Independent` chores — one per assignee. Omitted for rotating chores. */
  tracks?: { userId: string; completionsRequired: number }[]
}

export type CreateChoreRequest = ChoreRequest
export type UpdateChoreRequest = ChoreRequest

export interface CompleteChoreRequest {
  notes?: string | null
  /** Admin-only: credit the completion to another user instead of the caller. */
  completedByUserId?: string | null
}

export interface SkipChoreRequest {
  notes?: string | null
  /** Track-mode only: whose schedule to skip (defaults to the caller). */
  userId?: string | null
}

export interface ReassignChoreRequest {
  assigneeId: string
}

export interface RescheduleChoreRequest {
  dueAt: string
  dueTime?: string | null
  /** Track-mode only: whose schedule to reschedule. */
  userId?: string | null
}

export interface Tag {
  id: string
  name: string
}

export interface PushSubscribeRequest {
  endpoint: string
  p256dh: string
  auth: string
}

export interface PushDevice {
  id: string
  label: string
  endpoint: string
  createdAt: string
}

export interface NotificationInboxItem {
  id: string
  title: string
  body: string
  choreId?: string | null
  read: boolean
  createdAt: string
}

export type PointsLogType = 'Completion' | 'Redemption' | 'Adjustment'

export interface PointsLogEntry {
  id: string
  delta: number
  type: PointsLogType
  description?: string | null
  choreCompletionId?: string | null
  redemptionId?: string | null
  createdAt: string
}

export interface Award {
  id: string
  name: string
  description?: string | null
  emoji?: string | null
  cost: number
  createdAt: string
}

export interface AwardRequest {
  name: string
  description?: string | null
  emoji?: string | null
  cost: number
}

export type CreateAwardRequest = AwardRequest
export type UpdateAwardRequest = AwardRequest

export type RedemptionStatus = 'Pending' | 'Fulfilled'

export interface Redemption {
  id: string
  awardId?: string | null
  awardName: string
  awardEmoji?: string | null
  user: User
  pointsSpent: number
  status: RedemptionStatus
  redeemedAt: string
  fulfilledAt?: string | null
  /** Achievements the user just unlocked by redeeming, returned only on the redeem response so the
   * client can show a celebration popup. Absent/empty otherwise. */
  unlockedAchievements?: Achievement[]
}

export interface UserStats {
  userId: string
  displayName: string
  avatarColor: string
  weeklyCount: number
  monthlyCount: number
  allTimeCount: number
  onTimeCount: number
  overdueCount: number
  missedCount: number
}

export interface UserWeeklyCount {
  userId: string
  displayName: string
  avatarColor: string
  count: number
}

export interface ChartWeek {
  label: string
  weekStart: string
  userCounts: UserWeeklyCount[]
}

export interface Stats {
  userStats: UserStats[]
  chart: ChartWeek[]
  totalMissedCount: number
}

/** A catalog achievement projected for the current user. Cosmetic — no points. `progress` is
 * clamped to `threshold`; `earned`/`earnedAt` reflect whether it's been unlocked. */
export interface Achievement {
  key: string
  name: string
  description: string
  emoji: string
  category: string
  threshold: number
  progress: number
  earned: boolean
  earnedAt?: string | null
}
