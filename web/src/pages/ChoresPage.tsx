import { useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { choresApi, tagsApi, usersApi, ApiError } from '@/lib/api'
import { useAuthStore } from '@/store/auth'
import type {
  AssignmentStrategy,
  Chore,
  ChoreRequest,
  RecurrenceFields,
  RepeatType,
  SchedulingPreference,
  User,
  Weekday,
} from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Card } from '@/components/ui/Card'
import { Input, Label, Select } from '@/components/ui/Field'
import { Modal, Avatar } from '@/components/ui/Modal'
import { RecurrenceEditor } from '@/components/RecurrenceEditor'
import { CompleteModal } from '@/components/CompleteModal'
import { ChoreDetailsModal } from '@/components/ChoreDetailsModal'
import { cn } from '@/lib/utils'

const REPEAT_OPTIONS: { value: RepeatType; label: string }[] = [
  { value: 'OneTime', label: 'One-time' },
  { value: 'Daily', label: 'Daily' },
  { value: 'Weekly', label: 'Weekly' },
  { value: 'Monthly', label: 'Monthly' },
  { value: 'Yearly', label: 'Yearly' },
  { value: 'Custom', label: 'Custom' },
]

const STRATEGY_OPTIONS: { value: AssignmentStrategy; label: string }[] = [
  { value: 'KeepLastAssigned', label: 'Keep last assigned' },
  { value: 'RoundRobin', label: 'Round robin' },
  { value: 'Random', label: 'Random' },
  { value: 'RandomExceptLastAssigned', label: 'Random (except last)' },
  { value: 'LeastAssigned', label: 'Least assigned' },
  { value: 'LeastCompleted', label: 'Least completed' },
]

const SCHEDULING_OPTIONS: { value: SchedulingPreference; label: string }[] = [
  { value: 'FromScheduledDate', label: 'From scheduled date' },
  { value: 'FromCompletionDate', label: 'From completion date' },
  { value: 'ToFirstNextRepeat', label: 'To first next repeat' },
]

const WEEKDAY_SHORT: Record<Weekday, string> = {
  Sunday: 'Sun', Monday: 'Mon', Tuesday: 'Tue', Wednesday: 'Wed',
  Thursday: 'Thu', Friday: 'Fri', Saturday: 'Sat',
}
const WEEKDAY_ORDER: Weekday[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']
const MONTH_SHORT = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

function repeatLabel(chore: Chore): string {
  if (chore.repeatType !== 'Custom')
    return REPEAT_OPTIONS.find((o) => o.value === chore.repeatType)?.label ?? chore.repeatType

  switch (chore.customMode) {
    case 'Interval': {
      const unit = (chore.intervalUnit ?? 'Week').toLowerCase()
      const n = chore.intervalCount ?? 1
      return n === 1 ? `Every ${unit}` : `Every ${n} ${unit}s`
    }
    case 'DaysOfWeek':
      return [...chore.weekdays]
        .sort((a, b) => WEEKDAY_ORDER.indexOf(a) - WEEKDAY_ORDER.indexOf(b))
        .map((d) => WEEKDAY_SHORT[d])
        .join(', ') || 'Days of week'
    case 'DaysOfMonth': {
      const days = [...chore.daysOfMonth].sort((a, b) => a - b).join(', ')
      const months = [...chore.months].sort((a, b) => a - b).map((m) => MONTH_SHORT[m - 1]).join(', ')
      return `Days ${days}${months ? ` · ${months}` : ''}`
    }
    case 'Frequency':
      return `${chore.frequencyCount ?? 1}×/${(chore.frequencyPeriod ?? 'Week').toLowerCase()}`
    default:
      return 'Custom'
  }
}

function formatDate(iso?: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
}

// ── helpers ────────────────────────────────────────────────────────────────

function startOfDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate())
}

function choreDueStatus(chore: Chore): 'overdue' | 'today' | 'upcoming' | 'later' {
  if (!chore.dueAt) return 'later'
  const due = new Date(chore.dueAt)
  const todayStart = startOfDay(new Date())
  const tomorrowStart = new Date(todayStart.getTime() + 86_400_000)
  const weekEnd = new Date(todayStart.getTime() + 7 * 86_400_000)
  if (due < todayStart) return 'overdue'
  if (due < tomorrowStart) return 'today'
  if (due <= weekEnd) return 'upcoming'
  return 'later'
}

// ── main page ──────────────────────────────────────────────────────────────

export function ChoresPage() {
  const currentUser = useAuthStore((s) => s.user)
  const isAdmin = currentUser?.role === 'Admin'
  const queryClient = useQueryClient()

  const { data: chores, isLoading, error } = useQuery({ queryKey: ['chores'], queryFn: choresApi.list })

  const [editing, setEditing] = useState<Chore | null>(null)
  const [creating, setCreating] = useState(false)
  const [completing, setCompleting] = useState<Chore | null>(null)
  const [details, setDetails] = useState<Chore | null>(null)
  const [tagFilter, setTagFilter] = useState('')
  const [assigneeFilter, setAssigneeFilter] = useState('')

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['chores'] })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => choresApi.remove(id),
    onSuccess: invalidate,
    onError: (err) => alert(err instanceof ApiError ? err.message : 'Delete failed'),
  })

  const undoMutation = useMutation({
    mutationFn: (completionId: string) => choresApi.undoCompletion(completionId),
    onSuccess: invalidate,
    onError: (err) => alert(err instanceof ApiError ? err.message : 'Undo failed'),
  })

  const allTags = [...new Set((chores ?? []).flatMap((c) => c.tags))].sort()
  const allAssignees = [
    ...new Map(
      (chores ?? [])
        .filter((c) => c.currentAssignee)
        .map((c) => [c.currentAssignee!.id, c.currentAssignee!]),
    ).values(),
  ].sort((a, b) => a.displayName.localeCompare(b.displayName))

  const filtered = (chores ?? []).filter((c) => {
    if (tagFilter && !c.tags.includes(tagFilter)) return false
    if (assigneeFilter && c.currentAssignee?.id !== assigneeFilter) return false
    return true
  })

  const overdue = filtered.filter((c) => choreDueStatus(c) === 'overdue')
  const today = filtered.filter((c) => choreDueStatus(c) === 'today')
  const upcoming = filtered.filter((c) => choreDueStatus(c) === 'upcoming')
  const later = filtered.filter((c) => choreDueStatus(c) === 'later')

  const itemProps = (chore: Chore) => ({
    chore,
    isAdmin,
    undoPending: undoMutation.isPending,
    deletePending: deleteMutation.isPending,
    onComplete: () => setCompleting(chore),
    onUndo: () => {
      if (confirm('Undo the last completion? Points will be reversed.'))
        undoMutation.mutate(chore.lastCompletion!.id)
    },
    onEdit: () => setEditing(chore),
    onDelete: () => {
      if (confirm(`Delete "${chore.name}"? This wipes its completion history.`))
        deleteMutation.mutate(chore.id)
    },
    onDetails: () => setDetails(chore),
  })

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold text-foreground">Chores</h1>
        {isAdmin && <Button onClick={() => setCreating(true)}>Add chore</Button>}
      </div>

      {/* Filters */}
      {(allTags.length > 0 || allAssignees.length > 1) && (
        <div className="flex flex-wrap gap-3">
          {allTags.length > 0 && (
            <select
              value={tagFilter}
              onChange={(e) => setTagFilter(e.target.value)}
              className="rounded-lg border border-border bg-card px-3 py-1.5 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
            >
              <option value="">All tags</option>
              {allTags.map((t) => (
                <option key={t} value={t}>{t}</option>
              ))}
            </select>
          )}
          {allAssignees.length > 1 && (
            <select
              value={assigneeFilter}
              onChange={(e) => setAssigneeFilter(e.target.value)}
              className="rounded-lg border border-border bg-card px-3 py-1.5 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
            >
              <option value="">All members</option>
              {allAssignees.map((u) => (
                <option key={u.id} value={u.id}>{u.displayName}</option>
              ))}
            </select>
          )}
          {(tagFilter || assigneeFilter) && (
            <button
              type="button"
              onClick={() => { setTagFilter(''); setAssigneeFilter('') }}
              className="text-sm text-muted-foreground underline-offset-2 hover:text-foreground hover:underline"
            >
              Clear filters
            </button>
          )}
        </div>
      )}

      {isLoading && <p className="text-muted-foreground">Loading…</p>}
      {error && <p className="text-destructive">{(error as ApiError).message}</p>}

      {!isLoading && (chores ?? []).length === 0 && (
        <p className="text-muted-foreground">No chores yet{isAdmin ? ' — add one to get started.' : '.'}</p>
      )}
      {!isLoading && (chores ?? []).length > 0 && filtered.length === 0 && (
        <p className="text-muted-foreground">No chores match the current filters.</p>
      )}

      {overdue.length > 0 && (
        <ChoreSection title="Overdue" tone="destructive" count={overdue.length}>
          <div className="grid gap-6">
            {overdue.map((chore) => <ChoreListItem key={chore.id} {...itemProps(chore)} />)}
          </div>
        </ChoreSection>
      )}

      {today.length > 0 && (
        <ChoreSection title="Today" count={today.length}>
          <div className="grid gap-6">
            {today.map((chore) => <ChoreListItem key={chore.id} {...itemProps(chore)} />)}
          </div>
        </ChoreSection>
      )}

      {upcoming.length > 0 && (
        <ChoreSection title="This week" count={upcoming.length}>
          <div className="grid gap-6">
            {upcoming.map((chore) => <ChoreListItem key={chore.id} {...itemProps(chore)} />)}
          </div>
        </ChoreSection>
      )}

      {later.length > 0 && (
        <ChoreSection title="Later" count={later.length}>
          <div className="grid gap-6">
            {later.map((chore) => <ChoreListItem key={chore.id} {...itemProps(chore)} />)}
          </div>
        </ChoreSection>
      )}

      {creating && (
        <ChoreFormModal
          title="Add chore"
          onClose={() => setCreating(false)}
          onSaved={() => {
            setCreating(false)
            invalidate()
          }}
        />
      )}

      {editing && (
        <ChoreFormModal
          title="Edit chore"
          chore={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            invalidate()
          }}
        />
      )}

      {completing && (
        <CompleteModal
          chore={completing}
          onClose={() => setCompleting(null)}
          onDone={() => {
            setCompleting(null)
            invalidate()
          }}
        />
      )}

      {details && (
        <ChoreDetailsModal
          chore={details}
          onClose={() => setDetails(null)}
          onComplete={() => { setCompleting(details); setDetails(null) }}
        />
      )}
    </div>
  )
}

// ── chore section ──────────────────────────────────────────────────────────

function ChoreSection({
  title,
  tone = 'default',
  count,
  children,
}: {
  title: string
  tone?: 'default' | 'destructive'
  count?: number
  children: ReactNode
}) {
  const pillClass =
    tone === 'destructive'
      ? 'border-destructive/20 bg-destructive/10 text-destructive'
      : 'border-border bg-card text-muted-foreground'

  return (
    <section className="space-y-6">
      <div className="flex items-center gap-3">
        <div className="h-px flex-1 bg-border" />
        <span className={cn('rounded-full border px-3 py-0.5 text-xs font-semibold', pillClass)}>
          {title}{count != null ? ` ${count}` : ''}
        </span>
        <div className="h-px flex-1 bg-border" />
      </div>
      {children}
    </section>
  )
}

// ── chore list item ────────────────────────────────────────────────────────

function ChoreListItem({
  chore,
  isAdmin,
  undoPending,
  deletePending,
  onComplete,
  onUndo,
  onEdit,
  onDelete,
  onDetails,
}: {
  chore: Chore
  isAdmin: boolean
  undoPending: boolean
  deletePending: boolean
  onComplete: () => void
  onUndo: () => void
  onEdit: () => void
  onDelete: () => void
  onDetails: () => void
}) {
  return (
    <div className="relative">
      <div className="absolute left-4 top-0 z-10 flex -translate-y-1/2 items-center gap-2">
        {chore.dueAt && (
          <Badge tone="amber" className="border border-warning bg-card">Due {formatDate(chore.dueAt)}</Badge>
        )}
        <Badge tone="blue" className="border border-info bg-card">{repeatLabel(chore)}</Badge>
        {chore.customMode === 'Frequency' && (
          <Badge tone="violet" className="border border-primary bg-card">
            {chore.frequencyProgress ?? 0}/{chore.frequencyCount ?? 1} this {(chore.frequencyPeriod ?? 'Week').toLowerCase()}
          </Badge>
        )}
      </div>
      <Card className="min-w-0 p-4">
        <div className="space-y-2">
          <div className="flex items-start justify-between gap-3">
            <div className="flex min-w-0 items-start gap-2">
              {chore.emoji && <span className="shrink-0 text-xl leading-tight">{chore.emoji}</span>}
              <span className="line-clamp-2 font-semibold text-foreground">{chore.name}</span>
            </div>

            <div className="flex shrink-0 items-center gap-1">
              <button
                type="button"
                onClick={onComplete}
                disabled={!chore.dueAt}
                aria-label="Mark complete"
                className="inline-flex h-9 w-9 items-center justify-center rounded-full bg-primary text-primary-foreground transition-colors hover:bg-primary/90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-30"
              >
                <CheckIcon />
              </button>
              <ChoreMenu
                chore={chore}
                isAdmin={isAdmin}
                undoPending={undoPending}
                deletePending={deletePending}
                onDetails={onDetails}
                onUndo={onUndo}
                onEdit={onEdit}
                onDelete={onDelete}
              />
            </div>
          </div>

          {chore.description && (
            <p className="text-sm text-muted-foreground">{chore.description}</p>
          )}
          <div className="flex flex-wrap items-center gap-2">
            {chore.currentAssignee && (
              <>
                <Avatar color={chore.currentAssignee.avatarColor} name={chore.currentAssignee.displayName} size={24} />
                <span className="text-sm text-muted-foreground">{chore.currentAssignee.displayName}</span>
              </>
            )}
            <Badge tone="violet">{chore.points} pts</Badge>
            {chore.tags.map((tag) => (
              <Badge key={tag} tone="neutral">{tag}</Badge>
            ))}
          </div>
        </div>
      </Card>
    </div>
  )
}

// ── chore menu ─────────────────────────────────────────────────────────────

interface ChoreMenuProps {
  chore: Chore
  isAdmin: boolean
  undoPending: boolean
  deletePending: boolean
  onDetails: () => void
  onUndo: () => void
  onEdit: () => void
  onDelete: () => void
}

function ChoreMenu({ chore, isAdmin, undoPending, deletePending, onDetails, onUndo, onEdit, onDelete }: ChoreMenuProps) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  const hasUndo = Boolean(chore.lastCompletion)

  useEffect(() => {
    if (!open) return
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [open])

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-label="More options"
        aria-expanded={open}
        className="inline-flex h-9 w-9 items-center justify-center rounded-full text-muted-foreground transition-colors hover:bg-accent hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        <DotsIcon />
      </button>

      {open && (
        <div className="absolute right-0 top-full z-20 mt-1 min-w-[140px] rounded-lg border border-border bg-card py-1 shadow-pop">
          <button
            type="button"
            onClick={() => { setOpen(false); onDetails() }}
            className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent"
          >
            <InfoIcon />
            Details
          </button>
          {hasUndo && (
            <button
              type="button"
              disabled={undoPending}
              onClick={() => { setOpen(false); onUndo() }}
              className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent disabled:opacity-50"
            >
              <UndoIcon />
              Undo
            </button>
          )}
          {isAdmin && (
            <>
              <button
                type="button"
                onClick={() => { setOpen(false); onEdit() }}
                className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent"
              >
                <EditIcon />
                Edit
              </button>
              <button
                type="button"
                disabled={deletePending}
                onClick={() => { setOpen(false); onDelete() }}
                className="flex w-full items-center gap-2 px-3 py-2 text-sm text-destructive transition-colors hover:bg-destructive/10 disabled:opacity-50"
              >
                <TrashIcon />
                Delete
              </button>
            </>
          )}
        </div>
      )}
    </div>
  )
}

// ── icons ──────────────────────────────────────────────────────────────────

function CheckIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M20 6 9 17l-5-5" />
    </svg>
  )
}

function DotsIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <circle cx="12" cy="5" r="1" fill="currentColor" />
      <circle cx="12" cy="12" r="1" fill="currentColor" />
      <circle cx="12" cy="19" r="1" fill="currentColor" />
    </svg>
  )
}

function InfoIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <circle cx="12" cy="12" r="10" />
      <line x1="12" y1="8" x2="12" y2="8" strokeWidth="2.5" />
      <path d="M12 12v4" />
    </svg>
  )
}

function UndoIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M3 7v6h6" />
      <path d="M3 13C5.33 8.67 9.5 6 14 6c4.42 0 8 3.58 8 8s-3.58 8-8 8c-2.42 0-4.6-1.08-6.1-2.8" />
    </svg>
  )
}

function EditIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
      <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
    </svg>
  )
}

function TrashIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <polyline points="3 6 5 6 21 6" />
      <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" />
      <path d="M10 11v6M14 11v6" />
      <path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
    </svg>
  )
}

// ── chore form modal ───────────────────────────────────────────────────────

interface ChoreFormModalProps {
  title: string
  chore?: Chore
  onClose: () => void
  onSaved: () => void
}

function ChoreFormModal({ title, chore, onClose, onSaved }: ChoreFormModalProps) {
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
    frequencyCount: chore?.frequencyCount ?? 1,
    frequencyPeriod: chore?.frequencyPeriod ?? 'Week',
  })
  const [assignmentStrategy, setAssignmentStrategy] = useState<AssignmentStrategy>(
    chore?.assignmentStrategy ?? 'KeepLastAssigned',
  )
  const [schedulingPreference, setSchedulingPreference] = useState<SchedulingPreference>(
    chore?.schedulingPreference ?? 'FromScheduledDate',
  )
  const [startDate, setStartDate] = useState(
    (chore?.startDate ?? new Date().toISOString()).slice(0, 10),
  )
  const [assigneeIds, setAssigneeIds] = useState<string[]>(
    chore?.assignees.map((a) => a.id) ?? [],
  )
  const [currentAssigneeId, setCurrentAssigneeId] = useState(chore?.currentAssignee?.id ?? '')
  const [selectedTags, setSelectedTags] = useState<string[]>(chore?.tags ?? [])
  const [error, setError] = useState<string | null>(null)

  const toggleAssignee = (id: string) => {
    setAssigneeIds((prev) => {
      const next = prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]
      if (!next.includes(currentAssigneeId)) setCurrentAssigneeId(next[0] ?? '')
      return next
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
        assignmentStrategy,
        schedulingPreference,
        startDate: new Date(startDate).toISOString(),
        assigneeIds,
        currentAssigneeId,
        tagNames: selectedTags,
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
        className="max-h-[70vh] space-y-4 overflow-y-auto px-1 -mx-1"
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
          <div className="flex-1">
            <Label htmlFor="start">Start date</Label>
            <Input id="start" type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} required />
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
            {!(repeatType === 'Custom' && recurrence.customMode === 'Frequency') && (
              <div className="flex-1">
                <Label htmlFor="scheduling">Next due</Label>
                <Select
                  id="scheduling"
                  value={schedulingPreference}
                  onChange={(e) => setSchedulingPreference(e.target.value as SchedulingPreference)}
                >
                  {SCHEDULING_OPTIONS.map((o) => (
                    <option key={o.value} value={o.value}>{o.label}</option>
                  ))}
                </Select>
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
