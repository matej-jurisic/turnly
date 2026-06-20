import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { useConfirmStore } from '@/lib/confirm'

/** Global confirm-dialog outlet. Mount once near the app root; driven by the `confirm` helper. */
export function ConfirmHost() {
  const pending = useConfirmStore((s) => s.pending)
  const settle = useConfirmStore((s) => s.settle)

  if (!pending) return null

  return (
    <Modal title={pending.title ?? 'Are you sure?'} onClose={() => settle(false)}>
      <p className="text-sm text-muted-foreground">{pending.message}</p>
      <div className="mt-6 flex justify-end gap-2">
        <Button variant="secondary" onClick={() => settle(false)}>
          {pending.cancelLabel ?? 'Cancel'}
        </Button>
        <Button variant={pending.variant === 'primary' ? 'primary' : 'danger'} onClick={() => settle(true)}>
          {pending.confirmLabel ?? 'Confirm'}
        </Button>
      </div>
    </Modal>
  )
}
