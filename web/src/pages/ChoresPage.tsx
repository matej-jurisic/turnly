import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { choresApi, tagsApi, usersApi, ApiError } from '@/lib/api'
import { useAuthStore } from '@/store/auth'
import type {
  Chore,
  ChoreRequest,
  RepeatType,
  User,
  Weekday,
} from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Card } from '@/components/ui/Card'
import { Input, Label, Select } from '@/components/ui/Field'
import { Modal, Avatar } from '@/components/ui/Modal'

const REPEAT_OPTIONS: { value: RepeatType; label: string }[] = [
  { value: 'OneTime', label: 'One-time' },
  { value: 'Daily', label: 'Daily' },
  { value: 'Weekly', label: 'Weekly' },
  { value: 'Monthly', label: 'Monthly' },
  { value: 'Yearly', label: 'Yearly' },
]

const WEEKDAYS: Weekday[] = [
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
  'Sunday',
]

function repeatLabel(chore: Chore): string {
  if (chore.repeatType === 'Weekly' && chore.weekdays.length > 0)
    return `Weekly · ${chore.weekdays.map((d) => d.slice(0, 3)).join(', ')}`
  return REPEAT_OPTIONS.find((o) => o.value === chore.repeatType)?.label ?? chore.repeatType
}

function formatDate(iso?: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
}

export function ChoresPage() {
  const currentUser = useAuthStore((s) => s.user)
  const isAdmin = currentUser?.role === 'Admin'
  const queryClient = useQueryClient()

  const { data: chores, isLoading, error } = useQuery({ queryKey: ['chores'], queryFn: choresApi.list })

  const [editing, setEditing] = useState<Chore | null>(null)
  const [creating, setCreating] = useState(false)
  const [completing, setCompleting] = useState<Chore | null>(null)

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

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold text-foreground">Chores</h1>
        {isAdmin && <Button onClick={() => setCreating(true)}>Add chore</Button>}
      </div>

      {isLoading && <p className="text-muted-foreground">Loading…</p>}
      {error && <p className="text-destructive">{(error as ApiError).message}</p>}
      {chores?.length === 0 && (
        <p className="text-muted-foreground">No chores yet{isAdmin ? ' — add one to get started.' : '.'}</p>
      )}

      <div className="grid gap-4">
        {chores?.map((chore) => (
          <Card key={chore.id} className="p-4">
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div className="min-w-0 space-y-2">
                <div className="flex items-center gap-2">
                  {chore.emoji && <span className="text-xl">{chore.emoji}</span>}
                  <span className="truncate font-semibold text-foreground">{chore.name}</span>
                  <Badge tone="violet">{chore.points} pts</Badge>
                </div>
                {chore.description && (
                  <p className="text-sm text-muted-foreground">{chore.description}</p>
                )}
                <div className="flex flex-wrap items-center gap-2">
                  <Badge tone="blue">{repeatLabel(chore)}</Badge>
                  <Badge tone={chore.dueAt ? 'amber' : 'neutral'}>Due {formatDate(chore.dueAt)}</Badge>
                  {chore.tags.map((tag) => (
                    <Badge key={tag} tone="neutral">{tag}</Badge>
                  ))}
                </div>
                {chore.currentAssignee && (
                  <div className="flex items-center gap-2 text-sm text-muted-foreground">
                    <Avatar color={chore.currentAssignee.avatarColor} name={chore.currentAssignee.displayName} size={24} />
                    <span>{chore.currentAssignee.displayName}</span>
                  </div>
                )}
              </div>

              <div className="flex shrink-0 flex-wrap gap-1">
                <Button size="sm" onClick={() => setCompleting(chore)} disabled={!chore.dueAt}>
                  Complete
                </Button>
                {chore.lastCompletion && (
                  <Button
                    size="sm"
                    variant="ghost"
                    disabled={undoMutation.isPending}
                    onClick={() => {
                      if (confirm('Undo the last completion? Points will be reversed.'))
                        undoMutation.mutate(chore.lastCompletion!.id)
                    }}
                  >
                    Undo
                  </Button>
                )}
                {isAdmin && (
                  <>
                    <Button size="sm" variant="ghost" onClick={() => setEditing(chore)}>Edit</Button>
                    <Button
                      size="sm"
                      variant="ghost"
                      className="text-destructive hover:bg-destructive/10"
                      disabled={deleteMutation.isPending}
                      onClick={() => {
                        if (confirm(`Delete "${chore.name}"? This wipes its completion history.`))
                          deleteMutation.mutate(chore.id)
                      }}
                    >
                      Delete
                    </Button>
                  </>
                )}
              </div>
            </div>
          </Card>
        ))}
      </div>

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
    </div>
  )
}

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
  const [weekdays, setWeekdays] = useState<Weekday[]>(chore?.weekdays ?? [])
  const [startDate, setStartDate] = useState(
    (chore?.startDate ?? new Date().toISOString()).slice(0, 10),
  )
  const [assigneeIds, setAssigneeIds] = useState<string[]>(
    chore?.assignees.map((a) => a.id) ?? [],
  )
  const [currentAssigneeId, setCurrentAssigneeId] = useState(chore?.currentAssignee?.id ?? '')
  const [tags, setTags] = useState(chore?.tags.join(', ') ?? '')
  const [error, setError] = useState<string | null>(null)

  const toggleAssignee = (id: string) => {
    setAssigneeIds((prev) => {
      const next = prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]
      if (!next.includes(currentAssigneeId)) setCurrentAssigneeId(next[0] ?? '')
      return next
    })
  }

  const toggleWeekday = (day: Weekday) =>
    setWeekdays((prev) => (prev.includes(day) ? prev.filter((d) => d !== day) : [...prev, day]))

  const mutation = useMutation({
    mutationFn: () => {
      const body: ChoreRequest = {
        name,
        description: description.trim() || null,
        emoji: emoji.trim() || null,
        points: Number(points) || 0,
        repeatType,
        weekdays: repeatType === 'Weekly' ? weekdays : [],
        startDate: new Date(startDate).toISOString(),
        assigneeIds,
        currentAssigneeId,
        tagNames: tags.split(',').map((t) => t.trim()).filter(Boolean),
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
          setError(null)
          mutation.mutate()
        }}
        className="max-h-[70vh] space-y-4 overflow-y-auto"
      >
        <div>
          <Label htmlFor="name">Name</Label>
          <Input id="name" value={name} onChange={(e) => setName(e.target.value)} required />
        </div>
        <div className="flex gap-3">
          <div className="flex-1">
            <Label htmlFor="emoji">Emoji</Label>
            <Input id="emoji" value={emoji} onChange={(e) => setEmoji(e.target.value)} placeholder="🧹" />
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
        {repeatType === 'Weekly' && (
          <div>
            <Label>Weekdays</Label>
            <div className="flex flex-wrap gap-1">
              {WEEKDAYS.map((day) => (
                <button
                  key={day}
                  type="button"
                  onClick={() => toggleWeekday(day)}
                  className={
                    'rounded-md px-2 py-1 text-xs transition-colors ' +
                    (weekdays.includes(day)
                      ? 'bg-primary/10 text-primary'
                      : 'bg-accent text-muted-foreground hover:text-foreground')
                  }
                >
                  {day.slice(0, 3)}
                </button>
              ))}
            </div>
          </div>
        )}
        <div>
          <Label>Assignees</Label>
          <div className="space-y-1">
            {(allUsers ?? []).map((u) => (
              <label key={u.id} className="flex items-center gap-2 text-sm text-foreground">
                <input
                  type="checkbox"
                  checked={assigneeIds.includes(u.id)}
                  onChange={() => toggleAssignee(u.id)}
                />
                <Avatar color={u.avatarColor} name={u.displayName} size={20} />
                {u.displayName}
              </label>
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
          <Label htmlFor="tags">Tags (comma-separated)</Label>
          <Input
            id="tags"
            value={tags}
            onChange={(e) => setTags(e.target.value)}
            list="tag-options"
            placeholder="kitchen, weekly"
          />
          <datalist id="tag-options">
            {(allTags ?? []).map((t) => (
              <option key={t.id} value={t.name} />
            ))}
          </datalist>
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

function CompleteModal({ chore, onClose, onDone }: { chore: Chore; onClose: () => void; onDone: () => void }) {
  const [notes, setNotes] = useState('')
  const [error, setError] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: () => choresApi.complete(chore.id, { notes: notes.trim() || null }),
    onSuccess: onDone,
    onError: (err) => setError(err instanceof ApiError ? err.message : 'Failed to complete'),
  })

  return (
    <Modal title={`Complete "${chore.name}"`} onClose={onClose}>
      <form
        onSubmit={(e) => {
          e.preventDefault()
          setError(null)
          mutation.mutate()
        }}
        className="space-y-4"
      >
        <p className="text-sm text-muted-foreground">
          You'll earn <span className="font-medium text-foreground">{chore.points} points</span>.
        </p>
        <div>
          <Label htmlFor="notes">Notes (optional)</Label>
          <Input id="notes" value={notes} onChange={(e) => setNotes(e.target.value)} />
        </div>
        {error && <p className="text-sm text-destructive">{error}</p>}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>Cancel</Button>
          <Button type="submit" disabled={mutation.isPending}>
            {mutation.isPending ? 'Saving…' : 'Mark complete'}
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
