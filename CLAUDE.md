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
- **Manual assignment** (`AssignmentStrategy.Manual`): `RotateAssigneeAsync` short-circuits to `CurrentAssigneeId = null` (no `ChoreAssignment` row; undo restores from `ChoreCompletion.PreviousAssigneeId` as usual), so every rotation call site and the rotation period apply unchanged. An admin places it with the existing `ReassignAsync`. `ValidateAsync` lets a Manual chore be saved with a null current assignee; `GetChoreReassignmentsForFreezeAsync` routes Manual chores to `unassignable`; `PredictNextAssignee` returns null. Never reaches `AssignmentPicker`. No migration (enum stored as string). Frontend: `ChoreFormModal` offers "Unassigned" in the current-assignee picker, `ChoreMenu` labels the admin action "Assign" when nobody holds the chore (and allows it with a single assignee), `ChoreListItem`/`ChoreCompactItem` show "Unassigned".
- **Fixed pairs** (`Chore.Pairs`, `List<ChorePair(First, Second)>`): two assignees who do the chore together. Stored as a **JSON array of two-id arrays** on the chore (same pattern as `TimeLabels`; empty string = none, so the migration's `""` default is correct as emitted), not a table. A user is in at most one pair per chore (`Validators.Pairs`), which is what makes the partner a pure lookup.
  - **`CurrentAssigneeId` stays a single user** (either member) and the partner is **derived**: `Chore.PartnerOf(userId)` / `Chore.CurrentHolderIds()`, both of which need `Assignees` loaded. A **frozen partner returns null**, so every caller treats them as away for free. Nothing about who holds the chore is stored twice.
  - **Rotation**: `ChoreService.PickNext` collapses the pool, the assignment/completion history, the completer and the current holder onto each unit's representative (`UnitRepresentative`: the pair member who comes first in the eligible pool, or the user themselves) before calling `AssignmentPicker`, which never learns pairs exist. Shared by live rotation, the `NextAssignee` preview and freeze reassignment, so all three agree. A single unit predicts no next assignee.
  - **Completion is one row, two credits**: `CompleteAsync` sets `ChoreCompletion.PartnerUserId` (the completer's partner, whoever tapped it) and writes **one `PointsLogEntry` per person** linked to the same completion. One row keeps every per-occurrence count (`CompletionsRequired`, auto-advance, progress) untouched. `ReversePointsAsync` is the shared undo/delete path and loops over all of a completion's log entries. Per-user readers match either column: `AchievementService.ComputeStatsAsync`, `UserService.GetStatsAsync`, `StreakCalculator`, the history user filter, undo permission.
  - Auto-expired rows also set `PartnerUserId` (partner of the responsible assignee) so the miss is shared. ⚠️ That means **`PartnerUserId` is not "a completion happened"**: `GetStatsAsync` splits expired rows out before counting.
  - Other call sites: `ReassignAsync` no-ops onto the holder's own partner; `OfferAsync` rejects paired chores and `UpdateAsync` voids a live offer when pairs are set; `GetChoreReassignmentsForFreezeAsync` hands a frozen holder's chore to their partner (before the Manual check); `NotificationService` sends "current assignee" entries to `CurrentHolderIds()` and names both in `BuildMessage`. `ApplyFields` clears pairs on Independent chores.
  - API shape is `pairs: [[id, id]]` on both request and `ChoreDto`, plus `CurrentPartner`/`NextPartner` on the DTO and `Partner` on `ChoreCompletionDto`/`ChoreHistoryEntryDto`. Frontend: `partnerOf`/`pairLabel`/`holdsChore` in `lib/chore-format.ts`; `ChoreFormModal` has the pair editor (only pairs whose two people are still assignees are submitted); display reads the server's `currentPartner`/`nextPartner` rather than recomputing. `ChoreListItem` shows a pair as two non-overlapping avatars; the "A & B" text is hidden below `sm` for paired chores, and the avatar group is a button opening a small names dialog (current pair + next).
- **Backdated completion**: `CompleteChoreRequest.CompletedAt` (optional, any member) stamps `ChoreCompletion.CompletedAt` with when the chore was really done instead of `now`. `CompleteAsync` keeps the two apart: the backdated instant feeds only the completion row and the `completedAt` argument of `RecurrenceCalculator.NextDue` (`AdvanceScheduleAsync`/`AdvanceTrackAsync` read it off the entry), while `now` still drives `ToFirstNextRepeat`'s "skip what's already missed", the `ChoreAssignment` timestamp and `UserAchievement.EarnedAt`. Rejected when in the future (one minute of client-clock slack, then clamped to `now`) or earlier than the chore's latest logged entry (track chores: that user's own), which keeps history ordered and `LastCompletion` on the newest row. **Exception - filling a missed slot**: if the backdated instant falls in an occurrence that has since auto-expired (the earliest `IsExpired` row stamped at or after it, with no entry of a *different* occurrence in between; track chores: that user's own expired slots), `CompleteAsync` converts that row via `FillExpired` (shared with `UpdateActivityAsync`) and returns early: no new row, no schedule advance, no rotation, no offer voiding. The response carries the transient `ChoreDto.FilledExpiredDueAt` so `CompleteModal` can toast that the current occurrence is still open. Unlike the admin correction this is open to any member, same as completing. Stored as UTC like every other row. No migration. Frontend: `CompleteModal` hides it behind "Did it earlier? Set the time" (date + `TimeField`, sent via `toLocalDueInstant`).
- **Activity correction**: `ChoreService.UpdateActivityAsync` (`PUT /api/completions/{id}/activity`, admin-only, `UpdateActivityRequest(CompletedAt, CompletedByUserId?)`) edits a logged row in place. On a completion it only restamps `CompletedAt`. On an `IsExpired` row it turns the miss into a real completion: clears `IsExpired`, sets `CompletedByUserId` (required; on an Independent chore it is forced to the track owner snapshotted in `PreviousAssigneeId`, which is then nulled to match normal track completions) and `PartnerUserId` (partner of whoever is credited), awards the chore's **current** points through `AwardPoints` (the per-person points-log loop shared with `CompleteAsync`), and re-evaluates achievements. Like `DeleteActivityAsync` it is history cleanup: `chore.DueAt`, the current assignee and the rotation the expiry triggered are untouched. Skips are rejected. The new time can't be in the future, and can't move the row past an entry of a **different occurrence** (track chores: that user's own), so `LastCompletion` stays on the newest occurrence; moving within its own occurrence is free. Frontend: `ActivityList` in `ChoreDetailsModal` shows a check button on expired entries and an edit button on late completions (`isLateCompletion`: `at > occurrenceDueAt`; the server accepts any completion), opening the inline `ActivityEditForm`.
- **Per-occurrence count**: `CompletionsRequired` gates the advance in `ChoreService.AdvanceScheduleAsync` (needs DB counts — deliberately **not** in `RecurrenceCalculator.NextDue`). `Independent` uses `AdvanceTrackAsync` with per-track quota.
- **Rotation period**: `Chore.RotationPeriod` (`EveryOccurrence | Daily | Weekly | Monthly | Quarterly | Yearly`) decouples the handover from the occurrence. The pure `RotationBoundary.Crossed(period, closedDue, nextDue)` compares the calendar period (date, Monday-start week, year+month, year+quarter, year) in the due dates' **own stored offsets** (household-local, not UTC) and is stateless, so no occurrence counting. Adding a period needs no migration (enum stored as string). `ChoreService.CrossedRotationBoundary` gates all three rotation call sites: `CompleteAsync`, `AutoAdvanceAsync`, and `SkipAsync` (a skip rotates **only** across a calendar boundary, linked to the skip row so undo reverses it). `UnfreezeAsync` never rotates. `ApplyFields` normalizes it to `EveryOccurrence` where it can't apply (Independent, `KeepLastAssigned`, or a repeat type that can't come due more than once per period - the pure `RotationBoundary.AppliesTo(period, repeatType)`, mirrored on the client by the `repeats` list in `ROTATION_PERIOD_OPTIONS` / `rotationPeriodOptions` in `lib/chore-format.ts`) and forces `RotateOnEachCompletion` off when it's set. ⚠️ The migration's column default is hand-edited to `"EveryOccurrence"` (EF emits `""`, which wouldn't parse back into the enum).
- **Slot labels**: `Chore.TimeLabels` (`Dictionary<TimeOnly, string>`) names individual `TimesOfDay` slots ("Morning"/"Evening"). Sparse, display-only, never reaches `RecurrenceRule`. Stored as a **JSON object** keyed `"HH:mm"` (not the CSV converter the other lists use - labels are free text), empty string = none, so the migration's `""` default is correct as emitted. `ApplyFields` prunes labels whose time isn't in `TimesOfDay`, so keys are always a subset. `Chore.SlotLabel(dueAt)` resolves a label from the instant's time-of-day **in its own stored offset** (how `ScanSlot` stamps it); it backs `ChoreDto.DueLabel` (personalized with `DueAt` on track chores) and the push title in `NotificationService.BuildMessage` ("Name (Label)"). The client never matches slots itself - `nextDueTimeLabel` just prepends `dueLabel`. API shape is `timeLabels: { "08:00": "Morning" }` on both request and `ChoreDto`; `ChoreFormModal` keeps labels index-aligned with its time rows and only submits them when there are several times.
- **Auto-advance**: `ChoreAutoAdvanceService` polls `ChoreService.AutoAdvanceAsync(now)` every minute. Writes `ChoreCompletion.IsExpired` rows for unfilled occurrences, advances via `FromScheduledDate`, rotates. `OneTime` excluded.
- **Achievements**: catalog in `AchievementCatalog.cs`. `EvaluateForUserAsync` called inline by CompleteAsync/RedeemAsync/AdjustPointsAsync — no background worker. Earned badges are permanent (reversals don't revoke). `UnlockedAchievements` on ChoreDto/RedemptionDto surfaces new badges to the client.
- **Gacha**: catalog in `CosmeticCatalog.cs`. `EquippedFrameKey`/`EquippedThemeKey`/`AvatarEmoji`/`Dust`/`PullsSinceLegendary` on `User`. Equipping a Color cosmetic writes `User.AvatarColor = def.Value`; equipping an Emoji cosmetic writes `User.AvatarEmoji = def.Value` (a null key clears it, avatar falls back to initials) — both denormalized, thread via `UserDto.FromEntity`/`LeaderboardEntryDto`. The `Avatar` component (`ui/Modal.tsx`) renders `emoji` in place of initials wherever it renders `frame`. `Default = true` cosmetics are free starters everyone owns without a `UserCosmetic` row and that never enter the pull pool: colors `color-indigo`/`color-green`/`color-blue` and emojis `emoji-star`/`emoji-heart`/`emoji-paw`. `Default` means *owned*, not *equipped*: `CosmeticCatalog.StarterColorKey` (`color-indigo`, #6366f1) names the color new and freshly reset users start on, and no emoji is equipped out of the box (initials are the fallback). Equipping lives in `CustomizationModal.tsx`, not the Rewards page. "Gacha" is an internal name only (API, services, query key): the UI calls it **Cosmetics**, a tab of `RewardsPage`. Shared refetch helper: `syncAppearanceFromServer` in `lib/appearance.ts`.
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
- **Localization (English / Croatian)**: frontend-only, per device. `lib/i18n.ts` holds a Zustand `useLanguageStore` (localStorage key `turnly-language`, defaulting to the browser language; the inline script in `index.html` pre-applies `<html lang>`) and the imperative `t(key, params)` / `plural(key, count)` / `tRich(key, nodes)` helpers. Dictionaries are flat objects in `locales/en.ts` (source of truth: `TKey = keyof typeof en`) and `locales/hr.ts`, typed `Record<TKey | \`${PluralKey}_few\`, string>` so a missing Croatian key fails `tsc`.
  - **`t` is not reactive.** Nothing subscribes per string; `App` keys `<Routes>` and the root outlets on the language, so a switch remounts the tree. ⚠️ That means text must be resolved **at render time**: a module-level `const X = [{ label: t(...) }]` freezes the language at import. Module-level option tables use `localized(item, { label: key })`, which defines the field as a getter (see `REPEAT_OPTIONS` in `lib/chore-format.ts`).
  - **Plurals**: `key_one` / `key_other`, plus `key_few` in Croatian (1 dan, 2 dana, 5 dana), picked by `Intl.PluralRules`. Keep `{count}` in every form: Croatian `one` also matches 21, 31.
  - **Dates** go through `locale()` (`en-GB` / `hr-HR`). Times of day stay hardcoded `en-GB`: that is the 24-hour "HH:mm" format, not a language choice.
  - **Server-originated text stays English on the wire.** Achievements are translated on the client by their stable key with the server text as fallback: `tServer('achievement.{key}.name', a.name)`, `achievementCategory.{name}`; cosmetic names and rarity names are deliberately **not** translated (they read as proper names); `PointsPage` maps the known points-log description shapes. Still untranslated because they are composed or stored server-side: API error messages (`ApiError.message`), push / inbox notification titles and bodies, and admin-entered data.
  - Croatian copy uses the formal "vi" and avoids constructions that need a gendered participle or a declined name ("Za: {name}" rather than a dative).
- **Live updates**: Server-Sent Events, no payload. `ChangeBroadcaster` (`Turnly.Api/Realtime`, singleton, one coalescing channel per open stream) is published to from exactly two places: the `UseChangeBroadcast` middleware, after any successful non-GET `/api` request (`IsSharedChange` skips `/api/auth` and `/api/notifications`, which are per-user), and `ChoreAutoAdvanceService` when it advanced something. Services never publish, so a new endpoint is covered for free; a new **background** writer has to call `Publish()` itself. `GET /api/events` streams `event: change` plus a 20s `: ping` heartbeat and sets `X-Accel-Buffering: no` for nginx. Frontend: `useLiveUpdates()` (`lib/live.ts`, mounted in `Layout`) reads the stream with `fetch` (EventSource can't send the bearer), debounces, and calls `queryClient.invalidateQueries()` with **no key**, so every page updates without being listed anywhere. The stream is dropped while the tab is hidden and reopened on return (also on `online`, and with backoff after an error); any reopen after a gap refetches once to catch up. The signal says nothing about what changed, so it needs no per-viewer filtering.
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
- `Realtime/ChangeBroadcaster.cs` + `Endpoints/EventEndpoints.cs` — live-update fan-out and its SSE stream (`GET /api/events`).

**Frontend (`web/src`)**
- `App.tsx` — auth-gated routing; index → `/chores`. Native: gated behind `ServerSetupPage` if no saved origin.
- `pages/` — `ChoresPage`/`UsersPage` = canonical CRUD-with-modal. `RewardsPage` (`/rewards`) is a thin tab shell (header + points badge + tabs, active tab in `?tab=`) over `components/rewards/AwardsTab.tsx` (= "browse + member action + admin CRUD" pattern) and `components/rewards/CosmeticsTab.tsx` (pulls, pity, odds, collection + craft). The old `/awards` and `/gacha` routes redirect to it.
- `components/chores/OfferBoard.tsx` — "Up for grabs" band at the top of `ChoresPage`'s main column (every breakpoint, every view). Filters the loaded chore list to offers the viewer can claim and claims them inline. Returns null when empty.
- `components/chores/ChoreConfigTable.tsx` — admin-only `'config'` chore view (`ChoreView` / `VIEW_OPTIONS` in `ChoreFilters.tsx`, where `adminOnly` hides it from members' view menu): one table of every chore's settings, sorted by name with no due buckets, filters still applied, edit button per row. Frontend-only (everything is already on `ChoreDto`). `ChoresPage` falls back to `'list'` when a non-admin has `'config'` stored, and in this view drops the `max-w-2xl` cap and hides the rail so the table gets the full width. The table only renders at `xl`+; below that the same cells (`NameCell`/`ScheduleCell`/`AssignmentCell`/`CompletionCell`/`RemindersCell`) are stacked into one card per chore, so phones never scroll sideways.
- `components/chores/ChoreRail.tsx` — right-hand info rail on `ChoresPage`, desktop `xl`+ only (sticky, hidden below `xl`; shown in every view including calendar). Two widgets: "Your snapshot" (fresh `me` points/weekly + your overdue/due-today counts derived from the loaded chore list) and weekly leaderboard (top 5, rows open `UserDetailsModal`). Pure presentation over existing queries; no backend changes. `ChoresPage` runs full-width at `xl` (via `Layout`'s route check) so the rail anchors to the screen edge and the chore column centers in the space left of it (`max-w-2xl`); below `xl` it falls back to the standard centered `max-w-5xl` column with the rail hidden.
- `lib/api.ts` — `request<T>` (bearer + one-shot 401 refresh). `lib/types.ts` mirrors backend DTOs.
- `lib/live.ts` — `useLiveUpdates()`: SSE stream that invalidates all queries when someone else changes data.
- `lib/chore-format.ts` — shared chore helpers (`isIndependent`, `dueStatus`, `trackIsDone`, etc.).
- `lib/i18n.ts` — language store + `t` / `plural` / `tRich` / `tServer` / `locale` / `localized`. `locales/en.ts`, `locales/hr.ts` — the dictionaries. The picker is `LanguageCard` in `SettingsPage`.
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
- **All client-facing text goes through `t`** (`lib/i18n.ts`), with the string added to both `locales/en.ts` and `locales/hr.ts`. No literal UI text in components, including `aria-label`, `title`, `placeholder`, toasts and confirm dialogs.
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
