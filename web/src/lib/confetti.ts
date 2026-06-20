import confetti from 'canvas-confetti'

/**
 * Fires a short confetti burst to celebrate a chore completion. No-ops when the user prefers
 * reduced motion, so the delight never fights accessibility settings.
 */
export function celebrate(): void {
  if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return

  const colors = ['#5b4ee8', '#7b6ef6', '#16a34a', '#f59e0b']
  confetti({
    particleCount: 90,
    spread: 70,
    startVelocity: 38,
    origin: { y: 0.7 },
    colors,
    disableForReducedMotion: true,
  })
}
