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
- Chores are visible to all household members

### Recurrence

- **Repeat types:**
  - One-time
  - Daily
  - Weekly
  - Monthly
  - Yearly
  - Custom — one of four modes:
    - **Interval** — every `{x}` `{hours / days / weeks / months / years}`
    - **Days of the week** — repeats on selected weekdays (e.g. Mon, Wed, Fri)
    - **Days of the month** — repeats on selected days (e.g. 1, 15) within selected months (e.g. Jan, Jun); any combination of one or more days and one or more months
    - **Frequency** — must be completed `{x}` times per `{day / week / month / year}`; days are not fixed, just the count within the period
- **Start date / datetime** — when the first occurrence begins

### Scheduling Preference on Completion

Controls how the next due date is calculated after a chore is marked complete:

- **From scheduled date** — next due = scheduled date + interval (ignores when it was actually done)
- **From completion date** — next due = actual completion time + interval
- **To first next repeat** — next due = the next naturally occurring occurrence after now (skips any missed ones)

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
- Points awarded on completion (fixed value set per chore)
- Points are per-user, accumulated over time
- **Points log** — each user has a full history of point changes: earnings (per completion) and deductions (per redemption)

---

## Awards & Redemption

- Admin can create awards, each with: name, description (optional), and point cost
- Users can redeem an award by spending points from their balance
- Redemption is logged: who redeemed, which award, when
- Admin can fulfill / mark a redemption as delivered
- Point balance decreases on redemption

---

## Notifications

- Delivered via Web Push (PWA)
- Each chore has a configurable notification schedule — a list of entries, each defining:
  - **Type:** `reminder` (upcoming), `due` (at due time), `follow-up` (after due time passes without completion)
  - **When:** `before` | `at due` | `after` — with an offset in minutes / hours / days
  - **Who:** `current assignee` | `all assignees`
- All notifications for a chore instance stop firing once it is marked complete

---

## Dashboard

- Today's due chores, highlighted by assignee
- Overdue chores clearly flagged
- Upcoming chores (next 7 days)
- Per-user point totals (current week / all time)
- Filterable by tag and assignee

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

---

## On wait

- Investigate task update bug - db update concurrency exception on ChoreNotifications
- Chores title and add chore button take up too much vertical space on mobile
- Let current assignee be "anyone" (needs to be though out)
- Add more stuff to chore details page
- History -> Completions by member is stuffed on mobile
- Completion log filter dropdowns are too small on mobile
- Let users change their profile color

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