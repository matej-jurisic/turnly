import { useState } from 'react'
import { versionApi } from '@/lib/api'
import { useAvailableUpdate } from '@/lib/app-version'
import { t } from '@/lib/i18n'

/** Thin strip above the top bar when a newer version has been deployed. */
export function UpdateBanner() {
  const update = useAvailableUpdate()
  const [dismissed, setDismissed] = useState(false)
  if (!update || dismissed) return null

  const actionClass = 'shrink-0 rounded-md px-2 py-1 font-medium hover:bg-primary/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring'

  return (
    <div className="flex items-center gap-2 border-b border-border bg-primary/10 px-4 py-1.5 text-sm text-primary md:px-6">
      <span className="min-w-0 flex-1">
        {update === 'web' ? t('update.webAvailable') : t('update.androidAvailable')}
      </span>
      {update === 'web' ? (
        <button type="button" onClick={() => window.location.reload()} className={actionClass}>
          {t('update.reload')}
        </button>
      ) : (
        // A plain link: the WebView hands off-origin navigation to the system browser, which downloads it.
        <a href={versionApi.androidApkUrl()} className={actionClass}>
          {t('update.download')}
        </a>
      )}
      <button
        type="button"
        onClick={() => setDismissed(true)}
        aria-label={t('update.dismiss')}
        className="shrink-0 rounded-md p-1 hover:bg-primary/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M18 6 6 18M6 6l12 12" />
        </svg>
      </button>
    </div>
  )
}
