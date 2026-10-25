import type { ReactNode } from 'react'
import type { Chore } from '@/lib/types'
import { Badge } from '@/components/ui/Badge'
import { Card } from '@/components/ui/Card'
import { Avatar } from '@/components/ui/Modal'
import { EditIcon } from '@/components/chores/icons'
import {
  formatDate, formatDueTime, formatGrace, isAnyone, isIndependent, notificationRecipientsLabel,
  notificationTimingLabel, notificationTypeLabel, pairLabel, repeatLabel,
  rotationPeriodLabel, schedulingLabel, strategyLabel,
} from '@/lib/chore-format'
import { t } from '@/lib/i18n'

/** Admin-only "config" view: every chore's settings side by side in one table, so they can be
 * compared and audited without opening each chore. Shows configuration, not live state (no due
 * buckets); a row's edit button opens the usual form. */
export function ChoreConfigTable({ chores, onEdit }: { chores: Chore[]; onEdit: (chore: Chore) => void }) {
  const sorted = [...chores].sort((a, b) => a.name.localeCompare(b.name))

  return (
    <>
      {/* Below xl the table can't fit, so each chore becomes a card with the columns stacked. */}
      <ul className="space-y-3 xl:hidden">
        {sorted.map((chore) => (
          <li key={chore.id}>
            <Card className="p-4 text-sm">
              <div className="flex items-start gap-2">
                <div className="min-w-0 flex-1">
                  <NameCell chore={chore} />
                </div>
                <Badge tone="violet" className="shrink-0 whitespace-nowrap">{t('common.pts', { count: chore.points })}</Badge>
                <div className="-my-1.5 -mr-1.5 shrink-0">
                  <EditButton chore={chore} onEdit={onEdit} />
                </div>
              </div>
              <dl className="mt-3 grid gap-x-4 gap-y-3 border-t border-border pt-3 sm:grid-cols-2">
                <Section label={t('config.schedule')}><ScheduleCell chore={chore} /></Section>
                <Section label={t('details.assignment')}><AssignmentCell chore={chore} /></Section>
                <Section label={t('config.completion')}><CompletionCell chore={chore} /></Section>
                <Section label={t('details.reminders')}><RemindersCell chore={chore} /></Section>
              </dl>
            </Card>
          </li>
        ))}
      </ul>

      <Card className="hidden overflow-x-auto p-0 xl:block">
        <table className="w-full min-w-[60rem] border-collapse text-left text-sm">
          <thead>
            <tr className="border-b border-border text-xs text-muted-foreground">
              <Th>{t('config.chore')}</Th>
              <Th>{t('nav.points')}</Th>
              <Th>{t('config.schedule')}</Th>
              <Th>{t('details.assignment')}</Th>
              <Th>{t('config.completion')}</Th>
              <Th>{t('details.reminders')}</Th>
              <Th><span className="sr-only">{t('common.edit')}</span></Th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {sorted.map((chore) => (
              <tr key={chore.id} className="align-top">
                <Td><NameCell chore={chore} /></Td>
                <Td className="whitespace-nowrap">
                  <Badge tone="violet">{t('common.pts', { count: chore.points })}</Badge>
                </Td>
                <Td><ScheduleCell chore={chore} /></Td>
                <Td><AssignmentCell chore={chore} /></Td>
                <Td><CompletionCell chore={chore} /></Td>
                <Td><RemindersCell chore={chore} /></Td>
                <Td><EditButton chore={chore} onEdit={onEdit} /></Td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  )
}

function NameCell({ chore }: { chore: Chore }) {
  return (
    <>
      <div className="flex items-center gap-2">
        {chore.emoji && <span className="shrink-0 text-base leading-none">{chore.emoji}</span>}
        <span className="min-w-0 break-words text-foreground">{chore.name}</span>
      </div>
      {(chore.isFrozen || chore.tags.length > 0) && (
        <div className="mt-1.5 flex flex-wrap gap-1">
          {chore.isFrozen && <Badge tone="amber">{t('chores.paused')}</Badge>}
          {chore.tags.map((tag) => <Badge key={tag} tone="neutral">{tag}</Badge>)}
        </div>
      )}
    </>
  )
}

function RemindersCell({ chore }: { chore: Chore }) {
  if (chore.notifications.length === 0) return <Sub>{t('config.none')}</Sub>
  return (
    <div className="space-y-1">
      {chore.notifications.map((n) => (
        <div key={n.id} className="flex flex-wrap items-center gap-1.5">
          <Badge tone="blue">{notificationTypeLabel(n)}</Badge>
          <span className="text-foreground">{notificationTimingLabel(n)}</span>
          <span className="text-xs text-muted-foreground">{notificationRecipientsLabel(n)}</span>
        </div>
      ))}
    </div>
  )
}

function EditButton({ chore, onEdit }: { chore: Chore; onEdit: (chore: Chore) => void }) {
  return (
    <button
      type="button"
      onClick={() => onEdit(chore)}
      aria-label={t('config.editName', { name: chore.name })}
      title={t('common.edit')}
      className="inline-flex h-8 w-8 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-accent hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
    >
      <EditIcon />
    </button>
  )
}

function ScheduleCell({ chore }: { chore: Chore }) {
  const times = chore.timesOfDay ?? []
  return (
    <>
      <div className="text-foreground">{repeatLabel(chore)}</div>
      {chore.repeatType === 'OnDemand' ? null : times.length > 1 ? (
        // repeatLabel collapses 3+ slots to a count, so spell every slot out here.
        <Sub>
          {times
            .map((time) => (chore.timeLabels?.[time] ? `${formatDueTime(time)} ${chore.timeLabels[time]}` : formatDueTime(time)))
            .join(', ')}
        </Sub>
      ) : (
        <Sub>{chore.dueTime ? t('config.at', { time: formatDueTime(chore.dueTime) }) : t('config.noTime')}</Sub>
      )}
      {chore.repeatType !== 'OnDemand' && <Sub>{t('config.starts', { date: formatDate(chore.startDate) })}</Sub>}
    </>
  )
}

function AssignmentCell({ chore }: { chore: Chore }) {
  const byId = new Map(chore.assignees.map((u) => [u.id, u]))
  const pairs = chore.pairs
    .map(([a, b]) => [byId.get(a), byId.get(b)] as const)
    .filter((p): p is readonly [NonNullable<typeof p[0]>, NonNullable<typeof p[1]>] => Boolean(p[0] && p[1]))

  return (
    <>
      <div className="text-foreground">{strategyLabel(chore.assignmentStrategy)}</div>
      {chore.rotationPeriod !== 'EveryOccurrence' && (
        <Sub>{t('details.handsOver', { period: rotationPeriodLabel(chore.rotationPeriod).toLowerCase() })}</Sub>
      )}
      {chore.rotateOnEachCompletion && <Sub>{t('details.rotatesEach')}</Sub>}
      <div className="mt-1.5 flex flex-wrap gap-x-2.5 gap-y-1">
        {chore.assignees.map((u) => (
          <span
            key={u.id}
            className={'inline-flex items-center gap-1 text-xs ' + (u.isFrozen ? 'text-muted-foreground line-through' : 'text-foreground')}
            title={u.isFrozen ? t('chores.paused') : undefined}
          >
            <Avatar color={u.avatarColor} name={u.displayName} size={16} frame={u.equippedFrameKey} emoji={u.avatarEmoji} />
            {u.displayName}
          </span>
        ))}
      </div>
      {pairs.length > 0 && <Sub>{t('config.pairs', { pairs: pairs.map(([a, b]) => pairLabel(a, b)).join(', ') })}</Sub>}
      {!isIndependent(chore) && (
        <Sub>
          {t('config.current', {
            name: isAnyone(chore)
              ? t('chore.anyone')
              : chore.currentAssignee ? pairLabel(chore.currentAssignee, chore.currentPartner) : t('chore.unassigned'),
          })}
        </Sub>
      )}
    </>
  )
}

function CompletionCell({ chore }: { chore: Chore }) {
  const recurring = chore.repeatType !== 'OneTime' && chore.repeatType !== 'OnDemand'
  return (
    <>
      <div className="text-foreground">
        {chore.completionsRequired > 1 ? t('config.perOccurrence', { count: chore.completionsRequired }) : t('config.oncePerOccurrence')}
      </div>
      {recurring && (
        <>
          <Sub>
            {chore.schedulingPreference === 'SmartScheduling' && chore.graceMinutes
              ? t('config.nextDueGrace', {
                  value: schedulingLabel(chore.schedulingPreference).toLowerCase(),
                  grace: formatGrace(chore.graceMinutes),
                })
              : t('config.nextDue', { value: schedulingLabel(chore.schedulingPreference).toLowerCase() })}
          </Sub>
          <Sub>
            {!chore.autoAdvanceIncomplete
              ? t('config.autoAdvanceOff')
              : chore.completionWindowMinutes
                ? t('config.autoAdvanceOnWindow', { window: formatGrace(chore.completionWindowMinutes) })
                : t('config.autoAdvanceOn')}
          </Sub>
        </>
      )}
    </>
  )
}

function Th({ children }: { children?: ReactNode }) {
  return <th className="px-4 py-2.5 font-medium">{children}</th>
}

function Td({ className, children }: { className?: string; children?: ReactNode }) {
  return <td className={'px-4 py-3 ' + (className ?? '')}>{children}</td>
}

function Section({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="mb-1 text-xs font-medium text-muted-foreground">{label}</dt>
      <dd>{children}</dd>
    </div>
  )
}

function Sub({ children }: { children: ReactNode }) {
  return <div className="text-xs text-muted-foreground">{children}</div>
}
