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

export type CustomRecurrenceMode = 'Interval' | 'DaysOfWeek' | 'DaysOfMonth' | 'Frequency'

export type RecurrenceUnit = 'Day' | 'Week' | 'Month' | 'Year'

export type FrequencyPeriod = 'Day' | 'Week' | 'Month' | 'Year'

export type AssignmentStrategy =
  | 'Random'
  | 'LeastAssigned'
  | 'LeastCompleted'
  | 'KeepLastAssigned'
  | 'RandomExceptLastAssigned'
  | 'RoundRobin'

export type SchedulingPreference = 'FromScheduledDate' | 'FromCompletionDate' | 'ToFirstNextRepeat'

export type Weekday =
  | 'Sunday'
  | 'Monday'
  | 'Tuesday'
  | 'Wednesday'
  | 'Thursday'
  | 'Friday'
  | 'Saturday'

export interface ChoreCompletion {
  id: string
  choreId: string
  choreName: string
  completedBy: User
  completedAt: string
  occurrenceDueAt?: string | null
  notes?: string | null
  pointsAwarded: number
}

/** Custom-recurrence parameters; which fields apply depends on `customMode`. */
export interface RecurrenceFields {
  customMode?: CustomRecurrenceMode | null
  intervalCount?: number | null
  intervalUnit?: RecurrenceUnit | null
  weekdays: Weekday[]
  daysOfMonth: number[]
  months: number[]
  frequencyCount?: number | null
  frequencyPeriod?: FrequencyPeriod | null
}

export interface Chore extends RecurrenceFields {
  id: string
  name: string
  description?: string | null
  emoji?: string | null
  points: number
  repeatType: RepeatType
  assignmentStrategy: AssignmentStrategy
  schedulingPreference: SchedulingPreference
  startDate: string
  dueAt?: string | null
  currentAssignee?: User | null
  assignees: User[]
  tags: string[]
  lastCompletion?: ChoreCompletion | null
  /** Completions in the current period (frequency chores only). */
  frequencyProgress?: number | null
  createdAt: string
}

export interface ChoreRequest extends RecurrenceFields {
  name: string
  description?: string | null
  emoji?: string | null
  points: number
  repeatType: RepeatType
  assignmentStrategy: AssignmentStrategy
  schedulingPreference: SchedulingPreference
  startDate: string
  assigneeIds: string[]
  currentAssigneeId: string
  tagNames: string[]
}

export type CreateChoreRequest = ChoreRequest
export type UpdateChoreRequest = ChoreRequest

export interface CompleteChoreRequest {
  notes?: string | null
}

export interface Tag {
  id: string
  name: string
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
}
