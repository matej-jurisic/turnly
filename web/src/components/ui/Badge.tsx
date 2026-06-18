import type * as React from 'react'
import { cn } from '@/lib/utils'

export type BadgeTone = 'neutral' | 'violet' | 'red' | 'blue' | 'amber' | 'green'

const tones: Record<BadgeTone, string> = {
  neutral: 'badge-neutral',
  violet: 'badge-violet',
  red: 'badge-red',
  blue: 'badge-blue',
  amber: 'badge-amber',
  green: 'badge-green',
}

export function Badge({
  tone = 'neutral',
  className,
  ...props
}: React.HTMLAttributes<HTMLSpanElement> & { tone?: BadgeTone }) {
  return <span className={cn('badge', tones[tone], className)} {...props} />
}
