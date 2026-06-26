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
- Admin can cancel a still-pending redemption (refunds the spent points and removes it), or delete a redemption of **any** status — including an already-fulfilled one — which likewise refunds the points it spent
- Point balance decreases on redemption
- **Next-goal progress** — the awards page shows a progress bar toward the cheapest award the user can't yet afford (or a "redeem anything" nudge once everything is affordable)

---

## Achievements

- **Collectible badges**, separate from spendable points — purely cosmetic, no point value
- **Built-in catalog** — a predefined set defined by the app (not admin-configurable), grouped into:
    - **Completion milestones** — first chore, then 10 / 50 / 100 / 500 lifetime completions
    - **On-time streak milestones** — reaching a 7 / 30 / 100-occurrence on-time streak (reuses the streak)
    - **Points milestones** — 100 / 1,000 / 10,000 lifetime points _earned_ (gross positive inflow; spends and deductions don't lower it)
    - **Redemption** — first redemption, then 10 awards redeemed
    - **Variety** — completing 10 different chores, or chores across 5 different tags
- **Permanently earned** — once unlocked, a badge is never revoked automatically; undoing a completion, cancelling a redemption, or an admin deducting points lowers the _live_ progress of still-locked achievements but never takes back one already earned. An **admin can manually revoke** an earned badge from a user (it can be re-earned later if the threshold is met again)
- **Admin view** — an admin can view any user's achievements (their own by default) from the achievements page, and revoke earned ones from there
- **Unlock celebration** — earning a badge shows a one-time celebration popup (with confetti) the moment the earning activity is logged. The unlocked badge rides back on the completion/redemption response, so it pops for the person who earned it (a member completing their own chore or redeeming an award); an admin acting on someone else's behalf doesn't see another user's popup. No inbox item or push is sent for an unlock
- **Achievements page** — a dedicated page listing earned and still-locked achievements (locked ones show a progress bar toward their threshold), grouped by category

---

## Gacha (Cosmetics)

A points-funded gacha for **cosmetic** rewards. No real money: pulls are paid for with the same
chore-earned points used for awards, so it is a reward sink, not gambling.

- **Three cosmetic slots (v1):**
    - **Avatar frames** — a decorative ring around your avatar, visible to everyone on every avatar
      (leaderboard, chore assignees, account menu, user list)
    - **App theme palettes** — recolor your own app (e.g. Midnight, Sakura, Galaxy); only you see your palette
    - **Avatar colors** — the fill color of your avatar, visible to everyone. The default purple is free
      and owned by everyone; other colors are collectible. (Avatar color is no longer set from the profile
      or the admin user form - it is chosen by equipping a color, and new users start on the default purple.)
- **Built-in catalog** — a predefined set defined by the app (not admin-configurable), each item tagged with a **rarity**: Common / Rare / Epic / Legendary
- **Pulls** — spend points on a single pull or a discounted **10-pull**; each roll picks a rarity by published **drop rates**, then a random cosmetic of that rarity
- **Pity** — a counter guarantees a Legendary within a fixed number of pulls; it resets whenever a Legendary is obtained
- **Dust** — a duplicate pull pays out **dust** (scaled by rarity) instead of a second copy; dust is spent to **craft** a specific cosmetic directly (craft cost is higher than the dupe payout)
- **Published odds** — the drop rates are shown openly on the gacha page
- **Equip** — handled from a **Customization** option in the account menu (not the gacha page). It is one appearance picker: the base **Light** / **Dark** modes plus any owned **theme palettes** (mutually exclusive: choosing a palette supersedes light/dark, choosing a base mode clears the palette), any owned **avatar frame** (or None), and any owned **avatar color**. Equipping a theme recolors the app immediately and persists across devices and reloads (no flash)
- **Reveal** — a pull shows a celebration popup (with confetti) listing what dropped, with new unlocks and dust gained
- **Gacha page** — balances (points + dust), pull buttons, a pity progress bar, the odds disclosure, and a collection grid grouped by slot and rarity (shows owned vs. locked, with craft on locked items); equipping is done from the account menu Customization picker
- **Reversal-safe** — owned cosmetics are permanent; there is no admin revoke in v1

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

## Freeze / Away

### Per-chore freeze (admin only)

- Admin can **Pause** any chore from the chore menu or details modal
- While paused, completions and skips are blocked (the server rejects them)
- The chore appears in a **"Paused"** bucket on the chores page, separate from the normal overdue/today/upcoming/later sections
- No auto-advance fires, no notifications are sent
- On **Unpause**, recurring chores that are overdue have their due date stepped forward to the first future occurrence; one-time chores are left at their current due date

### Per-user freeze (admin only)

- Admin can **Freeze** a user (with an "Away" badge shown on their profile)
- A **preview** is shown before confirming: lists rotating chores that will be reassigned and any chores with no other eligible assignee (which will become unassigned)
- While frozen the user is excluded from rotation, their independent chore tracks receive no auto-advance or notifications, and they are removed from `AllAssignees` push notification recipients
- On **Unfreeze**, stale independent track due dates (in the past) are stepped forward to the next future occurrence

### Fresh start (admin only)

- A **Fresh start** action in Settings (Danger zone) gives a clean slate while **keeping every chore and its schedule**
- Clears all chore activity (completions, skips, auto-expired slots), assignment history, the points log, award redemptions, achievements, and gacha progress (owned cosmetics, dust, pity)
- Resets every user's points to **0** and their equipped cosmetics (frame, theme, avatar color) back to the free defaults
- Keeps users, tags, awards, push devices, and notification schedules; each chore keeps its current assignee and next due date
- Irreversible — a confirmation is required before it runs

---

## Dashboard

- Today's due chores, highlighted by assignee
- Overdue chores clearly flagged
- Upcoming chores (next 7 days)
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

## Standalone Android app

- A native Android APK (Capacitor) that bundles the web UI and connects to a self-hosted Turnly
  server chosen by the user
- **First-run server picker** — the user enters their server address (e.g. `https://turnly.myhome.net`),
  validated against the server before saving; changeable later under Settings. One APK works for any
  self-hosted instance
- **Native sign-in** — because the app has no same-origin server, the refresh token is stored in
  secure device storage rather than an httpOnly cookie (the web build is unchanged)
- The server must allow the app's origin (`https://localhost`) via `Cors:Origins`
- **Native push** via Firebase Cloud Messaging - reminders arrive as system notifications and deep-link
  the chore on tap (the same schedule as Web Push). Optional: the app and server run fine without
  Firebase (push disabled, in-app inbox still works)

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

## Out of Scope / not planned (ever)

- Offline support
- Photo proof of completion
- Multiple households per instance
- Third-party OAuth
- Native iOS/Android apps
