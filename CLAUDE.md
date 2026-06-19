# CLAUDE.md

Guidance for working in the Turnly repository. See `specs.md` for the full product spec
and 9-phase roadmap, and `README.md` for user-facing setup.

## What this is

Self-hosted family chore-management web app (PWA). **Phases 1–8 are complete.** Phase 1
(Foundation): auth, user CRUD + roles, password management, DB schema, Docker. Phase 2
(Chores – Core): chore CRUD (name, description, emoji, tags, assignees, points), basic
recurrence (one-time/daily/weekly/monthly/yearly + start date), mark complete + undo, and a
per-user points log. Phase 3 (Chores – Advanced): custom recurrence (`Custom` repeat type with
Interval / DaysOfWeek / DaysOfMonth / Frequency modes — day granularity, hourly deferred to
a later phase), six assignment strategies that rotate the current assignee on each new occurrence, and
three scheduling preferences for the next due date. Phase 4 (Dashboard): today / overdue /
upcoming chore views, per-user point totals, filtering by tag and assignee, and global search
(chores by name/description/tags). Phase 5 (History & Stats): completion log with filters,
per-user stats, completions-per-week bar chart. Phase 6 (Awards & Redemption): admin award CRUD
(name, description, emoji, point cost), member redemption that spends points and logs a
`Redemption`, admin fulfill (mark delivered) + cancel-with-refund of a pending redemption (a
redemption snapshots the award's name/emoji/cost so it survives award edits/deletion). Phase 7
(Skip & Reassign): skip a recurring chore's current occurrence (advances the schedule without
awarding points or rotating the assignee — logged as a points-less `ChoreCompletion` with `IsSkip`,
undoable like a completion; one-time chores can't be skipped — **admin only**), and one-off
reassignment of the current occurrence to another eligible assignee (open to any member). Phase 8
(Notifications): Web Push via self-hosted VAPID keys — a per-chore notification schedule
(`ChoreNotification` entries: reminder/due/follow-up × before/at-due/after offset × current/all
assignees), a minute-polling `BackgroundService` that fires due entries and prunes dead
subscriptions, "stop on completion" achieved by keying a `NotificationDelivery` dedup row to the
occurrence's `DueAt` (completing advances `DueAt`, so pending entries for the old occurrence are
never reached), and a minimal push-only service worker (`web/public/sw.js`) that receives pushes.
The completion-by-others opt-out from the original spec was dropped. Phase 9 (PWA: offline,
app shell, manifest/install, caching) is not started.

## Stack & layout

- **Backend:** ASP.NET Core (.NET 10) minimal APIs, EF Core. Solution file is `Turnly.slnx`.
- **Frontend:** React 19 + Vite + TypeScript, Tailwind CSS v4, TanStack Query, React Router.
- **Tests:** xUnit (unit + `WebApplicationFactory` integration).

```
src/Turnly.Core    Entities, EF DbContext, auth + business services. NO web dependencies —
                   all business logic lives here so it's unit-testable without a host.
src/Turnly.Api     ASP.NET Core host: endpoint groups, auth wiring, serves the built SPA.
tests/Turnly.Tests Unit/ and Integration/ test folders.
web/               React frontend (path alias `@` → `web/src`).
```

## Architecture reference (file map)

A navigation index so you don't have to re-scan the whole codebase. Patterns are consistent —
copy the nearest existing example. Paths are under `src/` / `web/src/` / `tests/Turnly.Tests/`.

**Backend (`Turnly.Core`)**
- `Entities/` — POCOs; convention is `Guid Id = Guid.NewGuid()` + `DateTimeOffset CreatedAt`,
  no base class. `User, RefreshToken, Chore, Tag, ChoreCompletion, ChoreAssignment, PointsLogEntry,
  Award, Redemption, ChoreNotification, PushSubscription, NotificationDelivery`. `ChoreNotification`
  is a chore's notification-schedule entry; `PushSubscription` is one Web Push device per user;
  `NotificationDelivery` is a `(ChoreNotificationId, OccurrenceDueAt)`-unique dedup marker that makes
  each entry fire once per occurrence (and is how notifications "stop on completion"). `ChoreAssignment`
  logs every assignment (initial + each rotation) — backs
  `LeastAssigned` and lets undo reverse a rotation via its `ChoreCompletionId` link. A skipped
  occurrence is a `ChoreCompletion` with `IsSkip = true` (zero points, no `PointsLogEntry`) —
  excluded from completion stats/counts but undoable on the same path. `Redemption`
  snapshots `AwardName`/`AwardEmoji`/`PointsSpent` (so it outlives the award; FK is `SetNull`) and
  `PointsLogEntry` carries both a `ChoreCompletionId` and a `RedemptionId` link so undo/cancel can
  reverse the matching deduction the same way.
- `Enums/` — `UserRole, RepeatType, PointsLogType, RedemptionStatus` + Phase 3's `CustomRecurrenceMode,
  RecurrenceUnit, FrequencyPeriod, AssignmentStrategy, SchedulingPreference` + Phase 8's
  `NotificationType, NotificationTiming, NotificationOffsetUnit, NotificationRecipients`; **stored as
  strings** (`HasConversion<string>`) and serialized as strings in JSON.
- `Data/TurnlyDbContext.cs` — DbSets + fluent config in `OnModelCreating`. Many-to-many via
  skip navs (`Chore.Assignees`, `Chore.Tags`). `Chore.Weekdays` (`List<DayOfWeek>`, custom
  DaysOfWeek mode) and `Chore.DaysOfMonth`/`Chore.Months` (`List<int>`) are stored via CSV
  `ValueConverter` + `ValueComparer` (`WeekdaysConverter` / `IntListConverter`).
- `Common/Result.cs` — `Result`/`Result<T>` + `Error(ErrorType, msg)` (Validation/NotFound/
  Conflict/Unauthorized/Forbidden). **Expected failures return Results, not exceptions.**
- `Common/Validators.cs` — shared static rules returning `Error?` (`Username`, `Password`,
  `ChoreName`, `Points`, …); membership/cross-field rules live in the service.
- `Dtos/Dtos.cs` — request/response records, each domain DTO has a static `FromEntity`.
  `ChoreDto.FromEntity(chore, lastCompletion?)` embeds the latest completion for undo.
- `Services/*Service.cs` — ctor-inject `TurnlyDbContext` (+ deps); methods return `Result`/
  `Result<T>`. `AuthService, UserService, SetupService, TagService, ChoreService, AwardService,
  RedemptionService, NotificationService`. Registered in `ServiceCollectionExtensions.AddTurnlyCore`
  (also `IPushSender → WebPushSender` singleton + `VapidOptions`). `RedemptionService`
  mirrors `ChoreService`'s points-award path: `RedeemAsync` writes a negative `PointsLogEntry` +
  decrements `User.Points`; `CancelAsync` reverses it like `UndoCompletionAsync`. `ChoreService.SkipAsync`
  mirrors `CompleteAsync` minus points/rotation (advances the schedule, writes an `IsSkip` completion);
  `ReassignAsync` sets `CurrentAssigneeId` + logs a `ChoreAssignment` (same as the edit path).
  Chore notifications ride the existing chore create/update path: `ChoreService.Apply` rebuilds
  `chore.Notifications` from the request (so `Query()` includes them). `NotificationService` owns
  `SubscribeAsync`/`UnsubscribeAsync` (upsert/delete `PushSubscription` by endpoint) and
  `ProcessDueAsync(now)` — the scan that fires due entries via `IPushSender`, records a
  `NotificationDelivery`, and prunes `Gone` subscriptions.
- `Recurrence/` — pure, unit-tested. `RecurrenceCalculator` works off a `RecurrenceRule` record
  (`FromChore`): `FirstOccurrence(rule, start)` + `NextDue(rule, pref, scheduledDue, completedAt,
  now)` (interval stepping, fixed-slot scanning, scheduling prefs) plus `PeriodStart/PeriodEnd`
  for frequency. `AssignmentPicker.Pick(...)` implements the six strategies (inject `Random`).
  **Frequency is NOT in `NextDue`** — it needs completion counts, so `ChoreService` handles its
  period rollover (and computes `FrequencyProgress` for the DTO) client-side.
- `Notifications/` — `NotificationPlanner.FireTime(entry, dueAt)` is pure + unit-tested (before/at/
  after offset math). `VapidOptions` (`IsConfigured`), `IPushSender`/`WebPushSender` (WebPush lib;
  maps 404/410 → `Gone` so the service prunes the subscription).
- ⚠️ **SQLite can't `ORDER BY` a `DateTimeOffset`** — order date fields client-side after
  `ToListAsync` (see `ChoreService.ListAsync`, `LatestCompletionsAsync`, `GetPointsLogAsync`).

**Backend (`Turnly.Api`)**
- `Program.cs` — `AddTurnlyCore`, `JsonStringEnumConverter`, JWT bearer (claims unmapped:
  `sub`/`role`), `"Admin"` authorization policy, then `app.Map*Endpoints()` + SPA fallback. Also
  registers `NotificationSchedulerService` (a `BackgroundService` that polls `ProcessDueAsync` every
  minute; idle until VAPID keys are configured).
- `Endpoints/*Endpoints.cs` — `MapGroup(...).RequireAuthorization()`; thin handlers:
  parse → call service → `result.Succeeded ? Results.Ok/... : result.Error!.ToProblem()`.
  Per-endpoint `.RequireAuthorization("Admin")` for admin-only ops (e.g. chore create/edit/
  delete, and chores/skip). Chores/complete + chores/reassign + completions/undo are open to any
  member; chores/skip is admin-only (skipping advances past the due date with no points).
  `AwardEndpoints` follows the
  same split: listing awards + redeeming (`POST /api/awards/{id}/redeem`) and `GET /api/redemptions`
  (own for members, all for admins) are member-open; award create/edit/delete and redemption
  fulfill/cancel are admin-only.
  `NotificationEndpoints` (`/api/notifications`): member-open `GET /vapid-key`, `POST /subscribe`
  (captures the User-Agent → friendly `PushSubscription.DeviceLabel`), `POST /unsubscribe`,
  `GET /devices` + `DELETE /devices/{id}` (a user's own push devices); admin-only `POST /test`
  (dev: immediate push to the caller's devices). Chore notification entries are nested in the chore
  create/update request, not a separate endpoint.
- `Endpoints/ApiResults.cs` — `Error.ToProblem()` (status mapping) and
  `principal.GetUserId()` (reads the `sub` claim).

**Frontend (`web/src`)**
- `App.tsx` — auth-gated routing; index redirects to `/chores`. `Layout.tsx` builds the
  sidebar from a `tabs` array (Chores for all, Users admin-only) + `navItemClass`.
- `pages/` — `UsersPage` and `ChoresPage` are the canonical CRUD-with-modal examples
  (lift modal state to the page, `useQuery`/`useMutation`, `invalidateQueries` after writes).
  `AwardsPage` (visible to all, admin-only CRUD controls) is the canonical "browse + member action +
  admin management on one page" example; after redeem/cancel it invalidates `['me']`/`['leaderboard']`/
  `['points-log', id]` so balances refresh. `awardsApi`/`redemptionsApi` live in `lib/api.ts`.
- `lib/api.ts` — `request<T>` (bearer + one-shot 401 refresh); per-resource objects
  (`usersApi`, `choresApi`, `tagsApi`, `notificationsApi`). `lib/types.ts` mirrors the backend DTOs.
- **Notifications (Phase 8):** `lib/push.ts` wraps the browser Push API
  (`enablePush`/`disablePush`/`isPushEnabled`, VAPID key decode); `web/public/sw.js` is a minimal
  push-only service worker (registered in `main.tsx`). `components/NotificationsEditor.tsx` is the
  per-chore schedule sub-form (modeled on `RecurrenceEditor`, embedded in the `ChoresPage` form);
  `SettingsPage`'s Notifications card has enable/disable-on-this-device, a list of the user's
  registered devices (label + "This device" marker + per-device remove), and an admin-only "Send
  test notification" button.
- `store/auth.ts` — Zustand; `useAuthStore(s => s.user)`, role via `user.role === 'Admin'`.
- `components/ui/` — `Button, Badge, Card(+Header/Title/Content), Modal(+Avatar), Field
  (Input/Label/Select), ColorPicker`. Semantic Tailwind tokens only (see theming section).

**Tests**
- `Unit/TestContext.cs` — in-memory SQLite (kept-open connection) + real services; construct
  new services here when added. Naming: `Method_scenario`. See `ChoreServiceTests`,
  `RecurrenceCalculatorTests`.
- `Integration/TurnlyApiFactory.cs` + `HttpHelpers.cs` (`SetupAdminAsync`, `LoginAsync`,
  `UseBearer`, `ReadAsync<T>`). Test classes are `IDisposable` with a fresh factory per class.

**EF migrations:** `dotnet-ef` is a global tool — if `dotnet ef` isn't found, prefix
`PATH="$PATH:$HOME/.dotnet/tools"`. SQLite migrations only (see gotcha below).

## Commands

```bash
dotnet build                              # build solution
dotnet test                               # all tests (currently 126, keep them green)
dotnet run --project src/Turnly.Api       # backend (dev) on http://localhost:5199
cd web && npm install && npm run dev      # frontend on :5173, proxies /api → :5199
cd web && npm run build                   # typechecks (tsc -b) + production build

# EF migrations (DbContext lives in Core; Api is the startup project):
dotnet ef migrations add <Name> --project src/Turnly.Core --startup-project src/Turnly.Api --output-dir Migrations

# Docker:
cp .env.example .env && docker compose up --build   # http://localhost:8080
```

## Conventions — follow these

- **Business logic goes in `Turnly.Core` services** (`AuthService`, `UserService`,
  `SetupService`), not in endpoints. Endpoints are thin: parse → call service → map result.
- **Result pattern, not exceptions, for expected failures.** Services return
  `Result` / `Result<T>` with an `Error(ErrorType, message)` (see `Common/Result.cs`).
  Endpoints map errors to HTTP via `error.ToProblem()` in `Endpoints/ApiResults.cs`
  (Validation→400, NotFound→404, Conflict→409, Unauthorized→401, Forbidden→403).
- **Shared validation** lives in `Common/Validators.cs` — reuse it across services.
- **DTOs** are in `Core/Dtos/Dtos.cs`; map entities with `UserDto.FromEntity`. Don't leak
  entities out of services.
- **Auth model:** access token (~15 min JWT) returned in the response body; the 6-month
  refresh token is set in an httpOnly `Secure` cookie (path `/api/auth`) and **rotated** on
  every refresh. Token logic is in `Core/Auth/TokenService.cs`; cookie I/O in
  `Api/Auth/RefreshCookieManager.cs`. JWT claims are unmapped — read user id from the `sub`
  claim (`principal.GetUserId()`), role from the `role` claim.
- **Enums serialize as strings** in JSON (configured in `Program.cs`). Frontend mirrors this
  (`UserRole = 'Admin' | 'Member'`).
- **Theming / colors:** the UI uses **semantic design tokens**, not hardcoded colors.
  Tokens are CSS variables defined once in `web/src/index.css` (`:root` = light, `.dark` =
  dark) and mapped into Tailwind via `@theme inline`, giving utilities like `bg-card`,
  `text-muted-foreground`, `border-border`, `bg-primary`, `text-destructive`, `text-success`.
  **To recolor the app, edit the variables in `index.css` — don't reintroduce `bg-slate-*` /
  `text-*-600` literals in components.** Alpha states use the `/NN` modifier (e.g.
  `hover:bg-primary/90`). Dark mode is class-based (`.dark` on `<html>`); the theme store is
  `web/src/lib/theme.ts` (system-aware, persisted to `localStorage`), toggled via
  `components/ThemeToggle.tsx`, and pre-applied by an inline script in `index.html` to avoid
  a flash. A new palette = another scope overriding the same variables.
- **Frontend:** `tsconfig.app.json` has `verbatimModuleSyntax` — use `import type` for
  type-only imports (e.g. `import type { FormEvent } from 'react'`), or `tsc` fails. Server
  state goes through TanStack Query (`queryKey: ['users']`, invalidate after mutations); auth
  state is in the Zustand store (`store/auth.ts`, access token in memory only). All API calls
  go through `lib/api.ts`, which auto-refreshes once on 401.

## Design language

Target aesthetic: **modern clean B2B SaaS dashboard** — unobtrusive, readable, low cognitive
load. New UI should match this; don't introduce one-off styles.

- **Layout — three-pane.** Fixed left **sidebar** for global nav (`md:` and up), a sticky white
  **top bar** (account menu now; search lives here too), and a centered **workspace**. Below
  `md` the sidebar collapses into the animated mobile **drawer** (mirrors the sidebar). See
  `components/Layout.tsx`.
- **Color — high-key cool-neutral.** Gray canvas (`--background`), pure-white cards
  (`--card`), white nav chrome (`--sidebar`). Accent is **violet-blue** (`--primary`
  `#5b4ee8` / dark `#7b6ef6`) used sparingly for primary actions, the logo, focus rings, and
  active state.
- **Active/selected = soft accent tint, never a solid block.** Use `bg-primary/10` + `text-primary`
  (see `navItemClass`). Hover uses neutral `bg-accent`.
- **Status pills** via the `Badge` component (`components/ui/Badge.tsx`): soft tinted background
  (`color-mix`, ~14%) + saturated same-hue text. Tones: `neutral | violet | red | blue | amber | green`.
- **Soft elevation, not flat.** White surfaces get diffuse shadows: `shadow-card` for cards,
  `shadow-pop` for popovers/drawer (tokens in `index.css`). Avoid hard/heavy shadows.
- **Rounded corners 8–12px** (`--radius-md/lg/xl`); pills and avatars stay fully round.
- **Typography:** Inter (self-hosted via `@fontsource-variable/inter`), **two weights** — 400
  body, 600 reserved for **headers and selected items only**. `font-medium` (500) only for
  micro-text legibility (badges, avatar initials). Don't bold body text, labels, or buttons.
- **Icons:** minimalist **outline/stroke** (Feather-style), uniform 2px stroke, `currentColor`.
- **Whitespace:** generous padding inside cards, clear separation between sections.
- **Avatars:** circular, initials on the user's `avatarColor`. Overlapping avatar stacks are the
  intended pattern for collaborator/assignee lists (Phase 2+).

## Gotchas

- **Postgres is wired but Phase 1 ships SQLite migrations only.** Switching
  `Database:Provider=postgres` requires generating a Postgres migration set first, or
  migrate-on-startup fails. SQLite is the default and the tested path.
- **`Jwt:Secret` must be set** (env var `Jwt__Secret`, or `.env` `JWT_SECRET`) and be ≥32
  bytes, or auth crashes. It's empty in `appsettings.json` by design; dev value is in
  `appsettings.Development.json`.
- **`COOKIE_SECURE`/`Auth:RefreshCookie:Secure`** must be `false` for plain-HTTP local
  testing (otherwise the refresh cookie is never stored/sent). Keep `true` in production.
- **Dev port quirk:** `dotnet run` honors `launchSettings.json` (port 5199 expected by the
  Vite proxy). When running the published DLL directly, set `ASPNETCORE_URLS` and run from a
  directory containing the `appsettings.json` (or pass config via env vars).
- **Tests use in-memory SQLite** with a kept-open connection; migrations are applied on
  startup. Integration tests get an isolated DB per test class.
- User deletion only blocks self / last-admin; the spec's reassign-chores + wipe-history
  behavior is **still a deferred extension point** in `UserService.DeleteAsync`. ⚠️ Now that
  chores exist, deleting a user who is a chore's `CurrentAssignee` or has any `ChoreCompletion`
  will fail at the DB level (FK `Restrict`) rather than returning a clean error — the
  reassign/wipe flow (a future phase) must land before user deletion is robust again.

## Verify changes

Run `dotnet test` for backend logic and `cd web && npm run build` for the frontend. For
end-to-end, run both dev servers (or `docker compose up --build`) and exercise the
setup → login → user-management flow.
