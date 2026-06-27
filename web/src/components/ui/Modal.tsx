import { useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import type { ReactNode } from 'react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/Card'
import { registerModal } from '@/lib/modalHistory'
import { frameClasses } from '@/lib/cosmetics'

interface ModalProps {
  title: ReactNode
  onClose: () => void
  children: ReactNode
  /** Tailwind max-width class for the dialog. Defaults to `max-w-md`; widen for denser content. */
  widthClassName?: string
}

export function Modal({ title, onClose, children, widthClassName = 'max-w-md' }: ModalProps) {
  // Lock background scroll while open (mirrors the drawer's scroll-lock in Layout).
  useEffect(() => {
    const prev = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      document.body.style.overflow = prev
    }
  }, [])

  // Make the device/browser Back button close the modal instead of navigating away. The shared
  // history manager owns the entry bookkeeping (Back, UI close, programmatic close, StrictMode,
  // rapid open/close) — see lib/modalHistory.ts.
  const onCloseRef = useRef(onClose)
  onCloseRef.current = onClose
  useEffect(() => registerModal(() => onCloseRef.current()), [])

  // Portal to <body> so the fixed backdrop isn't subject to an ancestor's `space-y-*` margin
  // (which would offset/shrink the `inset-0` overlay and leave an undimmed strip), transform, or
  // overflow clipping.
  return createPortal(
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
      onClick={onClose}
    >
      <Card
        className={`flex max-h-[calc(100dvh-2rem)] w-full flex-col ${widthClassName}`}
        onClick={(e) => e.stopPropagation()}
      >
        <CardHeader className="flex shrink-0 items-center justify-between">
          <CardTitle>{title}</CardTitle>
          <button
            onClick={onClose}
            className="text-muted-foreground hover:text-foreground"
            aria-label="Close"
          >
            ✕
          </button>
        </CardHeader>
        <CardContent className="overflow-y-auto">{children}</CardContent>
      </Card>
    </div>,
    document.body,
  )
}

export function Avatar({
  color,
  name,
  size = 36,
  frame,
  emoji,
}: {
  color: string
  name: string
  size?: number
  /** Equipped frame cosmetic key (gacha). When set, a decorative ring wraps the avatar. */
  frame?: string | null
  /** Equipped avatar emoji (gacha). When set, replaces the initials. */
  emoji?: string | null
}) {
  const glyph = emoji || name.trim().slice(0, 2).toUpperCase()
  // An emoji glyph is ~1em wide, so its centering offset is (size - fontSize)/2. To keep that offset
  // on a whole pixel (a half-pixel snaps the glyph to one side at dpr 1, reading as off-center), the
  // font size must share the avatar size's parity. Round to 0.5x (even for every avatar size we use,
  // and small enough that the glyph isn't clipped top/bottom), then nudge to match parity for safety.
  const emojiFontSize = (() => {
    const fs = Math.round(size * 0.5)
    return (size - fs) % 2 === 0 ? fs : fs + 1
  })()
  const inner = (
    <span
      className="gx-av inline-flex shrink-0 items-center justify-center overflow-hidden rounded-full font-medium text-white"
      style={{ backgroundColor: color, width: size, height: size, fontSize: emoji ? emojiFontSize : size * 0.4 }}
    >
      {emoji ? <span className="block w-full text-center leading-none">{glyph}</span> : glyph}
    </span>
  )

  if (!frame) return inner

  const pad = Math.max(2, Math.round(size * 0.09))
  return (
    <span className={frameClasses(frame)} style={{ padding: pad }}>
      {inner}
    </span>
  )
}
