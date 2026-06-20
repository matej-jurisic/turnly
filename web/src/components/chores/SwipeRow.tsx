import { useRef, useState } from 'react'
import type { PointerEvent as ReactPointerEvent, ReactNode } from 'react'
import { CheckIcon, InfoIcon } from '@/components/chores/icons'

const THRESHOLD = 64 // px the card must travel before the action fires
const SLOP = 10 // px of movement before we decide the gesture is horizontal

function prefersReducedMotion(): boolean {
  return typeof window !== 'undefined'
    && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
}

/**
 * After a touch swipe the browser synthesizes a `click` at the lift position. If the swipe just
 * opened a modal, that click would land on the modal's backdrop and immediately close it — so we
 * swallow the next click (cleaning up on a timeout if none arrives).
 */
function suppressNextClick(): void {
  const swallow = (e: Event) => {
    e.stopPropagation()
    e.preventDefault()
    cleanup()
  }
  const cleanup = () => {
    window.removeEventListener('click', swallow, true)
    window.clearTimeout(timer)
  }
  const timer = window.setTimeout(cleanup, 500)
  window.addEventListener('click', swallow, true)
}

/**
 * Touch swipe wrapper for a chore card: drag right to fire `onSwipeRight` (mark complete),
 * drag left to fire `onSwipeLeft` (open details). Vertical drags are ignored so the page still
 * scrolls. Pointer/mouse users are unaffected — the underlying buttons/menu still work.
 */
export function SwipeRow({
  children,
  onSwipeRight,
  onSwipeLeft,
}: {
  children: ReactNode
  onSwipeRight?: () => void
  onSwipeLeft?: () => void
}) {
  const [dx, setDx] = useState(0)
  const [animate, setAnimate] = useState(false)
  const start = useRef<{ x: number; y: number } | null>(null)
  const axis = useRef<'undecided' | 'horizontal' | 'vertical'>('undecided')

  // Only touch input gets swipe behaviour; mouse keeps the normal click targets.
  const onPointerDown = (e: ReactPointerEvent) => {
    if (e.pointerType === 'mouse') return
    if (!onSwipeRight && !onSwipeLeft) return
    start.current = { x: e.clientX, y: e.clientY }
    axis.current = 'undecided'
    setAnimate(false)
  }

  const onPointerMove = (e: ReactPointerEvent) => {
    if (!start.current) return
    const moveX = e.clientX - start.current.x
    const moveY = e.clientY - start.current.y

    if (axis.current === 'undecided') {
      if (Math.abs(moveX) < SLOP && Math.abs(moveY) < SLOP) return
      axis.current = Math.abs(moveX) > Math.abs(moveY) ? 'horizontal' : 'vertical'
    }
    if (axis.current !== 'horizontal') return

    e.currentTarget.setPointerCapture?.(e.pointerId)
    // The card always follows the finger (so the gesture clearly starts in either direction);
    // whether an action actually fires is decided on release. Resist over-drag a little.
    setDx(Math.max(-140, Math.min(140, moveX)))
  }

  const finish = () => {
    let fired = false
    if (axis.current === 'horizontal') {
      if (dx >= THRESHOLD && onSwipeRight) { onSwipeRight(); fired = true }
      else if (dx <= -THRESHOLD && onSwipeLeft) { onSwipeLeft(); fired = true }
    }
    start.current = null
    axis.current = 'undecided'
    // Swallow the trailing synthetic click so it doesn't dismiss the modal the swipe just opened.
    if (fired) suppressNextClick()
    // Animate the snap-back only for a cancelled swipe. When an action fires we reset instantly,
    // so the card doesn't visibly slide behind the modal/overlay that's opening.
    setAnimate(!fired && !prefersReducedMotion())
    setDx(0)
  }

  const revealRight = dx > 0 // swiping right → complete (green reveal on the left)
  const active = Math.abs(dx) >= THRESHOLD
  // Is there an action for the direction currently being dragged?
  const armed = revealRight ? Boolean(onSwipeRight) : Boolean(onSwipeLeft)

  return (
    // Only clip while a swipe is in progress — otherwise overflow-hidden would cut off the
    // ChoreMenu dropdown, which overflows the card.
    <div className={'relative rounded-xl ' + (dx !== 0 ? 'overflow-hidden' : '')} style={{ touchAction: 'pan-y' }}>
      {dx !== 0 && (
        <div
          className={
            'pointer-events-none absolute inset-0 flex items-center rounded-xl ' +
            (!armed
              // Dragging a direction with no action (e.g. a chore that can't be completed):
              // a muted reveal makes clear the swipe registered but won't do anything.
              ? (revealRight ? 'justify-start ' : 'justify-end ') + 'bg-muted text-muted-foreground'
              : 'text-primary-foreground ' +
                (revealRight
                  ? 'justify-start bg-primary ' + (active ? 'opacity-100' : 'opacity-70')
                  : 'justify-end bg-info ' + (active ? 'opacity-100' : 'opacity-70')))
          }
        >
          <span className="px-5">{revealRight ? <CheckIcon /> : <InfoIcon />}</span>
        </div>
      )}
      <div
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={finish}
        onPointerCancel={finish}
        style={{
          // Only set a transform mid-swipe: a resting `translateX(0px)` is still a non-`none`
          // transform, which creates a stacking context per card and traps the ChoreMenu dropdown
          // below the next card. `undefined` at rest keeps the dropdown's z-index effective.
          transform: dx !== 0 ? `translateX(${dx}px)` : undefined,
          transition: animate ? 'transform 200ms ease-out' : undefined,
        }}
      >
        {children}
      </div>
    </div>
  )
}
