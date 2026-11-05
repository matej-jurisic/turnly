import type { ChoreSlotTurn } from '@/lib/types'
import { Label } from '@/components/ui/Field'
import { pairLabel, slotName } from '@/lib/chore-format'
import { t } from '@/lib/i18n'

/** Chips choosing which time-of-day slot an action is for on a slot-turn chore, each with its holder. */
export function SlotPicker({
  slots,
  value,
  onChange,
}: {
  slots: ChoreSlotTurn[]
  value?: string
  onChange: (time: string) => void
}) {
  return (
    <div>
      <Label>{t('slots.pick')}</Label>
      <div className="flex flex-wrap gap-1">
        {slots.map((s) => (
          <button
            key={s.time}
            type="button"
            aria-pressed={s.time === value}
            onClick={() => onChange(s.time)}
            className={
              'flex items-center gap-1.5 rounded-md px-2 py-1 text-xs transition-colors ' +
              (s.time === value
                ? 'bg-primary/10 text-primary ring-1 ring-primary'
                : 'bg-accent text-muted-foreground hover:text-foreground')
            }
          >
            {slotName(s)}
            {s.currentAssignee && <span className="opacity-70">· {pairLabel(s.currentAssignee, s.currentPartner)}</span>}
          </button>
        ))}
      </div>
    </div>
  )
}
