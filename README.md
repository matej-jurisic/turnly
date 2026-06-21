# Turnly

Family chore management web app (PWA). Self-hosted, simple, actually works.

This repository currently implements **Phases 1–9**: foundation (auth, users, roles, Docker),
chore CRUD with recurrence and assignment strategies, a dashboard with today/overdue/upcoming
views, per-user point totals, filtering, and global search, a history/stats view, awards and
point redemption, per-occurrence skip and one-off reassignment, Web Push notifications
(per-chore reminder/due/follow-up schedule via self-hosted VAPID keys), and UX polish (swipe
actions, completion confetti, admin complete-on-behalf and activity-entry deletion). **Shared
chores** can also be set to "Everyone (independent)", giving each assignee their own schedule and
per-person quota — one chore for "everyone does the dishes once a week" without anyone blocking
anyone else. See [`specs.md`](./specs.md) for the full product spec and roadmap.

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
