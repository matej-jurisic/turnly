import { useAuthStore } from '@/store/auth'
import type {
  AuthResponse,
  Chore,
  CompleteChoreRequest,
  CreateChoreRequest,
  CreateUserRequest,
  LeaderboardEntry,
  PointsLogEntry,
  SetupRequest,
  Tag,
  UpdateChoreRequest,
  UpdateUserRequest,
  User,
} from '@/lib/types'

const BASE = '/api'

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

// De-duplicate concurrent refreshes: many 401s should trigger only one refresh call.
let refreshPromise: Promise<boolean> | null = null

async function performRefresh(): Promise<boolean> {
  const res = await fetch(`${BASE}/auth/refresh`, { method: 'POST', credentials: 'include' })
  if (!res.ok) return false
  const data = (await res.json()) as AuthResponse
  useAuthStore.getState().setAuth(data.accessToken, data.user)
  return true
}

export function tryRefresh(): Promise<boolean> {
  if (!refreshPromise) {
    refreshPromise = performRefresh().finally(() => {
      refreshPromise = null
    })
  }
  return refreshPromise
}

interface RequestOptions extends RequestInit {
  /** Whether a 401 should trigger a one-shot token refresh + retry (default true). */
  retry?: boolean
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { retry = true, ...init } = options
  const headers = new Headers(init.headers)

  const token = useAuthStore.getState().accessToken
  if (token) headers.set('Authorization', `Bearer ${token}`)
  if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')

  const res = await fetch(`${BASE}${path}`, { ...init, headers, credentials: 'include' })

  if (res.status === 401 && retry) {
    const refreshed = await tryRefresh()
    if (refreshed) return request<T>(path, { ...options, retry: false })
    useAuthStore.getState().clear()
    throw new ApiError(401, 'Your session has expired. Please sign in again.')
  }

  if (!res.ok) {
    throw new ApiError(res.status, await readError(res))
  }

  if (res.status === 204) return undefined as T
  return (await res.json()) as T
}

async function readError(res: Response): Promise<string> {
  try {
    const body = await res.json()
    return body.detail ?? body.title ?? res.statusText
  } catch {
    return res.statusText || 'Request failed'
  }
}

const json = (body: unknown) => JSON.stringify(body)

export const authApi = {
  status: () => request<{ needsSetup: boolean }>('/setup/status', { retry: false }),
  setup: (body: SetupRequest) =>
    request<AuthResponse>('/setup', { method: 'POST', body: json(body), retry: false }),
  login: (username: string, password: string) =>
    request<AuthResponse>('/auth/login', {
      method: 'POST',
      body: json({ username, password }),
      retry: false,
    }),
  logout: () => request<void>('/auth/logout', { method: 'POST', retry: false }),
  me: () => request<User>('/auth/me'),
  changePassword: (currentPassword: string, newPassword: string) =>
    request<void>('/auth/change-password', {
      method: 'POST',
      body: json({ currentPassword, newPassword }),
    }),
}

export const usersApi = {
  list: () => request<User[]>('/users'),
  leaderboard: () => request<LeaderboardEntry[]>('/users/leaderboard'),
  create: (body: CreateUserRequest) => request<User>('/users', { method: 'POST', body: json(body) }),
  update: (id: string, body: UpdateUserRequest) =>
    request<User>(`/users/${id}`, { method: 'PUT', body: json(body) }),
  remove: (id: string) => request<void>(`/users/${id}`, { method: 'DELETE' }),
  setPassword: (id: string, newPassword: string) =>
    request<void>(`/users/${id}/password`, { method: 'POST', body: json({ newPassword }) }),
  pointsLog: (id: string) => request<PointsLogEntry[]>(`/users/${id}/points-log`),
}

export const choresApi = {
  list: () => request<Chore[]>('/chores'),
  get: (id: string) => request<Chore>(`/chores/${id}`),
  create: (body: CreateChoreRequest) => request<Chore>('/chores', { method: 'POST', body: json(body) }),
  update: (id: string, body: UpdateChoreRequest) =>
    request<Chore>(`/chores/${id}`, { method: 'PUT', body: json(body) }),
  remove: (id: string) => request<void>(`/chores/${id}`, { method: 'DELETE' }),
  complete: (id: string, body: CompleteChoreRequest) =>
    request<Chore>(`/chores/${id}/complete`, { method: 'POST', body: json(body) }),
  undoCompletion: (completionId: string) =>
    request<void>(`/completions/${completionId}`, { method: 'DELETE' }),
}

export const tagsApi = {
  list: () => request<Tag[]>('/tags'),
  create: (name: string) => request<Tag>('/tags', { method: 'POST', body: json({ name }) }),
  remove: (id: string) => request<void>(`/tags/${id}`, { method: 'DELETE' }),
}
