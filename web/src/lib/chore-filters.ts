import type { RepeatType } from '@/lib/types'

export type DueStatus = 'overdue' | 'today' | 'upcoming' | 'later'

export interface ChoreFilterState {
  tags: string[]
  assignees: string[]
  /** When set, the assignee filter also matches chores where the member is the *next* assignee. */
  includeNext: boolean
  due: DueStatus[]
  repeat: RepeatType[]
}

export const emptyFilters: ChoreFilterState = {
  tags: [], assignees: [], includeNext: false, due: [], repeat: [],
}

/** The "Mine" quick view: chores the member currently holds. Also the page's default on open. */
export function mineFilters(userId: string): ChoreFilterState {
  return { ...emptyFilters, assignees: [userId] }
}

/** Number of active filter values (the "includeNext" modifier isn't counted on its own). */
export function filterCount(f: ChoreFilterState): number {
  return f.tags.length + f.assignees.length + f.due.length + f.repeat.length
}
