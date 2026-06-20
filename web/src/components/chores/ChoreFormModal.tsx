import { useState } from 'react'
import { useQuery, useMutation } from '@tanstack/react-query'
import { choresApi, tagsApi, usersApi, ApiError } from '@/lib/api'
import type {
  AssignmentStrategy,
  Chore,
  ChoreNotificationInput,
  ChoreRequest,
  RecurrenceFields,
  RepeatType,
  SchedulingPreference,
  User,
} from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { Input, IntegerInput, Label, Select } from '@/components/ui/Field'
import { TimeField } from '@/components/ui/TimeField'
import { Modal, Avatar } from '@/components/ui/Modal'
import { RecurrenceEditor } from '@/components/RecurrenceEditor'
import { NotificationsEditor } from '@/components/NotificationsEditor'
import {
  REPEAT_OPTIONS, STRATEGY_OPTIONS, SCHEDULING_OPTIONS, GRACE_UNITS,
  isIntervalStyle, splitGrace, toLocalDueInstant,
} from '@/lib/chore-format'

interface ChoreFormModalProps {
  title: string
  chore?: Chore
  onClose: () => void
  onSaved: () => void
}

export function ChoreFormModal({ title, chore, onClose, onSaved }: ChoreFormModalProps) {
  const isEdit = Boolean(chore)
  const { data: allUsers } = useQuery({ queryKey: ['chore-users'], queryFn: usersForChores })
  const { data: allTags } = useQuery({ queryKey: ['tags'], queryFn: tagsApi.list })

  const [name, setName] = useState(chore?.name ?? '')
  const [description, setDescription] = useState(chore?.description ?? '')
  const [emoji, setEmoji] = useState(chore?.emoji ?? '')
  const [points, setPoints] = useState(String(chore?.points ?? 0))
  const [repeatType, setRepeatType] = useState<RepeatType>(chore?.repeatType ?? 'OneTime')
  const [recurrence, setRecurrence] = useState<RecurrenceFields>({
    customMode: chore?.customMode ?? 'Interval',
    intervalCount: chore?.intervalCount ?? 1,
    intervalUnit: chore?.intervalUnit ?? 'Week',
    weekdays: chore?.weekdays ?? [],
    daysOfMonth: chore?.daysOfMonth ?? [],
    months: chore?.months ?? [],
  })
  const [completionsRequired, setCompletionsRequired] = useState(chore?.completionsRequired ?? 1)
  const [rotateOnEachCompletion, setRotateOnEachCompletion] = useState(chore?.rotateOnEachCompletion ?? false)
  const [assignmentStrategy, setAssignmentStrategy] = useState<AssignmentStrategy>(
    chore?.assignmentStrategy ?? 'LeastCompleted',
  )
  const [schedulingPreference, setSchedulingPreference] = useState<SchedulingPreference>(
    chore?.schedulingPreference ?? 'ToFirstNextRepeat',
  )
  const initialGrace = splitGrace(chore?.graceMinutes)
  const [graceValue, setGraceValue] = useState(initialGrace.value)
  const [graceUnit, setGraceUnit] = useState(initialGrace.unit)
  const [startDate, setStartDate] = useState(
    (chore?.startDate ?? new Date().toISOString()).slice(0, 10),
  )
  const [dueTime, setDueTime] = useState(chore?.dueTime ?? '')
  const [assigneeIds, setAssigneeIds] = useState<string[]>(
    chore?.assignees.map((a) => a.id) ?? [],
  )
  const [currentAssigneeId, setCurrentAssigneeId] = useState(chore?.currentAssignee?.id ?? '')
  const [selectedTags, setSelectedTags] = useState<string[]>(chore?.tags ?? [])
  const [notifications, setNotifications] = useState<ChoreNotificationInput[]>(
    chore?.notifications.map((n) => ({
      type: n.type,
      timing: n.timing,
      offsetValue: n.offsetValue,
      offsetUnit: n.offsetUnit,
      recipients: n.recipients,
    })) ?? [],
  )
  const [error, setError] = useState<string | null>(null)

  const toggleAssignee = (id: string) => {
    setAssigneeIds((prev) => {
      const next = prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]
      if (!next.includes(currentAssigneeId)) setCurrentAssigneeId(next[0] ?? '')
      return next
    })
  }

  // "Complete N times" only rides on the non-custom repeat types; custom recurrences are always 1.
  const isCustom = repeatType === 'Custom'

  // Smart scheduling is only offered for interval-style repeats (fixed-slot recurrences always hold
  // their grid). If it was selected and the repeat type is no longer interval-style, fall back.
  const intervalStyle = isIntervalStyle(repeatType, recurrence.customMode)
  const effectiveScheduling: SchedulingPreference =
    !intervalStyle && schedulingPreference === 'SmartScheduling' ? 'FromScheduledDate' : schedulingPreference
  const graceUnitMinutes = GRACE_UNITS.find((u) => u.value === graceUnit)?.minutes ?? 24 * 60
  const graceMinutes =
    effectiveScheduling === 'SmartScheduling' ? Math.max(1, graceValue) * graceUnitMinutes : null

  const mutation = useMutation({
    mutationFn: () => {
      const body: ChoreRequest = {
        name,
        description: description.trim() || null,
        emoji: emoji.trim() || '📋',
        points: Number(points) || 0,
        repeatType,
        ...recurrence,
        completionsRequired: isCustom ? 1 : Math.max(1, completionsRequired),
        rotateOnEachCompletion: !isCustom && completionsRequired > 1 && rotateOnEachCompletion,
        assignmentStrategy,
        schedulingPreference: effectiveScheduling,
        graceMinutes,
        startDate: toLocalDueInstant(startDate, dueTime),
        dueTime: dueTime || null,
        assigneeIds,
        currentAssigneeId,
        tagNames: selectedTags,
        notifications,
      }
      return isEdit && chore ? choresApi.update(chore.id, body) : choresApi.create(body)
    },
    onSuccess: onSaved,
    onError: (err) => setError(err instanceof ApiError ? err.message : 'Save failed'),
  })

  const selectedAssignees = (allUsers ?? []).filter((u) => assigneeIds.includes(u.id))

  return (
    <Modal title={title} onClose={onClose}>
      <form
        onSubmit={(e) => {
          e.preventDefault()
          if (assigneeIds.length === 0) {
            setError('Select at least one assignee.')
            return
          }
          setError(null)
          mutation.mutate()
        }}
        className="max-h-[70vh] space-y-4 overflow-y-auto pl-1 -ml-1 pr-4 -mr-4"
      >
        <div>
          <Label htmlFor="name">Name</Label>
          <Input id="name" value={name} onChange={(e) => setName(e.target.value)} required />
        </div>
        <div className="flex gap-3">
          <div className="flex-1">
            <Label htmlFor="emoji">Emoji</Label>
            <Input id="emoji" value={emoji} onChange={(e) => setEmoji(e.target.value)} />
          </div>
          <div className="flex-1">
            <Label htmlFor="points">Points</Label>
            <Input id="points" type="number" min={0} value={points} onChange={(e) => setPoints(e.target.value)} />
          </div>
        </div>
        <div>
          <Label htmlFor="description">Description</Label>
          <Input id="description" value={description} onChange={(e) => setDescription(e.target.value)} />
        </div>
        <div className="flex gap-3">
          <div className="flex-1">
            <Label htmlFor="repeat">Repeat</Label>
            <Select id="repeat" value={repeatType} onChange={(e) => setRepeatType(e.target.value as RepeatType)}>
              {REPEAT_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>{o.label}</option>
              ))}
            </Select>
          </div>
          {!isCustom && (
            <div className="w-28">
              <Label htmlFor="times">Times</Label>
              <IntegerInput
                id="times"
                value={completionsRequired}
                onCommit={(n) => setCompletionsRequired(Math.max(1, n))}
                aria-label="Completions needed per occurrence"
              />
            </div>
          )}
        </div>
        {!isCustom && completionsRequired > 1 && (
          <div className="-mt-1 space-y-2">
            <p className="text-xs text-muted-foreground">
              Must be completed {completionsRequired} times before it's due again.
            </p>
            <label className="flex items-start gap-2 text-sm text-foreground">
              <input
                type="checkbox"
                checked={rotateOnEachCompletion}
                onChange={(e) => setRotateOnEachCompletion(e.target.checked)}
                className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-ring"
              />
              <span>
                Rotate assignee after each completion
                <span className="block text-xs text-muted-foreground">
                  Otherwise the assignee only changes once all {completionsRequired} are done.
                </span>
              </span>
            </label>
          </div>
        )}
        <div className="flex gap-3">
          <div className="flex-1">
            <Label htmlFor="start">Start date</Label>
            <Input id="start" type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} required />
          </div>
          <div className="flex-1">
            <Label htmlFor="dueTime">Due time</Label>
            <TimeField id="dueTime" value={dueTime} onChange={setDueTime} />
          </div>
        </div>
        {repeatType === 'Custom' && (
          <RecurrenceEditor value={recurrence} onChange={setRecurrence} />
        )}
        {repeatType !== 'OneTime' && (
          <div className="flex gap-3">
            <div className="flex-1">
              <Label htmlFor="strategy">Assignment</Label>
              <Select
                id="strategy"
                value={assignmentStrategy}
                onChange={(e) => setAssignmentStrategy(e.target.value as AssignmentStrategy)}
              >
                {STRATEGY_OPTIONS.map((o) => (
                  <option key={o.value} value={o.value}>{o.label}</option>
                ))}
              </Select>
            </div>
            <div className="flex-1">
              <Label htmlFor="scheduling">Next due</Label>
              <Select
                id="scheduling"
                value={effectiveScheduling}
                onChange={(e) => setSchedulingPreference(e.target.value as SchedulingPreference)}
              >
                {SCHEDULING_OPTIONS.filter((o) => o.value !== 'SmartScheduling' || intervalStyle).map((o) => (
                  <option key={o.value} value={o.value}>{o.label}</option>
                ))}
              </Select>
            </div>
          </div>
        )}
        {repeatType !== 'OneTime' && effectiveScheduling === 'SmartScheduling' && (
          <div className="-mt-1 rounded-lg bg-accent/50 p-3">
            <Label className="mb-1">Grace window</Label>
            <div className="flex items-center gap-2">
              <div className="w-16">
                <IntegerInput
                  value={graceValue}
                  onCommit={(n) => setGraceValue(Math.max(1, n))}
                  aria-label="Grace window amount"
                />
              </div>
              <div className="w-28">
                <Select
                  value={graceUnit}
                  onChange={(e) => setGraceUnit(e.target.value)}
                  aria-label="Grace window unit"
                >
                  {GRACE_UNITS.map((u) => (
                    <option key={u.value} value={u.value}>{u.label}</option>
                  ))}
                </Select>
              </div>
            </div>
            <p className="mt-2 text-xs text-muted-foreground">
              If completed more than this early, reset the next due date to the completion date instead
              of holding the schedule.
            </p>
          </div>
        )}
        <div>
          <div className="mb-1 flex items-center justify-between">
            <Label className="mb-0">Assignees</Label>
            {(allUsers ?? []).length > 1 && (() => {
              const all = (allUsers ?? []).map((u) => u.id)
              const allSelected = all.every((id) => assigneeIds.includes(id))
              return (
                <button
                  type="button"
                  onClick={() => {
                    if (allSelected) {
                      setAssigneeIds([])
                      setCurrentAssigneeId('')
                    } else {
                      setAssigneeIds(all)
                      if (!all.includes(currentAssigneeId)) setCurrentAssigneeId(all[0] ?? '')
                    }
                  }}
                  className="text-xs text-muted-foreground underline-offset-2 hover:text-foreground hover:underline"
                >
                  {allSelected ? 'Deselect all' : 'Everyone'}
                </button>
              )
            })()}
          </div>
          <div className="flex flex-wrap gap-1">
            {(allUsers ?? []).map((u) => (
              <button
                key={u.id}
                type="button"
                onClick={() => toggleAssignee(u.id)}
                className={
                  'flex items-center gap-1.5 rounded-md px-2 py-1 text-xs transition-colors ' +
                  (assigneeIds.includes(u.id)
                    ? 'bg-primary/10 text-primary ring-1 ring-primary'
                    : 'bg-accent text-muted-foreground hover:text-foreground')
                }
              >
                <Avatar color={u.avatarColor} name={u.displayName} size={16} />
                {u.displayName}
              </button>
            ))}
          </div>
        </div>
        <div>
          <Label htmlFor="current">Current assignee</Label>
          <Select
            id="current"
            value={currentAssigneeId}
            onChange={(e) => setCurrentAssigneeId(e.target.value)}
            disabled={selectedAssignees.length === 0}
          >
            <option value="" disabled>Select an assignee</option>
            {selectedAssignees.map((u) => (
              <option key={u.id} value={u.id}>{u.displayName}</option>
            ))}
          </Select>
        </div>
        <div>
          <Label>Tags</Label>
          {allTags && allTags.length > 0 ? (
            <div className="flex flex-wrap gap-1">
              {allTags.map((t) => (
                <button
                  key={t.id}
                  type="button"
                  onClick={() => setSelectedTags((prev) =>
                    prev.includes(t.name) ? prev.filter((n) => n !== t.name) : [...prev, t.name]
                  )}
                  className={
                    'rounded-md px-2 py-1 text-xs transition-colors ' +
                    (selectedTags.includes(t.name)
                      ? 'bg-primary/10 text-primary ring-1 ring-primary'
                      : 'bg-accent text-muted-foreground hover:text-foreground')
                  }
                >
                  {t.name}
                </button>
              ))}
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">No tags yet — add them in Settings.</p>
          )}
        </div>
        <NotificationsEditor value={notifications} onChange={setNotifications} />
        {error && <p className="text-sm text-destructive">{error}</p>}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>Cancel</Button>
          <Button type="submit" disabled={mutation.isPending}>
            {mutation.isPending ? 'Saving…' : 'Save'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}

// Members need to see the user list to pick assignees, but the /users endpoint is admin-only.
// Assignees are also embedded in each chore, so derive the roster from chores for non-admins;
// admins get the full list.
async function usersForChores(): Promise<User[]> {
  try {
    return await usersApi.list()
  } catch {
    const chores = await choresApi.list()
    const map = new Map<string, User>()
    for (const c of chores) for (const a of c.assignees) map.set(a.id, a)
    return [...map.values()].sort((a, b) => a.displayName.localeCompare(b.displayName))
  }
}
