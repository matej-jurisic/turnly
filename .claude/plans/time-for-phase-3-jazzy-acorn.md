# Phase 3 — Chores (Advanced)

## Context

Phases 1–2 shipped chore CRUD with **basic** recurrence (OneTime/Daily/Weekly/Monthly/Yearly +
start date), mark-complete/undo, and a points log. The current model has gaps that Phase 3
fills, per `specs.md` §Chores:

1. **Custom recurrence** — four modes the basic types can't express: *Interval* (every X
   days/weeks/…), *Days-of-week* (specific weekdays), *Days-of-month* (selected days within
   selected months), *Frequency* (X times per period).
2. **Assignment strategies** — today `CurrentAssigneeId` never changes on completion. The spec
   wants six rotation strategies picked on each new occurrence.
3. **Scheduling preference on completion** — today the next due date is hard-coded to
   "from scheduled date". The spec wants three options.

Outcome: an admin can define rich recurring chores that rotate through assignees automatically
and reschedule according to a chosen rule, with the frontend form and chore cards reflecting all
of it.

**Decisions made with the user:**
- **Frequency mode** → *count-within-period*: the chore stays due for the whole period showing
  progress (e.g. "2/3 this week"); once the count is met it advances to the next period.
  Rollover is computed lazily at completion time (no background scheduler exists until Phase 5).
- **Time granularity** → *day-level only* for now. Interval units are Day/Week/Month/Year
  (no "hours"); start-date inputs stay date-only. Hourly + datetime is deferred to Phase 5.

The pre-Phase-3 UX fixes noted in `specs.md` ("Next in line") are already done (assignee pills,
checkmark + dropdown chore actions, tag management in settings, points tab) — no action needed.

---

## Backend — `src/Turnly.Core`

### New enums (`Enums/`, all stored as strings via `HasConversion<string>`)
- Add `Custom` to **`RepeatType`** (existing file).
- **`CustomRecurrenceMode`**: `Interval, DaysOfWeek, DaysOfMonth, Frequency`.
- **`RecurrenceUnit`**: `Day, Week, Month, Year` (no `Hour` per the day-granularity decision).
- **`FrequencyPeriod`**: `Day, Week, Month, Year`.
- **`AssignmentStrategy`**: `Random, LeastAssigned, LeastCompleted, KeepLastAssigned,
  RandomExceptLastAssigned, RoundRobin`.
- **`SchedulingPreference`**: `FromScheduledDate, FromCompletionDate, ToFirstNextRepeat`.

### `Entities/Chore.cs` — new fields
- `CustomRecurrenceMode? CustomMode`
- `int? IntervalCount`, `RecurrenceUnit? IntervalUnit`
- reuse existing `List<DayOfWeek> Weekdays` for `Custom/DaysOfWeek` (basic `Weekly` stays a plain
  +7-day step and stores no weekdays — keeps Phase 2 behavior intact)
- `List<int> DaysOfMonth = []` (1–31), `List<int> Months = []` (1–12)
- `int? FrequencyCount`, `FrequencyPeriod? FrequencyPeriod`
- `AssignmentStrategy AssignmentStrategy = AssignmentStrategy.KeepLastAssigned` (default preserves
  current "assignee never changes" behavior for existing rows)
- `SchedulingPreference SchedulingPreference = SchedulingPreference.FromScheduledDate` (matches
  current behavior)

### `Entities/ChoreCompletion.cs` — undo support for rotation
- Add `Guid? PreviousAssigneeId` — snapshot of the assignee *before* this completion rotated it,
  so undo can restore it. (Plain column, no navigation/FK — it's a snapshot, mirrors
  `OccurrenceDueAt`.)

### New entity `Entities/ChoreAssignment.cs` — assignment history
Needed because `LeastAssigned` counts *assignments* (distinct from completions, which anyone can
log). Fields: `Id, ChoreId, Chore?, UserId, User?, AssignedAt, Guid? ChoreCompletionId`. One row
is written on chore create (initial assignee) and on each rotation (linked to the completion that
caused it, so undo can delete it). Also reusable for Phase 6 history.

### `Recurrence/RecurrenceCalculator.cs` — rewrite (still pure, unit-tested)
Introduce a `RecurrenceRule` record (built from a chore, decoupled from EF) carrying the repeat
type + custom params. Public surface:

- `DateTimeOffset FirstOccurrence(RecurrenceRule rule, DateTimeOffset start)` — first due date
  ≥ `start`. For interval/basic types = `start`; for fixed-slot modes (DaysOfWeek,
  DaysOfMonth+Months) = first valid slot on/after `start`; for Frequency = end of the period
  containing `start`.
- `DateTimeOffset? NextDue(RecurrenceRule rule, SchedulingPreference pref, DateTimeOffset
  scheduledDue, DateTimeOffset completedAt, DateTimeOffset now)`:
  - `OneTime` → `null`.
  - **Interval-type** (Daily/Weekly/Monthly/Yearly/Custom-Interval) via an `AddInterval(date)`
    step (Monthly clamps month-end like today's `AddMonths`): `FromScheduledDate` → step from
    `scheduledDue`; `FromCompletionDate` → step from `completedAt`; `ToFirstNextRepeat` → step
    from `scheduledDue` repeatedly until `> now` (iteration cap to guard runaway).
  - **Fixed-slot** (DaysOfWeek; DaysOfMonth+Months): next valid slot strictly after the base
    (`scheduledDue` / `completedAt` / `now` respectively), preserving the start time-of-day.
    Search forward with a cap (e.g. ≤ 24 months) so impossible combos (day 31 in short months)
    skip cleanly.
  - **Frequency** is NOT handled here — it depends on completion counts and lives in
    `ChoreService` (see below).
- Period helpers (`PeriodStart/PeriodEnd` for `FrequencyPeriod`) **anchored to `StartDate`**
  (week = rolling 7-day windows from start; month/year calendar-aligned from start's day) so
  results are deterministic and locale-independent.

### New `Recurrence/AssignmentPicker.cs` — pure strategy logic
`static Guid Pick(AssignmentStrategy strategy, IReadOnlyList<Guid> orderedAssignees,
Guid? current, IReadOnlyDictionary<Guid,int> assignedCounts,
IReadOnlyDictionary<Guid,int> completedCounts, Random rng)`:
- `Random` → uniform pick. `RandomExceptLastAssigned` → uniform excluding `current` (falls back
  to the single assignee if only one). `LeastAssigned`/`LeastCompleted` → min count, stable
  tie-break by order. `KeepLastAssigned` → `current` if still eligible else first.
  `RoundRobin` → next in `orderedAssignees` after `current`, wrapping.
- `orderedAssignees` is a deterministic order (by `User.CreatedAt`, then `Id`). `rng` injected so
  tests seed it.

### `Services/ChoreService.cs`
- **`ValidateAsync`** — extend with a new `Validators.Recurrence(...)` call (below) and accept the
  new request fields.
- **`Apply`** — populate all new fields; normalize the relevant per-mode collections (dedupe/sort
  weekdays, days-of-month, months) and null out params irrelevant to the chosen mode.
- **`CreateAsync`** — set `DueAt = RecurrenceCalculator.FirstOccurrence(rule, StartDate)`; write
  the initial `ChoreAssignment` row.
- **`UpdateAsync`** — recompute `DueAt = FirstOccurrence(...)` (mirrors today's reset-to-start on
  edit); if `CurrentAssigneeId` changed, append a `ChoreAssignment` row.
- **`CompleteAsync`** — record `completion.PreviousAssigneeId = chore.CurrentAssigneeId`, award
  points (unchanged), then:
  - **Frequency**: count this chore's completions in the current period (incl. this one). If
    `count >= FrequencyCount` → advance `DueAt` to next period end **and rotate** (new
    occurrence); else keep `DueAt` (still due this period, **no rotation**).
  - **Other recurring**: `DueAt = NextDue(rule, pref, scheduledDue: completion.OccurrenceDueAt,
    completedAt: now, now)`. If non-null → **rotate**. `OneTime` → `DueAt = null`, no rotation.
  - **Rotate** = load `assignedCounts` (from `ChoreAssignment`) + `completedCounts` (from
    `ChoreCompletions` grouped by user, client-side per the SQLite ordering gotcha) →
    `AssignmentPicker.Pick(...)` → set `CurrentAssigneeId` → append a `ChoreAssignment` linked to
    this completion.
- **`UndoCompletionAsync`** — additionally restore `CurrentAssigneeId =
  completion.PreviousAssigneeId` and delete the `ChoreAssignment` row linked to this completion
  (existing points/`DueAt` restore stays).

### `Common/Validators.cs`
Add `static Error? Recurrence(RepeatType type, CustomRecurrenceMode? mode, int? intervalCount,
RecurrenceUnit? unit, IReadOnlyCollection<DayOfWeek> weekdays, IReadOnlyCollection<int>
daysOfMonth, IReadOnlyCollection<int> months, int? freqCount, FrequencyPeriod? freqPeriod)`:
- `Custom` requires `mode`; per mode: Interval → `intervalCount ≥ 1` (cap, e.g. ≤ 365) + `unit`;
  DaysOfWeek → non-empty weekdays; DaysOfMonth → non-empty days (1–31) and months (1–12);
  Frequency → `freqCount ≥ 1` (cap) + `freqPeriod`. Non-custom types ignore the params.

### `Dtos/Dtos.cs`
- Extend **`ChoreDto`**, **`CreateChoreRequest`**, **`UpdateChoreRequest`** with all new
  recurrence fields + `AssignmentStrategy` + `SchedulingPreference`. Update `ChoreDto.FromEntity`.
- Add `int? FrequencyProgress` (completions in the current period) to `ChoreDto` for the
  "2/3 this week" display; ChoreService computes it in `FromEntity`/list path.

### `Data/TurnlyDbContext.cs`
- Add `DbSet<ChoreAssignment>`.
- `Chore`: `HasConversion<string>` for `CustomMode, IntervalUnit, FrequencyPeriod,
  AssignmentStrategy, SchedulingPreference`; add CSV `ValueConverter` + `ValueComparer` for
  `DaysOfMonth` and `Months` (generalize the existing `WeekdaysConverter` int-list pattern).
- `ChoreCompletion`: `PreviousAssigneeId` as a plain nullable column.
- `ChoreAssignment`: keys, FK to `Chore` (cascade), FK to `User` (Restrict, consistent with
  completions), nullable `ChoreCompletionId`.

### Migration
One SQLite migration (DbContext in Core, Api is startup project):
```
PATH="$PATH:$HOME/.dotnet/tools" dotnet ef migrations add Phase3AdvancedChores \
  --project src/Turnly.Core --startup-project src/Turnly.Api --output-dir Migrations
```
(Postgres migrations remain out of scope per the standing gotcha.)

### `Api/Endpoints/ChoreEndpoints.cs`
No new endpoints — the existing create/update/complete/undo handlers carry the new fields through
the DTOs unchanged.

---

## Frontend — `web/src`

### `lib/types.ts`
- Extend `RepeatType` with `'Custom'`; add unions `CustomRecurrenceMode`, `RecurrenceUnit`,
  `FrequencyPeriod`, `AssignmentStrategy`, `SchedulingPreference`.
- Extend `Chore` and `ChoreRequest` with the new fields; add optional `frequencyProgress` to
  `Chore`.

### `pages/ChoresPage.tsx` (`ChoreFormModal`)
Extract a `RecurrenceFields` sub-component to keep the modal readable. Add:
- "Custom" to the Repeat select; when Custom, a mode selector (Interval / Days of week / Days of
  month / Frequency) that conditionally renders:
  - **Interval**: number `Input` + unit `Select` (Day/Week/Month/Year).
  - **Days of week**: 7 toggle pills (reuse the existing assignee/tag pill pattern at
    `ChoresPage.tsx:402–420`).
  - **Days of month**: grid of 1–31 pills + 12 month pills.
  - **Frequency**: number `Input` + period `Select`.
- **Assignment strategy** `Select` (6 options), hidden for OneTime.
- **Scheduling preference** `Select` (3 options), shown only for recurring types.
- Build the `ChoreRequest` body with the new fields (send only the active mode's params; backend
  nulls the rest). Reuse existing `Input/Label/Select` from `components/ui/Field.tsx`.

### Chore card display
- Upgrade `repeatLabel(chore)` to describe custom recurrence ("Every 2 weeks", "Mon, Wed, Fri",
  "Days 1, 15 · Jan, Jun", "3×/week"). Show a frequency-progress badge when applicable
  (`frequencyProgress`/`frequencyCount`).

---

## Tests — `tests/Turnly.Tests`

- **`Unit/RecurrenceCalculatorTests.cs`** — migrate the 5 existing cases to the new
  `NextDue`/`FirstOccurrence` API; add: custom interval steps, DaysOfWeek next-slot,
  DaysOfMonth+Months (incl. impossible-day skip), all three scheduling preferences (esp.
  `ToFirstNextRepeat` skipping missed occurrences), and `FirstOccurrence`.
- **New `Unit/AssignmentPickerTests.cs`** — each of the 6 strategies with a seeded `Random`,
  including single-assignee and tie-break cases.
- **`Unit/ChoreServiceTests.cs`** — completion rotates the assignee per strategy; frequency
  progress + period rollover; undo restores assignee + `DueAt` and removes the assignment row;
  scheduling-preference end-to-end; new validation cases. Construct any new collaborators in
  `Unit/TestContext.cs`.
- **`Integration/ChoreManagementTests.cs`** — create a custom-recurrence chore via the API,
  complete it, assert the assignee rotates and new fields round-trip.

---

## Verification

```bash
dotnet build
dotnet test                       # all existing 51 + new tests green
cd web && npm run build           # tsc -b typecheck + build
```
End-to-end (both dev servers or `docker compose up --build`): as admin, create a Custom chore in
each of the 4 modes with a non-trivial assignment strategy; mark it complete a few times and
confirm (a) the due date advances per the chosen scheduling preference, (b) the current assignee
rotates as expected, (c) frequency chores show progress and roll over only when the count is met,
and (d) undo restores both the due date and the previous assignee.
