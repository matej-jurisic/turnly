import { create } from 'zustand'
import type { User } from '@/lib/types'

export type AuthStatus = 'loading' | 'authenticated' | 'unauthenticated'

interface AuthState {
  accessToken: string | null
  user: User | null
  status: AuthStatus
  setAuth: (accessToken: string, user: User) => void
  setUser: (user: User) => void
  setStatus: (status: AuthStatus) => void
  clear: () => void
}

/**
 * Auth lives entirely in memory: the access token is never persisted (only the httpOnly
 * refresh cookie survives a reload, and is exchanged for a fresh token on startup).
 */
export const useAuthStore = create<AuthState>((set) => ({
  accessToken: null,
  user: null,
  status: 'loading',
  setAuth: (accessToken, user) => set({ accessToken, user, status: 'authenticated' }),
  setUser: (user) => set({ user }),
  setStatus: (status) => set({ status }),
  clear: () => set({ accessToken: null, user: null, status: 'unauthenticated' }),
}))
