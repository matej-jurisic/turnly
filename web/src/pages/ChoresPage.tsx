import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { choresApi, notificationsApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import { confirm } from '@/lib/confirm'
import { useAuthStore } from '@/store/auth'
import type { Chore, LeaderboardEntry, User } from '@/lib/types'
import { PlusIcon } from '@/components/chores/icons'
import { CompleteModal } from '@/components/CompleteModal'
import { ChoreDetailsModal } from '@/components/ChoreDetailsModal'
import { ChoreRail } from '@/components/chores/ChoreRail'
import { UserDetailsModal } from '@/components/UserDetailsModal'
import { ChoreSection } from '@/components/chores/ChoreSection'
import { ChoreListItem } from '@/components/chores/ChoreListItem'
import { ChoreCompactItem } from '@/components/chores/ChoreCompactItem'
import { ChoreCalendar } from '@/components/chores/ChoreCalendar'
import { ChoreConfigTable } from '@/components/chores/ChoreConfigTable'
import { Card } from '@/components/ui/Card'
import { ChoreFormModal } from '@/components/chores/ChoreFormModal'
import { ReassignModal } from '@/components/chores/ReassignModal'
import { OfferModal } from '@/components/chores/OfferModal'
import { NudgeModal } from '@/components/chores/NudgeModal'
import { OfferBoard } from '@/components/chores/OfferBoard'
import { CopyChoreModal } from '@/components/chores/CopyChoreModal'
import { RescheduleModal } from '@/components/chores/RescheduleModal'
import { ChoreFilters, type ChoreView } from '@/components/chores/ChoreFilters'
import { emptyFilters, mineFilters, type ChoreFilterState } from '@/lib/chore-filters'
import { choreDueStatus, isAnyone, isOnDemand } from '@/lib/chore-format'
import { cn } from '@/lib/utils'
import { t } from '@/lib/i18n'

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
  const [offering, setOffering] = useState<Chore | null>(null)
  const [nudging, setNudging] = useState<Chore | null>(null)
  const [rescheduling, setRescheduling] = useState<Chore | null>(null)
  const [copying, setCopying] = useState<Chore | null>(null)
  const [details, setDetails] = useState<Chore | null>(null)
  const [railUser, setRailUser] = useState<LeaderboardEntry | null>(null)
  const [pickedFilters, setFilters] = useState<ChoreFilterState | null>(null)
  const [storedView, setView] = useState<ChoreView>(
    () => (localStorage.getItem(VIEW_KEY) as ChoreView | null) ?? 'list',
  )
  // The config table is admin-only; a stored 'config' (e.g. from before a demotion, or another
  // account on this browser) falls back to the list.
  const view: ChoreView = storedView === 'config' && !isAdmin ? 'list' : storedView
  // Due-bucketed row layouts, as opposed to the calendar grid and the config table.
  const sectioned = view === 'list' || view === 'compact'
  const changeView = (v: ChoreView) => {
    setView(v)
    localStorage.setItem(VIEW_KEY, v)
  }

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['chores'] })

  const choreParam = searchParams.get('chore')
  // Open the linked chore while rendering (not in the effect) so it doesn't cost an extra render.
  const linked = choreParam ? chores?.find((c) => c.id === choreParam) : undefined
  if (linked && details?.id !== linked.id) setDetails(linked)
  useEffect(() => {
    if (!choreParam || !chores) return
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
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('chores.deleteFailed')),
  })

  const undoMutation = useMutation({
    mutationFn: (completionId: string) => choresApi.undoCompletion(completionId),
    onSuccess: invalidate,
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('chores.undoFailed')),
  })

  const skipMutation = useMutation({
    mutationFn: (id: string) => choresApi.skip(id, { notes: null }),
    onSuccess: invalidate,
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('chores.skipFailed')),
  })

  const freezeMutation = useMutation({
    mutationFn: (id: string) => choresApi.freeze(id),
    onSuccess: invalidate,
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('chores.pauseFailed')),
  })

  const unfreezeMutation = useMutation({
    mutationFn: (id: string) => choresApi.unfreeze(id),
    onSuccess: invalidate,
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('chores.unpauseFailed')),
  })

  // A mute is personal, so it only changes this viewer's chore list — no leaderboard/points to sync.
  const muteMutation = useMutation({
    mutationFn: ({ id, muted }: { id: string; muted: boolean }) =>
      muted ? notificationsApi.unmuteChore(id) : notificationsApi.muteChore(id),
    onSuccess: (_data, { muted }) => {
      queryClient.invalidateQueries({ queryKey: ['chores'] })
      toast.success(muted ? t('chores.unmuted') : t('chores.muted'))
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('common.error')),
  })

  const allTags = useMemo(
    () => [...new Set((chores ?? []).flatMap((c) => c.tags))].sort(),
    [chores],
  )
  const allAssignees = useMemo(() => {
    const map = new Map<string, User>()
    for (const c of chores ?? []) {
      for (const u of [c.currentAssignee, c.currentPartner, c.nextAssignee, c.nextPartner])
        if (u) map.set(u.id, u)
      for (const t of c.tracks) map.set(t.user.id, t.user)
      if (isAnyone(c)) for (const u of c.assignees) map.set(u.id, u)
    }
    return [...map.values()].sort((a, b) => a.displayName.localeCompare(b.displayName))
  }, [chores])

  // Until the user picks something, open on the "Mine" quick view when it's offered (more than one
  // member to distinguish).
  const meId = currentUser?.id
  const filters = useMemo<ChoreFilterState>(
    () => pickedFilters ?? (meId && allAssignees.length > 1 ? mineFilters(meId) : emptyFilters),
    [pickedFilters, meId, allAssignees.length],
  )

  const { overdue, today, upcoming, later, anytime, paused, filtered } = useMemo(() => {
    const filtered = (chores ?? []).filter((c) => {
      // OR within each dimension, AND across dimensions.
      if (filters.tags.length && !filters.tags.some((t) => c.tags.includes(t))) return false
      if (filters.assignees.length) {
        // Track-mode chores have no single current assignee — match any of their track owners; an
        // Anyone chore is on every assignee's plate.
        const ids = c.tracks.length
          ? c.tracks.map((t) => t.user.id as string | undefined)
          : isAnyone(c)
            ? c.assignees.map((u) => u.id as string | undefined)
            : [c.currentAssignee?.id, c.currentPartner?.id,
              ...(filters.includeNext ? [c.nextAssignee?.id, c.nextPartner?.id] : [])]
        if (!filters.assignees.some((a) => ids.includes(a))) return false
      }
      if (filters.repeat.length && !filters.repeat.includes(c.repeatType)) return false
      if (filters.due.length && !filters.due.includes(choreDueStatus(c))) return false
      return true
    })
    const paused = filtered.filter((c) => c.isFrozen)
    const active = filtered.filter((c) => !c.isFrozen)
    const buckets = { overdue: [] as Chore[], today: [] as Chore[], upcoming: [] as Chore[], later: [] as Chore[] }
    for (const c of active) if (!isOnDemand(c)) buckets[choreDueStatus(c)].push(c)
    const anytime = active.filter(isOnDemand)
    return { ...buckets, anytime, paused, filtered }
  }, [chores, filters])

  const itemProps = (chore: Chore) => ({
    chore,
    isAdmin,
    meId: currentUser?.id,
    undoPending: undoMutation.isPending,
    skipPending: skipMutation.isPending,
    deletePending: deleteMutation.isPending,
    freezePending: freezeMutation.isPending || unfreezeMutation.isPending,
    mutePending: muteMutation.isPending,
    onComplete: () => setCompleting(chore),
    onUndo: async () => {
      const wasSkip = chore.lastCompletion?.isSkip
      if (
        await confirm({
          title: wasSkip ? t('chores.undoSkipTitle') : t('chores.undoCompletionTitle'),
          message: wasSkip ? t('chores.undoSkipMessage') : t('chores.undoCompletionMessage'),
          confirmLabel: t('common.undo'),
        })
      ) {
        undoMutation.mutate(chore.lastCompletion!.id)
      }
    },
    onSkip: async () => {
      if (
        await confirm({
          title: t('chores.skipTitle'),
          message: t('chores.skipMessage', { name: chore.name }),
          confirmLabel: t('common.skip'),
          variant: 'primary',
        })
      ) {
        skipMutation.mutate(chore.id)
      }
    },
    onReassign: () => setReassigning(chore),
    onOffer: () => setOffering(chore),
    onNudge: () => setNudging(chore),
    onReschedule: () => setRescheduling(chore),
    onEdit: () => setEditing(chore),
    onCopy: () => setCopying(chore),
    onDelete: async () => {
      if (
        await confirm({
          title: t('chores.deleteTitle'),
          message: t('chores.deleteMessage', { name: chore.name }),
          confirmLabel: t('common.delete'),
        })
      ) {
        deleteMutation.mutate(chore.id)
      }
    },
    onDetails: () => setDetails(chore),
    onToggleMute: () => muteMutation.mutate({ id: chore.id, muted: chore.isMuted }),
    onFreeze: () => freezeMutation.mutate(chore.id),
    onUnfreeze: () => unfreezeMutation.mutate(chore.id),
  })

  return (
    <div className="pb-24">
      {/* Below xl: a centered max-w-5xl column (the rail is hidden), matching every other page.
          At xl: the page goes full-width so the rail anchors to the right edge and the chore column
          centers in the space left of it. */}
      <div className="mx-auto w-full max-w-5xl xl:max-w-none xl:flex xl:items-start xl:gap-6">
        {/* Main column grows to fill the space left of the rail; its content is centered within
            that space (capped width + side gutters) so it doesn't hug the left edge. */}
        <div className="min-w-0 flex-1">
          <div className={cn('w-full space-y-4 md:space-y-6', view !== 'config' && 'xl:mx-auto xl:max-w-2xl')}>
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
                isAdmin={isAdmin}
              />
            )}

            {/* Offers you can take, above everything else and in every view — it's the only place
                they're discoverable on a phone. Hidden when there's nothing to claim. */}
            <OfferBoard chores={chores ?? []} meId={currentUser?.id} onSelect={setDetails} />

            {isLoading && <p className="text-muted-foreground">{t('common.loading')}</p>}
            {error && <p className="text-destructive">{(error as ApiError).message}</p>}

            {!isLoading && (chores ?? []).length === 0 && (
              <p className="text-muted-foreground">{isAdmin ? t('chores.emptyAdmin') : t('chores.empty')}</p>
            )}
            {!isLoading && (chores ?? []).length > 0 && view !== 'calendar' && filtered.length === 0 && (
              <p className="text-muted-foreground">{t('chores.noMatch')}</p>
            )}

            {view === 'calendar' && (chores ?? []).length > 0 && (
              <ChoreCalendar chores={filtered} itemProps={itemProps} />
            )}

            {view === 'config' && filtered.length > 0 && (
              <ChoreConfigTable chores={filtered} onEdit={setEditing} />
            )}

            {sectioned &&
              ([
                { title: t('due.overdue'), tone: 'destructive' as const, items: overdue },
                { title: t('due.today'), tone: undefined, items: today },
                { title: t('due.upcoming'), tone: undefined, items: upcoming },
                { title: t('due.later'), tone: undefined, items: later },
                { title: t('due.anytime'), tone: undefined, items: anytime },
                { title: t('chores.paused'), tone: 'neutral' as const, items: paused },
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
          </div>
        </div>

        {/* Right info rail (desktop xl+ only). The config table takes the full width instead. */}
        {currentUser && view !== 'config' && (
          <aside className="mt-4 hidden xl:mt-0 xl:block xl:w-72 xl:shrink-0">
            <div className="sticky top-20">
              <ChoreRail chores={chores ?? []} currentUser={currentUser} onSelectUser={setRailUser} />
            </div>
          </aside>
        )}
      </div>

      {creating && (
        <ChoreFormModal
          title={t('chores.add')}
          onClose={() => setCreating(false)}
          onSaved={() => {
            setCreating(false)
            invalidate()
          }}
        />
      )}

      {editing && (
        <ChoreFormModal
          title={t('chores.editTitle')}
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

      {offering && (
        <OfferModal
          chore={offering}
          onClose={() => setOffering(null)}
          onDone={() => {
            setOffering(null)
            invalidate()
          }}
        />
      )}

      {nudging && (
        <NudgeModal chore={nudging} onClose={() => setNudging(null)} />
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
          onNudge={() => { setNudging(details); setDetails(null) }}
        />
      )}

      {railUser && (
        <UserDetailsModal
          userId={railUser.id}
          displayName={railUser.displayName}
          avatarColor={railUser.avatarColor}
          avatarEmoji={railUser.avatarEmoji}
          equippedFrameKey={railUser.equippedFrameKey}
          points={railUser.points}
          weeklyPoints={railUser.weeklyPoints}
          onClose={() => setRailUser(null)}
        />
      )}

      {isAdmin && (
        <button
          type="button"
          onClick={() => setCreating(true)}
          aria-label={t('chores.add')}
          className="fixed bottom-6 left-6 z-30 flex h-14 w-14 items-center justify-center rounded-full bg-primary text-primary-foreground shadow-pop transition-colors hover:bg-primary/90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring md:left-auto md:right-6"
        >
          <PlusIcon />
        </button>
      )}
    </div>
  )
}
