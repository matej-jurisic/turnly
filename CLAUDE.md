# CLAUDE.md

Guidance for working in the Turnly repository. See `specs.md` for the full product spec
and feature list, and `README.md` for user-facing setup.

> **Keep the docs in sync.** Whenever you add a feature or change how something works, check that
> `CLAUDE.md`, `specs.md`, and `README.md` still describe the behavior accurately. `CLAUDE.md` is the
> implementation/architecture reference, `specs.md` the product spec, `README.md` the user-facing overview.

## What this is

Self-hosted family chore-management PWA. See `specs.md` for the feature list. This file is the
implementation/architecture reference.

Key non-obvious implementation decisions:

- **Independent tracks** (`AssignmentStrategy.Independent`): each assignee has a `ChoreAssigneeTrack { ChoreId, UserId, DueAt, CompletionsRequired }`. `chore.DueAt` is a **mirror = earliest track DueAt** so all existing `DueAt != null` checks keep working. `ToDto(..., viewerId)` personalizes `DueAt`/`OccurrenceProgress` to the viewer's own track. Complete/Skip/Undo branch per-track; `ReassignAsync` rejected. Notifications fan out per track (`NotificationDelivery.UserId` = track owner).
- **Per-occurrence count**: `CompletionsRequired` gates the advance in `ChoreService.AdvanceScheduleAsync` (needs DB counts — deliberately **not** in `RecurrenceCalculator.NextDue`). `Independent` uses `AdvanceTrackAsync` with per-track quota.
- **Auto-advance**: `ChoreAutoAdvanceService` polls `ChoreService.AutoAdvanceAsync(now)` every minute. Writes `ChoreCompletion.IsExpired` rows for unfilled occurrences, advances via `FromScheduledDate`, rotates. `OneTime` excluded.
- **Achievements**: catalog in `AchievementCatalog.cs`. `EvaluateForUserAsync` called inline by CompleteAsync/RedeemAsync/AdjustPointsAsync — no background worker. Earned badges are permanent (reversals don't revoke). `UnlockedAchievements` on ChoreDto/RedemptionDto surfaces new badges to the client.
- **Gacha**: catalog in `CosmeticCatalog.cs`. `EquippedFrameKey`/`EquippedThemeKey`/`Dust`/`PullsSinceLegendary` on `User`. Equipping a Color cosmetic writes `User.AvatarColor = def.Value` (threads via `UserDto.FromEntity`). `color-indigo` (#6366f1) is the one `Default = true` cosmetic (free for everyone). Equipping lives in `CustomizationModal.tsx`, not GachaPage. Shared refetch helper: `syncAppearanceFromServer` in `lib/appearance.ts`.
- **Freeze**: `Chore.IsFrozen` blocks complete/skip/auto-advance; on unfreeze, recurring chores step forward via `FromScheduledDate` until `DueAt >= now`. `User.IsFrozen` excludes from rotation; freeze auto-reassigns owned chores via `ExecuteChoreReassignmentsForFreezeAsync`.
- **Capacitor (Android)**: `isNative()` in `lib/native.ts` is the single switch. Three web assumptions lifted on native: (1) API base URL uses stored origin from `@capacitor/preferences`; (2) refresh token returned in response body (`X-Turnly-Client: android`) and read from `X-Refresh-Token` header; (3) CORS allows `https://localhost`. FCM push via `FcmSender` (inert when unconfigured). Web path is byte-for-byte unchanged.
- **Fresh start**: `ResetService.FreshStartAsync` deletes ChoreCompletions, ChoreAssignments, PointsLog, Redemptions, UserAchievements, UserCosmetics, UserNotifications then zeroes user state. Chore schedules untouched.
- **SmartScheduling**: `max(FromScheduledDate, FromCompletionDate)`, with optional `Chore.GraceMinutes` that resets the cadence from completion when done early beyond the grace window.

## Stack & layout

- **Backend:** ASP.NET Core (.NET 10) minimal APIs, EF Core. Solution: `Turnly.slnx`.
- **Frontend:** React 19 + Vite + TypeScript, Tailwind CSS v4, TanStack Query, React Router.
- **Tests:** xUnit (unit + `WebApplicationFactory` integration).

```
src/Turnly.Core    Entities, EF DbContext, business services. No web dependencies.
src/Turnly.Api     ASP.NET Core host: endpoints, auth wiring, serves the SPA.
tests/Turnly.Tests Unit/ and Integration/ folders.
web/               React frontend (path alias `@` → `web/src`).
```

## Architecture reference (file map)

Paths under `src/` / `web/src/` / `tests/Turnly.Tests/`. Copy the nearest existing example.

**Backend (`Turnly.Core`)**
- `Entities/` — POCOs; `Guid Id = Guid.NewGuid()` + `DateTimeOffset CreatedAt`, no base class.
  Key entities: `User, Chore, ChoreCompletion, ChoreAssignment, ChoreAssigneeTrack, PointsLogEntry,
  Award, Redemption, ChoreNotification, PushSubscription, FcmDevice, NotificationDelivery,
  UserNotification, AppSetting, UserAchievement, UserCosmetic`.
  `ChoreCompletion` has `IsSkip`/`IsExpired` flags. `ChoreAssignment` logs every assignment (backs
  `LeastAssigned` + lets undo reverse rotation via `ChoreCompletionId` link). `NotificationDelivery`
  is `(ChoreNotificationId, OccurrenceDueAt, UserId)`-unique dedup — how "stop on completion" works.
- `Enums/` — `UserRole, RepeatType, PointsLogType, RedemptionStatus, CustomRecurrenceMode,
  AssignmentStrategy, SchedulingPreference, NotificationType, NotificationTiming,
  NotificationOffsetUnit, NotificationRecipients`; **stored as strings** (`HasConversion<string>`).
- `Data/TurnlyDbContext.cs` — DbSets + `OnModelCreating`. Many-to-many via skip navs (`Chore.Assignees`,
  `Chore.Tags`). List fields (`Weekdays`, `WeeksOfMonth`, `DaysOfMonth`, `Months`, `TimesOfDay`)
  stored via CSV `ValueConverter` + `ValueComparer`.
- `Common/Result.cs` — `Result`/`Result<T>` + `Error(ErrorType, msg)`. **Expected failures = Results, not exceptions.**
- `Common/Validators.cs` — shared static validation rules.
- `Dtos/Dtos.cs` — request/response records with `FromEntity` static factory.
- `Services/*Service.cs` — ctor-inject `TurnlyDbContext`; return `Result`/`Result<T>`.
  `AchievementService` is a ctor dep of `ChoreService`, `RedemptionService`, `UserService`.
  `UserService` depends on `ChoreService` (for freeze reassign). Registered in `AddTurnlyCore`.
- `Recurrence/RecurrenceCalculator.cs` — pure; `FirstOccurrence` + `NextDue` (interval stepping,
  fixed-slot scanning, scheduling prefs). `AssignmentPicker.Pick` for rotating strategies.
  `StreakCalculator.CurrentStreak` for on-time streaks.
- `Notifications/` — `NotificationPlanner.FireTime` (pure, before/at/after offset math).
  `VapidOptions` (`IsConfigured`), `IPushSender`/`WebPushSender`, `IFcmSender`/`FcmSender`.
  `QuietHours.Contains` (pure, wrap-aware). `TimeZoneResolver` (IANA/Windows → `TimeZoneInfo`).
- ⚠️ **SQLite can't `ORDER BY` a `DateTimeOffset`** — sort client-side after `ToListAsync`.

**Backend (`Turnly.Api`)**
- `Program.cs` — registers core services, two background services (`NotificationSchedulerService`
  polls every minute, idle without VAPID; `ChoreAutoAdvanceService` polls every minute, always runs),
  JWT + `"Admin"` policy, SPA fallback.
- `Endpoints/*Endpoints.cs` — thin: parse → service → `result.ToProblem()`. Admin ops use
  `.RequireAuthorization("Admin")`. Chore list/get pass `principal.GetUserId()` as viewer id.
- `Endpoints/ApiResults.cs` — `Error.ToProblem()` + `principal.GetUserId()` (reads `sub` claim).

**Frontend (`web/src`)**
- `App.tsx` — auth-gated routing; index → `/chores`. Native: gated behind `ServerSetupPage` if no saved origin.
- `pages/` — `ChoresPage`/`UsersPage` = canonical CRUD-with-modal. `AwardsPage` = "browse + member action + admin CRUD" pattern.
- `lib/api.ts` — `request<T>` (bearer + one-shot 401 refresh). `lib/types.ts` mirrors backend DTOs.
- `lib/chore-format.ts` — shared chore helpers (`isIndependent`, `dueStatus`, `trackIsDone`, etc.).
- `lib/cosmetics.ts` — frame key → CSS class, rarity → Badge tone.
- `lib/appearance.ts` — `syncAppearanceFromServer(queryClient)`: refetch `me` → `setUser` → `applyPalette` → invalidate `gacha`/`me`/`leaderboard`.
- `lib/native.ts` `isNative()`, `lib/server-config.ts` saved origin, `lib/native-auth.ts` device token.
- `lib/palette.ts` — sets `data-palette` on `<html>`; pre-applied by inline script in `index.html`.
- `store/auth.ts` — Zustand; `user.role === 'Admin'` for role checks.
- `components/ui/` — `Button, Badge, Card(+Header/Title/Content), Modal(+Avatar), Field`.
- `components/CustomizationModal.tsx` — appearance picker (Light/Dark + theme palettes + avatar frames).
- `web/public/sw.js` — Web Push service worker. `web/public/manifest.webmanifest` — PWA manifest.

**Tests**
- `Unit/TestContext.cs` — in-memory SQLite + real services. Naming: `Method_scenario`.
- `Integration/TurnlyApiFactory.cs` + `HttpHelpers.cs` — `SetupAdminAsync`, `LoginAsync`,
  `UseBearer`, `ReadAsync<T>`. Fresh factory per class (`IDisposable`).

**EF migrations:** prefix `PATH="$PATH:$HOME/.dotnet/tools"` if `dotnet ef` not found. SQLite only.

## Commands

```bash
dotnet build
dotnet test                               # all tests (keep them green)
dotnet run --project src/Turnly.Api       # backend on :5199
cd web && npm install && npm run dev      # frontend on :5173, proxies /api → :5199
cd web && npm run build                   # tsc -b + production build

# EF migration:
dotnet ef migrations add <Name> --project src/Turnly.Core --startup-project src/Turnly.Api --output-dir Migrations

# Docker:
cp .env.example .env && docker compose up --build   # http://localhost:8080
```

## Conventions — follow these

- **Business logic in `Turnly.Core` services.** Endpoints are thin: parse → service → map result.
- **Result pattern, not exceptions.** `Error(ErrorType, msg)` → `error.ToProblem()`
  (Validation→400, NotFound→404, Conflict→409, Unauthorized→401, Forbidden→403).
- **No em dashes in client-facing text.** Use a hyphen, comma, or colon. Code comments are exempt.
- **Shared validation** in `Common/Validators.cs`. Cross-field rules live in the service.
- **DTOs** in `Core/Dtos/Dtos.cs`; map via `FromEntity`. Don't leak entities.
- **Auth model:** JWT access token in response body (~15 min); 6-month refresh token in httpOnly
  `Secure` cookie (path `/api/auth`), rotated on every refresh. Read user id from `sub` claim
  (`principal.GetUserId()`), role from `role` claim. Logic in `TokenService.cs`; cookie I/O in
  `RefreshCookieManager.cs`.
- **Enums as strings** (`Program.cs` + frontend `UserRole = 'Admin' | 'Member'`).
- **Theming:** semantic CSS variables in `index.css` → Tailwind via `@theme inline`. Never hardcode
  `bg-slate-*` / `text-*-600`. Dark mode = `.dark` on `<html>`. Palettes override the same variables.
- **Frontend:** `verbatimModuleSyntax` — use `import type` for type-only imports. TanStack Query for
  server state (`queryKey: ['users']`, invalidate after writes); Zustand for auth (access token in memory).

## Design language

Target: **modern clean B2B SaaS dashboard** — unobtrusive, low cognitive load.

- **Three-pane layout.** Fixed left sidebar (`md:`+), sticky top bar, centered workspace. Below `md` → animated drawer. See `Layout.tsx`.
- **Color:** gray canvas (`--background`), white cards/nav. Accent: violet-blue (`--primary` `#5b4ee8` / dark `#7b6ef6`), sparingly.
- **Active = soft tint.** `bg-primary/10` + `text-primary`. Hover: `bg-accent`.
- **Status pills** via `Badge`: soft `color-mix` bg + saturated text. Tones: `neutral | violet | red | blue | amber | green`.
- **Elevation:** `shadow-card` for cards, `shadow-pop` for popovers. No heavy shadows.
- **Corners:** 8–12px (`--radius-md/lg/xl`). Avatars fully round.
- **Typography:** Inter, 400 body / 600 headers+selected only. `font-medium` for micro-text only. Don't bold body, labels, or buttons.
- **Icons:** outline/stroke, 2px, `currentColor`.
- **Avatars:** circular initials on `avatarColor`. Overlapping stacks for assignee lists.

## Gotchas

- **SQLite migrations only.** `Database:Provider=postgres` needs a Postgres migration set first.
- **`Jwt:Secret` ≥32 bytes** (`Jwt__Secret` env / `JWT_SECRET` in `.env`); empty in `appsettings.json` by design.
- **`COOKIE_SECURE`** must be `false` for plain-HTTP local dev; `true` in production.
- **Dev port:** `dotnet run` uses `launchSettings.json` (port 5199). Published DLL: set `ASPNETCORE_URLS`.
- **Tests:** in-memory SQLite, kept-open connection, migrations on startup. Isolated DB per integration test class.
- **User deletion** not robust when user is `CurrentAssignee` or has `ChoreCompletion` rows — FK `Restrict` fails at the DB level. Reassign/wipe flow is a deferred extension.

## Verify changes

`dotnet test` for backend; `cd web && npm run build` for frontend. End-to-end: both dev servers or `docker compose up --build`.
