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
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: ['gacha'] }),
    queryClient.invalidateQueries({ queryKey: ['me'] }),
    queryClient.invalidateQueries({ queryKey: ['leaderboard'] }),
  ])
}
