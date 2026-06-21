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
    weeksOfMonth: chore?.weeksOfMonth ?? [],
    daysOfMonth: chore?.daysOfMonth ?? [],
    months: chore?.months ?? [],
  })
  const [completionsRequired, setCompletionsRequired] = useState(chore?.completionsRequired ?? 1)
  const [rotateOnEachCompletion, setRotateOnEachCompletion] = useState(chore?.rotateOnEachCompletion ?? false)
  const [assignmentStrategy, setAssignmentStrategy] = useState<AssignmentStrategy>(
    chore?.assignmentStrategy ?? 'LeastCompleted',
  )
  // Per-assignee quotas for track ("Everyone independent") mode, keyed by user id.
  const [trackQuotas, setTrackQuotas] = useState<Record<string, number>>(
    Object.fromEntries((chore?.tracks ?? []).map((t) => [t.user.id, t.completionsRequired])),
  )
  const [schedulingPreference, setSchedulingPreference] = useState<SchedulingPreference>(
    chore?.schedulingPreference ?? 'ToFirstNextRepeat',
  )
  const initialGrace = splitGrace(chore?.graceMinutes)
  // Grace window is opt-in (off by default); only pre-enabled when editing a chore that has one.
  const [graceEnabled, setGraceEnabled] = useState(!!chore?.graceMinutes)
  const [graceValue, setGraceValue] = useState(initialGrace.value)
  const [graceUnit, setGraceUnit] = useState(initialGrace.unit)
  const initialWindow = splitGrace(chore?.completionWindowMinutes)
  const [autoAdvanceEnabled, setAutoAdvanceEnabled] = useState(chore?.autoAdvanceIncomplete ?? false)
  const [windowEnabled, setWindowEnabled] = useState(!!chore?.completionWindowMinutes)
  const [windowValue, setWindowValue] = useState(initialWindow.value)
  const [windowUnit, setWindowUnit] = useState(initialWindow.unit)
  const [startDate, setStartDate] = useState(
    (chore?.startDate ?? new Date().toISOString()).slice(0, 10),
  )
  // Times-of-day list; index 0 doubles as the single "due time" when only one slot is set. Always at
  // least one row (an empty row = "no specific time" / end of day, matching the old single field).
  const [timesOfDay, setTimesOfDay] = useState<string[]>(
    chore?.timesOfDay?.length ? chore.timesOfDay : chore?.dueTime ? [chore.dueTime] : [''],
  )
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
  // Track mode: every assignee gets their own schedule + quota, no rotation, no single current assignee.
  const isIndependent = assignmentStrategy === 'Independent'

  // Smart scheduling is only offered for interval-style repeats (fixed-slot recurrences always hold
  // their grid). If it was selected and the repeat type is no longer interval-style, fall back.
  const intervalStyle = isIntervalStyle(repeatType, recurrence.customMode)
  const effectiveScheduling: SchedulingPreference =
    !intervalStyle && schedulingPreference === 'SmartScheduling' ? 'FromScheduledDate' : schedulingPreference
  const graceUnitMinutes = GRACE_UNITS.find((u) => u.value === graceUnit)?.minutes ?? 24 * 60
  const graceMinutes =
    effectiveScheduling === 'SmartScheduling' && graceEnabled ? Math.max(1, graceValue) * graceUnitMinutes : null
  const windowUnitMinutes = GRACE_UNITS.find((u) => u.value === windowUnit)?.minutes ?? 24 * 60
  const completionWindowMinutes =
    autoAdvanceEnabled && windowEnabled ? Math.max(1, windowValue) * windowUnitMinutes : null
  const showAutoAdvance = !isCustom && !isIndependent && completionsRequired > 1

  // "N times a day" fixed slots are only meaningful for day-resolution schedules.
  const supportsTimes =
    repeatType === 'Daily' ||
    (isCustom && (recurrence.customMode === 'DaysOfWeek' || recurrence.customMode === 'DaysOfMonth'))
  // Resolve the time list into a single due time (when one slot) vs. a multi-slot set (when several).
  const trimmedTimes = (supportsTimes ? timesOfDay : timesOfDay.slice(0, 1)).map((t) => t.trim())
  const uniqueSortedTimes = [...new Set(trimmedTimes.filter(Boolean))].sort()
  const isMultiTime = supportsTimes && uniqueSortedTimes.length > 1
  // Single slot keeps row 0's value verbatim (may be '' = end of day); multi uses the earliest slot
  // as the anchor for the start instant and the stored mirror.
  const primaryTime = isMultiTime ? uniqueSortedTimes[0] : trimmedTimes[0] || ''

  const mutation = useMutation({
    mutationFn: () => {
      const body: ChoreRequest = {
        name,
        description: description.trim() || null,
        emoji: emoji.trim() || '📋',
        points: Number(points) || 0,
        repeatType,
        ...recurrence,
        completionsRequired: isCustom || isIndependent ? 1 : Math.max(1, completionsRequired),
        rotateOnEachCompletion: !isCustom && !isIndependent && completionsRequired > 1 && rotateOnEachCompletion,
        assignmentStrategy,
        schedulingPreference: effectiveScheduling,
        graceMinutes,
        autoAdvanceIncomplete: showAutoAdvance && autoAdvanceEnabled,
        completionWindowMinutes,
        startDate: toLocalDueInstant(startDate, primaryTime),
        dueTime: primaryTime || null,
        timesOfDay: isMultiTime ? uniqueSortedTimes : null,
        assigneeIds,
        currentAssigneeId: isIndependent ? null : currentAssigneeId,
        tagNames: selectedTags,
        notifications,
        tracks: isIndependent
          ? assigneeIds.map((id) => ({ userId: id, completionsRequired: Math.max(1, trackQuotas[id] ?? 1) }))
          : undefined,
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
          {!isCustom && !isIndependent && (
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
        {!isCustom && !isIndependent && completionsRequired > 1 && (
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
        {showAutoAdvance && (
          <div className="-mt-1 rounded-lg bg-accent/50 p-3">
            <label className="flex items-start gap-2 text-sm text-foreground">
              <input
                type="checkbox"
                checked={autoAdvanceEnabled}
                onChange={(e) => setAutoAdvanceEnabled(e.target.checked)}
                className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-ring"
              />
              <span>
                Auto-advance incomplete occurrences
                <span className="block text-xs text-muted-foreground">
                  If not all {completionsRequired} completions are logged, the occurrence expires
                  and automatically moves to the next one.
                </span>
              </span>
            </label>
            {autoAdvanceEnabled && (
              <div className="mt-2 space-y-2 pl-6">
                <label className="flex items-start gap-2 text-sm text-foreground">
                  <input
                    type="checkbox"
                    checked={windowEnabled}
                    onChange={(e) => setWindowEnabled(e.target.checked)}
                    className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-ring"
                  />
                  <span>
                    Delay auto-advance after due date
                    <span className="block text-xs text-muted-foreground">
                      Without this, the occurrence expires as soon as it becomes overdue.
                    </span>
                  </span>
                </label>
                {windowEnabled && (
                  <div className="flex items-center gap-2">
                    <div className="w-16">
                      <IntegerInput
                        value={windowValue}
                        onCommit={(n) => setWindowValue(Math.max(1, n))}
                        aria-label="Completion window amount"
                      />
                    </div>
                    <div className="w-28">
                      <Select
                        value={windowUnit}
                        onChange={(e) => setWindowUnit(e.target.value)}
                        aria-label="Completion window unit"
                      >
                        {GRACE_UNITS.map((u) => (
                          <option key={u.value} value={u.value}>{u.label}</option>
                        ))}
                      </Select>
                    </div>
                    <span className="text-sm text-muted-foreground">after due date</span>
                  </div>
                )}
              </div>
            )}
          </div>
        )}
        <div className="flex gap-3">
          <div className="flex-1">
            <Label htmlFor="start">Start date</Label>
            <Input id="start" type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} required />
          </div>
          {!supportsTimes && (
            <div className="flex-1">
              <Label htmlFor="dueTime">Due time</Label>
              <TimeField
                id="dueTime"
                value={timesOfDay[0] ?? ''}
                onChange={(v) => setTimesOfDay((prev) => [v, ...prev.slice(1)])}
              />
            </div>
          )}
        </div>
        {repeatType === 'Custom' && (
          <RecurrenceEditor value={recurrence} onChange={setRecurrence} />
        )}
        {supportsTimes && (
          <div className="space-y-2 rounded-lg border border-border bg-accent/40 p-3">
            <Label className="mb-0">Times of day</Label>
            <p className="-mt-0.5 text-xs text-muted-foreground">
              Due at each of these times — every slot is its own to-do (e.g. 08:00 and 20:00 for twice
              a day). Leave a single time for a once-a-day chore.
            </p>
            <div className="space-y-1.5">
              {timesOfDay.map((t, i) => (
                <div key={i} className="flex items-center gap-2">
                  <div className="w-32">
                    <TimeField
                      value={t}
                      onChange={(v) => setTimesOfDay((prev) => prev.map((x, j) => (j === i ? v : x)))}
                      aria-label={`Time ${i + 1}`}
                    />
                  </div>
                  {timesOfDay.length > 1 && (
                    <button
                      type="button"
                      onClick={() => setTimesOfDay((prev) => prev.filter((_, j) => j !== i))}
                      className="text-xs text-muted-foreground underline-offset-2 hover:text-destructive hover:underline"
                    >
                      Remove
                    </button>
                  )}
                </div>
              ))}
            </div>
            <button
              type="button"
              onClick={() => setTimesOfDay((prev) => [...prev, ''])}
              className="text-xs text-primary underline-offset-2 hover:underline"
            >
              + Add another time
            </button>
          </div>
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
            <label className="flex items-start gap-2 text-sm text-foreground">
              <input
                type="checkbox"
                checked={graceEnabled}
                onChange={(e) => setGraceEnabled(e.target.checked)}
                className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-ring"
              />
              <span>
                Reset schedule when completed early (grace window)
                <span className="block text-xs text-muted-foreground">
                  If completed more than this early, reset the next due date to the completion date
                  instead of holding the schedule.
                </span>
              </span>
            </label>
            {graceEnabled && (
              <div className="mt-3 flex items-center gap-2">
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
            )}
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
        {isIndependent ? (
          <div>
            <Label>Per-person counts</Label>
            <p className="-mt-0.5 mb-1.5 text-xs text-muted-foreground">
              Each person does it on their own schedule, so one being late never blocks the others.
            </p>
            {selectedAssignees.length === 0 ? (
              <p className="text-sm text-muted-foreground">Select assignees above.</p>
            ) : (
              <div className="space-y-1.5">
                {selectedAssignees.map((u) => (
                  <div key={u.id} className="flex items-center justify-between gap-2">
                    <span className="flex items-center gap-1.5 text-sm text-foreground">
                      <Avatar color={u.avatarColor} name={u.displayName} size={16} />
                      {u.displayName}
                    </span>
                    <div className="flex items-center gap-1.5">
                      <div className="w-16">
                        <IntegerInput
                          value={trackQuotas[u.id] ?? 1}
                          onCommit={(n) =>
                            setTrackQuotas((prev) => ({ ...prev, [u.id]: Math.max(1, n) }))
                          }
                          aria-label={`Times for ${u.displayName}`}
                        />
                      </div>
                      <span className="text-xs text-muted-foreground">×</span>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        ) : (
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
        )}
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
            <p className="text-sm text-muted-foreground">No tags yet. Add them in Settings.</p>
          )}
        </div>
        <NotificationsEditor value={notifications} onChange={setNotifications} independent={isIndependent} />
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
