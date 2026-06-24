import { useEffect } from 'react'
import { useAchievementCelebrationStore } from '@/lib/achievementCelebration'
import { celebrate } from '@/lib/confetti'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'

/**
 * Shows a celebratory popup when the user unlocks an achievement. Achievements arrive on the
 * completion/redemption response and are enqueued via `celebrateAchievements`; they're shown one at a
 * time (a single action can cross several thresholds at once). Mounted once at the app root.
 */
export function AchievementCelebration() {
  const current = useAchievementCelebrationStore((s) => s.queue[0])
  const remaining = useAchievementCelebrationStore((s) => s.queue.length)
  const dismiss = useAchievementCelebrationStore((s) => s.dismiss)

  // A fresh burst each time a new badge surfaces (reduced-motion aware via celebrate()).
  useEffect(() => {
    if (current) celebrate()
  }, [current])

  if (!current) return null

  return (
    <Modal title="Achievement unlocked!" onClose={dismiss}>
      <div className="flex flex-col items-center gap-4 py-2 text-center">
        <span className="text-6xl leading-none">{current.emoji}</span>
        <div className="space-y-1">
          <p className="text-lg font-semibold text-foreground">{current.name}</p>
          <p className="text-sm text-muted-foreground">{current.description}</p>
        </div>
        <Button onClick={dismiss} className="mt-1">
          {remaining > 1 ? `Next (${remaining - 1} more)` : 'Nice!'}
        </Button>
      </div>
    </Modal>
  )
}
