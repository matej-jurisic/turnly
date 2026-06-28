import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { choresApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import type { Chore } from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { Label, Select } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'

export function ReassignModal({ chore, isAdmin, onClose, onDone }: { chore: Chore; isAdmin: boolean; onClose: () => void; onDone: () => void }) {
  // Admins default to the current assignee; members can't pick themselves, so default to the first
  // other assignee.
  const [assigneeId, setAssigneeId] = useState(
    isAdmin
      ? chore.currentAssignee?.id ?? chore.assignees[0]?.id ?? ''
      : chore.assignees.find((u) => u.id !== chore.currentAssignee?.id)?.id ?? '',
  )

  const mutation = useMutation({
    mutationFn: () => choresApi.reassign(chore.id, { assigneeId }),
    onSuccess: () => {
      if (!isAdmin) toast.success('Reassignment request sent.')
      onDone()
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : 'Reassign failed'),
  })

  return (
    <Modal title={isAdmin ? 'Reassign chore' : 'Request reassignment'} onClose={onClose}>
      <form
        className="space-y-4"
        onSubmit={(e) => { e.preventDefault(); mutation.mutate() }}
      >
        <p className="text-sm text-muted-foreground">
          {isAdmin ? (
            <>
              Reassign the current occurrence of <span className="font-semibold text-foreground">{chore.name}</span> to
              another member. Future occurrences still follow the chore's assignment strategy.
            </>
          ) : (
            <>
              Ask another member to take the current occurrence of <span className="font-semibold text-foreground">{chore.name}</span>.
              It stays your chore until they accept the request.
            </>
          )}
        </p>
        <div className="space-y-1.5">
          <Label htmlFor="reassign-assignee">{isAdmin ? 'Assignee' : 'Send request to'}</Label>
          <Select id="reassign-assignee" value={assigneeId} onChange={(e) => setAssigneeId(e.target.value)}>
            {chore.assignees
              .filter((u) => isAdmin || u.id !== chore.currentAssignee?.id)
              .map((u) => (
                <option key={u.id} value={u.id}>{u.displayName}</option>
              ))}
          </Select>
        </div>
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>Cancel</Button>
          <Button type="submit" disabled={mutation.isPending || !assigneeId}>
            {mutation.isPending ? 'Saving…' : isAdmin ? 'Reassign' : 'Send request'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
