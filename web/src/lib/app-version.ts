import { useQuery } from '@tanstack/react-query'
import { App } from '@capacitor/app'
import { versionApi } from '@/lib/api'
import { isNative } from '@/lib/native'

// A fallback only: every live-update event (including the reconnect after a redeploy restarts the
// server) invalidates all queries, which refetches this one too.
const CHECK_INTERVAL_MS = 10 * 60_000

/** The installed APK's versionCode (Android only). */
async function installedAndroidBuild(): Promise<number | null> {
  const build = Number((await App.getInfo()).build)
  return Number.isFinite(build) ? build : null
}

/**
 * What this client should update to, if anything. On the web a reload picks up the newly deployed
 * bundle; the Android app ships its bundle inside the APK, so it waits for a newer published APK.
 */
export function useAvailableUpdate(): 'web' | 'android' | null {
  const { data } = useQuery({
    queryKey: ['app-version'],
    queryFn: async () => {
      const server = await versionApi.get()
      return { server, installedAndroid: isNative() ? await installedAndroidBuild() : null }
    },
    refetchInterval: CHECK_INTERVAL_MS,
  })
  if (!data) return null
  const { server, installedAndroid } = data
  if (isNative()) {
    return server.android !== null && installedAndroid !== null && server.android > installedAndroid ? 'android' : null
  }
  return server.web !== null && server.web !== __BUILD_ID__ ? 'web' : null
}
