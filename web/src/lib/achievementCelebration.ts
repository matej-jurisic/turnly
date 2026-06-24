import { create } from 'zustand'
import type { Achievement } from '@/lib/types'

interface AchievementCelebrationState {
  /** Unlocked achievements waiting to be celebrated, shown one at a time. */
  queue: Achievement[]
  enqueue: (achievements: Achievement[]) => void
  dismiss: () => void
}

/** Backing store for the celebration popup. Prefer the imperative {@link celebrateAchievements}
 * helper at call sites. */
export const useAchievementCelebrationStore = create<AchievementCelebrationState>((set) => ({
  queue: [],
  enqueue: (achievements) => set((s) => ({ queue: [...s.queue, ...achievements] })),
  dismiss: () => set((s) => ({ queue: s.queue.slice(1) })),
}))

/**
 * Imperative API — call from a mutation's `onSuccess` with the unlocked achievements a completion or
 * redemption response carries. No-ops on an empty/missing list. Rendered by `<AchievementCelebration />`,
 * mounted once at the app root.
 */
export function celebrateAchievements(achievements: Achievement[] | undefined | null): void {
  if (achievements && achievements.length > 0)
    useAchievementCelebrationStore.getState().enqueue(achievements)
}
