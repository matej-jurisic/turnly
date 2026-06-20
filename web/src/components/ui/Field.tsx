import type * as React from 'react'
import { useEffect, useState } from 'react'
import { cn } from '@/lib/utils'

export function Label({ className, ...props }: React.LabelHTMLAttributes<HTMLLabelElement>) {
  return (
    <label className={cn('mb-1 block text-sm text-foreground', className)} {...props} />
  )
}

const fieldClasses =
  'w-full rounded-md border border-input bg-card px-3 py-2 text-sm text-foreground placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50'

export function Input({ className, ...props }: React.InputHTMLAttributes<HTMLInputElement>) {
  return <input className={cn(fieldClasses, className)} {...props} />
}

export function Select({ className, ...props }: React.SelectHTMLAttributes<HTMLSelectElement>) {
  return <select className={cn(fieldClasses, className)} {...props} />
}

interface IntegerInputProps
  extends Omit<React.InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'type'> {
  value: number
  onCommit: (n: number) => void
  /** Smallest value accepted; also the fallback when the field is left empty/invalid. Default 1. */
  min?: number
}

/**
 * Number input that lets the field go empty while typing (so you can clear "1" and type "5")
 * and only commits a valid integer ≥ `min`, falling back to `min` on blur if left empty/invalid.
 */
export function IntegerInput({ value, onCommit, min = 1, ...props }: IntegerInputProps) {
  const [draft, setDraft] = useState(String(value))

  // Keep the field in sync when the value changes from outside (e.g. switching entries).
  useEffect(() => setDraft(String(value)), [value])

  return (
    <Input
      type="number"
      min={min}
      value={draft}
      {...props}
      onChange={(e) => {
        setDraft(e.target.value)
        const n = Number(e.target.value)
        if (e.target.value !== '' && Number.isFinite(n) && n >= min) onCommit(Math.floor(n))
      }}
      onBlur={() => {
        const n = Number(draft)
        const next = draft !== '' && Number.isFinite(n) && n >= min ? Math.floor(n) : min
        setDraft(String(next))
        onCommit(next)
      }}
    />
  )
}
