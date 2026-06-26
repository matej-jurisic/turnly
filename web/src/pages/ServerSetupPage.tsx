import { useState } from 'react'
import type { FormEvent } from 'react'
import { Button } from '@/components/ui/Button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/Card'
import { Input, Label } from '@/components/ui/Field'
import { ThemeToggle } from '@/components/ThemeToggle'
import { normalizeOrigin, setServerOrigin } from '@/lib/server-config'

/**
 * First-run screen for the native app: the user enters the address of their self-hosted Turnly
 * server. We probe it before saving so a typo or an unreachable host fails here rather than on the
 * login screen. On success `onConnected` re-runs the app bootstrap against the chosen server.
 */
export function ServerSetupPage({ onConnected }: { onConnected: () => void }) {
  const [address, setAddress] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)

    let origin: string
    try {
      origin = normalizeOrigin(address)
    } catch {
      setError('Enter a valid server address, for example https://turnly.myhome.net')
      return
    }

    setSubmitting(true)
    try {
      // setup/status is unauthenticated and cheap; a 200 confirms it's a reachable Turnly server.
      const res = await fetch(`${origin}/api/setup/status`, { method: 'GET' })
      if (!res.ok) throw new Error('unexpected status')
      await res.json()
      await setServerOrigin(origin)
      onConnected()
    } catch {
      setError(
        'Could not reach a Turnly server at that address. Check the URL, and that the server allows this app (Cors:Origins).',
      )
      setSubmitting(false)
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center p-4">
      <div className="fixed right-4 top-4">
        <ThemeToggle />
      </div>
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle>Connect to your server</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={onSubmit} className="space-y-4">
            <div>
              <Label htmlFor="server">Server address</Label>
              <Input
                id="server"
                value={address}
                onChange={(e) => setAddress(e.target.value)}
                placeholder="https://turnly.myhome.net"
                autoCapitalize="none"
                autoCorrect="off"
                inputMode="url"
                required
              />
              <p className="mt-2 text-sm text-muted-foreground">
                Enter the address of your self-hosted Turnly instance.
              </p>
            </div>
            {error && <p className="text-sm text-destructive">{error}</p>}
            <Button type="submit" className="w-full" disabled={submitting}>
              {submitting ? 'Connecting…' : 'Connect'}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
