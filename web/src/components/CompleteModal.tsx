import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { choresApi, usersApi, ApiError } from '@/lib/api'
import { celebrate } from '@/lib/confetti'
import { useAuthStore } from '@/store/auth'
import type { Chore } from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { Input, Label, Select } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'

interface CompleteModalProps {
  chore: Chore
  onClose: () => void
  onDone: () => void
}

export function CompleteModal({ chore, onClose, onDone }: CompleteModalProps) {
  const currentUser = useAuthStore((s) => s.user)
  const isAdmin = currentUser?.role === 'Admin'

  const [notes, setNotes] = useState('')
  const [completedByUserId, setCompletedByUserId] = useState(currentUser?.id ?? '')
  const [error, setError] = useState<string | null>(null)

  // Admins can credit the completion to another household member. The /users endpoint is
  // admin-only, which is fine since only admins see this picker.
  const { data: users } = useQuery({ queryKey: ['users'], queryFn: usersApi.list, enabled: isAdmin })

  const onBehalf = isAdmin && completedByUserId !== '' && completedByUserId !== currentUser?.id

  const mutation = useMutation({
    mutationFn: () =>
      choresApi.complete(chore.id, {
        notes: notes.trim() || null,
        completedByUserId: onBehalf ? completedByUserId : null,
      }),
    onSuccess: () => {
      celebrate()
      onDone()
    },
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
          {onBehalf ? (
            <>This earns <span className="font-medium text-foreground">{chore.points} points</span> for the selected member.</>
          ) : (
            <>You'll earn <span className="font-medium text-foreground">{chore.points} points</span>.</>
          )}
        </p>
        {isAdmin && (users?.length ?? 0) > 0 && (
          <div>
            <Label htmlFor="completed-by">Completed by</Label>
            <Select
              id="completed-by"
              value={completedByUserId}
              onChange={(e) => setCompletedByUserId(e.target.value)}
            >
              {users!.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.displayName}{u.id === currentUser?.id ? ' (you)' : ''}
                </option>
              ))}
            </Select>
          </div>
        )}
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
