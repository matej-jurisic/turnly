import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { usersApi, ApiError } from '@/lib/api'
import { toast } from '@/lib/toast'
import { confirm } from '@/lib/confirm'
import { useAuthStore } from '@/store/auth'
import type { CreateUserRequest, UpdateUserRequest, User, UserRole } from '@/lib/types'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Card } from '@/components/ui/Card'
import { Input, Label, Select } from '@/components/ui/Field'
import { Modal, Avatar } from '@/components/ui/Modal'
import { FreezeUserModal } from '@/components/FreezeUserModal'
import { UserMenu } from '@/components/users/UserMenu'
import { t } from '@/lib/i18n'

export function UsersPage() {
  const currentUser = useAuthStore((s) => s.user)
  const queryClient = useQueryClient()
  const { data: users, isLoading, error } = useQuery({ queryKey: ['users'], queryFn: usersApi.list })

  const [editing, setEditing] = useState<User | null>(null)
  const [creating, setCreating] = useState(false)
  const [passwordFor, setPasswordFor] = useState<User | null>(null)
  const [adjustPointsFor, setAdjustPointsFor] = useState<User | null>(null)
  const [freezingUser, setFreezingUser] = useState<User | null>(null)

  const invalidateAll = () => {
    queryClient.invalidateQueries({ queryKey: ['users'] })
    queryClient.invalidateQueries({ queryKey: ['chores'] })
  }

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['users'] })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => usersApi.remove(id),
    onSuccess: invalidate,
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('chores.deleteFailed')),
  })

  const unfreezeMutation = useMutation({
    mutationFn: (id: string) => usersApi.unfreeze(id),
    onSuccess: invalidateAll,
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t('users.unfreezeFailed')),
  })

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold text-foreground">{t('nav.users')}</h1>
        <Button onClick={() => setCreating(true)}>{t('users.add')}</Button>
      </div>

      {isLoading && <p className="text-muted-foreground">{t('common.loading')}</p>}
      {error && <p className="text-destructive">{(error as ApiError).message}</p>}

      <Card className="divide-y divide-border">
        {users?.map((user) => (
          <div key={user.id} className="flex items-center gap-3 px-4 py-3 sm:gap-4">
            <div className="flex min-w-0 flex-1 items-center gap-3">
              <Avatar color={user.avatarColor} name={user.displayName} frame={user.equippedFrameKey} emoji={user.avatarEmoji} />
              <div className="min-w-0">
                <p className="truncate text-foreground">
                  {user.displayName}
                  {user.id === currentUser?.id && <span className="ml-2 text-xs text-muted-foreground">{t('users.you')}</span>}
                </p>
                <div className="mt-0.5 flex items-center gap-2">
                  <span className="truncate text-sm text-muted-foreground">@{user.username}</span>
                  <Badge tone={user.role === 'Admin' ? 'violet' : 'neutral'}>{t(`role.${user.role}`)}</Badge>
                  {user.isFrozen && <Badge tone="amber">{t('users.away')}</Badge>}
                </div>
              </div>
            </div>
            <div className="flex justify-end sm:shrink-0">
              <UserMenu
                user={user}
                isSelf={user.id === currentUser?.id}
                unfreezePending={unfreezeMutation.isPending}
                deletePending={deleteMutation.isPending}
                onEdit={() => setEditing(user)}
                onPassword={() => setPasswordFor(user)}
                onPoints={() => setAdjustPointsFor(user)}
                onFreeze={() => setFreezingUser(user)}
                onUnfreeze={() => unfreezeMutation.mutate(user.id)}
                onDelete={async () => {
                  if (
                    await confirm({
                      title: t('users.deleteTitle'),
                      message: t('users.deleteMessage', { name: user.displayName }),
                      confirmLabel: t('common.delete'),
                    })
                  ) {
                    deleteMutation.mutate(user.id)
                  }
                }}
              />
            </div>
          </div>
        ))}
      </Card>

      {creating && (
        <UserFormModal
          title={t('users.add')}
          onClose={() => setCreating(false)}
          onSaved={() => {
            setCreating(false)
            invalidate()
          }}
        />
      )}

      {editing && (
        <UserFormModal
          title={t('users.editTitle')}
          user={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            invalidate()
          }}
        />
      )}

      {passwordFor && (
        <PasswordModal user={passwordFor} onClose={() => setPasswordFor(null)} />
      )}

      {adjustPointsFor && (
        <AdjustPointsModal
          user={adjustPointsFor}
          onClose={() => setAdjustPointsFor(null)}
          onSaved={() => {
            setAdjustPointsFor(null)
            queryClient.invalidateQueries({ queryKey: ['users'] })
            queryClient.invalidateQueries({ queryKey: ['leaderboard'] })
            queryClient.invalidateQueries({ queryKey: ['points-log', adjustPointsFor.id] })
          }}
        />
      )}

      {freezingUser && (
        <FreezeUserModal
          user={freezingUser}
          onClose={() => setFreezingUser(null)}
          onDone={() => {
            setFreezingUser(null)
            invalidateAll()
          }}
        />
      )}
    </div>
  )
}

interface UserFormModalProps {
  title: string
  user?: User
  onClose: () => void
  onSaved: () => void
}

function UserFormModal({ title, user, onClose, onSaved }: UserFormModalProps) {
  const isEdit = Boolean(user)
  const [username, setUsername] = useState(user?.username ?? '')
  const [displayName, setDisplayName] = useState(user?.displayName ?? '')
  const [password, setPassword] = useState('')
  const [role, setRole] = useState<UserRole>(user?.role ?? 'Member')
  const [error, setError] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: () => {
      if (isEdit && user) {
        const body: UpdateUserRequest = { displayName, role }
        return usersApi.update(user.id, body)
      }
      const body: CreateUserRequest = { username, displayName, password, role }
      return usersApi.create(body)
    },
    onSuccess: onSaved,
    onError: (err) => setError(err instanceof ApiError ? err.message : t('form.saveFailed')),
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
          <Label htmlFor="displayName">{t('setup.displayName')}</Label>
          <Input id="displayName" value={displayName} onChange={(e) => setDisplayName(e.target.value)} required />
        </div>
        {!isEdit && (
          <div>
            <Label htmlFor="username">{t('auth.username')}</Label>
            <Input id="username" value={username} onChange={(e) => setUsername(e.target.value)} required />
          </div>
        )}
        {!isEdit && (
          <div>
            <Label htmlFor="password">{t('auth.password')}</Label>
            <Input id="password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
          </div>
        )}
        <div>
          <Label htmlFor="role">{t('users.role')}</Label>
          <Select id="role" value={role} onChange={(e) => setRole(e.target.value as UserRole)}>
            <option value="Member">{t('role.Member')}</option>
            <option value="Admin">{t('role.Admin')}</option>
          </Select>
        </div>
        {error && <p className="text-sm text-destructive">{error}</p>}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" disabled={mutation.isPending}>
            {mutation.isPending ? t('common.saving') : t('common.save')}
          </Button>
        </div>
      </form>
    </Modal>
  )
}

function AdjustPointsModal({ user, onClose, onSaved }: { user: User; onClose: () => void; onSaved: () => void }) {
  const [delta, setDelta] = useState('')
  const [description, setDescription] = useState('')
  const [error, setError] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: () => usersApi.adjustPoints(user.id, { delta: Number(delta), description: description || undefined }),
    onSuccess: onSaved,
    onError: (err) => setError(err instanceof ApiError ? err.message : t('common.failed')),
  })

  return (
    <Modal title={t('users.adjustTitle', { name: user.displayName })} onClose={onClose}>
      <form
        onSubmit={(e) => {
          e.preventDefault()
          setError(null)
          mutation.mutate()
        }}
        className="space-y-4"
      >
        <div>
          <Label htmlFor="delta">{t('users.adjustLabel')}</Label>
          <Input
            id="delta"
            type="number"
            placeholder={t('users.adjustPlaceholder')}
            value={delta}
            onChange={(e) => setDelta(e.target.value)}
            required
          />
          <p className="mt-1 text-xs text-muted-foreground">
            {t('users.adjustHint', { count: user.points })}
          </p>
        </div>
        <div>
          <Label htmlFor="reason">{t('users.reason')}</Label>
          <Input
            id="reason"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder={t('users.reasonPlaceholder')}
          />
        </div>
        {error && <p className="text-sm text-destructive">{error}</p>}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="submit" disabled={mutation.isPending || !delta || Number(delta) === 0}>
            {mutation.isPending ? t('common.saving') : t('common.apply')}
          </Button>
        </div>
      </form>
    </Modal>
  )
}

function PasswordModal({ user, onClose }: { user: User; onClose: () => void }) {
  const [newPassword, setNewPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)

  const mutation = useMutation({
    mutationFn: () => usersApi.setPassword(user.id, newPassword),
    onSuccess: () => setDone(true),
    onError: (err) => setError(err instanceof ApiError ? err.message : t('common.failed')),
  })

  return (
    <Modal title={t('users.setPasswordTitle', { name: user.displayName })} onClose={onClose}>
      {done ? (
        <div className="space-y-4">
          <p className="text-sm text-success">{t('settings.passwordUpdated')}</p>
          <div className="flex justify-end">
            <Button onClick={onClose}>{t('common.close')}</Button>
          </div>
        </div>
      ) : (
        <form
          onSubmit={(e) => {
            e.preventDefault()
            setError(null)
            mutation.mutate()
          }}
          className="space-y-4"
        >
          <div>
            <Label htmlFor="newPassword">{t('settings.newPassword')}</Label>
            <Input id="newPassword" type="password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} required />
          </div>
          {error && <p className="text-sm text-destructive">{error}</p>}
          <div className="flex justify-end gap-2">
            <Button type="button" variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending ? t('common.saving') : t('users.setPassword')}
            </Button>
          </div>
        </form>
      )}
    </Modal>
  )
}
