# CLAUDE.md

Guidance for working in the Turnly repository. See `specs.md` for the full product spec
and feature list, and `README.md` for user-facing setup.

> **Keep the docs in sync.** Whenever you add a feature or change how something works, check that
> `CLAUDE.md`, `specs.md`, and `README.md` still describe the behavior accurately, and update any that
> have drifted. `CLAUDE.md` is the implementation/architecture reference, `specs.md` is the product
> spec + feature list, and `README.md` is the user-facing overview — a behavior change usually touches more
> than one. Treat this as part of the change, not a follow-up.

## What this is

Self-hosted family chore-management web app (PWA). The product is feature-complete; `specs.md` is
the canonical feature list and `README.md` the user-facing overview. This file is the
implementation/architecture reference for the shipped feature set, described below.

**Foundation:** auth, user CRUD + roles, password management, DB schema, Docker. **Chores (core):**
chore CRUD (name, description, emoji, tags, assignees, points), basic
recurrence (one-time/daily/weekly/monthly/yearly + start date), mark complete + undo, and a
per-user points log. **Chores (advanced):** custom recurrence (`Custom` repeat type with
Interval / DaysOfWeek / DaysOfMonth modes — day granularity, hourly is out of scope;
DaysOfWeek can be further restricted by `Chore.WeeksOfMonth` to specific monthly
occurrences — 1–4 for the nth weekday, -1 for the last; empty = every week), six assignment strategies that rotate the current assignee on each new occurrence, and
four scheduling preferences for the next due date (`FromScheduledDate`, `FromCompletionDate`,
`ToFirstNextRepeat`, and `SmartScheduling` — the last holds the planned cadence but never schedules
sooner than one interval after the actual completion, i.e. `max(FromScheduledDate, FromCompletionDate)`,
with an optional `Chore.GraceMinutes` window that resets the cadence from completion when a chore is
done more than the grace early; offered only for interval-style repeats, computed in
`RecurrenceCalculator.NextDue`). **Per-occurrence completion count:** an orthogonal
`Chore.CompletionsRequired` (default 1, offered only on the non-custom repeat types
OneTime/Daily/Weekly/Monthly/Yearly). An occurrence closes after N completions/skips (counted by
`ChoreCompletion.OccurrenceDueAt == Chore.DueAt`), then advances via the normal
`RecurrenceCalculator.NextDue` path and rotates — so the scheduling preferences apply uniformly.
(There is no "N times per period" `Frequency`/`FrequencyPeriod` or `PeriodStart`/`PeriodEnd`
calendar-window machinery — that was superseded by this count.) **Dashboard:** today / overdue /
upcoming chore views, per-user point totals, filtering by tag and assignee, and global search
(chores by name/description/tags). **History & stats:** completion log with filters,
per-user stats, completions-per-week bar chart. **Awards & redemption:** admin award CRUD
(name, description, emoji, point cost), member redemption that spends points and logs a
`Redemption`, admin fulfill (mark delivered) + cancel-with-refund of a pending redemption (a
redemption snapshots the award's name/emoji/cost so it survives award edits/deletion).
**Skip & reassign:** skip a recurring chore's current occurrence (advances the schedule without
awarding points or rotating the assignee — logged as a points-less `ChoreCompletion` with `IsSkip`,
undoable like a completion; one-time chores can't be skipped — **admin only**), and one-off
reassignment of the current occurrence to another eligible assignee (open to any member).
**Notifications:** Web Push via self-hosted VAPID keys — a per-chore notification schedule
(`ChoreNotification` entries: reminder/due/follow-up × before/at-due/after offset × current/all
assignees), a minute-polling `BackgroundService` that fires due entries and prunes dead
subscriptions, "stop on completion" achieved by keying a `NotificationDelivery` dedup row to the
occurrence's `DueAt` (completing advances `DueAt`, so pending entries for the old occurrence are
never reached), and a service worker (`web/public/sw.js`) that receives pushes (showing them with
the app's own `icon-192.png`) and deep-links the chore on click. **Notification inbox:** an
in-app inbox — every fired entry (and each test send) also writes a per-recipient
`UserNotification` row (independent of whether a push device was reachable), surfaced by a bell
dropdown in the top bar; both push-click and inbox-click open the chore via `/chores?chore=<id>`.
Basic PWA install (`manifest.webmanifest` + icons + the push service worker) ships alongside
notifications; full offline (offline read, completion queue, app shell, asset caching) is **out of
scope**. **UX polish:** touch swipe
actions on chore cards (swipe right → complete, left → details, via `components/chores/SwipeRow.tsx`;
mouse unaffected), completion **confetti** (`canvas-confetti` wrapped in `web/src/lib/confetti.ts`,
reduced-motion aware, fired from `CompleteModal`), admin **complete-on-behalf** of any user
(`CompleteChoreRequest.CompletedByUserId`; service requires admin when crediting someone else;
`CompleteModal` shows a "Completed by" picker for admins), and admin **deletion of activity entries**
(completions/skips) from a chore's details — points-only reversal, **no reschedule**
(`ChoreService.DeleteActivityAsync`, admin-only `DELETE /api/completions/{id}/activity`, distinct
from the member undo at `DELETE /api/completions/{id}`; surfaced as an Activity list in
`ChoreDetailsModal`). The chores page is split into
`web/src/components/chores/*` + shared helpers in `web/src/lib/chore-format.ts`.

**Per-assignee independent tracks ("Everyone independently").** A seventh
`AssignmentStrategy.Independent` makes a *shared* chore give **each assignee their own schedule and
per-person quota** instead of one rotating assignee — so "everyone does the dishes once a week"
(all quotas 1) or an uneven "Alice 3× / Bob 2×" is **one chore**, and a slow person never blocks a
fast one. Model: a new child entity `ChoreAssigneeTrack { ChoreId, UserId, DueAt?, CompletionsRequired }`
(one row per assignee, holding their own due date + quota); `Chore.AssigneeTracks` nav. In track mode
`CurrentAssigneeId` is null, the scalar `CompletionsRequired`/`RotateOnEachCompletion` are unused, and
there is **no rotation**. Two tricks keep the blast radius small: (1) `chore.DueAt` is kept as a
**mirror = the earliest track `DueAt`** (`ChoreService.EarliestTrackDue`) so every existing
`DueAt != null` check (listing order, "nothing scheduled" guards, the notification chore-load filter)
keeps working; (2) `ChoreService.ToDto(..., viewerId)` **personalises** the top-level `DueAt`/
`OccurrenceProgress` to the viewing user's own track (else earliest), so the frontend's existing
bucketing/ordering/complete-disabled logic gives per-user placement with no bucket rewrite —
`viewerId` is threaded from `ChoreEndpoints` (`principal.GetUserId()`) into list/get.
`Complete`/`Skip`/`Undo`/`Reschedule` branch to a per-track path (`AdvanceTrackAsync` gates on the
per-person quota, never rotates); `SkipChoreRequest`/`RescheduleChoreRequest` gained an optional
`UserId` to target one assignee's track (skip is surfaced per-track in `ChoreDetailsModal`);
`ReassignAsync` is rejected in track mode. Notifications fan out **per track** (see
`NotificationDelivery.UserId` below). Recurring-only in v1 — `OneTime`-shared, "all assignees"
notification recipients in track mode, and history migration on strategy switch are out of scope.

**Scheduling, points & UX extensions.** A batch of smaller features:
- **Multiple times of day** (`Chore.TimesOfDay`, `List<TimeOnly>` via CSV converter) — a day-resolution
  chore (Daily, or custom DaysOfWeek/DaysOfMonth) can list several fixed times, each a distinct
  occurrence; `DueTime` mirrors the earliest. The validator restricts it to day-resolution modes;
  `RecurrenceCalculator` multiplies each qualifying day by the times list when scanning occurrences.
- **Auto-advance incomplete** (`Chore.AutoAdvanceIncomplete` + `CompletionWindowMinutes`) — a separate
  minute-polling `BackgroundService` (`Turnly.Api/ChoreAutoAdvanceService`, runs unconditionally, no
  VAPID dependency) calls `ChoreService.AutoAdvanceAsync(now)`: once an occurrence's
  window (`DueAt + CompletionWindowMinutes`) closes still short of `CompletionsRequired`, it writes
  `ChoreCompletion.IsExpired` rows for the missing slots (no points, no actor, **not undoable**),
  advances via `FromScheduledDate`, and rotates so the misser isn't kept on duty. Branches for rotating
  and Independent (per-track) chores; `OneTime` excluded. Custom repeats are supported (they always
  close on a single completion, so one `IsExpired` row is written before advancing).
- **On-time streaks** (`Recurrence/StreakCalculator.CurrentStreak`, pure + unit-tested) — counts the
  most recent consecutive occurrences completed on/before their `OccurrenceDueAt`; a late, skipped, or
  expired occurrence resets it. Surfaced as `ChoreDto.CurrentStreak` (personalised to the viewer's own
  track in Independent mode) and per-track `ChoreAssigneeTrackDto.Streak`.
- **Chore copying** (`ChoreService.CopyAsync`, `POST /api/chores/{id}/copy` with `CopyChoreRequest`,
  admin-only; `web/.../chores/CopyChoreModal.tsx`) — clones a chore's definition through the normal
  create path under a new name, so the copy's schedule starts fresh.
- **Manual point adjustment** (`UserService.AdjustPointsAsync`, `POST /api/users/{id}/points` with
  `AdjustPointsRequest(Delta, Description)`, admin-only) — writes a `PointsLogType.Adjustment` entry and
  moves the balance, like the completion/redemption paths. Surfaced on `UsersPage`.
- **Quiet hours** (`User.QuietHoursStart/End`, set self-service via `UpdateProfileRequest`;
  `Notifications/QuietHours.Contains`, wrap-aware) — during the window `NotificationService.SendEntryAsync`
  suppresses the push but still writes the inbox row. Evaluated against the **family timezone**.
- **Family timezone** (`AppSetting` entity + `AppSettingsService`, `Notifications/TimeZoneResolver`;
  `GET /api/settings` member-open, `PUT` admin-only via `SettingsEndpoints`) — an instance-wide IANA/
  Windows zone id (empty = server local) the notification scan uses to turn `now` into local wall-clock
  time for quiet-hours evaluation. Set on `SettingsPage` (admin).
- **Next-goal progress** (`NextGoalCard` on `AwardsPage`, frontend-only) — a progress bar toward the
  cheapest unaffordable award.
- **Chore views** (`ChoreView = 'list' | 'compact' | 'calendar'`; `chores/ChoreCalendar.tsx`,
  `chores/ChoreCompactItem.tsx`, a view switcher in `ChoreFilters`, persisted to `localStorage`).
- **Inbox delete/clear** (`NotificationService.DeleteInboxAsync`/`ClearInboxAsync`) round out the inbox.

**Achievements.** Cosmetic, collectible badges (no points). The catalog is **defined in
code** (`Core/Achievements/AchievementCatalog.cs`: `AchievementDefinition` records with a stable `Key`,
presentation, `Category`, `Threshold`, and a pure `Progress` selector over an `AchievementStats` record)
— completion milestones, on-time-streak milestones, lifetime-points milestones, redemption count, and
variety (distinct chores / distinct tags). Adding one is just another catalog entry, no migration.
`UserAchievement { UserId, AchievementKey, EarnedAt }` (unique on `(UserId, AchievementKey)`) is the
per-user "unlocked" marker; earned achievements are **permanent** (except for an explicit admin revoke —
`AchievementService.RevokeAsync(userId, key)` deletes the `UserAchievement` row; the badge can later be
re-earned if its threshold is met again). `AchievementService` has a
**side-effect-free read** (`ListForUserAsync` → `AchievementDto[]` with live progress + earned status,
progress clamped to threshold) and an **inline grant** (`EvaluateForUserAsync(userId, now)`) that grants
newly-met achievements and **returns them as `AchievementDto[]`** (there is no inbox/push notification for an
unlock — see below). Granting is wired into the three metric-moving
mutations — `ChoreService.CompleteAsync`, `RedemptionService.RedeemAsync`, `UserService.AdjustPointsAsync`
each call it after they save — so a badge unlocks at the moment it's earned (no background worker). The
completion and redeem responses surface the freshly-unlocked badges back to the client via a
`UnlockedAchievements` init-property on `ChoreDto`/`RedemptionDto` (completion only when the earner is the
acting user — an admin completing on someone else's behalf gets nothing), which the frontend turns into a
**celebration popup** (`celebrateAchievements` → the `useAchievementCelebrationStore` queue rendered by
`components/AchievementCelebration.tsx`, mounted at the app root with confetti; mirrors the `lib/toast`
pattern). `AdjustPointsAsync` still grants but doesn't surface a popup (the earner isn't the actor). Those
three services therefore take `AchievementService` as a ctor dependency (it depends only on the DbContext,
so no cycle). Because the model is permanent and the read is side-effect-free, **reversals
need no handling**: undo / redemption-cancel / negative adjustment lower the *live* metrics (so unearned
progress correctly drops) but never revoke an earned badge, and re-crossing a threshold is a no-op (the
"already earned" guard skips it — no re-grant). `ComputeStatsAsync` aggregates the metrics
(lifetime points = sum of *positive* points-log deltas); streak milestones reuse
`StreakCalculator.CurrentStreak(completions, userId?)` — the optional `userId` overload attributes the
streak to the person who *closed* each occurrence (stops when someone else takes a turn), preserving the
chore-/track-wide behavior when null. Endpoints: `GET /api/achievements` (caller's own; admins may pass
`?userId=` to view anyone's) + admin-only `DELETE /api/achievements/{userId}/{key}` (revoke). Frontend:
`pages/AchievementsPage.tsx` (grouped-by-category grid, earned vs. locked-with-progress-bar; for admins a
user picker + a per-badge Revoke action), `achievementsApi` in `lib/api.ts`, `/achievements` route + nav tab.

**Freeze (per-chore & per-user).** Two admin-only "pause" toggles:
- **Per-chore freeze** (`Chore.IsFrozen bool`) — blocks `CompleteAsync`/`SkipAsync` (returns Validation error),
  skips the chore in `AutoAdvanceAsync` and `NotificationService.ProcessDueAsync`, and places it in a
  "Paused" bucket visible to all users. On unfreeze (`ChoreService.UnfreezeAsync`), recurring chores whose
  `DueAt` is in the past are stepped forward via `FromScheduledDate` until `DueAt >= now`; Independent chores
  apply this per-track and recalculate the mirror `DueAt`; OneTime chores are left at their current `DueAt`
  (appear overdue but remain completable). `RescheduleAsync` is allowed while frozen. Endpoints: admin-only
  `POST /api/chores/{id}/freeze` + `POST /api/chores/{id}/unfreeze`. Frontend: "Paused" bucket in
  `ChoresPage`, grey "Paused" badge on cards, complete button disabled, "Pause / Unpause" in `ChoreMenu`,
  banner in `ChoreDetailsModal`.
- **Per-user freeze** (`User.IsFrozen bool`) — excludes the user from rotation in `RotateAssigneeAsync`,
  skips their Independent tracks in `AutoAdvanceAsync` and `NotificationService.ProcessDueAsync` (also
  filters them from `AllAssignees` recipients), and at freeze time auto-reassigns any rotating chore they
  currently own (`UserService.FreezeAsync` calls `ChoreService.ExecuteChoreReassignmentsForFreezeAsync` —
  picks a new assignee using the same `AssignmentPicker` with the user excluded; chores with no other
  eligible assignee get `CurrentAssigneeId = null`). `UserService.GetFreezePreviewAsync` computes and
  returns the predicted reassignments as `UserFreezePreviewDto` before any change. On unfreeze
  (`UserService.UnfreezeAsync`), stale Independent tracks (DueAt in the past) are stepped forward per-track
  via `ChoreService.StepForwardIfOverdue` (same loop as chore unfreeze). `UserService` now depends on
  `ChoreService` (no cycle). Endpoints: admin-only `GET /api/users/{id}/freeze-preview`,
  `POST /api/users/{id}/freeze`, `POST /api/users/{id}/unfreeze`. Frontend: "Away" amber badge on frozen
  users in `UsersPage`, Freeze/Unfreeze action buttons, `FreezeUserModal.tsx` shows the preview before
  confirming. Both `UserDto.IsFrozen` and `ChoreDto.IsFrozen` are `init` properties (not positional).

**Gacha (cosmetics).** A points-funded gacha for **cosmetic** rewards (no real money — pulls spend the
same points as awards). v1 ships three slots: **avatar frames** (visible on every avatar), **app theme
palettes** (recolor only the owner's view), and **avatar colors** (the avatar fill, visible to everyone).
Modeled closely on Achievements: the catalog is **code-defined**
(`Core/Cosmetics/CosmeticCatalog.cs`: `CosmeticDefinition(Key, Name, Description, Slot, Rarity, Value?,
Default)` records + per-rarity `RarityConfig` for odds/dust + the `PullCost`/`TenPullCost`/`PityThreshold`
constants); the **visual** lives in the frontend keyed by the same stable `Key` (the contract), except for
**Color** cosmetics whose `Value` is the hex (the backend resolves it). `Default = true` marks a cosmetic
everyone owns for free and that pulls never roll — the **default purple `color-indigo` (#6366f1)** is the
one default, so every user always has a free color and new users start there. Adding a cosmetic is one
catalog entry (+ a frontend visual entry for frames/themes), no migration.
**Avatar color is no longer set on the profile or the admin user form** — `UpdateUserRequest` and
`UpdateProfileRequest` dropped `AvatarColor` (and `UserService.UpdateAsync`/`UpdateProfileAsync` no longer
touch it; the Setup/admin-create paths just default it), so color is changed only by equipping a Color
cosmetic. Equipping a Color writes `User.AvatarColor = def.Value` (which then threads everywhere via
`UserDto.FromEntity`, same as frames). The old `ColorPicker` component + `AVATAR_COLORS` list were removed. `UserCosmetic { UserId, CosmeticKey, Count }`
(unique on `(UserId, CosmeticKey)`) is the per-user ownership marker (a near-clone of `UserAchievement`).
Four columns on `User` hold the rest: `EquippedFrameKey`/`EquippedThemeKey` (one equipped per slot), `Dust`
(dupe currency), `PullsSinceLegendary` (pity counter). **Putting the equipped frame on `User` is the trick:
every avatar is projected via `UserDto.FromEntity`, so the frame threads to all assignee/completed-by/etc.
render sites for free** (only `LeaderboardEntryDto` needs the field added explicitly, as it isn't a
`UserDto`). `GachaService` (ctor-injects `TurnlyDbContext` + an optional `Random` so pulls are seed-testable)
owns `GetStateAsync` (read-only catalog projection), `PullAsync` (validates points, deducts via a negative
`PointsLogEntry { Type = PointsLogType.GachaPull }` mirroring `RedemptionService.RedeemAsync`, then per roll
picks a rarity by odds — forced Legendary at the pity threshold — and either unlocks a `UserCosmetic` or
awards dust on a dupe; resets pity on a Legendary; saves once), `CraftAsync` (spend dust → grant a specific
unowned cosmetic) and `EquipAsync` (set/clear the slot column, validating ownership + slot). Endpoints:
member-open `GET /api/gacha` + `POST /api/gacha/pull|craft|equip` (`GachaEndpoints`). Frontend: a visual
registry (`web/src/lib/cosmetics.ts`: frame key -> CSS class, rarity -> Badge tone), global CSS in `index.css`
(the `.gx-frame`/`.frame-*`/`.gx-spin` ring classes + `[data-palette="theme-*"]` token-override scopes that
compose with `.dark`), the `Avatar` component's new `frame?` prop (`components/ui/Modal.tsx`), palette
application (`web/src/lib/palette.ts` sets `data-palette` from the user's `equippedThemeKey`, cached in
`localStorage` and pre-applied by the inline script in `index.html`; `App.tsx` re-applies on the auth user
changing), and `pages/GachaPage.tsx` (`/gacha` route + nav tab) — pull x1/x10 with a confetti reveal popup,
a pity bar, the odds disclosure, and a collection grid grouped by slot + rarity (view owned / craft locked).
`gachaApi` in `lib/api.ts`. **Equipping is not on the gacha page** — it lives in
`components/CustomizationModal.tsx`, opened from the account menu (`UserMenu`, which replaced its dark/light
toggle with a "Customization" entry). The modal unifies appearance into one picker: base **Light/Dark**
modes + owned **theme palettes** (mutually exclusive — choosing a palette supersedes light/dark since the
palette overrides all tokens; choosing Light/Dark clears the equipped palette), plus owned **avatar frames**
(+ None). It reuses the `['gacha']` query for the owned lists and the shared
`web/src/lib/appearance.ts` `syncAppearanceFromServer(queryClient)` helper (refetch `me` -> `setUser` ->
`applyPalette` -> invalidate `gacha`/`me`/`leaderboard`), which the gacha page's pull/craft also call. The
pre-auth `ThemeToggle` (Login/Setup screens) is unchanged. (The earlier dev-only `/gacha-showcase` page was
removed once this shipped; the brainstorm doc `gacha.md` is kept.)

**Fresh start (admin reset).** An admin-only "clean slate" that keeps every chore (and its schedule)
but wipes the accumulated state, e.g. at the start of a new month. `ResetService.FreshStartAsync`
(ctor-injects only `TurnlyDbContext`) runs one transaction of `ExecuteDeleteAsync` over
`ChoreCompletions` (completions/skips/expired activity), `ChoreAssignments` (assignment history — the
`*Id` back-links are plain columns, not FK constraints, so order is free), `PointsLog`, `Redemptions`,
`UserAchievements`, `UserCosmetics`, and `UserNotifications` (inbox), then one `ExecuteUpdateAsync` that
zeroes every user's `Points`/`Dust`/`PullsSinceLegendary`, clears `EquippedFrameKey`/`EquippedThemeKey`,
and resets `AvatarColor` to the one `Default` cosmetic's hex. Chores keep their `CurrentAssigneeId`/
`DueAt`/tracks untouched (rotation re-reads assignment rows fresh, so an empty table just resets
`LeastAssigned` fairness); users, tags, awards, push devices, and notification schedules are kept.
Endpoint: admin-only `POST /api/settings/fresh-start` (`SettingsEndpoints`). Frontend: a "Danger zone"
card on `SettingsPage` (`DangerZoneCard`, admin-only) with a destructive `confirm` then
`settingsApi.freshStart()`; on success it calls `syncAppearanceFromServer` (the current user's
points/cosmetics reset) and invalidates the history/balance surfaces.

**Standalone Android app (Capacitor).** The React build is packaged into a native Android APK
(`web/android/`, generated by Capacitor; `web/capacitor.config.ts` — `appId net.turnly.app`,
`androidScheme: https` so the WebView origin is `https://localhost`). The whole design is "detect
native at runtime and branch, leave the web path byte-for-byte unchanged": `web/src/lib/native.ts`
`isNative()` (`Capacitor.isNativePlatform()`) is the single switch. Three same-origin assumptions are
lifted only on native: (1) **API base URL** — `lib/api.ts` `apiBase()` returns `/api` on web but
`${savedOrigin}/api` on native, where the origin is the user-chosen self-hosted server persisted via
`@capacitor/preferences` (`lib/server-config.ts`), entered on a first-run **server picker**
(`pages/ServerSetupPage.tsx`, gated in `App.tsx` when native + no saved server; a "Change server"
card in `SettingsPage` clears it); (2) **auth transport** — native can't rely on the cross-origin
httpOnly refresh cookie, so the backend returns the rotated refresh token in the auth response body
when the request carries `X-Turnly-Client: android` (`ApiResults.IsNativeClient`/`WriteAuth`) and
reads it from the `X-Refresh-Token` header on refresh/logout (`AuthEndpoints` + `ApiResults.ReadRefreshHeader`);
the app stores it in device storage (`lib/native-auth.ts`) and hydrates server+token at startup
before the bootstrap refresh; (3) **CORS** — the host opts the app origin in via `Cors:Origins`
(`Program.cs`, the `app` policy; add `https://localhost`). The web build is inert for all of this
(`isNative()` is false, cookie path unchanged). `AuthResponse.RefreshToken` is null for web.
Build flow: `npm run android:sync` / `android:apk` (needs JDK 17 + Android SDK; release signing is
env-driven via `TURNLY_KEYSTORE*` in `android/app/build.gradle`).
**Native FCM push** runs alongside Web Push (which still serves the PWA): a sibling `FcmDevice`
entity (token per install, unique, pruned on `Unregistered`) registered via
`POST /api/notifications/fcm-subscribe`/`fcm-unsubscribe` (`@capacitor/push-notifications` →
`web/src/lib/native-push.ts`, auto-registers after sign-in and deep-links taps like `sw.js`); a
second sender `IFcmSender`/`FcmSender` (Firebase Admin SDK, lazy-init from `FcmOptions` —
`Fcm:CredentialsPath`/`CredentialsJson`; **inert when unconfigured**, like VAPID), fanned out
per-recipient in `NotificationService.SendEntryAsync`/`SendTestAsync` next to the Web Push loop
(quiet hours suppress both, inbox row always written). The Gradle `google-services` plugin is
file-guarded, so the APK builds without Firebase; `google-services.json` (Android) + a server
service-account credential turn it on.

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
  no base class. `User, RefreshToken, Chore, Tag, ChoreCompletion, ChoreAssignment, ChoreAssigneeTrack,
  PointsLogEntry, Award, Redemption, ChoreNotification, PushSubscription, NotificationDelivery,
  UserNotification, AppSetting`. `ChoreAssigneeTrack` is one assignee's own `DueAt` + quota for an
  `AssignmentStrategy.Independent` chore (one row per assignee; absent for rotating chores, where the
  single `Chore.DueAt`/`CurrentAssigneeId` apply). `AppSetting` is a key/value config row (currently
  just the family timezone). `User.QuietHoursStart/End` is the per-user push-suppression window.
  `ChoreCompletion.IsExpired` marks an occurrence the auto-advance service closed unfilled (no points,
  no actor, not undoable) — alongside `IsSkip`; `Chore.TimesOfDay` (multiple due times/day) and
  `Chore.AutoAdvanceIncomplete`/`CompletionWindowMinutes` back those features.
  `ChoreNotification` is a chore's notification-schedule entry; `PushSubscription` is one Web Push
  device per user; `NotificationDelivery` is a `(ChoreNotificationId, OccurrenceDueAt, UserId)`-unique
  dedup marker that makes each entry fire once per occurrence — `UserId` is the track owner in
  Independent mode (one row per assignee) and null for rotating chores (one row per occurrence); it's
  how notifications "stop on completion"; `UserNotification` is a per-user in-app inbox row (Title/Body/ChoreId/ReadAt) written
  when a notification fires or a test is sent — `ChoreId` FK is `SetNull` so the record outlives the
  chore. `ChoreAssignment`
  logs every assignment (initial + each rotation) — backs
  `LeastAssigned` and lets undo reverse a rotation via its `ChoreCompletionId` link. A skipped
  occurrence is a `ChoreCompletion` with `IsSkip = true` (zero points, no `PointsLogEntry`) —
  excluded from completion stats/counts but undoable on the same path. `Redemption`
  snapshots `AwardName`/`AwardEmoji`/`PointsSpent` (so it outlives the award; FK is `SetNull`) and
  `PointsLogEntry` carries both a `ChoreCompletionId` and a `RedemptionId` link so undo/cancel can
  reverse the matching deduction the same way.
- `Enums/` — `UserRole, RepeatType, PointsLogType` (`Completion`/`Redemption`/`Adjustment`),
  `RedemptionStatus`, `CustomRecurrenceMode,
  RecurrenceUnit, AssignmentStrategy` (which also has the `Independent` value),
  `SchedulingPreference`,
  `NotificationType, NotificationTiming, NotificationOffsetUnit, NotificationRecipients`; **stored as
  strings** (`HasConversion<string>`) and serialized as strings in JSON.
- `Data/TurnlyDbContext.cs` — DbSets + fluent config in `OnModelCreating`. Many-to-many via
  skip navs (`Chore.Assignees`, `Chore.Tags`); `Chore.AssigneeTracks`/`Notifications` are cascade
  child collections. `Chore.Weekdays` (`List<DayOfWeek>`, custom
  DaysOfWeek mode), `Chore.WeeksOfMonth` (`List<int>`, optional nth-occurrence restriction for
  DaysOfWeek), `Chore.DaysOfMonth`/`Chore.Months` (`List<int>`) and `Chore.TimesOfDay`
  (`List<TimeOnly>`) are stored via CSV `ValueConverter` + `ValueComparer` (`WeekdaysConverter` /
  `IntListConverter` / `TimesOfDayConverter`). `AppSetting` is keyed by its `Key`.
- `Common/Result.cs` — `Result`/`Result<T>` + `Error(ErrorType, msg)` (Validation/NotFound/
  Conflict/Unauthorized/Forbidden). **Expected failures return Results, not exceptions.**
- `Common/Validators.cs` — shared static rules returning `Error?` (`Username`, `Password`,
  `ChoreName`, `Points`, …); membership/cross-field rules live in the service.
- `Dtos/Dtos.cs` — request/response records, each domain DTO has a static `FromEntity`.
  `ChoreDto.FromEntity(chore, lastCompletion?)` embeds the latest completion for undo; `ChoreDto.Tracks`
  (`ChoreAssigneeTrackDto[]`) carries the per-assignee schedules for `Independent` chores, and the
  chore-input records carry `TrackInput[] Tracks` (per-assignee quotas).
- `Services/*Service.cs` — ctor-inject `TurnlyDbContext` (+ deps); methods return `Result`/
  `Result<T>`. `AuthService, UserService, SetupService, TagService, ChoreService, AwardService,
  RedemptionService, NotificationService, AppSettingsService, AchievementService, GachaService,
  ResetService`. Registered in
  `ServiceCollectionExtensions.AddTurnlyCore` (also `IPushSender → WebPushSender` singleton +
  `VapidOptions`). `AppSettingsService` reads/writes the family timezone (`AppSetting`);
  `ResetService.FreshStartAsync` is the admin "fresh start" wipe (see the Fresh start section). `UserService`
  also has `AdjustPointsAsync` (admin manual point grant/deduction → `PointsLogType.Adjustment`).
  `ChoreService` also has `CopyAsync` (clone via the create path under a new name) and
  `AutoAdvanceAsync(now)` (expire-and-advance unfilled occurrences, driven by `ChoreAutoAdvanceService`).
  `RedemptionService`
  mirrors `ChoreService`'s points-award path: `RedeemAsync` writes a negative `PointsLogEntry` +
  decrements `User.Points`; `CancelAsync` reverses it like `UndoCompletionAsync` (pending only), and
  `DeleteAsync` (admin) removes a redemption of **any** status and refunds its points the same way (so a
  fulfilled redemption can also be reversed). `ChoreService.SkipAsync`
  mirrors `CompleteAsync` minus points/rotation (advances the schedule, writes an `IsSkip` completion);
  `ReassignAsync` sets `CurrentAssigneeId` + logs a `ChoreAssignment` (same as the edit path).
  For `Independent` chores these branch instead to a per-track path: `Apply` nulls the current
  assignee + zeroes the scalar count, `SyncTracks` reconciles `chore.AssigneeTracks` from the request's
  `Tracks` (preserving each track's advanced `DueAt` unless the schedule changed; new assignees join at
  the current cadence; inserts via the DbSet per the [[turnly-ef-child-collection-rebuild]] gotcha),
  and `Complete`/`Skip`/`Undo` move only the relevant track's `DueAt` (recomputing the `chore.DueAt`
  mirror) with no rotation; `ReassignAsync` is rejected.
  Chore notifications ride the existing chore create/update path: `ChoreService.Apply` rebuilds
  `chore.Notifications` from the request (so `Query()` includes them). `NotificationService` owns
  `SubscribeAsync`/`UnsubscribeAsync` (upsert/delete `PushSubscription` by endpoint) and
  `ProcessDueAsync(now)` — the scan that fires due entries via `IPushSender`, writes a
  `UserNotification` inbox row per recipient, records a `NotificationDelivery`, and prunes `Gone`
  subscriptions. For an `Independent` chore the scan iterates `chore.AssigneeTracks` and fires each
  entry **per track**, off that track's own `DueAt`, to the track owner (dedup keyed by the track's
  `UserId`), so "stop on completion" works per person — plus `ListInboxAsync`/`MarkInboxReadAsync` for
  the in-app inbox.
- `Recurrence/` — pure, unit-tested. `RecurrenceCalculator` works off a `RecurrenceRule` record
  (`FromChore`): `FirstOccurrence(rule, start)` + `NextDue(rule, pref, scheduledDue, completedAt,
  now)` (interval stepping, fixed-slot scanning, scheduling prefs).
  `AssignmentPicker.Pick(...)` implements the six **rotating** strategies (inject `Random`); the
  seventh, `Independent`, has no rotation and is handled entirely in `ChoreService` (per-assignee
  tracks), not here.
  **The per-occurrence completion count is NOT in `NextDue`** — it needs DB completion counts, so
  `ChoreService.AdvanceScheduleAsync` gates the advance: an occurrence only closes (and then calls
  `NextDue` + rotates) once `CompletionsRequired` completions/skips share the current `DueAt`;
  earlier ones leave `DueAt`/assignee untouched. `ToDto` computes `OccurrenceProgress` the same way.
  `Independent` chores use the parallel `AdvanceTrackAsync`, gated the same way but on each track's
  own per-person quota and `DueAt`. `StreakCalculator.CurrentStreak(completions)` is the pure on-time
  streak counter (resets on a late/skipped/expired occurrence); `ToDto` feeds it the right completion
  set (whole chore, or one track's rows in Independent mode).
- `Notifications/` — `NotificationPlanner.FireTime(entry, dueAt)` is pure + unit-tested (before/at/
  after offset math). `VapidOptions` (`IsConfigured`), `IPushSender`/`WebPushSender` (WebPush lib;
  maps 404/410 → `Gone` so the service prunes the subscription). `QuietHours.Contains(start, end, now)`
  (pure, wrap-aware) gates per-user push suppression; `TimeZoneResolver` turns the configured family
  zone id (IANA or Windows) into a `TimeZoneInfo` (falls back to server-local) so the scan can evaluate
  quiet hours in local wall-clock time.
- ⚠️ **SQLite can't `ORDER BY` a `DateTimeOffset`** — order date fields client-side after
  `ToListAsync` (see `ChoreService.ListAsync`, `LatestCompletionsAsync`, `GetPointsLogAsync`).

**Backend (`Turnly.Api`)**
- `Program.cs` — `AddTurnlyCore`, `JsonStringEnumConverter`, JWT bearer (claims unmapped:
  `sub`/`role`), `"Admin"` authorization policy, then `app.Map*Endpoints()` + SPA fallback. Also
  registers two `BackgroundService`s: `NotificationSchedulerService` (polls `ProcessDueAsync` every
  minute; idle until VAPID keys are configured) and `ChoreAutoAdvanceService` (polls
  `ChoreService.AutoAdvanceAsync` every minute; runs unconditionally, no VAPID dependency).
- `Endpoints/*Endpoints.cs` — `MapGroup(...).RequireAuthorization()`; thin handlers:
  parse → call service → `result.Succeeded ? Results.Ok/... : result.Error!.ToProblem()`.
  Per-endpoint `.RequireAuthorization("Admin")` for admin-only ops (e.g. chore create/edit/
  delete, and chores/skip). Chores/complete + chores/reassign + completions/undo are open to any
  member; chores/skip is admin-only (skipping advances past the due date with no points). The
  chore list/get handlers pass `principal.GetUserId()` as the viewer id so track-mode chores are
  personalised to the caller; `skip`/`reschedule` take an optional `UserId` to target one assignee's
  track. Admin-only `POST /api/chores/{id}/copy` (`CopyChoreRequest`) duplicates a chore, and admin-only
  `POST /api/users/{id}/points` (`AdjustPointsRequest`) grants/deducts points.
  `SettingsEndpoints` (`/api/settings`): member-open `GET` (family + server timezone), admin-only `PUT`,
  and admin-only `POST /fresh-start` (the `ResetService` wipe). `AwardEndpoints` follows the
  same split: listing awards + redeeming (`POST /api/awards/{id}/redeem`) and `GET /api/redemptions`
  (own for members, all for admins) are member-open; award create/edit/delete and redemption
  fulfill/cancel/delete (`DELETE /api/redemptions/{id}`, refund + remove any status) are admin-only.
  `NotificationEndpoints` (`/api/notifications`): member-open `GET /vapid-key`, `POST /subscribe`
  (captures the User-Agent → friendly `PushSubscription.DeviceLabel`), `POST /unsubscribe`,
  `GET /devices` + `DELETE /devices/{id}` (a user's own push devices), `GET /inbox` +
  `POST /inbox/read` + `DELETE /inbox/{id}` + `DELETE /inbox` (the in-app inbox); admin-only `POST /test`
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
- **Independent chores (track mode):** `ChoreFormModal`'s "Everyone (independent)" strategy swaps the
  current-assignee picker + "Times" field for a **per-assignee quota editor**; `ChoreListItem` and
  `ChoreDetailsModal` render the per-person roster (avatars with a done-check / overdue ring) instead
  of the single current→next assignee, the details modal exposing an admin per-track Skip; `ChoreMenu`
  hides Reassign; `CompleteModal` limits the admin "Completed by" picker to the chore's assignees.
  Helpers (`isIndependent`, `dueStatus`, `trackIsDone`, `trackStatusText`) live in `lib/chore-format.ts`.
- **Scheduling/UX frontend bits:** `ChoreFormModal` also exposes the multiple-times-of-day editor and the
  auto-advance toggle + window; `chores/CopyChoreModal.tsx` is the copy-under-new-name dialog (from
  `ChoreMenu`). `ChoreFilters` carries the **view switcher** (`ChoreView = 'list' | 'compact' | 'calendar'`,
  persisted to `localStorage`), rendered by `chores/ChoreCompactItem.tsx` and `chores/ChoreCalendar.tsx`.
  `AwardsPage`'s `NextGoalCard` shows progress to the cheapest unaffordable award. `UsersPage` has the
  admin point-adjust action. `SettingsPage` holds the user **quiet hours** editor and the admin **family
  timezone** setting (`settingsApi` in `lib/api.ts`). Streaks come through on `ChoreDto.currentStreak` /
  per-track `streak`.
- **Notifications:** `lib/push.ts` wraps the browser Push API
  (`enablePush`/`disablePush`/`isPushEnabled`, VAPID key decode); `web/public/sw.js` is the service
  worker (push + `notificationclick` opens `/chores?chore=<id>`, shows the app's `icon-192.png` +
  a no-op `fetch` for installability), registered in `main.tsx`.
  `web/public/manifest.webmanifest` + `icon-192/512.png` make the app installable (basic PWA install
  only — full offline/app-shell/caching is out of scope for v1); linked from `index.html`.
  `components/NotificationsBell.tsx` is the top-bar inbox dropdown (in `Layout`, polls `['inbox']`,
  unread badge, marks read on close, click opens the chore via `Layout`'s details modal);
  `ChoresPage` reads `?chore=<id>` to open the same modal from a push deep-link.
  `components/NotificationsEditor.tsx` is the
  per-chore schedule sub-form (modeled on `RecurrenceEditor`, embedded in the `ChoresPage` form;
  hides the recipients selector for `Independent` chores, where reminders always go to each track
  owner);
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
dotnet test                               # all tests (currently 298, keep them green)
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
- **No em dashes in client-facing text.** Never use em dashes (`—`) in any user-facing frontend
  text (JSX copy, labels, placeholders, button/aria text, toast/confirm messages, page titles,
  etc.). Use a regular hyphen, comma, colon, or reword instead. This applies only to text the user
  sees; code comments are exempt.
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
  intended pattern for collaborator/assignee lists.

## Gotchas

- **Postgres is wired but the app ships SQLite migrations only.** Switching
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
  reassign/wipe flow (a future extension) must land before user deletion is robust again.

## Verify changes

Run `dotnet test` for backend logic and `cd web && npm run build` for the frontend. For
end-to-end, run both dev servers (or `docker compose up --build`) and exercise the
setup → login → user-management flow.
