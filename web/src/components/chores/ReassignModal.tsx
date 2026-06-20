import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { choresApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import type { Chore } from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { Label, Select } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'

export function ReassignModal({ chore, onClose, onDone }: { chore: Chore; onClose: () => void; onDone: () => void }) {
  const [assigneeId, setAssigneeId] = useState(chore.currentAssignee?.id ?? chore.assignees[0]?.id ?? '')

  const mutation = useMutation({
    mutationFn: () => choresApi.reassign(chore.id, { assigneeId }),
    onSuccess: onDone,
    onError: (err) => toast.error(err instanceof ApiError ? err.message : 'Reassign failed'),
  })

  return (
    <Modal title="Reassign chore" onClose={onClose}>
      <form
        className="space-y-4"
        onSubmit={(e) => { e.preventDefault(); mutation.mutate() }}
      >
        <p className="text-sm text-muted-foreground">
          Reassign the current occurrence of <span className="font-semibold text-foreground">{chore.name}</span> to
          another member. Future occurrences still follow the chore's assignment strategy.
        </p>
        <div className="space-y-1.5">
          <Label htmlFor="reassign-assignee">Assignee</Label>
          <Select id="reassign-assignee" value={assigneeId} onChange={(e) => setAssigneeId(e.target.value)}>
            {chore.assignees.map((u) => (
              <option key={u.id} value={u.id}>{u.displayName}</option>
            ))}
          </Select>
        </div>
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>Cancel</Button>
          <Button type="submit" disabled={mutation.isPending || !assigneeId}>
            {mutation.isPending ? 'Saving…' : 'Reassign'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
