import type { ReactNode } from 'react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/Card'

interface ModalProps {
  title: ReactNode
  onClose: () => void
  children: ReactNode
}

export function Modal({ title, onClose, children }: ModalProps) {
  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
      onClick={onClose}
    >
      <Card
        className="w-full max-w-md"
        onClick={(e) => e.stopPropagation()}
      >
        <CardHeader className="flex items-center justify-between">
          <CardTitle>{title}</CardTitle>
          <button
            onClick={onClose}
            className="text-muted-foreground hover:text-foreground"
            aria-label="Close"
          >
            ✕
          </button>
        </CardHeader>
        <CardContent>{children}</CardContent>
      </Card>
    </div>
  )
}

export function Avatar({ color, name, size = 36 }: { color: string; name: string; size?: number }) {
  const initials = name.trim().slice(0, 2).toUpperCase()
  return (
    <span
      className="inline-flex shrink-0 items-center justify-center rounded-full font-medium text-white"
      style={{ backgroundColor: color, width: size, height: size, fontSize: size * 0.4 }}
    >
      {initials}
    </span>
  )
}
