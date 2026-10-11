import { useQuery } from '@tanstack/react-query'
import { useSearchParams } from 'react-router-dom'
import { authApi } from '@/lib/api'
import { cn } from '@/lib/utils'
import { Badge } from '@/components/ui/Badge'
import { AwardsTab } from '@/components/rewards/AwardsTab'
import { CosmeticsTab } from '@/components/rewards/CosmeticsTab'
import { localized, t } from '@/lib/i18n'

type RewardsTab = 'awards' | 'cosmetics'

const TABS: { value: RewardsTab; label: string }[] = [
  localized({ value: 'awards' as const }, { label: 'rewards.tab.awards' }),
  localized({ value: 'cosmetics' as const }, { label: 'rewards.tab.cosmetics' }),
]

/** Everything points can be spent on: admin-defined awards and the cosmetic pulls. The active tab
 * lives in the URL (`/rewards?tab=cosmetics`) so it survives reloads and can be linked to. */
export function RewardsPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const tab: RewardsTab = searchParams.get('tab') === 'cosmetics' ? 'cosmetics' : 'awards'

  const { data: me } = useQuery({ queryKey: ['me'], queryFn: authApi.me })

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold text-foreground">{t('rewards.title')}</h1>
        <Badge tone="violet">{t('common.pts', { count: me?.points ?? 0 })}</Badge>
      </div>

      <div role="tablist" aria-label={t('rewards.title')} className="flex gap-1 border-b border-border">
        {TABS.map((t) => {
          const active = t.value === tab
          return (
            <button
              key={t.value}
              type="button"
              role="tab"
              aria-selected={active}
              onClick={() => setSearchParams(t.value === 'awards' ? {} : { tab: t.value }, { replace: true })}
              className={cn(
                '-mb-px border-b-2 px-3 py-2 text-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring',
                active
                  ? 'border-primary font-semibold text-primary'
                  : 'border-transparent text-muted-foreground hover:text-foreground',
              )}
            >
              {t.label}
            </button>
          )
        })}
      </div>

      {tab === 'awards' ? <AwardsTab /> : <CosmeticsTab />}
    </div>
  )
}
