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
- **Current assignee** — the specific user currently assigned to this chore instance; must be selected from the assignees list on creation (a `Manual` chore may instead be left unassigned)
- **Reassign occurrence** — the current assignee for a single occurrence can be manually overridden (e.g. "you take today's") without changing the chore's assignment strategy; the override applies only to the current occurrence and the strategy resumes on the next recurrence. Reassigning directly is **admin-only** and takes effect immediately.
- **Offer a chore** — anyone holding a chore can put its current occurrence up for grabs, optionally attaching a **points bounty**. There is no recipient to pick: every other assignee is notified and the first to claim it gets it. The chore does not move until someone claims it. Offers you can take appear in an "Up for grabs" band at the top of the chore list, on every screen size. A chore with a live offer carries an "Up for grabs" badge with its bounty in the chore list, for everyone including the offerer.
    - The bounty is deducted from the offerer when the offer is made and paid to whoever claims it, so a payout can never bounce. It is refunded in full if the offer is withdrawn, superseded by a newer offer, or if the occurrence goes away (the offerer completes or skips it, an admin reassigns, freeze rotation moves it, or it auto-advances unclaimed).
    - A bounty can't exceed the offerer's balance. Bounties are transfers between members, so they don't count toward lifetime-points achievements.
    - Only one live offer exists per chore at a time (a newer offer supersedes the older). The offerer, or an admin, can withdraw one.
- **Pairs** — for a job two people have to do together, an admin can set fixed pairs on the chore (e.g. Ana & Ben, Cy & Dee). Optional, set in the chore form from the chore's assignees.
    - A pair is one unit: it holds the turn together and the assignment strategy rotates between whole pairs. Assignees left out of every pair still take their turn alone.
    - Pairs can overlap: one person can be in several (Ana & Ben, Ana & Cy, Ben & Cy), and each pair is its own turn in the rotation, so someone in three pairs comes up three times per round. The only rule is that the same pair isn't listed twice. Where the chore form or the admin's Reassign dialog picks who holds the chore, it lists each pair separately.
    - Either partner marks it done and the occurrence closes for both: each gets the chore's full points, and the completion counts toward both partners' stats, streaks and achievements. Either of them (or an admin) can undo it, which takes the points back from both. The credit goes to the pair holding the turn, not to the completer's other pairs. Someone outside the holding pair who completes it brings their own partner only if they have exactly one pair; otherwise they are credited alone.
    - Both partners get the chore's notifications, and both names show wherever the current assignee does. A missed (auto-expired) occurrence counts as missed for both.
    - A frozen partner is treated as away: the other carries the turn alone and is the only one credited. Freezing the person nominally holding the chore leaves it with their partner rather than rotating it away. While someone is frozen their pairs drop out of the rotation; a partner left with no other pair takes turns alone.
    - Paired chores can't be offered (one partner can't hand over a joint turn, and a claim would commit the claimer's partner); an admin can still reassign one to another pair. Not available on `Everyone (independent)` chores, which have no shared turn.
- **Nudge** — anyone in the household (assigned to the chore or not) can send a reminder to whoever owes its current occurrence, with an optional note of up to 200 characters. By default it reaches everyone holding the chore: the assignee and their partner on a paired chore, or every other assignee with an open schedule on an `Everyone (independent)` chore. A specific holder can be picked instead. Available from the chore's menu and its details dialog.
    - You can't nudge yourself, someone who isn't holding the chore, anyone on leave, or anyone about a paused chore.
    - A sender can nudge the same person about the same chore at most once every 30 minutes. Nudging "everyone" skips whoever is still cooling down and says who was reached.
    - Nudges are their own notification category, so the recipient's delivery settings, chore mutes and quiet hours apply. The chore itself doesn't change.
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
    - `Manual (admin assigns)` — nobody is picked automatically: whenever the chore would hand over
      it goes back to **Unassigned** and stays there until an admin assigns it (the chore menu's
      "Assign" action). It can also be created or edited to start unassigned. Anyone can still
      complete an unassigned chore; members can't claim it for themselves. Freezing the person
      holding one leaves it unassigned rather than picking a replacement. Combined with a calendar
      rotation period it returns to unassigned once per period instead of after every occurrence.
- **Rotation period** ("Hand over") — how often a rotating chore changes hands:
    - `Every occurrence` (default) — the strategy picks a new assignee each time an occurrence closes
    - `Once a day` / `Once a week` / `Once a month` / `Once a quarter` / `Once a year` — the same
      person keeps the chore for every occurrence in that calendar period, and the strategy only
      picks the next assignee when the due date crosses into a new one. Periods follow the calendar
      (the day, the Monday-to-Sunday week, the month, Jan-Mar/Apr-Jun/Jul-Sep/Oct-Dec, the year), not
      a rolling window from when the person took over. `Once a month` models "you do this once a
      week for the month, then someone else does it" as a Weekly chore with real weekly due dates;
      `Once a day` keeps a morning-and-evening chore with one person all day. The handover follows
      the calendar however the last occurrence closed (completed, skipped, or auto-expired), so
      skipping the final occurrence doesn't keep the chore for another period. A manual reassign or
      claimed offer mid-period just changes who holds it until the period turns. Unfreezing a paused
      chore doesn't hand over by itself.
    - Offered with a rotating strategy (not `Keep Last Assigned` or `Everyone (independent)`), and
      only where the chore can come due more than once inside the period: day and week for Daily and
      Custom repeats; month for Daily, Weekly and Custom; quarter and year for those plus Monthly.
      Yearly chores always hand over every occurrence. Replaces "rotate after each completion" on
      multi-completion chores.
- Chores are visible to all household members

### Recurrence

- **Repeat types:**
    - One-time
    - On demand — no schedule and no due date: the chore can be completed whenever it's needed, as
      often as needed. Every completion awards points and counts as a turn, so a rotating chore hands
      over after each one. No start date, due time, completion count, scheduling preference,
      auto-advance or reminders; it can't be skipped, rescheduled or use per-person scheduling. Listed
      under its own "Anytime" section on the chores page
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
    - **Slot labels** — each time can optionally be given a name (e.g. 08:00 = "Morning", 20:00 =
      "Evening", up to 32 characters). The label of the occurrence that is currently due is shown next
      to the due time on the chore, in the chore details, and in the push notification title
      ("Feed the dog (Morning)"). Labels are display-only and don't affect scheduling; removing a time
      removes its label.
- **Auto-advance incomplete** — for multi-completion chores, the schedule can roll forward
  automatically once the completion window closes: any unfilled slots are recorded as **missed
  (expired)** and the chore advances to the next occurrence (and rotates the assignee) without waiting
  for a person to act. An optional window (minutes/hours/days after the due time) gives a grace period
  before the occurrence is considered missed.
- **Completing with a custom time fills a missed occurrence** - when the chosen time falls inside an
  occurrence that was already recorded as missed (expired), that missed entry becomes the completion
  (with points) and the current occurrence stays open, instead of the current one being completed.
- **Correcting the activity log** (admin) - in a chore's details, under Activity, an admin can mark a
  missed (expired) entry as completed by choosing who did it and when, and can change the time of a
  completion that was logged after it was due. The person (and their partner on a paired chore) gets
  the chore's points. The chore's current schedule and assignee do not change, and the time can't be
  in the future or jump past an entry of another occurrence. On a per-person chore a missed entry
  can only be credited to the person whose slot it was.
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
- **Completed at (optional)** - a chore that was done earlier but couldn't be logged at the time can be given the time it was actually done. That time is what the log shows and what decides on-time vs late, the streak, and (for the completion-based scheduling preferences) the next due date. It can't be in the future or earlier than the chore's last logged entry. Left unset, the completion is stamped with the current time.
- Completions can be undone; points are reversed on undo
- **Skip occurrence** — a recurring chore's current occurrence can be skipped instead of completed: it advances the recurrence to the next due date (per the chore's scheduling preference) without awarding points or flagging it overdue; skips are logged and can be undone (one-time chores cannot be skipped)
- **Missed / expired occurrence** — when a chore has **auto-advance** on, the background service closes an unfilled occurrence after its completion window passes: the missing slots are logged as expired (no points, no actor, not undoable) and the schedule advances. Expired occurrences appear in history and break the on-time streak.
- Points awarded on completion (fixed value set per chore)
- Points are per-user, accumulated over time
- **Points log** — each user has a full history of point changes: earnings (per completion), deductions (per redemption), and **manual admin adjustments**
- **Manual point adjustment** — an admin can grant or deduct points from any user with an optional reason; the change is recorded in that user's points log
- **Rescue** - when someone other than the person (or pair) holding a chore completes it after it went overdue, the completion is a rescue: it counts as late for nobody (neither the rescuer's nor the holder's late count), and history shows it as "rescue" instead of "on time" or "late". The rescuer still gets the points and the completion count. Does not apply to `Everyone (independent)` chores, where nobody can do another person's share. The chore's on-time streak still resets, since the occurrence itself was not done on time.
- **On-time streak** — each chore tracks how many of its most recent occurrences were completed on or before the due date; a late completion, a skip, or a missed (expired) occurrence resets the streak to 0. For `Everyone (independent)` chores the streak is tracked per assignee.

---

## Awards & Redemption

- Admin can create awards, each with: name, description (optional), and point cost
- Users can redeem an award by spending points from their balance
- Redemption is logged: who redeemed, which award, when
- Admin can fulfill / mark a redemption as delivered
- Admin can cancel a still-pending redemption (refunds the spent points and removes it), or delete a redemption of **any** status — including an already-fulfilled one — which likewise refunds the points it spent
- Point balance decreases on redemption
- Awards live on the **Awards** tab of the **Rewards** page (the one place points are spent), next to the **Cosmetics** tab
- **Next-goal progress** — the Awards tab shows a progress bar toward the cheapest award the user can't yet afford (or a "redeem anything" nudge once everything is affordable)

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

## Cosmetics

A points-funded random-pull system for **cosmetic** rewards, shown as the **Cosmetics** tab of the
**Rewards** page. No real money: pulls are paid for with the same
chore-earned points used for awards, so it is a reward sink, not gambling.

- **Four cosmetic slots:**
    - **Avatar frames** — a decorative ring around your avatar, visible to everyone on every avatar
      (leaderboard, chore assignees, account menu, user list)
    - **App theme palettes** — recolor your own app (e.g. Midnight, Sakura, Galaxy); only you see your palette
    - **Avatar colors** — the fill color of your avatar, visible to everyone. Three starter colors
      (the default purple, green and blue) are free and owned by everyone; other colors are collectible.
      (Avatar color is no longer set from the profile or the admin user form - it is chosen by equipping
      a color, and new users start on the default purple.)
    - **Avatar emojis** — shown on your avatar in place of your initials, visible to everyone. Three
      starter emojis (star, heart, paw) are free and owned by everyone; the rest are collectible. None is
      equipped to begin with, so a new avatar shows initials.
    - Free starters are never rolled in a pull.
- **Built-in catalog** — a predefined set defined by the app (not admin-configurable), each item tagged with a **rarity**: Common / Rare / Epic / Legendary
- **Pulls** — spend points on a single pull or a discounted **10-pull**; each roll picks a rarity by published **drop rates**, then a random cosmetic of that rarity
- **Pity** — a counter guarantees a Legendary within a fixed number of pulls; it resets whenever a Legendary is obtained
- **Dust** — a duplicate pull pays out **dust** (scaled by rarity) instead of a second copy; dust is spent to **craft** a specific cosmetic directly (craft cost is higher than the dupe payout)
- **Published odds** — the drop rates are shown openly on the Cosmetics tab
- **Equip** — handled from a **Customization** option in the account menu (not the Rewards page). It is one appearance picker: the base **Light** / **Dark** modes plus any owned **theme palettes** (mutually exclusive: choosing a palette supersedes light/dark, choosing a base mode clears the palette), any owned **avatar frame** (or None), and any owned **avatar color**. Equipping a theme recolors the app immediately and persists across devices and reloads (no flash)
- **Reveal** — a pull shows a celebration popup (with confetti) listing what dropped, with new unlocks and dust gained
- **Cosmetics tab** — balances (points in the page header, dust on the pull card), pull buttons, a pity progress bar, the odds disclosure, and a collection grid grouped by slot and rarity (shows owned vs. locked, with craft on locked items); equipping is done from the account menu Customization picker
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

### Personal notification controls

Each user decides what reaches them. These settings are personal: they never change what anyone else
receives, and an admin cannot set them on someone's behalf.

- **Per-category delivery** — every notification belongs to one category, and each can be set to:
    - **Push** (default) — buzz your devices and record it in your inbox
    - **Inbox** — record it in your inbox without buzzing your devices
    - **Off** — don't send it at all (no push, no inbox row)
  The categories are: **chore reminders** (nudges before the due time), **chore due** (at the due
  time), **overdue chases** (follow-ups after the due time), **chores up for grabs** (someone puts a chore
  up for grabs), **replies to your offers** (someone claims a chore you
  offered), **nudges** (someone reminds you about a chore you hold), and **app messages** (test pushes and account notices - always delivered, so a test push
  can never be silently swallowed).
- **Mute a chore** — silence one specific chore from its menu, rather than a whole category. A mute is
  yours alone: everyone else still gets notified normally. It can be indefinite or set to lapse at a
  chosen time, and a muted chore shows a small bell-off marker in the chore list.
- **How the controls combine** — the most restrictive answer wins, and suppression only travels one
  way: nothing can promote a notification back up. A mute or an active quiet-hours window downgrades a
  push to an inbox record (you are saying "don't interrupt me", not "pretend it never happened"), while
  **Off** is the only setting that discards a notification outright.
- Quiet hours and these preferences apply to **every** notification, including offer and nudge notifications,
  not only the scheduled per-chore ones.
- A **fresh start** keeps these settings: they are preferences, not activity.

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
- Clears all chore activity (completions, skips, auto-expired slots), assignment history, the points log, award redemptions, achievements, and cosmetics progress (owned cosmetics, dust, pity)
- Resets every user's points to **0** and their equipped cosmetics (frame, theme, avatar color) back to the free defaults
- Keeps users, tags, awards, push devices, notification schedules, and each user's personal notification preferences and chore mutes; each chore keeps its current assignee and next due date
- Irreversible — a confirmation is required before it runs

---

## Dashboard

- Today's due chores, highlighted by assignee
- Overdue chores clearly flagged
- Upcoming chores (next 7 days)
- Filterable by tag and assignee
- **Chore views** — list, compact, and calendar layouts (the chosen view persists per browser). Admins get a fourth **Config** view: one table of every chore's settings (points, schedule, assignment, completion rules, reminders) with an edit button per row
- **Live updates** - changes made by anyone else (a completion, a reassignment, a new chore, an offer) and chores the server moves on its own show up on open screens within a moment, with no reload. A screen that was in the background catches up when it is reopened

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
- Per-user completion count (weekly / monthly / all time), with that period's on-time / late / rescue / missed breakdown (on time + late + rescues adds up to the count)
- Per-chore completion history: each entry shows when it happened and which occurrence it was for (due date, the slot time on timed chores, and the slot label when it has one)
- Simple bar chart: completions per user per week

---

## Language

- The interface is available in **English** and **Croatian** (Hrvatski), chosen under **Settings → Language**
- The choice is **per device** (like light/dark mode), applies immediately, and survives reloads; a device that has never chosen follows the browser language
- Covers the whole interface: navigation, chores, forms, dialogs, toasts, dates, plural forms, and the names of achievements
- Not translated: cosmetic names and rarity tiers (they stay as proper names), text the server composes or stores (error messages, push and inbox notification text) and anything a user typed (chore names, tags, awards, notes)

---

## PWA

- Installable on iOS and Android via browser
- Works offline (read-only, queues completions for sync)
- App-like experience: no browser chrome, splash screen, home screen icon
- **Update banner** - after a redeploy, a tab still running the old version shows a small banner
  above the top bar with a Reload button (dismissable for the session)

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
- **Update banner** - when the server has a newer APK published than the installed one, the app shows
  a small banner with a Download link (served by the Turnly server from `Releases:Path`)

---

## Self-Hosting

- Single Docker Compose deployment
- SQLite by default (Postgres optional)
- Environment variable configuration
- No external dependencies required (push handled via self-hosted VAPID keys)
- **Family timezone** — an admin sets the instance's timezone (IANA or Windows id) in settings; it is
  used to evaluate per-user quiet hours against local wall-clock time. Falls back to the server's host
  zone when unset.
- **Day start** — an admin sets when the household's day rolls over (midnight by default, up to
  12:00). A chore with no due time stays "due today" until then instead of going overdue at 00:00:
  with a 05:00 day start, Monday's chore done at 01:00 on Tuesday is still on time, keeps the on-time
  streak, schedules its next occurrence as if done on Monday, and auto-advance waits until 05:00 (plus
  the chore's completion window) before marking it missed. Chores with a due time are unaffected: they
  go overdue the moment that time passes. The "today" and "upcoming" groupings follow the same day.
  Reminders still fire relative to the stored due time.

---

## Technical Requirements

### Backend

- **Framework:** ASP.NET Core (C#)
- **ORM:** Entity Framework Core
- **Auth:** JWT access tokens + long-lived refresh tokens (6 months)
- **Push notifications:** Web Push via self-hosted VAPID keys
- **Live updates:** Server-Sent Events (`GET /api/events`), in-process, no extra infrastructure
- **Testing:** xUnit — unit tests for business logic (recurrence, points, assignment strategies), integration tests for API endpoints

### Frontend

- **Framework:** React with Vite + TypeScript
- **Server state:** TanStack Query (fetching, caching, invalidation)
- **Styling:** Tailwind CSS + shadcn/ui
- **Communication:** REST API (JSON)
- **Localization:** English and Croatian, client-side dictionaries with a small typed `t()` helper (no i18n library)

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
