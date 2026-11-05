import type {
  ChoreNotificationInput,
  NotificationOffsetUnit,
  NotificationRecipients,
  NotificationTiming,
  NotificationType,
} from '@/lib/types'
import { IntegerInput, Label, Select } from '@/components/ui/Field'
import { localized, t } from '@/lib/i18n'
import { notificationTimingLabel } from '@/lib/chore-format'

const TYPE_OPTIONS: { value: NotificationType; label: string }[] = [
  localized({ value: 'Reminder' as const }, { label: 'notifType.Reminder' }),
  localized({ value: 'Due' as const }, { label: 'notifType.Due' }),
  localized({ value: 'FollowUp' as const }, { label: 'notifType.FollowUp' }),
]

const TIMING_OPTIONS: { value: NotificationTiming; label: string }[] = [
  localized({ value: 'Before' as const }, { label: 'notifEdit.before' }),
  localized({ value: 'AtDue' as const }, { label: 'notifTiming.atDue' }),
  localized({ value: 'After' as const }, { label: 'notifEdit.after' }),
]

const UNIT_OPTIONS: { value: NotificationOffsetUnit; label: string }[] = [
  localized({ value: 'Minutes' as const }, { label: 'unit.minutes' }),
  localized({ value: 'Hours' as const }, { label: 'unit.hours' }),
  localized({ value: 'Days' as const }, { label: 'unit.days' }),
]

const RECIPIENT_OPTIONS: { value: NotificationRecipients; label: string }[] = [
  localized({ value: 'CurrentAssignee' as const }, { label: 'form.currentAssignee' }),
  localized({ value: 'AllAssignees' as const }, { label: 'notifEdit.allAssignees' }),
]

const PERSONAL_RECIPIENT_OPTIONS: { value: NotificationRecipients; label: string }[] = [
  localized({ value: 'CurrentAssignee' as const }, { label: 'notifEdit.myTurn' }),
  localized({ value: 'AllAssignees' as const }, { label: 'notifEdit.always' }),
]

const DEFAULT_ENTRY: ChoreNotificationInput = {
  type: 'Reminder',
  timing: 'Before',
  offsetValue: 1,
  offsetUnit: 'Hours',
  recipients: 'CurrentAssignee',
}

const entry = (
  type: NotificationType, timing: NotificationTiming, offsetValue = 0, offsetUnit: NotificationOffsetUnit = 'Hours',
): ChoreNotificationInput => ({ type, timing, offsetValue, offsetUnit, recipients: 'CurrentAssignee' })

/** Common combinations offered as a starting point; picking one replaces the list. */
const TEMPLATES: ChoreNotificationInput[][] = [
  [entry('Reminder', 'Before', 1)],
  [entry('Reminder', 'Before', 1), entry('Due', 'AtDue')],
  [entry('Reminder', 'Before', 1, 'Days'), entry('Due', 'AtDue')],
  [entry('Due', 'AtDue'), entry('FollowUp', 'After', 1)],
  [entry('Reminder', 'Before', 1, 'Days'), entry('Reminder', 'Before', 1), entry('Due', 'AtDue'), entry('FollowUp', 'After', 2)],
]

/** "1 day before due, at due time", read off the entries so it can't drift from what gets applied. */
function templateLabel(entries: ChoreNotificationInput[]): string {
  const text = entries.map(notificationTimingLabel).join(', ')
  return text.charAt(0).toUpperCase() + text.slice(1)
}

interface Props {
  value: ChoreNotificationInput[]
  onChange: (next: ChoreNotificationInput[]) => void
  /** Track-mode chore: reminders always go to each person about their own schedule, so the
   * recipients selector is hidden (the choice is moot). */
  independent?: boolean
  /** A user's own entries: they only ever reach that user, so recipients read as when they fire. */
  personal?: boolean
}

/** Editor for a chore's notification schedule — a list of reminder/due/follow-up entries. */
export function NotificationsEditor({ value, onChange, independent = false, personal = false }: Props) {
  const update = (i: number, patch: Partial<ChoreNotificationInput>) =>
    onChange(value.map((e, idx) => (idx === i ? { ...e, ...patch } : e)))
  const remove = (i: number) => onChange(value.filter((_, idx) => idx !== i))
  const add = () => onChange([...value, { ...DEFAULT_ENTRY }])

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between gap-3">
        <Label className="mb-0 shrink-0">{t('notifEdit.title')}</Label>
        <div className="flex min-w-0 flex-1 items-center justify-end gap-3">
          <select
            value=""
            onChange={(e) => {
              const picked = TEMPLATES[Number(e.target.value)]
              if (picked) onChange(picked.map((n) => ({ ...n })))
            }}
            aria-label={t('notifEdit.template')}
            className="h-8 min-w-0 max-w-44 flex-1 rounded-md border border-input bg-card px-2 text-xs text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          >
            <option value="" disabled>{t('notifEdit.template')}</option>
            {TEMPLATES.map((entries, i) => (
              <option key={i} value={i}>{templateLabel(entries)}</option>
            ))}
          </select>
          <button
            type="button"
            onClick={add}
            className="shrink-0 text-xs text-muted-foreground underline-offset-2 hover:text-foreground hover:underline"
          >
            {t('notifEdit.add')}
          </button>
        </div>
      </div>

      {value.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t(personal ? 'notifEdit.personalEmpty' : 'notifEdit.empty')}</p>
      ) : (
        <div className="space-y-2">
          {value.map((entry, i) => (
            <div key={i} className="space-y-2 rounded-lg border border-border bg-accent/40 p-3">
              <div className="flex gap-2">
                <div className="flex-1">
                  <Select
                    value={entry.type}
                    onChange={(e) => update(i, { type: e.target.value as NotificationType })}
                    aria-label={t('notifEdit.type')}
                  >
                    {TYPE_OPTIONS.map((o) => (
                      <option key={o.value} value={o.value}>{o.label}</option>
                    ))}
                  </Select>
                </div>
                <button
                  type="button"
                  onClick={() => remove(i)}
                  aria-label={t('notifEdit.remove')}
                  className="px-1 text-muted-foreground transition-colors hover:text-destructive"
                >
                  ✕
                </button>
              </div>

              <div className="flex items-center gap-2">
                {entry.timing !== 'AtDue' && (
                  <div className="w-20 shrink-0">
                    <IntegerInput
                      value={entry.offsetValue}
                      onCommit={(n) => update(i, { offsetValue: n })}
                      aria-label={t('notifEdit.offsetValue')}
                    />
                  </div>
                )}
                {entry.timing !== 'AtDue' && (
                  <div className="flex-1">
                    <Select
                      value={entry.offsetUnit}
                      onChange={(e) => update(i, { offsetUnit: e.target.value as NotificationOffsetUnit })}
                      aria-label={t('notifEdit.offsetUnit')}
                    >
                      {UNIT_OPTIONS.map((o) => (
                        <option key={o.value} value={o.value}>{o.label}</option>
                      ))}
                    </Select>
                  </div>
                )}
                <div className="flex-1">
                  <Select
                    value={entry.timing}
                    onChange={(e) => update(i, { timing: e.target.value as NotificationTiming })}
                    aria-label={t('notifEdit.timing')}
                  >
                    {TIMING_OPTIONS.map((o) => (
                      <option key={o.value} value={o.value}>{o.label}</option>
                    ))}
                  </Select>
                </div>
              </div>

              {independent ? (
                <p className="text-xs text-muted-foreground">{t('notifEdit.independent')}</p>
              ) : (
                <Select
                  value={entry.recipients}
                  onChange={(e) => update(i, { recipients: e.target.value as NotificationRecipients })}
                  aria-label={t('notifEdit.recipients')}
                >
                  {(personal ? PERSONAL_RECIPIENT_OPTIONS : RECIPIENT_OPTIONS).map((o) => (
                    <option key={o.value} value={o.value}>{o.label}</option>
                  ))}
                </Select>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
