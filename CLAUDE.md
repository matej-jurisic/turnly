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
- **Rotation period**: `Chore.RotationPeriod` (`EveryOccurrence | Monthly`) decouples the handover from the occurrence. The pure `RotationBoundary.Crossed(period, closedDue, nextDue)` compares year+month in the due dates' **own stored offsets** (household-local, not UTC) and is stateless, so no occurrence counting. `ChoreService.CrossedRotationBoundary` gates all three rotation call sites: `CompleteAsync`, `AutoAdvanceAsync`, and `SkipAsync` (a skip rotates **only** across a calendar boundary, linked to the skip row so undo reverses it). `UnfreezeAsync` never rotates. `ApplyFields` normalizes it to `EveryOccurrence` where it can't apply (Independent, `KeepLastAssigned`, OneTime/Monthly/Yearly) and forces `RotateOnEachCompletion` off when it's set. ⚠️ The migration's column default is hand-edited to `"EveryOccurrence"` (EF emits `""`, which wouldn't parse back into the enum).
- **Auto-advance**: `ChoreAutoAdvanceService` polls `ChoreService.AutoAdvanceAsync(now)` every minute. Writes `ChoreCompletion.IsExpired` rows for unfilled occurrences, advances via `FromScheduledDate`, rotates. `OneTime` excluded.
- **Achievements**: catalog in `AchievementCatalog.cs`. `EvaluateForUserAsync` called inline by CompleteAsync/RedeemAsync/AdjustPointsAsync — no background worker. Earned badges are permanent (reversals don't revoke). `UnlockedAchievements` on ChoreDto/RedemptionDto surfaces new badges to the client.
- **Gacha**: catalog in `CosmeticCatalog.cs`. `EquippedFrameKey`/`EquippedThemeKey`/`AvatarEmoji`/`Dust`/`PullsSinceLegendary` on `User`. Equipping a Color cosmetic writes `User.AvatarColor = def.Value`; equipping an Emoji cosmetic writes `User.AvatarEmoji = def.Value` (a null key clears it, avatar falls back to initials) — both denormalized, thread via `UserDto.FromEntity`/`LeaderboardEntryDto`. The `Avatar` component (`ui/Modal.tsx`) renders `emoji` in place of initials wherever it renders `frame`. `color-indigo` (#6366f1) is the one `Default = true` cosmetic (free for everyone); Emoji has no default (initials are the fallback). Equipping lives in `CustomizationModal.tsx`, not the Rewards page. "Gacha" is an internal name only (API, services, query key): the UI calls it **Cosmetics**, a tab of `RewardsPage`. Shared refetch helper: `syncAppearanceFromServer` in `lib/appearance.ts`.
- **Notification controls**: every send path funnels through the pure `NotificationRouter.Resolve(category, preference, choreMuted, inQuietHours)` → `DeliveryChannel` (`PushAndInbox | InboxOnly | Off`). Rules compose by taking the **most restrictive** answer; suppression only travels one way (nothing promotes a notification back up). `Off` drops the inbox row too; a mute or quiet window only demotes push to inbox-only. `NotificationCategory.System` is non-configurable (`IsConfigurable`) so the test push can't be made to lie about the delivery path.
  - `UserNotificationPreference { UserId, Category, Channel }` is **sparse** - an absent row means the default, so existing users need no backfill and `UpdatePreferenceAsync` *deletes* the row when set back to `PushAndInbox`. `ListPreferencesAsync` returns the matrix complete (defaults filled in) so the client doesn't have to know them.
  - `ChoreMute { UserId, ChoreId, Until? }` is per-user: muting a shared chore only quiets it for the muter. An expired mute is simply not loaded rather than swept on a timer. Surfaces on `ChoreDto.IsMuted`/`MutedUntil`, personalized by `viewerId` the same way `DueAt` is on track chores.
  - `NotificationService.LoadRoutingAsync` builds the `RoutingContext` (preferences + mutes + quiet windows + local now) **once** per scan or fan-out. `NotifyUsersAsync` is the fan-out entry point - `ChoreService.OfferAsync`'s open-offer broadcast uses it so routing isn't reloaded per recipient. ⚠️ SQLite can't compare a `DateTimeOffset` in SQL, so mute-expiry filtering runs client-side (same gotcha as ordering).
  - Fresh start deliberately **keeps** preferences and mutes: they're settings, not activity.
- **Freeze**: `Chore.IsFrozen` blocks complete/skip/auto-advance; on unfreeze, recurring chores step forward via `FromScheduledDate` until `DueAt >= now`. `User.IsFrozen` excludes from rotation; freeze auto-reassigns owned chores via `ExecuteChoreReassignmentsForFreezeAsync`.
- **Reassignment**: `ChoreService.ReassignAsync` is **admin-only** and moves the chore immediately (logs a `ChoreAssignment` reassignment row + voids any live offer). Members hand chores over through offers instead.
- **Chore offers**: a member puts the occurrence they hold up for someone to take via `OfferAsync` (`POST /api/chores/{id}/offer`), recording a `ChoreOffer { ChoreId, FromUserId, ToUserId?, BountyPoints, ClaimedByUserId?, Status }` (`OfferStatus.Pending`). `ToUserId` **null = open** (fans out `NotificationService.NotifyUserAsync` to every other non-frozen assignee, first claim wins); set = targeted at one person, who may also decline. The chore does **not** move until `ClaimOfferAsync` (`POST .../offer/claim`) sets `CurrentAssigneeId` + logs the `ChoreAssignment`. Also `DeclineOfferAsync` (targeted only) and `WithdrawOfferAsync` (`DELETE .../offer`, offerer or admin).
  - **Bounty escrow**: `BountyPoints` is deducted from the offerer in `OfferAsync` (capped at their balance) and credited to the claimer in `ClaimOfferAsync`, both as `PointsLogType.Bounty` rows. Every non-claim ending refunds via `RefundOfferAsync`. `VoidOffersAsync(chore, status)` is the single cancel+refund path, called from: a newer offer superseding, admin reassign, freeze rotation (`ExecuteChoreReassignmentsForFreezeAsync`), `CompleteAsync`/`SkipAsync` (non-track branch), and `AutoAdvanceAsync`. **`AchievementService` filters out `PointsLogType.Bounty`** when summing lifetime points — bounties are zero-sum transfers, so counting them would let members farm milestones.
  - **Claim race**: `ClaimOfferAsync` wraps a compare-and-set `ExecuteUpdateAsync` on `Status == Pending` in a transaction; 0 rows → `Conflict`. ExecuteUpdate bypasses the change tracker, so it follows with `Entry(offer).ReloadAsync` (otherwise the tracked copy stays `Pending` and a shared-context caller reads stale state).
  - The live offer surfaces on `ChoreDto.OpenOffer` (loaded per chore in List/Get via `OpenOffersByChoreAsync`). Frontend: `ChoreMenu` gates Reassign on `isAdmin` and "Offer" on `currentAssignee === meId && !openOffer && !isFrozen`; `OfferModal` (bounty + optional recipient) and admin-only `ReassignModal`; `ChoreListItem`/`ChoreCompactItem` show a compact badge naming who the chore is waiting on plus the bounty (`offerBadgeText`/`offerSentence` in `lib/chore-format.ts` — badge text "To {name}" / "To you" / "Up for grabs", full sentence as the tooltip). Claim/decline/withdraw live in `ChoreDetailsModal` (where the notification deep-link `/chores?chore={id}` lands). **`OfferBoard`** is the "Up for grabs" band at the top of `ChoresPage`'s main column — the primary discovery surface, deliberately *not* in the `xl`-only `ChoreRail` so it reaches phones and the native app; renders nothing when nothing is claimable. Anything touching a bounty invalidates `['chores']` + `['me']` + `['leaderboard']`.
- **Capacitor (Android)**: `isNative()` in `lib/native.ts` is the single switch. Three web assumptions lifted on native: (1) API base URL uses stored origin from `@capacitor/preferences`; (2) refresh token returned in response body (`X-Turnly-Client: android`) and read from `X-Refresh-Token` header; (3) CORS allows `https://localhost`. FCM push via `FcmSender` (inert when unconfigured). The native WebView has no `serviceWorker`/`PushManager`, so `SettingsPage` renders a separate `NativeNotificationsCard` (FCM permission + enable/disable) instead of the Web Push `NotificationsCard`; `lib/native-push.ts` exposes `nativePushPermission()` and an idempotent `registerNativePush` (listeners attach once, safe to re-call from settings). Web path is byte-for-byte unchanged.
- **Fresh start**: `ResetService.FreshStartAsync` deletes ChoreCompletions, ChoreAssignments, ChoreOffers, PointsLog, Redemptions, UserAchievements, UserCosmetics, UserNotifications then zeroes user state. Chore schedules untouched.
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
  Key entities: `User, Chore, ChoreCompletion, ChoreAssignment, ChoreAssigneeTrack,
  ChoreOffer, ChoreMute, PointsLogEntry, Award, Redemption, ChoreNotification, PushSubscription,
  FcmDevice, NotificationDelivery, UserNotification, UserNotificationPreference, AppSetting,
  UserAchievement, UserCosmetic`.
  `ChoreCompletion` has `IsSkip`/`IsExpired` flags. `ChoreAssignment` logs every assignment (backs
  `LeastAssigned` + lets undo reverse rotation via `ChoreCompletionId` link). `NotificationDelivery`
  is `(ChoreNotificationId, OccurrenceDueAt, UserId)`-unique dedup — how "stop on completion" works.
- `Enums/` — `UserRole, RepeatType, PointsLogType, RedemptionStatus, CustomRecurrenceMode,
  AssignmentStrategy, RotationPeriod, SchedulingPreference, OfferStatus, NotificationType, NotificationTiming,
  NotificationOffsetUnit, NotificationRecipients, NotificationCategory, DeliveryChannel`;
  **stored as strings** (`HasConversion<string>`).
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
  `RotationBoundary.Crossed` for the rotation period.
  `StreakCalculator.CurrentStreak` for on-time streaks.
- `Notifications/` — `NotificationPlanner.FireTime` (pure, before/at/after offset math).
  `NotificationRouter.Resolve`/`IsConfigurable`/`CategoryOf` (pure, the single suppression gate).
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
- `pages/` — `ChoresPage`/`UsersPage` = canonical CRUD-with-modal. `RewardsPage` (`/rewards`) is a thin tab shell (header + points badge + tabs, active tab in `?tab=`) over `components/rewards/AwardsTab.tsx` (= "browse + member action + admin CRUD" pattern) and `components/rewards/CosmeticsTab.tsx` (pulls, pity, odds, collection + craft). The old `/awards` and `/gacha` routes redirect to it.
- `components/chores/OfferBoard.tsx` — "Up for grabs" band at the top of `ChoresPage`'s main column (every breakpoint, every view). Filters the loaded chore list to offers the viewer can claim and claims them inline. Returns null when empty.
- `components/chores/ChoreRail.tsx` — right-hand info rail on `ChoresPage`, desktop `xl`+ only (sticky, hidden below `xl`; shown in every view including calendar). Two widgets: "Your snapshot" (fresh `me` points/weekly + your overdue/due-today counts derived from the loaded chore list) and weekly leaderboard (top 5, rows open `UserDetailsModal`). Pure presentation over existing queries; no backend changes. `ChoresPage` runs full-width at `xl` (via `Layout`'s route check) so the rail anchors to the screen edge and the chore column centers in the space left of it (`max-w-2xl`); below `xl` it falls back to the standard centered `max-w-5xl` column with the rail hidden.
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
