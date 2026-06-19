import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { choresApi, ApiError } from '@/lib/api'
import type { Chore } from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { Input, Label } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'

interface CompleteModalProps {
  chore: Chore
  onClose: () => void
  onDone: () => void
}

export function CompleteModal({ chore, onClose, onDone }: CompleteModalProps) {
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
