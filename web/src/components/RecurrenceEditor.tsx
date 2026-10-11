import type {
  CustomRecurrenceMode,
  RecurrenceFields,
  RecurrenceUnit,
} from '@/lib/types'
import { IntegerInput, Label, Select } from '@/components/ui/Field'
import { WEEKDAY_ORDER, monthShort, weekdayShort, weekOccurrenceShort } from '@/lib/chore-format'
import { localized, t } from '@/lib/i18n'

const MODE_OPTIONS: { value: CustomRecurrenceMode; label: string }[] = [
  localized({ value: 'Interval' as const }, { label: 'recur.mode.Interval' }),
  localized({ value: 'DaysOfWeek' as const }, { label: 'repeat.daysOfWeek' }),
  localized({ value: 'DaysOfMonth' as const }, { label: 'recur.mode.DaysOfMonth' }),
]

const UNIT_OPTIONS: { value: RecurrenceUnit; label: string }[] = [
  localized({ value: 'Day' as const }, { label: 'unit.days' }),
  localized({ value: 'Week' as const }, { label: 'unit.weeks' }),
  localized({ value: 'Month' as const }, { label: 'unit.months' }),
  localized({ value: 'Year' as const }, { label: 'unit.years' }),
]

// nth occurrence of the chosen weekday within a month; -1 is the last one.
const WEEK_OCCURRENCES = [1, 2, 3, 4, -1]

const MONTH_NUMBERS = Array.from({ length: 12 }, (_, i) => i + 1)

// Highest day a month can hold (Feb = 29 so leap-year-only chores stay valid).
const MONTH_MAX_DAYS = [31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]

const pillClass = (active: boolean) =>
  'rounded-md px-2 py-1 text-xs transition-colors ' +
  (active
    ? 'bg-primary/10 text-primary ring-1 ring-primary'
    : 'bg-accent text-muted-foreground hover:text-foreground')

function toggle<T>(list: T[], value: T): T[] {
  return list.includes(value) ? list.filter((v) => v !== value) : [...list, value]
}

interface Props {
  value: RecurrenceFields
  onChange: (next: RecurrenceFields) => void
}

/** The custom-recurrence sub-form, shown when a chore's repeat type is "Custom". */
export function RecurrenceEditor({ value, onChange }: Props) {
  const set = (patch: Partial<RecurrenceFields>) => onChange({ ...value, ...patch })
  const mode = value.customMode ?? 'Interval'

  // The largest day any selected month can hold; days beyond it can never occur.
  const maxDayAllowed = value.months.length
    ? Math.max(...value.months.map((m) => MONTH_MAX_DAYS[m - 1]))
    : 31

  // Toggling a month can make already-picked days impossible — prune them so we never submit an
  // unsatisfiable combo (e.g. day 31 with February only).
  const toggleMonth = (month: number) => {
    const months = toggle(value.months, month)
    const limit = months.length ? Math.max(...months.map((m) => MONTH_MAX_DAYS[m - 1])) : 31
    set({ months, daysOfMonth: value.daysOfMonth.filter((d) => d <= limit) })
  }

  return (
    <div className="space-y-3 rounded-lg border border-border bg-accent/40 p-3">
      <div>
        <Label htmlFor="custom-mode">{t('recur.custom')}</Label>
        <Select
          id="custom-mode"
          value={mode}
          onChange={(e) => set({ customMode: e.target.value as CustomRecurrenceMode })}
        >
          {MODE_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>{o.label}</option>
          ))}
        </Select>
      </div>

      {mode === 'Interval' && (
        <div className="flex items-end gap-2">
          <span className="pb-2 text-sm text-muted-foreground">{t('recur.every')}</span>
          <div className="w-20">
            <IntegerInput
              value={value.intervalCount ?? 1}
              onCommit={(n) => set({ intervalCount: n })}
              aria-label={t('recur.intervalCount')}
            />
          </div>
          <div className="flex-1">
            <Select
              value={value.intervalUnit ?? 'Week'}
              onChange={(e) => set({ intervalUnit: e.target.value as RecurrenceUnit })}
              aria-label={t('recur.intervalUnit')}
            >
              {UNIT_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>{o.label}</option>
              ))}
            </Select>
          </div>
        </div>
      )}

      {mode === 'DaysOfWeek' && (
        <div className="space-y-2">
          <div className="flex flex-wrap gap-1">
            {/* Monday-first display, but values are the .NET DayOfWeek names the API expects. */}
            {WEEKDAY_ORDER.map((day) => (
              <button
                key={day}
                type="button"
                onClick={() => set({ weekdays: toggle(value.weekdays, day) })}
                className={pillClass(value.weekdays.includes(day))}
              >
                {weekdayShort(day)}
              </button>
            ))}
          </div>
          <div>
            <Label htmlFor="weeks-scope">{t('recur.occurs')}</Label>
            <Select
              id="weeks-scope"
              value={value.weeksOfMonth.length ? 'specific' : 'every'}
              // Switching to "every week" clears the occurrence restriction; switching to "specific"
              // seeds the 1st so the user always has at least one selected.
              onChange={(e) => set({ weeksOfMonth: e.target.value === 'specific' ? [1] : [] })}
            >
              <option value="every">{t('repeat.every.Week')}</option>
              <option value="specific">{t('recur.specific')}</option>
            </Select>
          </div>
          {value.weeksOfMonth.length > 0 && (
            <div className="flex flex-wrap gap-1">
              {WEEK_OCCURRENCES.map((week) => (
                <button
                  key={week}
                  type="button"
                  onClick={() => set({ weeksOfMonth: toggle(value.weeksOfMonth, week) })}
                  className={pillClass(value.weeksOfMonth.includes(week))}
                >
                  {weekOccurrenceShort(week)}
                </button>
              ))}
            </div>
          )}
        </div>
      )}

      {mode === 'DaysOfMonth' && (
        <div className="space-y-2">
          <div>
            <Label>{t('recur.days')}</Label>
            <div className="flex flex-wrap gap-1">
              {Array.from({ length: 31 }, (_, i) => i + 1).map((day) => {
                const disabled = day > maxDayAllowed
                return (
                  <button
                    key={day}
                    type="button"
                    disabled={disabled}
                    title={disabled ? t('recur.neverOccurs') : undefined}
                    onClick={() => set({ daysOfMonth: toggle(value.daysOfMonth, day) })}
                    className={
                      pillClass(value.daysOfMonth.includes(day)) +
                      ' w-7 text-center' +
                      (disabled ? ' cursor-not-allowed opacity-30' : '')
                    }
                  >
                    {day}
                  </button>
                )
              })}
            </div>
          </div>
          <div>
            <Label>{t('recur.months')}</Label>
            <div className="flex flex-wrap gap-1">
              {MONTH_NUMBERS.map((month) => (
                <button
                  key={month}
                  type="button"
                  onClick={() => toggleMonth(month)}
                  className={pillClass(value.months.includes(month))}
                >
                  {monthShort(month)}
                </button>
              ))}
            </div>
          </div>
          <p className="text-xs text-muted-foreground">
            {t('recur.skipNote')}
          </p>
        </div>
      )}
    </div>
  )
}
