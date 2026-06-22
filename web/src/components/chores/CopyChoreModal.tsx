import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { choresApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import type { Chore } from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { Input, Label } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'

export function CopyChoreModal({ chore, onClose, onDone }: { chore: Chore; onClose: () => void; onDone: () => void }) {
  const [newName, setNewName] = useState(chore.name)

  const mutation = useMutation({
    mutationFn: () => choresApi.copy(chore.id, { newName: newName.trim() }),
    onSuccess: () => {
      toast.success('Chore copied')
      onDone()
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : 'Copy failed'),
  })

  return (
    <Modal title="Copy chore" onClose={onClose}>
      <form
        className="space-y-4"
        onSubmit={(e) => { e.preventDefault(); mutation.mutate() }}
      >
        <p className="text-sm text-muted-foreground">
          Create a copy of <span className="font-semibold text-foreground">{chore.name}</span> with all the
          same settings. The copy starts fresh, with no completion history carried over.
        </p>
        <div className="space-y-1.5">
          <Label htmlFor="copy-name">Name</Label>
          <Input
            id="copy-name"
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
            autoFocus
            required
          />
        </div>
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>Cancel</Button>
          <Button type="submit" disabled={mutation.isPending || !newName.trim()}>
            {mutation.isPending ? 'Copying…' : 'Copy'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
