import type {
  ChoreNotificationInput,
  NotificationOffsetUnit,
  NotificationRecipients,
  NotificationTiming,
  NotificationType,
} from '@/lib/types'
import { IntegerInput, Label, Select } from '@/components/ui/Field'
import { localized, t } from '@/lib/i18n'

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

const DEFAULT_ENTRY: ChoreNotificationInput = {
  type: 'Reminder',
  timing: 'Before',
  offsetValue: 1,
  offsetUnit: 'Hours',
  recipients: 'CurrentAssignee',
}

interface Props {
  value: ChoreNotificationInput[]
  onChange: (next: ChoreNotificationInput[]) => void
  /** Track-mode chore: reminders always go to each person about their own schedule, so the
   * recipients selector is hidden (the choice is moot). */
  independent?: boolean
}

/** Editor for a chore's notification schedule — a list of reminder/due/follow-up entries. */
export function NotificationsEditor({ value, onChange, independent = false }: Props) {
  const update = (i: number, patch: Partial<ChoreNotificationInput>) =>
    onChange(value.map((e, idx) => (idx === i ? { ...e, ...patch } : e)))
  const remove = (i: number) => onChange(value.filter((_, idx) => idx !== i))
  const add = () => onChange([...value, { ...DEFAULT_ENTRY }])

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <Label className="mb-0">{t('notifEdit.title')}</Label>
        <button
          type="button"
          onClick={add}
          className="text-xs text-muted-foreground underline-offset-2 hover:text-foreground hover:underline"
        >
          {t('notifEdit.add')}
        </button>
      </div>

      {value.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('notifEdit.empty')}</p>
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
                  {RECIPIENT_OPTIONS.map((o) => (
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
