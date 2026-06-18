export type UserRole = 'Admin' | 'Member'

export interface User {
  id: string
  username: string
  displayName: string
  avatarColor: string
  role: UserRole
  points: number
  createdAt: string
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

export type RepeatType = 'OneTime' | 'Daily' | 'Weekly' | 'Monthly' | 'Yearly'

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
  notes?: string | null
  pointsAwarded: number
}

export interface Chore {
  id: string
  name: string
  description?: string | null
  emoji?: string | null
  points: number
  repeatType: RepeatType
  weekdays: Weekday[]
  startDate: string
  dueAt?: string | null
  currentAssignee?: User | null
  assignees: User[]
  tags: string[]
  lastCompletion?: ChoreCompletion | null
  createdAt: string
}

export interface ChoreRequest {
  name: string
  description?: string | null
  emoji?: string | null
  points: number
  repeatType: RepeatType
  weekdays: Weekday[]
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
  createdAt: string
}
