export function cn(...classes: Array<string | false | null | undefined>): string {
  return classes.filter(Boolean).join(' ')
}

/** A small set of pleasant avatar colors offered when creating/editing users. */
export const AVATAR_COLORS = [
  '#6366f1', // indigo
  '#ef4444', // red
  '#f59e0b', // amber
  '#22c55e', // green
  '#06b6d4', // cyan
  '#ec4899', // pink
  '#8b5cf6', // violet
  '#64748b', // slate
  '#f97316', // orange
  '#14b8a6', // teal
  '#84cc16', // lime
  '#3b82f6', // blue
  '#a855f7', // purple
  '#e11d48', // rose
]
