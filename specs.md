# Turnly — Feature Specification

> Family chore management web application with PWA. Simple, self-hosted, actually works.

---

## Users & Households

- Single household per instance
- Multiple user accounts (admin + member roles)
- Admin can create/edit/delete users
- Each user has a display name and avatar color
- Simple username/password auth (no OAuth required)
- Long refresh tokens (6 months)
- Admin can set a new password for any user
- Users can change their own password
- **On user deletion:** all completion history for that user is wiped; admin must select a replacement assignee for any chores currently assigned to the deleted user

---

## Chores

- Create chores with: name, description (optional), icon/emoji
- **Tags** — freeform labels for grouping/filtering chores
- **Assignees** — list of users eligible to be assigned this chore; must include at least one user
- **Current assignee** — the specific user currently assigned to this chore instance; must be selected from the assignees list on creation
- **Reassign occurrence** — the current assignee for a single occurrence can be manually overridden (e.g. "you take today's") without changing the chore's assignment strategy; the override applies only to the current occurrence and the strategy resumes on the next recurrence
- **Assignment strategy** — determines how the next assignee is picked on each recurrence:
  - `Random` — pick any assignee at random
  - `Least Assigned` — pick the assignee who has been assigned this chore fewest times
  - `Least Completed` — pick the assignee who has completed this chore fewest times
  - `Keep Last Assigned` — reassign to the same person as the previous occurrence
  - `Random Except Last Assigned` — random, but exclude whoever did it last
  - `Round Robin` — cycle through assignees in order
  - `Everyone (independent)` — no rotation: **every assignee gets their own independent schedule**
    with its own due date and per-person quota. Lets one chore model "everyone does the dishes once a
    week" (all quotas 1) or an uneven split ("Alice 3× / Bob 2×"); each person advances on their own,
    so a late assignee never blocks the others. (Recurring chores only.)
- Chores are visible to all household members

### Recurrence

- **Repeat types:**
  - One-time
  - Daily
  - Weekly
  - Monthly
  - Yearly
  - Custom — one of three modes:
    - **Interval** — every `{x}` `{days / weeks / months / years}` (day granularity; hourly out of scope)
    - **Days of the week** — repeats on selected weekdays (e.g. Mon, Wed, Fri); optionally restricted to specific occurrences within the month (any combination of 1st / 2nd / 3rd / 4th / last, e.g. "1st and 3rd Monday"), or every week by default
    - **Days of the month** — repeats on selected days (e.g. 1, 15) within selected months (e.g. Jan, Jun); any combination of one or more days and one or more months
- **Completion count** — for the non-custom repeat types, a chore can require being completed `{x}`
  times before the occurrence advances (e.g. "3× per week"); each completion **or skip** counts toward
  closing the occurrence. (For `Everyone (independent)` chores this count is set **per assignee**.)
- **Multiple times of day** — a day-resolution chore (Daily, or the custom Days-of-week / Days-of-month
  modes) can list several fixed times of day (e.g. 08:00 and 20:00 for "twice a day"); each time is a
  distinct occurrence. Empty = a single daily slot at the chore's due time.
- **Auto-advance incomplete** — for multi-completion chores, the schedule can roll forward
  automatically once the completion window closes: any unfilled slots are recorded as **missed
  (expired)** and the chore advances to the next occurrence (and rotates the assignee) without waiting
  for a person to act. An optional window (minutes/hours/days after the due time) gives a grace period
  before the occurrence is considered missed.
- **Start date / datetime** — when the first occurrence begins

### Scheduling Preference on Completion

Controls how the next due date is calculated after a chore is marked complete:

- **From scheduled date** — next due = scheduled date + interval (ignores when it was actually done)
- **From completion date** — next due = actual completion time + interval
- **To first next repeat** — next due = the next naturally occurring occurrence after now (skips any missed ones)
- **Smart scheduling** — holds the planned cadence but never schedules sooner than one interval after the actual completion (`max(from-scheduled, from-completion)`); an optional **grace window** resets the cadence from the completion when a chore is done more than that window early. Offered only for interval-style repeats.

### Notifications

Per-chore notification schedule — a list of notification entries, each with:

- **Type:** `reminder` | `due` | `follow-up`
- **When:** `before` | `at due` | `after` — offset in minutes / hours / days
- **Who:** `current assignee` | `all assignees`

### Points

- Fixed point value set per chore (not derived from a difficulty tier)
- Awarded to the completing user on completion

---

## Completion & Points

- Any household member can mark a chore complete
- Completing a chore logs: who, when, notes (optional)
- Completions can be undone; points are reversed on undo
- **Skip occurrence** — a recurring chore's current occurrence can be skipped instead of completed: it advances the recurrence to the next due date (per the chore's scheduling preference) without awarding points or flagging it overdue; skips are logged and can be undone (one-time chores cannot be skipped)
- **Missed / expired occurrence** — when a chore has **auto-advance** on, the background service closes an unfilled occurrence after its completion window passes: the missing slots are logged as expired (no points, no actor, not undoable) and the schedule advances. Expired occurrences appear in history and break the on-time streak.
- Points awarded on completion (fixed value set per chore)
- Points are per-user, accumulated over time
- **Points log** — each user has a full history of point changes: earnings (per completion), deductions (per redemption), and **manual admin adjustments**
- **Manual point adjustment** — an admin can grant or deduct points from any user with an optional reason; the change is recorded in that user's points log
- **On-time streak** — each chore tracks how many of its most recent occurrences were completed on or before the due date; a late completion, a skip, or a missed (expired) occurrence resets the streak to 0. For `Everyone (independent)` chores the streak is tracked per assignee.

---

## Awards & Redemption

- Admin can create awards, each with: name, description (optional), and point cost
- Users can redeem an award by spending points from their balance
- Redemption is logged: who redeemed, which award, when
- Admin can fulfill / mark a redemption as delivered
- Point balance decreases on redemption
- **Next-goal progress** — the awards page shows a progress bar toward the cheapest award the user can't yet afford (or a "redeem anything" nudge once everything is affordable)

---

## Notifications

- Delivered via Web Push (PWA)
- Each chore has a configurable notification schedule — a list of entries, each defining:
  - **Type:** `reminder` (upcoming), `due` (at due time), `follow-up` (after due time passes without completion)
  - **When:** `before` | `at due` | `after` — with an offset in minutes / hours / days
  - **Who:** `current assignee` | `all assignees`
- All notifications for a chore instance stop firing once it is marked complete (for
  `Everyone (independent)` chores, reminders fire **per assignee** and stop once that person completes
  their own share)
- **Quiet hours** — each user can set a nightly window (e.g. 22:00–07:00, wrap-aware) during which push
  notifications are suppressed; the in-app inbox row is still written so nothing is lost. The window is
  evaluated against the configured **family timezone** (see Self-Hosting).

---

## Dashboard

- Today's due chores, highlighted by assignee
- Overdue chores clearly flagged
- Upcoming chores (next 7 days)
- Per-user point totals (current week / all time)
- Filterable by tag and assignee
- **Chore views** — list, compact, and calendar layouts (the chosen view persists per browser)

---

## Search

- Global search available from the top bar, anywhere in the app
- Searches chores by name, description, and tags
- Also matches users (display name) and awards (name)
- Results grouped by type (chores / users / awards); selecting a result jumps to it
- Keyboard accessible: focus shortcut, arrow-key navigation, Enter to open

---

## History & Stats

- Completion log: chore, completed by, timestamp — filterable by tag, assignee, and chore
- Per-user completion count (weekly / monthly / all time)
- Per-chore completion history
- Simple bar chart: completions per user per week

---

## PWA

- Installable on iOS and Android via browser
- Works offline (read-only, queues completions for sync)
- App-like experience: no browser chrome, splash screen, home screen icon

---

## Self-Hosting

- Single Docker Compose deployment
- SQLite by default (Postgres optional)
- Environment variable configuration
- No external dependencies required (push handled via self-hosted VAPID keys)
- **Family timezone** — an admin sets the instance's timezone (IANA or Windows id) in settings; it is
  used to evaluate per-user quiet hours against local wall-clock time. Falls back to the server's host
  zone when unset.

---

## Technical Requirements

### Backend
- **Framework:** ASP.NET Core (C#)
- **ORM:** Entity Framework Core
- **Auth:** JWT access tokens + long-lived refresh tokens (6 months)
- **Push notifications:** Web Push via self-hosted VAPID keys
- **Testing:** xUnit — unit tests for business logic (recurrence, points, assignment strategies), integration tests for API endpoints

### Frontend
- **Framework:** React with Vite + TypeScript
- **Server state:** TanStack Query (fetching, caching, invalidation)
- **Styling:** Tailwind CSS + shadcn/ui
- **Communication:** REST API (JSON)

### Design Language
Modern clean B2B SaaS dashboard aesthetic — readable, organized, low cognitive load.
- **Layout:** three-pane — left sidebar (global nav), top bar (search + account), central workspace; collapses to a drawer on mobile
- **Color:** high-key cool-neutral (light-gray canvas, white cards/nav); violet-blue accent used sparingly for primary actions, logo, and active state (rendered as a soft tint, not a solid block)
- **Status:** pastel semantic pills — soft tinted background with saturated same-hue text
- **Depth:** soft, diffuse shadows lift white cards off the canvas (not flat)
- **Shape:** rounded corners (8–12px) throughout
- **Type:** Inter, two weights (regular + semibold); bold reserved for headers and selected items
- **Icons:** minimalist outline/stroke, uniform weight
- **Spacing:** generous whitespace inside cards and between sections
- **Theming:** semantic CSS-variable tokens; light + dark out of the box. Implementation detail (tokens, components, paths) lives in `CLAUDE.md`.

### Database
- **Default:** SQLite
- **Optional:** PostgreSQL (switchable via connection string config)
- Migrations managed via EF Core

### Deployment
- Single Docker Compose file (backend + frontend served as static files)
- Configuration via a `.env` file loaded by Docker Compose

---

## Development Phases

### Phase 1 — Foundation
Auth, user CRUD, password management, roles, DB schema, Docker setup. Everything else depends on this.

### Phase 2 — Chores (Core)
Chore CRUD (name, description, emoji, tags, assignees, points), basic recurrence (one-time, daily, weekly, monthly, yearly), start date, mark complete, undo, points log.

### Phase 3 — Chores (Advanced)
Custom recurrence (all 4 modes), assignment strategies, scheduling preferences on completion.

### Phase 4 — Dashboard
Today / overdue / upcoming views, per-user point totals, filtering by tag and assignee, global search.

### Phase 5 — History & Stats
Completion log with filters, per-user stats, bar chart.

### Phase 6 — Awards & Redemption
Award CRUD (admin), redemption flow, fulfillment tracking, points deduction.

### Phase 7 — Skip & Reassign
Skip an occurrence (advance recurrence without awarding points - with logs), one-off reassignment of the current assignee for a single occurrence.

### Phase 8 — Notifications
Web Push / VAPID setup, per-chore notification schedule, stop-on-completion logic, push service worker,
per-user device management, in-app notification inbox, and basic PWA install (manifest + icons).

### Phase 9 — UX Polish
Swipe actions on chores, completion delight, admin deletion of activity entries
(completions and skips) from chore details, admin completing a chore on behalf of any user, and
refactoring the chores page into multiple components.

### Post-Phase-9 — Per-assignee independent tracks
The `Everyone (independent)` assignment strategy: a shared chore gives each assignee their own
schedule + per-person quota (no rotation), an admin manual **reschedule** of the current occurrence,
and notifications that fan out per assignee.

### Post-Phase-9 — Scheduling, points & UX extensions
Shipped after the independent-tracks work, in no strict phase order:
- **Multiple times of day** per day-resolution chore (each time a distinct occurrence).
- **Auto-advance incomplete** occurrences — a background service expires unfilled multi-completion
  slots after a configurable window, logs them as missed, and advances/rotates.
- **On-time streaks** per chore (per assignee for independent chores).
- **Chore copying** — duplicate a chore (fresh schedule) under a new name.
- **Manual point adjustments** by an admin (logged in the points log).
- **Quiet hours** — per-user nightly push-suppression window, evaluated in the family timezone.
- **Family timezone** — admin-configured instance timezone backing quiet hours.
- **Next-goal progress** on the awards page; **list / compact / calendar** chore views.

---

## On wait

- Complete at time

- **Vacation / availability** — `UserAvailability` (date range per user); `AdvanceScheduleAsync`
  filters unavailable users out of the assignee list before `AssignmentPicker.Pick` (fall back to
  the full list if everyone's away), and `SendEntryAsync` skips away recipients (push + inbox).
- **Per-user time zone** — quiet hours currently use a single instance-wide **family timezone**
  (`AppSetting`); a per-user `User.TimeZoneId` (IANA) would let each member's quiet hours/digest run in
  their own zone. Not yet implemented.
- **Quiet hours — defer & replay.** Shipped (per-user wrap-aware window suppresses push, keeps the
  inbox row). The remaining extension: instead of dropping the muted push, defer and replay it after
  the window (needs a per-recipient pending-push queue, since today's dedup is per-occurrence).
- **Daily digest** — opt-in per-user morning summary (one push instead of N): `DigestEnabled` +
  `DigestAtLocal` on `User`, a `DigestDelivery (UserId, LocalDate)` dedup row, and a new
  `ProcessDigestsAsync(now)` scan called from the same minute tick. Reuses the dashboard's
  today/overdue grouping. Consider a per-user "per-event | digest | both" notification style that
  ties this together with quiet hours.

## Out of Scope (v1)

- Full offline support — offline read, completion queue, app shell, and asset caching (basic
  install via manifest + service worker already shipped in Phase 8)
- Photo proof of completion
- Chore archiving / pausing
- Multiple households per instance
- Third-party OAuth
- Native iOS/Android apps
- Chore sub-tasks

## Ideas - also out of scope

- Capacitor app (let user set server url)