import { useEffect, useState } from 'react'
import { Input } from '@/components/ui/Field'

interface TimeFieldProps {
  /** "HH:mm" (24-hour) or "" for no specific time. */
  value: string
  onChange: (value: string) => void
  id?: string
}

const pad = (n: number) => String(n).padStart(2, '0')

/**
 * Parses loose user input into a normalized 24-hour "HH:mm" string. Accepts "9", "9:3",
 * "930", "0930", "9:30", etc. Returns "" for empty input, or null when it can't be read
 * as a valid time (so the caller can revert).
 */
function normalize(raw: string): string | null {
  const s = raw.trim()
  if (!s) return ''

  let hh: number
  let mm: number
  if (s.includes(':')) {
    const [a, b] = s.split(':')
    hh = parseInt(a, 10)
    mm = b ? parseInt(b, 10) : 0
  } else {
    const d = s.replace(/\D/g, '')
    if (!d) return null
    if (d.length <= 2) {
      hh = parseInt(d, 10)
      mm = 0
    } else {
      hh = parseInt(d.slice(0, -2), 10)
      mm = parseInt(d.slice(-2), 10)
    }
  }

  if (!Number.isFinite(hh) || !Number.isFinite(mm) || hh > 23 || mm > 59) return null
  return `${pad(hh)}:${pad(mm)}`
}

/**
 * Always-24-hour time input. The native `<input type="time">` follows the browser/OS locale
 * and shows AM/PM for some users, so this is a plain text field that accepts typed times and
 * normalizes them to "HH:mm" on blur. Empty means "no specific time" (due at end of day).
 */
export function TimeField({ value, onChange, id }: TimeFieldProps) {
  const [draft, setDraft] = useState(value)

  // Re-sync when the value changes from outside (e.g. switching between chores).
  useEffect(() => setDraft(value), [value])

  const commit = () => {
    const next = normalize(draft)
    if (next === null) {
      setDraft(value) // unparseable — revert to the last good value
    } else {
      setDraft(next)
      if (next !== value) onChange(next)
    }
  }

  return (
    <Input
      id={id}
      type="text"
      inputMode="numeric"
      placeholder="HH:mm"
      maxLength={5}
      value={draft}
      aria-label="Due time (24-hour)"
      onChange={(e) => {
        // Mask digit-only input into "HH:mm" so the numeric mobile keyboard (no colon key)
        // still produces a colon. Backspacing the colon just drops back to "HH".
        const digits = e.target.value.replace(/\D/g, '').slice(0, 4)
        setDraft(digits.length > 2 ? `${digits.slice(0, 2)}:${digits.slice(2)}` : digits)
      }}
      onBlur={commit}
      onKeyDown={(e) => {
        if (e.key === 'Enter') {
          e.preventDefault()
          commit()
        }
      }}
    />
  )
}
