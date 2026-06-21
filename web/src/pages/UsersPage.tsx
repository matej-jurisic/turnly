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
import { ColorPicker } from '@/components/ui/ColorPicker'
import { AVATAR_COLORS } from '@/lib/utils'

export function UsersPage() {
  const currentUser = useAuthStore((s) => s.user)
  const queryClient = useQueryClient()
  const { data: users, isLoading, error } = useQuery({ queryKey: ['users'], queryFn: usersApi.list })

  const [editing, setEditing] = useState<User | null>(null)
  const [creating, setCreating] = useState(false)
  const [passwordFor, setPasswordFor] = useState<User | null>(null)
  const [adjustPointsFor, setAdjustPointsFor] = useState<User | null>(null)

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['users'] })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => usersApi.remove(id),
    onSuccess: invalidate,
    onError: (err) => toast.error(err instanceof ApiError ? err.message : 'Delete failed'),
  })

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold text-foreground">Users</h1>
        <Button onClick={() => setCreating(true)}>Add user</Button>
      </div>

      {isLoading && <p className="text-muted-foreground">Loading…</p>}
      {error && <p className="text-destructive">{(error as ApiError).message}</p>}

      <Card className="divide-y divide-border">
        {users?.map((user) => (
          <div key={user.id} className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:gap-4">
            <div className="flex min-w-0 flex-1 items-center gap-3">
              <Avatar color={user.avatarColor} name={user.displayName} />
              <div className="min-w-0">
                <p className="truncate text-foreground">
                  {user.displayName}
                  {user.id === currentUser?.id && <span className="ml-2 text-xs text-muted-foreground">(you)</span>}
                </p>
                <div className="mt-0.5 flex items-center gap-2">
                  <span className="truncate text-sm text-muted-foreground">@{user.username}</span>
                  <Badge tone={user.role === 'Admin' ? 'violet' : 'neutral'}>{user.role}</Badge>
                </div>
              </div>
            </div>
            <div className="flex gap-1 sm:shrink-0">
              <Button size="sm" variant="ghost" onClick={() => setEditing(user)}>Edit</Button>
              <Button size="sm" variant="ghost" onClick={() => setPasswordFor(user)}>Password</Button>
              <Button size="sm" variant="ghost" onClick={() => setAdjustPointsFor(user)}>Points</Button>
              <Button
                size="sm"
                variant="ghost"
                className="text-destructive hover:bg-destructive/10"
                disabled={user.id === currentUser?.id || deleteMutation.isPending}
                onClick={async () => {
                  if (
                    await confirm({
                      title: 'Delete user',
                      message: `Delete ${user.displayName}? This wipes their history.`,
                      confirmLabel: 'Delete',
                    })
                  ) {
                    deleteMutation.mutate(user.id)
                  }
                }}
              >
                Delete
              </Button>
            </div>
          </div>
        ))}
      </Card>

      {creating && (
        <UserFormModal
          title="Add user"
          onClose={() => setCreating(false)}
          onSaved={() => {
            setCreating(false)
            invalidate()
          }}
        />
      )}

      {editing && (
        <UserFormModal
          title="Edit user"
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
  const [avatarColor, setAvatarColor] = useState(user?.avatarColor ?? AVATAR_COLORS[0])
  const [error, setError] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: () => {
      if (isEdit && user) {
        const body: UpdateUserRequest = { displayName, avatarColor, role }
        return usersApi.update(user.id, body)
      }
      const body: CreateUserRequest = { username, displayName, password, role, avatarColor }
      return usersApi.create(body)
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
          <Label htmlFor="displayName">Display name</Label>
          <Input id="displayName" value={displayName} onChange={(e) => setDisplayName(e.target.value)} required />
        </div>
        {!isEdit && (
          <div>
            <Label htmlFor="username">Username</Label>
            <Input id="username" value={username} onChange={(e) => setUsername(e.target.value)} required />
          </div>
        )}
        {!isEdit && (
          <div>
            <Label htmlFor="password">Password</Label>
            <Input id="password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
          </div>
        )}
        <div>
          <Label htmlFor="role">Role</Label>
          <Select id="role" value={role} onChange={(e) => setRole(e.target.value as UserRole)}>
            <option value="Member">Member</option>
            <option value="Admin">Admin</option>
          </Select>
        </div>
        <div>
          <Label>Avatar color</Label>
          <ColorPicker value={avatarColor} onChange={setAvatarColor} />
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

function AdjustPointsModal({ user, onClose, onSaved }: { user: User; onClose: () => void; onSaved: () => void }) {
  const [delta, setDelta] = useState('')
  const [description, setDescription] = useState('')
  const [error, setError] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: () => usersApi.adjustPoints(user.id, { delta: Number(delta), description: description || undefined }),
    onSuccess: onSaved,
    onError: (err) => setError(err instanceof ApiError ? err.message : 'Failed'),
  })

  return (
    <Modal title={`Adjust points for ${user.displayName}`} onClose={onClose}>
      <form
        onSubmit={(e) => {
          e.preventDefault()
          setError(null)
          mutation.mutate()
        }}
        className="space-y-4"
      >
        <div>
          <Label htmlFor="delta">Points adjustment</Label>
          <Input
            id="delta"
            type="number"
            placeholder="e.g. 10 or -5"
            value={delta}
            onChange={(e) => setDelta(e.target.value)}
            required
          />
          <p className="mt-1 text-xs text-muted-foreground">
            Current balance: {user.points} pts. Use a negative number to deduct.
          </p>
        </div>
        <div>
          <Label htmlFor="reason">Reason (optional)</Label>
          <Input
            id="reason"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="e.g. Bonus for helping out"
          />
        </div>
        {error && <p className="text-sm text-destructive">{error}</p>}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>Cancel</Button>
          <Button type="submit" disabled={mutation.isPending || !delta || Number(delta) === 0}>
            {mutation.isPending ? 'Saving…' : 'Apply'}
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
    onError: (err) => setError(err instanceof ApiError ? err.message : 'Failed'),
  })

  return (
    <Modal title={`Set password for ${user.displayName}`} onClose={onClose}>
      {done ? (
        <div className="space-y-4">
          <p className="text-sm text-success">Password updated.</p>
          <div className="flex justify-end">
            <Button onClick={onClose}>Close</Button>
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
            <Label htmlFor="newPassword">New password</Label>
            <Input id="newPassword" type="password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} required />
          </div>
          {error && <p className="text-sm text-destructive">{error}</p>}
          <div className="flex justify-end gap-2">
            <Button type="button" variant="secondary" onClick={onClose}>Cancel</Button>
            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending ? 'Saving…' : 'Set password'}
            </Button>
          </div>
        </form>
      )}
    </Modal>
  )
}
