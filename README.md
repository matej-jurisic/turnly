# Turnly

Family chore management web app (PWA). Self-hosted, simple, actually works.

Turnly is feature-complete. See [`specs.md`](./specs.md) for the full product spec and feature list.

## Features

### Chores and scheduling

- Chore CRUD with recurrence, per-occurrence skip, and copying.
- **Multiple times a day**: each time can be named (e.g. Morning and Evening).
- **On demand** chores: no schedule or due date, completed whenever needed, rotating after each completion.
- **Auto-advance** past missed occurrences, and an **on-time streak** per chore.
- **Backdated completions**: set when you actually did it.
- **Day start**: a chore with no due time can stay due until, say, 05:00 instead of going overdue at midnight.
- **Pause individual chores** (admin): blocks completions, hides them from the active list and suppresses
  notifications. Unpausing steps overdue chores forward to the next future occurrence.

### Assignment

- Rotating assignment strategies, with **hand-over once a day, week, month, quarter or year** instead of
  every occurrence (one person does the weekly chore all month, then the next person takes over).
- **Everyone**: each assignee gets their own schedule and per-person quota, so "everyone does
  the dishes once a week" is one chore and nobody blocks anyone else.
- **Anyone**: nobody is assigned, and whoever gets to it first does it (and can credit the other assignees who helped).
- **Fixed pairs** for two-person jobs: the pair holds the turn together, rotates as one, and both get the
  points when either marks it done. One person can be in several pairs, each taking its own turn.
- Admin **reassignment**, and **chore offers**: put a chore you're holding up for anyone to take, optionally
  with a points bounty.
- **Nudges**: remind whoever holds a chore, with an optional note.
- **Covered for you**: if someone else completes a chore you're holding, you get a notification so you don't do it twice.
- **Rescues**: stepping in late for someone else's overdue chore is logged as a rescue rather than a late
  completion.
- **Freeze users** (admin): marks them as "Away", excluded from rotation, independent tracks paused, no
  notifications, with a preview of affected chores before confirming.

### Dashboard and history

- Today / overdue / upcoming views in list, compact and calendar layouts, plus an admin-only config table
  of every chore's settings.
- Filtering, global search, and a history/stats view.
- Swipe actions, completion confetti, admin complete-on-behalf (credit any number of people, who all get the points).
- Activity-entry deletion and correction: mark an auto-expired entry as completed, or fix the time of a late
  completion.
- Open screens **update live** when someone else changes something, so nobody has to reload.

### Points, rewards and achievements

- Per-user point totals; admins can **adjust points** manually.
- A **Rewards** page with awards and point redemption (with next-goal progress). Admins can **delete a
  redemption of any status**, refunding its points.
- **Achievements**: collectible, permanently earned badges for milestones (completion counts, on-time
  streaks, points earned, redemptions, variety), unlocked the moment they're earned. Admins can view any
  user's badges and revoke them.
- A points-funded **Cosmetics** tab (no real money): random pulls for rarity-tiered **avatar frames** and
  **app theme palettes**, with a pity counter that guarantees a Legendary, a dust economy that crafts
  specific items from duplicates, published drop rates, and one-per-slot equipping.

### Notifications

- Web Push and native (FCM) push with a per-chore reminder / due / follow-up schedule.
- Per-user quiet hours, per-category delivery controls, and per-chore mutes.

### Administration

- Auth, users, roles, Docker deployment, and a configurable **family timezone**.
- **Fresh start** (Settings → Danger zone): wipes all activity, point history, redemptions, achievements and
  cosmetics progress and resets everyone's points to 0, while keeping every chore and its schedule intact.
- The interface is available in **English and Croatian**, picked per device under Settings → Language. Notifications
  follow the language of the device you used most recently.

## Stack

- **Backend:** ASP.NET Core (.NET 10) minimal APIs, EF Core, JWT access tokens + rotating
  6-month refresh tokens (httpOnly cookie). SQLite by default, PostgreSQL optional.
- **Frontend:** React + Vite + TypeScript, Tailwind CSS, TanStack Query, React Router.
- **Tests:** xUnit (unit + `WebApplicationFactory` integration tests).

## Project layout

```
src/Turnly.Core    Entities, EF DbContext, auth + business services (unit-testable)
src/Turnly.Api     ASP.NET Core host: endpoints, auth wiring, serves the built SPA
tests/Turnly.Tests xUnit unit + integration tests
web/               React frontend
Dockerfile         Multi-stage build (frontend + backend) into one image
docker-compose.yml App service (SQLite volume) + optional Postgres profile
```

## Run with Docker (recommended)

```bash
cp .env.example .env       # then edit JWT_SECRET (e.g. openssl rand -base64 48)
docker compose up --build
```

Open http://localhost:8080. On first run you'll be guided through a one-time setup screen
to create the admin account. Data persists in the `turnly-data` volume.

> Serve over HTTPS in production (the refresh cookie is `Secure`). For plain-HTTP local
> testing, set `COOKIE_SECURE=false` in `.env`.

## Local development

First, create your local dev config (it's gitignored, as it holds dev-only secrets):

```bash
cp src/Turnly.Api/appsettings.Development.json.example \
   src/Turnly.Api/appsettings.Development.json
```

Backend (terminal 1):

```bash
dotnet run --project src/Turnly.Api      # http://localhost:5199
```

Frontend (terminal 2):

```bash
cd web && npm install && npm run dev     # http://localhost:5173 (proxies /api → backend)
```

## Android app (Capacitor)

Turnly can be packaged as a standalone Android APK that bundles the web UI and connects to a
self-hosted backend you choose at first launch. The native shell lives in `web/android/`
(generated by Capacitor) and is driven from `web/`.

**Build prerequisites** (on the build machine):

- **JDK 17** (the Android Gradle Plugin requires it; newer JDKs may fail the Gradle build).
- **Android SDK** (install via Android Studio, or the command-line tools) with `ANDROID_SDK_ROOT`
  (or `ANDROID_HOME`) set.

**Build the APK:**

```bash
cd web
npm install
npm run android:apk     # builds the web app, syncs it, then gradlew assembleRelease
# or open the project in Android Studio:
npm run android:open
```

`npm run android:sync` (build + `cap sync`) is the inner loop after a web change; it does not
require the Android SDK.

**Connect the app to your server.** Because the app has no same-origin backend, the backend must
allow the app's WebView origin. Add `https://localhost` to `Cors:Origins` on the server
(`appsettings.json` or the `Cors__Origins__0` env var). The app then talks to the server URL you
enter on first launch.

**Release signing** is opt-in via environment variables so the keystore stays out of source
control. Generate a keystore once:

```bash
keytool -genkey -v -keystore turnly.jks -keyalg RSA -keysize 2048 -validity 10000 -alias turnly
```

Then set `TURNLY_KEYSTORE` (path), `TURNLY_KEYSTORE_PASSWORD`, `TURNLY_KEY_ALIAS`, and
`TURNLY_KEY_PASSWORD` before running `android:apk`. Without them the release build is left unsigned
(fine for local testing).

**One-step server build.** `./turnly-build.sh` builds and signs the APK inside Docker (no local JDK or
Android SDK needed), bumps the version, publishes it to `/data/turnly/releases`, and redeploys the web
app. `--no-web` builds the APK only, `--web-only` only redeploys. It expects the keystore at
`~/.turnly/turnly-release.jks`, its password in `~/.turnly/keystore.pass` (alias `turnly`, override
with `TURNLY_KEY_ALIAS`), and `web/android/app/google-services.json`.

**Update banner.** After a redeploy, open browser tabs show a small "new version" banner with a Reload
button. The Android app shows one when a newer APK is published: point `Releases__Path` at the
releases folder as the container sees it (e.g. `/data/releases`) and the server reports the highest
`turnly-v{name}-{code}.apk` there and serves it at `/api/version/android.apk`.

**Push notifications (FCM).** The app builds and runs without Firebase (push is simply disabled; the
in-app inbox still works). To enable native push:

1. Create a Firebase project and add an Android app with package name `net.turnly.app`. Download its
   `google-services.json` into `web/android/app/` (the Gradle build picks it up automatically).
2. On the **server**, supply a Firebase service-account credential so it can send via FCM: set
   `FCM_CREDENTIALS_PATH` (in `.env`, mounting the JSON into the container) or `Fcm:CredentialsJson`.
   Leave blank to keep native push disabled.

Each user's device registers its FCM token automatically after sign-in; reminders then arrive as
system notifications, and tapping one deep-links the chore (the same `?chore=<id>` contract as Web
Push). Web Push (VAPID) continues to serve the browser/PWA build in parallel.

## Notifications (Web Push)

Push notifications are optional and stay off until VAPID keys are configured. Generate one
keypair once:

```bash
npx web-push generate-vapid-keys
```

- **Docker / production:** set `VAPID_PUBLIC_KEY`, `VAPID_PRIVATE_KEY`, and `VAPID_SUBJECT`
  (e.g. `mailto:you@example.com`) in `.env`.
- **Local dev:** put the same values under the `Vapid` section of
  `src/Turnly.Api/appsettings.Development.json`.

Then each user enables push per-device under **Settings → Notifications**, and chores get a
notification schedule (reminder / due / follow-up) in the chore form. Without keys, the
background scheduler stays idle and the rest of the app works normally.

Users can also set **quiet hours** (a nightly window that suppresses push but still records the
in-app inbox item) in settings. Quiet hours are evaluated against the **family timezone** an admin
configures under **Settings** (falling back to the server's host zone when unset).

### Choosing what notifies you

Under **Settings → What notifies you**, each person decides how every kind of notification reaches
them: **Push** (buzz your devices, default), **Inbox** (record it in the bell without buzzing), or
**Off** (don't send it at all). The categories cover chore reminders, chores coming due, overdue
chases, chores put up for grabs, claims on chores you offered, nudges from other members, and chores someone else completed for you. App messages such as the test push are
always delivered.

To silence one specific chore instead of a whole category, use **Mute** on that chore's menu. A mute
is personal: everyone else still gets notified normally, and the chore shows a small bell-off marker
in your list. Muting and quiet hours downgrade a push to an inbox record rather than discarding it,
so nothing is lost; only **Off** drops a notification entirely. These are per-user settings, so an
admin cannot change them for someone else, and a fresh start leaves them intact.

## Tests

```bash
dotnet test
```

## Database migrations (EF Core)

```bash
dotnet ef migrations add <Name> \
  --project src/Turnly.Core --startup-project src/Turnly.Api --output-dir Migrations
```

Migrations are applied automatically on startup (`Database:MigrateOnStartup`).
