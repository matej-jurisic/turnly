export type UserRole = 'Admin' | 'Member'

export interface User {
  id: string
  username: string
  displayName: string
  avatarColor: string
  role: UserRole
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
