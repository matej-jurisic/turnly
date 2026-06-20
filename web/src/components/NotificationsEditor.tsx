import { useEffect, useState } from 'react'
import type {
  ChoreNotificationInput,
  NotificationOffsetUnit,
  NotificationRecipients,
  NotificationTiming,
  NotificationType,
} from '@/lib/types'
import { Input, Label, Select } from '@/components/ui/Field'

/**
 * Number input that lets the field go empty while typing (so you can clear "1" and type "5")
 * and only commits a valid integer ≥ 1, falling back to 1 on blur if left empty/invalid.
 */
function OffsetInput({ value, onCommit }: { value: number; onCommit: (n: number) => void }) {
  const [draft, setDraft] = useState(String(value))

  // Keep the field in sync when the value changes from outside (e.g. switching entries).
  useEffect(() => setDraft(String(value)), [value])

  return (
    <Input
      type="number"
      min={1}
      value={draft}
      aria-label="Offset value"
      onChange={(e) => {
        setDraft(e.target.value)
        const n = Number(e.target.value)
        if (e.target.value !== '' && Number.isFinite(n) && n >= 1) onCommit(Math.floor(n))
      }}
      onBlur={() => {
        const n = Number(draft)
        const next = draft !== '' && Number.isFinite(n) && n >= 1 ? Math.floor(n) : 1
        setDraft(String(next))
        onCommit(next)
      }}
    />
  )
}

const TYPE_OPTIONS: { value: NotificationType; label: string }[] = [
  { value: 'Reminder', label: 'Reminder' },
  { value: 'Due', label: 'Due' },
  { value: 'FollowUp', label: 'Follow-up' },
]

const TIMING_OPTIONS: { value: NotificationTiming; label: string }[] = [
  { value: 'Before', label: 'before due' },
  { value: 'AtDue', label: 'at due time' },
  { value: 'After', label: 'after due' },
]

const UNIT_OPTIONS: { value: NotificationOffsetUnit; label: string }[] = [
  { value: 'Minutes', label: 'minutes' },
  { value: 'Hours', label: 'hours' },
  { value: 'Days', label: 'days' },
]

const RECIPIENT_OPTIONS: { value: NotificationRecipients; label: string }[] = [
  { value: 'CurrentAssignee', label: 'Current assignee' },
  { value: 'AllAssignees', label: 'All assignees' },
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
}

/** Editor for a chore's notification schedule — a list of reminder/due/follow-up entries. */
export function NotificationsEditor({ value, onChange }: Props) {
  const update = (i: number, patch: Partial<ChoreNotificationInput>) =>
    onChange(value.map((e, idx) => (idx === i ? { ...e, ...patch } : e)))
  const remove = (i: number) => onChange(value.filter((_, idx) => idx !== i))
  const add = () => onChange([...value, { ...DEFAULT_ENTRY }])

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <Label className="mb-0">Notifications</Label>
        <button
          type="button"
          onClick={add}
          className="text-xs text-muted-foreground underline-offset-2 hover:text-foreground hover:underline"
        >
          + Add
        </button>
      </div>

      {value.length === 0 ? (
        <p className="text-sm text-muted-foreground">No notifications — add one to send push reminders.</p>
      ) : (
        <div className="space-y-2">
          {value.map((entry, i) => (
            <div key={i} className="space-y-2 rounded-lg border border-border bg-accent/40 p-3">
              <div className="flex gap-2">
                <div className="flex-1">
                  <Select
                    value={entry.type}
                    onChange={(e) => update(i, { type: e.target.value as NotificationType })}
                    aria-label="Notification type"
                  >
                    {TYPE_OPTIONS.map((o) => (
                      <option key={o.value} value={o.value}>{o.label}</option>
                    ))}
                  </Select>
                </div>
                <button
                  type="button"
                  onClick={() => remove(i)}
                  aria-label="Remove notification"
                  className="px-1 text-muted-foreground transition-colors hover:text-destructive"
                >
                  ✕
                </button>
              </div>

              <div className="flex items-center gap-2">
                {entry.timing !== 'AtDue' && (
                  <div className="w-14">
                    <OffsetInput
                      value={entry.offsetValue}
                      onCommit={(n) => update(i, { offsetValue: n })}
                    />
                  </div>
                )}
                {entry.timing !== 'AtDue' && (
                  <div className="flex-1">
                    <Select
                      value={entry.offsetUnit}
                      onChange={(e) => update(i, { offsetUnit: e.target.value as NotificationOffsetUnit })}
                      aria-label="Offset unit"
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
                    aria-label="Notification timing"
                  >
                    {TIMING_OPTIONS.map((o) => (
                      <option key={o.value} value={o.value}>{o.label}</option>
                    ))}
                  </Select>
                </div>
              </div>

              <Select
                value={entry.recipients}
                onChange={(e) => update(i, { recipients: e.target.value as NotificationRecipients })}
                aria-label="Notification recipients"
              >
                {RECIPIENT_OPTIONS.map((o) => (
                  <option key={o.value} value={o.value}>{o.label}</option>
                ))}
              </Select>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
