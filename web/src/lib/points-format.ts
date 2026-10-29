import { t, tServer } from '@/lib/i18n'
import type { TKey } from '@/lib/i18n'
import type { PointsLogEntry } from '@/lib/types'

// The points log stores the server's English description. These are the shapes the server writes;
// anything else (a chore name, an admin's own reason) is shown as stored.
const DESCRIPTION_PATTERNS: [RegExp, TKey][] = [
  [/^Redeemed: (.*)$/, 'pointsDesc.redeemed'],
  [/^Gacha pull$/, 'pointsDesc.pull'],
  [/^Gacha pull x10$/, 'pointsDesc.pull10'],
  [/^Bounty offered on (.*)$/, 'pointsDesc.bountyOffered'],
  [/^Bounty claimed on (.*)$/, 'pointsDesc.bountyClaimed'],
  [/^Bounty refunded on (.*)$/, 'pointsDesc.bountyRefunded'],
]

function localizeDescription(description: string): string {
  for (const [pattern, key] of DESCRIPTION_PATTERNS) {
    const match = pattern.exec(description)
    if (match) return t(key, { name: match[1] ?? '' })
  }
  return description
}

export function pointsLogLabel(entry: PointsLogEntry): string {
  return entry.description ? localizeDescription(entry.description) : tServer(`pointsType.${entry.type}`, entry.type)
}
