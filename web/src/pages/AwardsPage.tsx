import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, authApi, awardsApi, redemptionsApi } from '@/lib/api'
import { toast } from '@/lib/toast'
import { confirm } from '@/lib/confirm'
import { useAuthStore } from '@/store/auth'
import type { Award, AwardRequest, Redemption } from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/Card'
import { Input, Label } from '@/components/ui/Field'
import { Modal, Avatar } from '@/components/ui/Modal'

export function AwardsPage() {
  const currentUser = useAuthStore((s) => s.user)
  const isAdmin = currentUser?.role === 'Admin'
  const queryClient = useQueryClient()

  const { data: me } = useQuery({ queryKey: ['me'], queryFn: authApi.me })
  const { data: awards, isLoading, error } = useQuery({ queryKey: ['awards'], queryFn: awardsApi.list })

  const [editing, setEditing] = useState<Award | null>(null)
  const [creating, setCreating] = useState(false)

  // Redeeming/cancelling change points, so refresh balance, log, and leaderboard too.
  const refreshAfterSpend = () => {
    void queryClient.invalidateQueries({ queryKey: ['redemptions'] })
    void queryClient.invalidateQueries({ queryKey: ['me'] })
    void queryClient.invalidateQueries({ queryKey: ['leaderboard'] })
    if (currentUser) void queryClient.invalidateQueries({ queryKey: ['points-log', currentUser.id] })
  }

  const deleteMutation = useMutation({
    mutationFn: (id: string) => awardsApi.remove(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['awards'] }),
    onError: (err) => toast.error(err instanceof ApiError ? err.message : 'Delete failed'),
  })

  const redeemMutation = useMutation({
    mutationFn: (id: string) => awardsApi.redeem(id),
    onSuccess: refreshAfterSpend,
    onError: (err) => toast.error(err instanceof ApiError ? err.message : 'Redeem failed'),
  })

  const balance = me?.points ?? 0

  return (
    <div className="space-y-8">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold text-foreground">Awards</h1>
        <div className="flex items-center gap-3">
          <Badge tone="violet">{balance} pts</Badge>
          {isAdmin && <Button onClick={() => setCreating(true)}>Add award</Button>}
        </div>
      </div>

      {awards && awards.length > 0 && <NextGoalCard balance={balance} awards={awards} />}

      {isLoading && <p className="text-muted-foreground">Loading…</p>}
      {error && <p className="text-destructive">{(error as ApiError).message}</p>}

      {awards && awards.length === 0 && (
        <p className="text-sm text-muted-foreground">
          No awards yet.{isAdmin ? ' Add one to get started.' : ''}
        </p>
      )}

      {awards && awards.length > 0 && (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {awards.map((award) => {
            const affordable = balance >= award.cost
            return (
              <Card key={award.id} className="flex flex-col">
                <CardContent className="flex flex-1 flex-col gap-3">
                  <div className="flex items-start gap-3">
                    <span className="text-3xl leading-none">{award.emoji ?? '🎁'}</span>
                    <div className="min-w-0 flex-1">
                      <p className="truncate font-semibold text-foreground">{award.name}</p>
                      {award.description && (
                        <p className="mt-0.5 text-sm text-muted-foreground">{award.description}</p>
                      )}
                    </div>
                  </div>
                  <div className="mt-auto flex items-center justify-between pt-2">
                    <Badge tone="violet">{award.cost} pts</Badge>
                    <div className="flex gap-1">
                      {isAdmin && (
                        <>
                          <Button size="sm" variant="ghost" onClick={() => setEditing(award)}>Edit</Button>
                          <Button
                            size="sm"
                            variant="ghost"
                            className="text-destructive hover:bg-destructive/10"
                            disabled={deleteMutation.isPending}
                            onClick={async () => {
                              if (
                                await confirm({
                                  title: 'Delete award',
                                  message: `Delete ${award.name}? Past redemptions are kept.`,
                                  confirmLabel: 'Delete',
                                })
                              ) {
                                deleteMutation.mutate(award.id)
                              }
                            }}
                          >
                            Delete
                          </Button>
                        </>
                      )}
                      <Button
                        size="sm"
                        disabled={!affordable || redeemMutation.isPending}
                        title={affordable ? undefined : 'Not enough points'}
                        onClick={async () => {
                          if (
                            await confirm({
                              title: 'Redeem award',
                              message: `Redeem ${award.name} for ${award.cost} points?`,
                              confirmLabel: 'Redeem',
                              variant: 'primary',
                            })
                          ) {
                            redeemMutation.mutate(award.id)
                          }
                        }}
                      >
                        Redeem
                      </Button>
                    </div>
                  </div>
                </CardContent>
              </Card>
            )
          })}
        </div>
      )}

      <RedemptionsSection isAdmin={isAdmin} onChange={refreshAfterSpend} />

      {creating && (
        <AwardFormModal
          title="Add award"
          onClose={() => setCreating(false)}
          onSaved={() => {
            setCreating(false)
            void queryClient.invalidateQueries({ queryKey: ['awards'] })
          }}
        />
      )}

      {editing && (
        <AwardFormModal
          title="Edit award"
          award={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            void queryClient.invalidateQueries({ queryKey: ['awards'] })
          }}
        />
      )}
    </div>
  )
}

/** Headline progress toward the cheapest award the user can't yet afford. Once everything is
 * affordable it turns into a small "you can redeem anything" nudge. */
function NextGoalCard({ balance, awards }: { balance: number; awards: Award[] }) {
  // Cheapest award still out of reach = the natural next goal.
  const goal = awards
    .filter((a) => a.cost > balance)
    .sort((a, b) => a.cost - b.cost)[0]

  if (!goal) {
    return (
      <Card>
        <CardContent className="flex items-center gap-3 py-4">
          <span className="text-2xl leading-none">🎉</span>
          <p className="text-sm text-foreground">
            You have enough points to redeem any award. Treat yourself!
          </p>
        </CardContent>
      </Card>
    )
  }

  const remaining = goal.cost - balance
  const pct = Math.max(0, Math.min(100, Math.round((balance / goal.cost) * 100)))

  return (
    <Card>
      <CardContent className="space-y-3 py-4">
        <div className="flex items-center justify-between gap-3">
          <div className="flex min-w-0 items-center gap-2">
            <span className="text-2xl leading-none">{goal.emoji ?? '🎁'}</span>
            <div className="min-w-0">
              <p className="text-xs text-muted-foreground">Next goal</p>
              <p className="truncate font-semibold text-foreground">{goal.name}</p>
            </div>
          </div>
          <Badge tone="amber">{remaining} pts to go</Badge>
        </div>
        <div className="h-2 overflow-hidden rounded-full bg-accent">
          <div className="h-full rounded-full bg-primary transition-all" style={{ width: `${pct}%` }} />
        </div>
        <p className="text-xs text-muted-foreground">
          {balance} / {goal.cost} pts ({pct}%)
        </p>
      </CardContent>
    </Card>
  )
}

function RedemptionsSection({ isAdmin, onChange }: { isAdmin: boolean; onChange: () => void }) {
  const { data: redemptions } = useQuery({ queryKey: ['redemptions'], queryFn: redemptionsApi.list })

  const fulfillMutation = useMutation({
    mutationFn: (id: string) => redemptionsApi.fulfill(id),
    onSuccess: onChange,
    onError: (err) => toast.error(err instanceof ApiError ? err.message : 'Fulfill failed'),
  })

  const cancelMutation = useMutation({
    mutationFn: (id: string) => redemptionsApi.cancel(id),
    onSuccess: onChange,
    onError: (err) => toast.error(err instanceof ApiError ? err.message : 'Cancel failed'),
  })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => redemptionsApi.remove(id),
    onSuccess: onChange,
    onError: (err) => toast.error(err instanceof ApiError ? err.message : 'Delete failed'),
  })

  return (
    <Card>
      <CardHeader>
        <CardTitle>{isAdmin ? 'Redemptions' : 'My redemptions'}</CardTitle>
      </CardHeader>
      <CardContent className="px-0 py-0">
        {!redemptions || redemptions.length === 0 ? (
          <p className="px-6 py-4 text-sm text-muted-foreground">No redemptions yet.</p>
        ) : (
          <ul className="divide-y divide-border">
            {redemptions.map((r) => (
              <RedemptionRow
                key={r.id}
                redemption={r}
                isAdmin={isAdmin}
                onFulfill={() => fulfillMutation.mutate(r.id)}
                onCancel={async () => {
                  if (
                    await confirm({
                      title: 'Cancel redemption',
                      message: `Cancel this redemption and refund ${r.pointsSpent} points?`,
                      confirmLabel: 'Cancel redemption',
                      cancelLabel: 'Keep',
                    })
                  ) {
                    cancelMutation.mutate(r.id)
                  }
                }}
                onDelete={async () => {
                  if (
                    await confirm({
                      title: 'Delete redemption',
                      message: `Delete this redemption and refund ${r.pointsSpent} points? This removes it from the history.`,
                      confirmLabel: 'Delete',
                    })
                  ) {
                    deleteMutation.mutate(r.id)
                  }
                }}
                busy={fulfillMutation.isPending || cancelMutation.isPending || deleteMutation.isPending}
              />
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}

function RedemptionRow({
  redemption: r,
  isAdmin,
  onFulfill,
  onCancel,
  onDelete,
  busy,
}: {
  redemption: Redemption
  isAdmin: boolean
  onFulfill: () => void
  onCancel: () => void
  onDelete: () => void
  busy: boolean
}) {
  return (
    <li className="flex flex-wrap items-center gap-x-3 gap-y-2 px-4 py-3 sm:px-6">
      <span className="text-2xl leading-none">{r.awardEmoji ?? '🎁'}</span>
      {isAdmin && <Avatar color={r.user.avatarColor} name={r.user.displayName} size={28} />}
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm text-foreground">
          {r.awardName}
          {isAdmin && <span className="ml-2 text-xs text-muted-foreground">{r.user.displayName}</span>}
        </p>
        <p className="text-xs text-muted-foreground">{new Date(r.redeemedAt).toLocaleDateString()}</p>
      </div>
      <div className="ml-auto flex items-center gap-3">
        <span className="text-sm text-destructive">−{r.pointsSpent}</span>
        <Badge tone={r.status === 'Fulfilled' ? 'green' : 'amber'}>{r.status}</Badge>
      </div>
      {isAdmin && (
        <div className="flex gap-1">
          {r.status === 'Pending' && (
            <>
              <Button size="sm" variant="ghost" disabled={busy} onClick={onFulfill}>Fulfill</Button>
              <Button
                size="sm"
                variant="ghost"
                className="text-destructive hover:bg-destructive/10"
                disabled={busy}
                onClick={onCancel}
              >
                Cancel
              </Button>
            </>
          )}
          <Button
            size="sm"
            variant="ghost"
            className="text-destructive hover:bg-destructive/10"
            disabled={busy}
            onClick={onDelete}
          >
            Delete
          </Button>
        </div>
      )}
    </li>
  )
}

interface AwardFormModalProps {
  title: string
  award?: Award
  onClose: () => void
  onSaved: () => void
}

function AwardFormModal({ title, award, onClose, onSaved }: AwardFormModalProps) {
  const isEdit = Boolean(award)
  const [name, setName] = useState(award?.name ?? '')
  const [description, setDescription] = useState(award?.description ?? '')
  const [emoji, setEmoji] = useState(award?.emoji ?? '')
  const [cost, setCost] = useState(String(award?.cost ?? ''))
  const [error, setError] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: () => {
      const body: AwardRequest = {
        name,
        description: description.trim() || null,
        emoji: emoji.trim() || null,
        cost: Number(cost),
      }
      return isEdit && award ? awardsApi.update(award.id, body) : awardsApi.create(body)
    },
    onSuccess: onSaved,
    onError: (err) => setError(err instanceof ApiError ? err.message : 'Save failed'),
  })

  return (
    <Modal title={title} onClose={onClose}>
      <form
        onSubmit={(e) => {
          e.preventDefault()
          setError(null)
          mutation.mutate()
        }}
        className="space-y-4"
      >
        <div>
          <Label htmlFor="name">Name</Label>
          <Input id="name" value={name} onChange={(e) => setName(e.target.value)} required />
        </div>
        <div>
          <Label htmlFor="description">Description</Label>
          <Input id="description" value={description} onChange={(e) => setDescription(e.target.value)} />
        </div>
        <div className="flex gap-3">
          <div className="w-24">
            <Label htmlFor="emoji">Emoji</Label>
            <Input id="emoji" value={emoji} onChange={(e) => setEmoji(e.target.value)} placeholder="🎁" />
          </div>
          <div className="flex-1">
            <Label htmlFor="cost">Cost (points)</Label>
            <Input
              id="cost"
              type="number"
              min={1}
              value={cost}
              onChange={(e) => setCost(e.target.value)}
              required
            />
          </div>
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
