import type {
  CustomRecurrenceMode,
  FrequencyPeriod,
  RecurrenceFields,
  RecurrenceUnit,
  Weekday,
} from '@/lib/types'
import { Input, Label, Select } from '@/components/ui/Field'

const MODE_OPTIONS: { value: CustomRecurrenceMode; label: string }[] = [
  { value: 'Interval', label: 'Every…' },
  { value: 'DaysOfWeek', label: 'Days of week' },
  { value: 'DaysOfMonth', label: 'Days of month' },
  { value: 'Frequency', label: 'Times per period' },
]

const UNIT_OPTIONS: { value: RecurrenceUnit; label: string }[] = [
  { value: 'Day', label: 'days' },
  { value: 'Week', label: 'weeks' },
  { value: 'Month', label: 'months' },
  { value: 'Year', label: 'years' },
]

const PERIOD_OPTIONS: { value: FrequencyPeriod; label: string }[] = [
  { value: 'Day', label: 'day' },
  { value: 'Week', label: 'week' },
  { value: 'Month', label: 'month' },
  { value: 'Year', label: 'year' },
]

// Monday-first display, but values are the .NET DayOfWeek names the API expects.
const WEEKDAYS: { value: Weekday; label: string }[] = [
  { value: 'Monday', label: 'Mon' },
  { value: 'Tuesday', label: 'Tue' },
  { value: 'Wednesday', label: 'Wed' },
  { value: 'Thursday', label: 'Thu' },
  { value: 'Friday', label: 'Fri' },
  { value: 'Saturday', label: 'Sat' },
  { value: 'Sunday', label: 'Sun' },
]

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

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
        <Label htmlFor="custom-mode">Custom recurrence</Label>
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
          <span className="pb-2 text-sm text-muted-foreground">Every</span>
          <div className="w-20">
            <Input
              type="number"
              min={1}
              value={value.intervalCount ?? 1}
              onChange={(e) => set({ intervalCount: Number(e.target.value) || 1 })}
              aria-label="Interval count"
            />
          </div>
          <div className="flex-1">
            <Select
              value={value.intervalUnit ?? 'Week'}
              onChange={(e) => set({ intervalUnit: e.target.value as RecurrenceUnit })}
              aria-label="Interval unit"
            >
              {UNIT_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>{o.label}</option>
              ))}
            </Select>
          </div>
        </div>
      )}

      {mode === 'DaysOfWeek' && (
        <div className="flex flex-wrap gap-1">
          {WEEKDAYS.map((d) => (
            <button
              key={d.value}
              type="button"
              onClick={() => set({ weekdays: toggle(value.weekdays, d.value) })}
              className={pillClass(value.weekdays.includes(d.value))}
            >
              {d.label}
            </button>
          ))}
        </div>
      )}

      {mode === 'DaysOfMonth' && (
        <div className="space-y-2">
          <div>
            <Label>Days</Label>
            <div className="flex flex-wrap gap-1">
              {Array.from({ length: 31 }, (_, i) => i + 1).map((day) => {
                const disabled = day > maxDayAllowed
                return (
                  <button
                    key={day}
                    type="button"
                    disabled={disabled}
                    title={disabled ? 'Never occurs in the selected months' : undefined}
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
            <Label>Months</Label>
            <div className="flex flex-wrap gap-1">
              {MONTHS.map((label, i) => (
                <button
                  key={label}
                  type="button"
                  onClick={() => toggleMonth(i + 1)}
                  className={pillClass(value.months.includes(i + 1))}
                >
                  {label}
                </button>
              ))}
            </div>
          </div>
          <p className="text-xs text-muted-foreground">
            Days that don't exist in a selected month are skipped (e.g. the 31st only fires in
            months that have one).
          </p>
        </div>
      )}

      {mode === 'Frequency' && (
        <div className="flex items-end gap-2">
          <div className="w-20">
            <Input
              type="number"
              min={1}
              value={value.frequencyCount ?? 1}
              onChange={(e) => set({ frequencyCount: Number(e.target.value) || 1 })}
              aria-label="Times per period"
            />
          </div>
          <span className="pb-2 text-sm text-muted-foreground">times per</span>
          <div className="flex-1">
            <Select
              value={value.frequencyPeriod ?? 'Week'}
              onChange={(e) => set({ frequencyPeriod: e.target.value as FrequencyPeriod })}
              aria-label="Frequency period"
            >
              {PERIOD_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>{o.label}</option>
              ))}
            </Select>
          </div>
        </div>
      )}
    </div>
  )
}
