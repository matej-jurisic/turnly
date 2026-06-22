import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { choresApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import { confirm } from '@/lib/confirm'
import { useAuthStore } from '@/store/auth'
import type { Chore, User } from '@/lib/types'
import { PlusIcon } from '@/components/chores/icons'
import { CompleteModal } from '@/components/CompleteModal'
import { ChoreDetailsModal } from '@/components/ChoreDetailsModal'
import { ChoreSection } from '@/components/chores/ChoreSection'
import { ChoreListItem } from '@/components/chores/ChoreListItem'
import { ChoreCompactItem } from '@/components/chores/ChoreCompactItem'
import { ChoreCalendar } from '@/components/chores/ChoreCalendar'
import { Card } from '@/components/ui/Card'
import { ChoreFormModal } from '@/components/chores/ChoreFormModal'
import { ReassignModal } from '@/components/chores/ReassignModal'
import { CopyChoreModal } from '@/components/chores/CopyChoreModal'
import { RescheduleModal } from '@/components/chores/RescheduleModal'
import {
  ChoreFilters, emptyFilters, type ChoreFilterState, type ChoreView,
} from '@/components/chores/ChoreFilters'
import { choreDueStatus } from '@/lib/chore-format'

const VIEW_KEY = 'turnly:chore-view'

export function ChoresPage() {
  const currentUser = useAuthStore((s) => s.user)
  const isAdmin = currentUser?.role === 'Admin'
  const queryClient = useQueryClient()

  const { data: chores, isLoading, error } = useQuery({ queryKey: ['chores'], queryFn: choresApi.list })

  // Deep-link from a notification (push or inbox): /chores?chore=<id> opens its details modal.
  const [searchParams, setSearchParams] = useSearchParams()

  const [editing, setEditing] = useState<Chore | null>(null)
  const [creating, setCreating] = useState(false)
  const [completing, setCompleting] = useState<Chore | null>(null)
  const [reassigning, setReassigning] = useState<Chore | null>(null)
  const [rescheduling, setRescheduling] = useState<Chore | null>(null)
  const [copying, setCopying] = useState<Chore | null>(null)
  const [details, setDetails] = useState<Chore | null>(null)
  const [filters, setFilters] = useState<ChoreFilterState>(emptyFilters)
  const [view, setView] = useState<ChoreView>(
    () => (localStorage.getItem(VIEW_KEY) as ChoreView | null) ?? 'list',
  )
  const changeView = (v: ChoreView) => {
    setView(v)
    localStorage.setItem(VIEW_KEY, v)
  }

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['chores'] })

  const choreParam = searchParams.get('chore')
  useEffect(() => {
    if (!choreParam || !chores) return
    const match = chores.find((c) => c.id === choreParam)
    if (match) setDetails(match)
    // Consume the param either way so it doesn't re-open after the modal is closed.
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        next.delete('chore')
        return next
      },
      { replace: true },
    )
  }, [choreParam, chores, setSearchParams])

  const deleteMutation = useMutation({
    mutationFn: (id: string) => choresApi.remove(id),
    onSuccess: invalidate,
    onError: (err) => toast.error(err instanceof ApiError ? err.message : 'Delete failed'),
  })

  const undoMutation = useMutation({
    mutationFn: (completionId: string) => choresApi.undoCompletion(completionId),
    onSuccess: invalidate,
    onError: (err) => toast.error(err instanceof ApiError ? err.message : 'Undo failed'),
  })

  const skipMutation = useMutation({
    mutationFn: (id: string) => choresApi.skip(id, { notes: null }),
    onSuccess: invalidate,
    onError: (err) => toast.error(err instanceof ApiError ? err.message : 'Skip failed'),
  })

  const allTags = useMemo(
    () => [...new Set((chores ?? []).flatMap((c) => c.tags))].sort(),
    [chores],
  )
  const allAssignees = useMemo(() => {
    const map = new Map<string, User>()
    for (const c of chores ?? []) {
      if (c.currentAssignee) map.set(c.currentAssignee.id, c.currentAssignee)
      if (c.nextAssignee) map.set(c.nextAssignee.id, c.nextAssignee)
      for (const t of c.tracks) map.set(t.user.id, t.user)
    }
    return [...map.values()].sort((a, b) => a.displayName.localeCompare(b.displayName))
  }, [chores])

  const { overdue, today, upcoming, later, filtered } = useMemo(() => {
    const filtered = (chores ?? []).filter((c) => {
      // OR within each dimension, AND across dimensions.
      if (filters.tags.length && !filters.tags.some((t) => c.tags.includes(t))) return false
      if (filters.assignees.length) {
        // Track-mode chores have no single current assignee — match any of their track owners.
        const ids = c.tracks.length
          ? c.tracks.map((t) => t.user.id as string | undefined)
          : [c.currentAssignee?.id, ...(filters.includeNext ? [c.nextAssignee?.id] : [])]
        if (!filters.assignees.some((a) => ids.includes(a))) return false
      }
      if (filters.repeat.length && !filters.repeat.includes(c.repeatType)) return false
      if (filters.due.length && !filters.due.includes(choreDueStatus(c))) return false
      return true
    })
    const buckets = { overdue: [] as Chore[], today: [] as Chore[], upcoming: [] as Chore[], later: [] as Chore[] }
    for (const c of filtered) buckets[choreDueStatus(c)].push(c)
    return { ...buckets, filtered }
  }, [chores, filters])

  const itemProps = (chore: Chore) => ({
    chore,
    isAdmin,
    undoPending: undoMutation.isPending,
    skipPending: skipMutation.isPending,
    deletePending: deleteMutation.isPending,
    onComplete: () => setCompleting(chore),
    onUndo: async () => {
      const wasSkip = chore.lastCompletion?.isSkip
      if (
        await confirm({
          title: wasSkip ? 'Undo skip' : 'Undo completion',
          message: wasSkip
            ? 'Undo the last skip? The chore returns to its previous due date.'
            : 'Undo the last completion? Points will be reversed.',
          confirmLabel: 'Undo',
        })
      ) {
        undoMutation.mutate(chore.lastCompletion!.id)
      }
    },
    onSkip: async () => {
      if (
        await confirm({
          title: 'Skip occurrence',
          message: `Skip this occurrence of "${chore.name}"? It advances to the next due date without awarding points.`,
          confirmLabel: 'Skip',
          variant: 'primary',
        })
      ) {
        skipMutation.mutate(chore.id)
      }
    },
    onReassign: () => setReassigning(chore),
    onReschedule: () => setRescheduling(chore),
    onEdit: () => setEditing(chore),
    onCopy: () => setCopying(chore),
    onDelete: async () => {
      if (
        await confirm({
          title: 'Delete chore',
          message: `Delete "${chore.name}"? This wipes its completion history.`,
          confirmLabel: 'Delete',
        })
      ) {
        deleteMutation.mutate(chore.id)
      }
    },
    onDetails: () => setDetails(chore),
  })

  return (
    <div className="space-y-4 pb-24 md:space-y-6">
      {/* Quick views + view switcher + filters (round icon toolbar) */}
      {(chores ?? []).length > 0 && (
        <ChoreFilters
          value={filters}
          onChange={setFilters}
          tags={allTags}
          assignees={allAssignees}
          currentUserId={currentUser?.id}
          view={view}
          onViewChange={changeView}
        />
      )}

      {isLoading && <p className="text-muted-foreground">Loading…</p>}
      {error && <p className="text-destructive">{(error as ApiError).message}</p>}

      {!isLoading && (chores ?? []).length === 0 && (
        <p className="text-muted-foreground">No chores yet{isAdmin ? ', add one to get started.' : '.'}</p>
      )}
      {!isLoading && (chores ?? []).length > 0 && view !== 'calendar' && filtered.length === 0 && (
        <p className="text-muted-foreground">No chores match the current filters.</p>
      )}

      {view === 'calendar' && (chores ?? []).length > 0 && (
        <ChoreCalendar chores={filtered} itemProps={itemProps} />
      )}

      {view !== 'calendar' &&
        ([
          { title: 'Overdue', tone: 'destructive' as const, items: overdue },
          { title: 'Today', tone: undefined, items: today },
          { title: 'This week', tone: undefined, items: upcoming },
          { title: 'Later', tone: undefined, items: later },
        ])
          .filter((s) => s.items.length > 0)
          .map((s) => (
            <ChoreSection key={s.title} title={s.title} tone={s.tone} count={s.items.length}>
              {view === 'compact' ? (
                <Card className="divide-y divide-border p-0">
                  {s.items.map((chore) => <ChoreCompactItem key={chore.id} {...itemProps(chore)} />)}
                </Card>
              ) : (
                <div className="grid gap-6">
                  {s.items.map((chore) => <ChoreListItem key={chore.id} {...itemProps(chore)} />)}
                </div>
              )}
            </ChoreSection>
          ))}

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

      {reassigning && (
        <ReassignModal
          chore={reassigning}
          onClose={() => setReassigning(null)}
          onDone={() => {
            setReassigning(null)
            invalidate()
          }}
        />
      )}

      {rescheduling && (
        <RescheduleModal
          chore={rescheduling}
          onClose={() => setRescheduling(null)}
          onDone={() => {
            setRescheduling(null)
            invalidate()
          }}
        />
      )}

      {copying && (
        <CopyChoreModal
          chore={copying}
          onClose={() => setCopying(null)}
          onDone={() => {
            setCopying(null)
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

      {isAdmin && (
        <button
          type="button"
          onClick={() => setCreating(true)}
          aria-label="Add chore"
          className="fixed bottom-6 left-6 z-30 flex h-14 w-14 items-center justify-center rounded-full bg-primary text-primary-foreground shadow-pop transition-colors hover:bg-primary/90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring md:left-auto md:right-6"
        >
          <PlusIcon />
        </button>
      )}
    </div>
  )
}
