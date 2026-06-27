import type { QueryClient } from '@tanstack/react-query'
import { authApi } from '@/lib/api'
import { useAuthStore } from '@/store/auth'
import { applyPalette } from '@/lib/palette'

/**
 * After a gacha mutation that can change the signed-in user's balances or equipped cosmetics, pull a
 * fresh `me`, push it into the auth store, re-apply the equipped theme palette, and refresh the
 * surfaces that show balances or avatars. Shared by the gacha page and the customization modal.
 */
export async function syncAppearanceFromServer(queryClient: QueryClient): Promise<void> {
  const me = await authApi.me()
  useAuthStore.getState().setUser(me)
  applyPalette(me.equippedThemeKey)
  // Fire the invalidations without awaiting their refetches: callers only need the store/palette
  // updated synchronously, and the affected surfaces can refresh on their own. invalidateQueries
  // only refetches *active* (mounted) queries, so listing every avatar-bearing surface is cheap.
  // These all embed the user's avatar (color/frame/emoji) in their payload, so a customization
  // change must refresh them or they show stale avatars until the next navigation.
  for (const key of APPEARANCE_QUERY_KEYS) void queryClient.invalidateQueries({ queryKey: [key] })
}

/** Query keys whose cached payloads embed a user's avatar (color/frame/emoji) and so must be
 * refreshed when the signed-in user changes their appearance. */
const APPEARANCE_QUERY_KEYS = [
  'gacha', 'me', 'leaderboard', 'chores', 'chore-users', 'users', 'history', 'stats',
  'awards', 'redemptions', 'points-log',
]
