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
}

export function Modal({ title, onClose, children }: ModalProps) {
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
        className="flex max-h-[calc(100dvh-2rem)] w-full max-w-md flex-col"
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
}: {
  color: string
  name: string
  size?: number
  /** Equipped frame cosmetic key (gacha). When set, a decorative ring wraps the avatar. */
  frame?: string | null
}) {
  const initials = name.trim().slice(0, 2).toUpperCase()
  const inner = (
    <span
      className="gx-av inline-flex shrink-0 items-center justify-center rounded-full font-medium text-white"
      style={{ backgroundColor: color, width: size, height: size, fontSize: size * 0.4 }}
    >
      {initials}
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
