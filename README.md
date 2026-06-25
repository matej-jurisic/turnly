# Turnly

Family chore management web app (PWA). Self-hosted, simple, actually works.

Turnly is feature-complete. It provides foundation
(auth, users, roles, Docker), chore CRUD with recurrence and assignment strategies, a dashboard
with today/overdue/upcoming views (list, compact, and calendar layouts), per-user point totals,
filtering, and global search, a history/stats view, awards and point redemption (with next-goal
progress), per-occurrence skip and one-off reassignment, Web Push notifications (per-chore
reminder/due/follow-up schedule via self-hosted VAPID keys, with per-user quiet hours), and UX
polish (swipe actions, completion confetti, admin complete-on-behalf and activity-entry deletion).
**Shared chores** can also be set to "Everyone (independent)", giving each assignee their own
schedule and per-person quota — one chore for "everyone does the dishes once a week" without anyone
blocking anyone else. Chores can also fire **multiple times a day**, **auto-advance** past missed
occurrences, track an **on-time streak**, and be **copied**; admins can **adjust points** manually,
**delete a redemption of any status** (refunding its points), and set the instance's **family
timezone**. Members also earn **achievements** — collectible, permanently-earned badges for
milestones (completion counts, on-time streaks, points earned, redemptions, and variety), unlocked
the moment they're earned and shown on a dedicated page where admins can view any user's badges and
revoke them. Admins can also **pause individual chores** (blocks completions, hides from the active
list, suppresses notifications — unpausing steps overdue chores forward to the next future
occurrence) and **freeze users** (marks them as "Away": excluded from rotation, independent tracks
paused, no notifications — with a preview of affected chores before confirming). There's also a
points-funded **gacha** for cosmetics (no real money): spend points on pulls to collect rarity-tiered
**avatar frames** (shown on every avatar) and **app theme palettes** (recolor your own app), with a
pity counter that guarantees a Legendary, a dust economy that turns duplicates into a specific
crafted item, published drop rates, and one-per-slot equipping. For a clean slate, admins can run a
**fresh start** (Settings → Danger zone) that wipes all activity, point history, redemptions,
achievements, and gacha progress and resets everyone's points to 0, while keeping every chore and its
schedule intact. See [`specs.md`](./specs.md) for the full product spec and feature list.

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
