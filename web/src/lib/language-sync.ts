import { useEffect } from 'react'
import { authApi } from '@/lib/api'
import { useLanguageStore } from '@/lib/i18n'
import { useAuthStore } from '@/store/auth'

/**
 * Tells the server which language this device uses, so push and inbox notifications are worded in
 * it. The language stays a per-device choice; the server keeps whichever device reported last.
 * Mounted once, in `Layout`.
 */
export function useLanguageSync(): void {
  const language = useLanguageStore((s) => s.language)
  const userId = useAuthStore((s) => s.user?.id)
  const serverLanguage = useAuthStore((s) => s.user?.language)
  useEffect(() => {
    if (!userId || serverLanguage === language) return
    authApi
      .setLanguage(language)
      .then((updated) => {
        const current = useAuthStore.getState().user
        if (current) useAuthStore.getState().setUser({ ...current, language: updated.language })
      })
      .catch(() => {
        // Best effort: notifications keep their previous language until the next attempt.
      })
  }, [userId, serverLanguage, language])
}
