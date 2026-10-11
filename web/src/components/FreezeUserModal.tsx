import { useMutation, useQuery } from '@tanstack/react-query'
import type { User } from '@/lib/types'
import { usersApi, ApiError } from '@/lib/api'
import { Modal, Avatar } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { toast } from '@/lib/toast'
import { t, tRich } from '@/lib/i18n'

interface FreezeUserModalProps {
  user: User
  onClose: () => void
  onDone: () => void
}

export function FreezeUserModal({ user, onClose, onDone }: FreezeUserModalProps) {
  const { data: preview, isLoading, error } = useQuery({
    queryKey: ['freeze-preview', user.id],
    queryFn: () => usersApi.freezePreview(user.id),
  })

  const freezeMutation = useMutation({
    mutationFn: () => usersApi.freeze(user.id),
    onSuccess: onDone,
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('freeze.failed')),
  })

  return (
    <Modal title={t('freeze.title', { name: user.displayName })} onClose={onClose}>
      <div className="space-y-4">
        <p className="text-sm text-muted-foreground">
          {tRich('freeze.intro', { name: <strong className="text-foreground">{user.displayName}</strong> })}
        </p>

        {isLoading && <p className="text-sm text-muted-foreground">{t('freeze.checking')}</p>}
        {error && <p className="text-sm text-destructive">{t('freeze.previewFailed', { error: (error as ApiError).message })}</p>}

        {preview && (
          <div className="space-y-3">
            {preview.reassignments.length > 0 && (
              <div>
                <p className="mb-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">
                  {t('freeze.reassigned')}
                </p>
                <div className="space-y-1.5">
                  {preview.reassignments.map((r) => (
                    <div
                      key={r.choreId}
                      className="flex items-center justify-between gap-2 rounded-lg border border-border bg-accent/50 px-3 py-2"
                    >
                      <span className="text-sm text-foreground">
                        {r.choreEmoji && <span className="mr-1">{r.choreEmoji}</span>}
                        {r.choreName}
                      </span>
                      <span className="flex shrink-0 items-center gap-1.5 text-xs text-muted-foreground">
                        <span>→</span>
                        <Avatar color={r.newAssigneeAvatarColor} name={r.newAssigneeName} size={18} />
                        <span>{r.newAssigneeName}</span>
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            )}

            {preview.unassignable.length > 0 && (
              <div>
                <p className="mb-1.5 text-xs font-medium uppercase tracking-wide text-warning">
                  {t('freeze.unassignable')}
                </p>
                <div className="space-y-1.5">
                  {preview.unassignable.map((u) => (
                    <div
                      key={u.choreId}
                      className="flex items-center gap-2 rounded-lg border border-warning/30 bg-warning/5 px-3 py-2"
                    >
                      <span className="text-sm text-foreground">
                        {u.choreEmoji && <span className="mr-1">{u.choreEmoji}</span>}
                        {u.choreName}
                      </span>
                      <span className="ml-auto text-xs text-warning">{t('freeze.willUnassign')}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}

            {preview.reassignments.length === 0 && preview.unassignable.length === 0 && (
              <p className="text-sm text-muted-foreground">{t('freeze.none')}</p>
            )}
          </div>
        )}

        <div className="flex justify-end gap-2 border-t border-border pt-4">
          <Button variant="ghost" onClick={onClose}>{t('common.cancel')}</Button>
          <Button
            onClick={() => freezeMutation.mutate()}
            disabled={isLoading || freezeMutation.isPending}
          >
            {freezeMutation.isPending ? t('freeze.freezing') : t('freeze.submit')}
          </Button>
        </div>
      </div>
    </Modal>
  )
}
