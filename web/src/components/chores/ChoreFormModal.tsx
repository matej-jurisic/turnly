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
  RotationPeriod,
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
  isIntervalStyle, rotationPeriodOptions, splitGrace, toLocalDueInstant, toLocalDateInput,
} from '@/lib/chore-format'
import { t } from '@/lib/i18n'

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
  const [rotationPeriod, setRotationPeriod] = useState<RotationPeriod>(chore?.rotationPeriod ?? 'EveryOccurrence')
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
    toLocalDateInput(chore?.startDate ? new Date(chore.startDate) : new Date()),
  )
  // Times-of-day list; index 0 doubles as the single "due time" when only one slot is set. Always at
  // least one row (an empty row = "no specific time" / end of day, matching the old single field).
  const [timesOfDay, setTimesOfDay] = useState<string[]>(
    chore?.timesOfDay?.length ? chore.timesOfDay : chore?.dueTime ? [chore.dueTime] : [''],
  )
  // Optional slot names ("Morning"), index-aligned with `timesOfDay` so a label follows its row
  // through edits and removals rather than being keyed by a time that is still being typed.
  const [timeLabels, setTimeLabels] = useState<string[]>(
    (chore?.timesOfDay ?? []).map((t) => chore?.timeLabels?.[t] ?? ''),
  )
  const [assigneeIds, setAssigneeIds] = useState<string[]>(
    chore?.assignees.map((a) => a.id) ?? [],
  )
  const [currentAssigneeId, setCurrentAssigneeId] = useState(chore?.currentAssignee?.id ?? '')
  // Which of the current assignee's pairs holds the turn ('' = none chosen; see `currentUnit`).
  const [currentPartnerId, setCurrentPartnerId] = useState(chore?.currentPartner?.id ?? '')
  // Fixed pairs who do the chore together. Kept as entered; a pair only counts while both of its
  // people are still selected as assignees (see `activePairs`).
  const [pairs, setPairs] = useState<string[][]>(chore?.pairs ?? [])
  const [pairDraft, setPairDraft] = useState<[string, string]>(['', ''])
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

  // Manual chores may sit unassigned ('' in the picker) until an admin places them.
  const isManual = assignmentStrategy === 'Manual'
  const keepUnassigned = isManual && currentAssigneeId === ''

  const toggleAssignee = (id: string) => {
    setAssigneeIds((prev) => {
      const next = prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]
      if (!keepUnassigned && !next.includes(currentAssigneeId)) setCurrentAssigneeId(next[0] ?? '')
      return next
    })
  }

  // "Complete N times" only rides on the non-custom repeat types; custom recurrences are always 1.
  const isCustom = repeatType === 'Custom'
  // On demand: no schedule at all, so no start date, due time, per-occurrence count or reminders.
  const isOnDemand = repeatType === 'OnDemand'
  // Per-person schedules need something to schedule.
  const strategyOptions = isOnDemand ? STRATEGY_OPTIONS.filter((o) => o.value !== 'Independent') : STRATEGY_OPTIONS
  // Track mode: every assignee gets their own schedule + quota, no rotation, no single current assignee.
  const isIndependent = assignmentStrategy === 'Independent'
  // Anyone: nobody holds it, so there's no current assignee, pair or rotation to pick.
  const isAnyone = assignmentStrategy === 'Anyone'
  const noHolder = isIndependent || isAnyone
  // Pairs whose two people are both still assignees; the rest are ignored and never submitted.
  const activePairs = pairs.filter((p) => p.every((id) => assigneeIds.includes(id)))
  // Whoever can hold the chore: each active pair, then each assignee outside every pair.
  const holderUnits: string[][] = [
    ...activePairs,
    ...assigneeIds.filter((id) => !activePairs.some((p) => p.includes(id))).map((id) => [id]),
  ]
  // The unit the picker shows: the chosen pair while it still exists, else the assignee's first.
  const currentUnit =
    holderUnits.find((u) => u.includes(currentAssigneeId) && (u.length === 1 || u.includes(currentPartnerId)))
    ?? holderUnits.find((u) => u.includes(currentAssigneeId))
  // A rotation period only applies to chores that rotate and come due more often than the period;
  // fall back to the default otherwise so a hidden choice is never submitted.
  const rotationOptions = rotationPeriodOptions(repeatType, assignmentStrategy)
  const showRotationPeriod = rotationOptions.length > 0
  const rotationOption = rotationOptions.find((o) => o.value === rotationPeriod)
  const effectiveRotationPeriod: RotationPeriod = rotationOption?.value ?? 'EveryOccurrence'
  // Per-completion rotation contradicts a calendar rotation period, so it's only offered without one.
  const showRotateEach =
    !isCustom && !isOnDemand && !noHolder && completionsRequired > 1 && effectiveRotationPeriod === 'EveryOccurrence'

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
  const showAutoAdvance = !isIndependent && repeatType !== 'OneTime' && !isOnDemand

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
  // Labels only mean something with several slots; a duplicated time keeps its first row's label.
  const labelsByTime: Record<string, string> = {}
  if (isMultiTime) {
    trimmedTimes.forEach((t, i) => {
      const label = (timeLabels[i] ?? '').trim()
      if (t && label && !(t in labelsByTime)) labelsByTime[t] = label
    })
  }

  const mutation = useMutation({
    mutationFn: () => {
      const body: ChoreRequest = {
        name,
        description: description.trim() || null,
        emoji: emoji.trim() || '📋',
        points: Number(points) || 0,
        repeatType,
        ...recurrence,
        completionsRequired: isCustom || isOnDemand || isIndependent ? 1 : Math.max(1, completionsRequired),
        rotateOnEachCompletion: showRotateEach && rotateOnEachCompletion,
        rotationPeriod: effectiveRotationPeriod,
        assignmentStrategy,
        schedulingPreference: effectiveScheduling,
        graceMinutes,
        autoAdvanceIncomplete: showAutoAdvance && autoAdvanceEnabled,
        completionWindowMinutes,
        startDate: toLocalDueInstant(startDate, primaryTime),
        dueTime: isOnDemand ? null : primaryTime || null,
        timesOfDay: isMultiTime ? uniqueSortedTimes : null,
        timeLabels: isMultiTime ? labelsByTime : null,
        assigneeIds,
        currentAssigneeId: noHolder ? null : currentAssigneeId || (isManual ? null : ''),
        currentPartnerId: noHolder ? null : currentUnit?.find((id) => id !== currentAssigneeId) ?? null,
        tagNames: selectedTags,
        notifications: isOnDemand ? [] : notifications,
        tracks: isIndependent
          ? assigneeIds.map((id) => ({ userId: id, completionsRequired: Math.max(1, trackQuotas[id] ?? 1) }))
          : undefined,
        pairs: noHolder ? null : activePairs,
      }
      return isEdit && chore ? choresApi.update(chore.id, body) : choresApi.create(body)
    },
    onSuccess: onSaved,
    onError: (err) => setError(err instanceof ApiError ? err.message : t('form.saveFailed')),
  })

  const selectedAssignees = (allUsers ?? []).filter((u) => assigneeIds.includes(u.id))
  const userName = (id: string) => (allUsers ?? []).find((u) => u.id === id)?.displayName ?? '?'
  const isPair = (a: string, b: string) => activePairs.some((p) => p.includes(a) && p.includes(b))
  const addPair = () => {
    const [a, b] = pairDraft
    if (!a || !b || a === b) return
    // Replace a stale copy of the same pair (one half was deselected and picked again) instead of
    // listing it twice.
    setPairs((prev) => [...prev.filter((p) => !(p.includes(a) && p.includes(b))), [a, b]])
    setPairDraft(['', ''])
  }

  return (
    <Modal title={title} onClose={onClose}>
      <form
        onSubmit={(e) => {
          e.preventDefault()
          if (assigneeIds.length === 0) {
            setError(t('form.selectAssignee'))
            return
          }
          setError(null)
          mutation.mutate()
        }}
        className="max-h-[70vh] space-y-2 overflow-y-auto pl-1 -ml-1 pr-4 -mr-4"
      >
        <div>
          <Label htmlFor="name">{t('form.name')}</Label>
          <Input id="name" value={name} onChange={(e) => setName(e.target.value)} required />
        </div>
        <div className="flex gap-3">
          <div className="flex-1">
            <Label htmlFor="emoji">{t('slot.Emoji')}</Label>
            <Input id="emoji" value={emoji} onChange={(e) => setEmoji(e.target.value)} />
          </div>
          <div className="flex-1">
            <Label htmlFor="points">{t('nav.points')}</Label>
            <Input id="points" type="number" min={0} value={points} onChange={(e) => setPoints(e.target.value)} />
          </div>
        </div>
        <div>
          <Label htmlFor="description">{t('form.description')}</Label>
          <Input id="description" value={description} onChange={(e) => setDescription(e.target.value)} />
        </div>
        <div className="flex gap-3">
          <div className="flex-1">
            <Label htmlFor="repeat">{t('filters.repeat')}</Label>
            <Select
              id="repeat"
              value={repeatType}
              onChange={(e) => {
                const next = e.target.value as RepeatType
                setRepeatType(next)
                if (next === 'OnDemand' && assignmentStrategy === 'Independent') setAssignmentStrategy('LeastCompleted')
              }}
            >
              {REPEAT_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>{o.label}</option>
              ))}
            </Select>
          </div>
          {!isCustom && !isOnDemand && !isIndependent && (
            <div className="w-28">
              <Label htmlFor="times">{t('form.times')}</Label>
              <IntegerInput
                id="times"
                value={completionsRequired}
                onCommit={(n) => setCompletionsRequired(Math.max(1, n))}
                aria-label={t('form.timesLabel')}
              />
            </div>
          )}
        </div>
        {!isCustom && !isOnDemand && !isIndependent && completionsRequired > 1 && (
          <div className="-mt-1 space-y-2">
            <p className="text-xs text-muted-foreground">
              {t('form.mustComplete', { count: completionsRequired })}
            </p>
            {showRotateEach && (
              <label className="flex items-start gap-2 text-sm text-foreground">
                <input
                  type="checkbox"
                  checked={rotateOnEachCompletion}
                  onChange={(e) => setRotateOnEachCompletion(e.target.checked)}
                  className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-ring"
                />
                <span>
                  {t('form.rotateEach')}
                  <span className="block text-xs text-muted-foreground">
                    {t('form.rotateEachHint', { count: completionsRequired })}
                  </span>
                </span>
              </label>
            )}
          </div>
        )}
        {showAutoAdvance && (
          <div className="-mt-1 space-y-2">
            <label className="flex items-start gap-2 text-sm text-foreground">
              <input
                type="checkbox"
                checked={autoAdvanceEnabled}
                onChange={(e) => setAutoAdvanceEnabled(e.target.checked)}
                className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-ring"
              />
              <span>
                {t('form.autoAdvance')}
                <span className="block text-xs text-muted-foreground">
                  {completionsRequired > 1
                    ? t('form.autoAdvanceHintN', { count: completionsRequired })
                    : t('form.autoAdvanceHint')}
                </span>
              </span>
            </label>
            {autoAdvanceEnabled && (
              <div className="space-y-2 pl-6">
                <label className="flex items-start gap-2 text-sm text-foreground">
                  <input
                    type="checkbox"
                    checked={windowEnabled}
                    onChange={(e) => setWindowEnabled(e.target.checked)}
                    className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-ring"
                  />
                  <span>
                    {t('form.delayAdvance')}
                    <span className="block text-xs text-muted-foreground">
                      {t('form.delayAdvanceHint')}
                    </span>
                  </span>
                </label>
                {windowEnabled && (
                  <div className="flex items-center gap-2">
                    <div className="w-16">
                      <IntegerInput
                        value={windowValue}
                        onCommit={(n) => setWindowValue(Math.max(1, n))}
                        aria-label={t('form.windowAmount')}
                      />
                    </div>
                    <div className="w-28">
                      <Select
                        value={windowUnit}
                        onChange={(e) => setWindowUnit(e.target.value)}
                        aria-label={t('form.windowUnit')}
                      >
                        {GRACE_UNITS.map((u) => (
                          <option key={u.value} value={u.value}>{u.label}</option>
                        ))}
                      </Select>
                    </div>
                    <span className="text-sm text-muted-foreground">{t('form.afterDue')}</span>
                  </div>
                )}
              </div>
            )}
          </div>
        )}
        {!isOnDemand && (
          <div className="flex gap-3">
            <div className="flex-1">
              <Label htmlFor="start">{t('details.startDate')}</Label>
              <Input id="start" type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} required />
            </div>
            {!supportsTimes && (
              <div className="flex-1">
                <Label htmlFor="dueTime">{t('form.dueTime')}</Label>
                <TimeField
                  id="dueTime"
                  value={timesOfDay[0] ?? ''}
                  onChange={(v) => setTimesOfDay((prev) => [v, ...prev.slice(1)])}
                />
              </div>
            )}
          </div>
        )}
        {repeatType === 'Custom' && (
          <RecurrenceEditor value={recurrence} onChange={setRecurrence} />
        )}
        {supportsTimes && (
          <div className="space-y-2 rounded-lg border border-border bg-accent/40 p-3">
            <Label className="mb-0">{t('form.timesOfDay')}</Label>
            <p className="-mt-0.5 text-xs text-muted-foreground">
              {t('form.timesOfDayHint')}
            </p>
            <div className="space-y-1.5">
              {timesOfDay.map((time, i) => (
                <div key={i} className="flex items-center gap-2">
                  <div className="w-32">
                    <TimeField
                      value={time}
                      onChange={(v) => setTimesOfDay((prev) => prev.map((x, j) => (j === i ? v : x)))}
                      aria-label={t('form.timeN', { n: i + 1 })}
                    />
                  </div>
                  {timesOfDay.length > 1 && (
                    <Input
                      className="min-w-0 flex-1"
                      value={timeLabels[i] ?? ''}
                      onChange={(e) =>
                        setTimeLabels((prev) => timesOfDay.map((_, j) => (j === i ? e.target.value : prev[j] ?? '')))
                      }
                      maxLength={32}
                      placeholder={t('form.labelOptional')}
                      aria-label={t('form.labelForTime', { n: i + 1 })}
                    />
                  )}
                  {timesOfDay.length > 1 && (
                    <button
                      type="button"
                      onClick={() => {
                        setTimesOfDay((prev) => prev.filter((_, j) => j !== i))
                        setTimeLabels((prev) => prev.filter((_, j) => j !== i))
                      }}
                      className="text-xs text-muted-foreground underline-offset-2 hover:text-destructive hover:underline"
                    >
                      {t('common.remove')}
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
              {t('form.addTime')}
            </button>
          </div>
        )}
        {repeatType !== 'OneTime' && (
          <div className="flex gap-3">
            <div className="flex-1">
              <Label htmlFor="strategy">{t('details.assignment')}</Label>
              <Select
                id="strategy"
                value={assignmentStrategy}
                onChange={(e) => setAssignmentStrategy(e.target.value as AssignmentStrategy)}
              >
                {strategyOptions.map((o) => (
                  <option key={o.value} value={o.value}>{o.label}</option>
                ))}
              </Select>
            </div>
            {!isOnDemand && (
              <div className="flex-1">
                <Label htmlFor="scheduling">{t('details.nextDue')}</Label>
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
            )}
          </div>
        )}
        {showRotationPeriod && (
          <div>
            <Label htmlFor="rotation">{t('form.handOver')}</Label>
            <Select
              id="rotation"
              value={effectiveRotationPeriod}
              onChange={(e) => setRotationPeriod(e.target.value as RotationPeriod)}
            >
              {rotationOptions.map((o) => (
                <option key={o.value} value={o.value}>{o.label}</option>
              ))}
            </Select>
            {rotationOption?.span && !isManual && (
              <p className="mt-1 text-xs text-muted-foreground">
                {rotationOption.span}
              </p>
            )}
          </div>
        )}
        {repeatType !== 'OneTime' && !isOnDemand && effectiveScheduling === 'SmartScheduling' && (
          <div className="-mt-1 rounded-lg bg-accent/50 p-3">
            <label className="flex items-start gap-2 text-sm text-foreground">
              <input
                type="checkbox"
                checked={graceEnabled}
                onChange={(e) => setGraceEnabled(e.target.checked)}
                className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-ring"
              />
              <span>
                {t('form.grace')}
                <span className="block text-xs text-muted-foreground">
                  {t('form.graceHint')}
                </span>
              </span>
            </label>
            {graceEnabled && (
              <div className="mt-3 flex items-center gap-2">
                <div className="w-16">
                  <IntegerInput
                    value={graceValue}
                    onCommit={(n) => setGraceValue(Math.max(1, n))}
                    aria-label={t('form.graceAmount')}
                  />
                </div>
                <div className="w-28">
                  <Select
                    value={graceUnit}
                    onChange={(e) => setGraceUnit(e.target.value)}
                    aria-label={t('form.graceUnit')}
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
            <Label className="mb-0">{t('details.assignees')}</Label>
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
                      if (!keepUnassigned && !all.includes(currentAssigneeId)) setCurrentAssigneeId(all[0] ?? '')
                    }
                  }}
                  className="text-xs text-muted-foreground underline-offset-2 hover:text-foreground hover:underline"
                >
                  {allSelected ? t('form.deselectAll') : t('form.everyone')}
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
                <Avatar color={u.avatarColor} name={u.displayName} size={16} frame={u.equippedFrameKey} emoji={u.avatarEmoji} />
                {u.displayName}
              </button>
            ))}
          </div>
        </div>
        {isIndependent ? (
          <div>
            <Label>{t('form.perPerson')}</Label>
            <p className="-mt-0.5 mb-1.5 text-xs text-muted-foreground">
              {t('form.perPersonHint')}
            </p>
            {selectedAssignees.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t('form.selectAbove')}</p>
            ) : (
              <div className="space-y-1.5">
                {selectedAssignees.map((u) => (
                  <div key={u.id} className="flex items-center justify-between gap-2">
                    <span className="flex items-center gap-1.5 text-sm text-foreground">
                      <Avatar color={u.avatarColor} name={u.displayName} size={16} frame={u.equippedFrameKey} emoji={u.avatarEmoji} />
                      {u.displayName}
                    </span>
                    <div className="flex items-center gap-1.5">
                      <div className="w-16">
                        <IntegerInput
                          value={trackQuotas[u.id] ?? 1}
                          onCommit={(n) =>
                            setTrackQuotas((prev) => ({ ...prev, [u.id]: Math.max(1, n) }))
                          }
                          aria-label={t('form.timesFor', { name: u.displayName })}
                        />
                      </div>
                      <span className="text-xs text-muted-foreground">×</span>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        ) : !isAnyone && (
          <div>
            <Label htmlFor="current">{t('form.currentAssignee')}</Label>
            <Select
              id="current"
              value={currentUnit?.join('|') ?? ''}
              onChange={(e) => {
                const [first = '', second = ''] = e.target.value.split('|')
                setCurrentAssigneeId(first)
                setCurrentPartnerId(second)
              }}
              disabled={selectedAssignees.length === 0}
            >
              {isManual
                ? <option value="">{t('chore.unassigned')}</option>
                : <option value="" disabled>{t('form.selectAnAssignee')}</option>}
              {holderUnits.map((unit) => (
                <option key={unit.join('|')} value={unit.join('|')}>
                  {unit.map(userName).join(' & ')}
                </option>
              ))}
            </Select>
          </div>
        )}
        {!noHolder && selectedAssignees.length >= 2 && (
          <div>
            <Label>{t('form.pairs')}</Label>
            {activePairs.length > 0 && (
              <div className="mb-1.5 flex flex-wrap gap-1">
                {activePairs.map((pair) => (
                  <span
                    key={pair.join('-')}
                    className="flex items-center gap-1.5 rounded-md bg-primary/10 px-2 py-1 text-xs text-primary ring-1 ring-primary"
                  >
                    {userName(pair[0])} & {userName(pair[1])}
                    <button
                      type="button"
                      onClick={() => setPairs((prev) => prev.filter((p) => p !== pair))}
                      className="opacity-70 hover:opacity-100"
                      aria-label={t('form.removePair', { a: userName(pair[0]), b: userName(pair[1]) })}
                    >
                      ×
                    </button>
                  </span>
                ))}
              </div>
            )}
            <div className="flex items-center gap-1.5">
              {[0, 1].map((slot) => (
                <Select
                  key={slot}
                  value={pairDraft[slot]}
                  onChange={(e) =>
                    setPairDraft((prev) => (slot === 0 ? [e.target.value, prev[1]] : [prev[0], e.target.value]))
                  }
                  aria-label={slot === 0 ? t('form.firstInPair') : t('form.secondInPair')}
                >
                  <option value="">{t('form.selectPerson')}</option>
                  {selectedAssignees
                    .filter((u) => u.id !== pairDraft[1 - slot] && !isPair(u.id, pairDraft[1 - slot]))
                    .map((u) => (
                      <option key={u.id} value={u.id}>{u.displayName}</option>
                    ))}
                </Select>
              ))}
              <Button
                type="button"
                variant="secondary"
                onClick={addPair}
                disabled={!pairDraft[0] || !pairDraft[1]}
                className="shrink-0"
              >
                {t('form.addPair')}
              </Button>
            </div>
          </div>
        )}
        <div>
          <Label>{t('filters.tags')}</Label>
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
            <p className="text-sm text-muted-foreground">{t('form.noTags')}</p>
          )}
        </div>
        {!isOnDemand && (
          <NotificationsEditor value={notifications} onChange={setNotifications} independent={isIndependent} />
        )}
        {error && <p className="text-sm text-destructive">{error}</p>}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" disabled={mutation.isPending}>
            {mutation.isPending ? t('common.saving') : t('common.save')}
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
