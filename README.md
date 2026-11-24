# Book Finder

Paste a messy blob of whatever you remember about a book (a title, an author, a character, a year) and get back the book it most likely means, with a one-line reason grounded in Open Library data.

InfoTrack "Find That Book" take-home. React + TypeScript frontend, .NET 10 Web API, Gemini for parsing and explanations, Open Library for book data.

- **Live demo:** _coming soon_ (web on Vercel, API on Render)
- **API health:** _coming soon_

> **Status:** phase 1 (scaffold and deploy). The search pipeline lands in the next phase; this README grows with it.

## Why .NET 10 and not .NET 8

The brief asks for .NET 8. .NET 8 reached end of support on Nov 10, 2026, and .NET 10 is the current LTS, so this project targets .NET 10. The recruiter approved the change.

## Prerequisites

| Tool | Version | Needed for |
|---|---|---|
| Docker Desktop | any recent | `docker compose up` (simplest) |
| .NET SDK | 10.0 (pinned in `api/global.json`) | running the API without Docker, tests |
| Node.js | 22+ | running the web app without Docker |

## Run locally

### With Docker

```bash
cp .env.example .env          # optional for now; fill GEMINI_API_KEY from phase 2
docker compose up --build
```

- Web: http://localhost:5173
- API: http://localhost:8080/health

The header pill reads "Waking up the server…" until the API answers, then "API connected".

### Without Docker

Terminal 1, the API (listens on http://localhost:8080):

```bash
cd api
dotnet run --project src/BookFinder.Api
```

Terminal 2, the web app (http://localhost:5173):

```bash
cd web
npm install
npm run dev
```

The web app calls `http://localhost:8080` by default. To point it elsewhere, create `web/.env.local` with `VITE_API_URL=...`.

### Tests

```bash
cd api && dotnet test
cd web && npm run build      # type-check + production build
```

## Configuration

| Variable | Where | Purpose |
|---|---|---|
| `GEMINI_API_KEY` | API | Gemini key (from phase 2). Locally: `dotnet user-secrets` or `.env` for Docker |
| `OPENLIBRARY_CONTACT_EMAIL` | API | Contact in the Open Library User-Agent (from phase 2) |
| `ALLOWED_ORIGINS` | API | Comma-separated origins allowed by CORS, no trailing slash |
| `PORT` | API | Port to bind on `0.0.0.0`. Render sets it (default 10000) |
| `VITE_API_URL` | Web | API base URL. Baked in at build time, so never put secrets here |

See `.env.example`.

## Project layout

```
api/                         .NET 10 solution (BookFinder.sln) + Dockerfile
  src/BookFinder.Api           Minimal API endpoints, DI, CORS, ProblemDetails, /health
  src/BookFinder.Application   Search orchestration, ranking, interfaces
  src/BookFinder.Domain        Domain records
  src/BookFinder.Infrastructure  Gemini + Open Library clients, caching
  tests/BookFinder.Tests       xUnit tests (WebApplicationFactory for API tests)
web/                         Vite + React + TypeScript + Tailwind v4
docker-compose.yml
```

## API

| Method | Path | Response |
|---|---|---|
| GET | `/health` | `200 { "status": "ok", "version": "0.1.0", "commit": "abc1234" }` (`commit` only on Render) |

Errors use RFC 7807 ProblemDetails.

## Deployment

- **API on Render:** Web Service, Docker runtime, root directory `api`, health check `/health`. Env vars: `ALLOWED_ORIGINS`, `OPENLIBRARY_CONTACT_EMAIL`, later `GEMINI_API_KEY`.
- **Web on Vercel:** root directory `web`, Vite preset, env `VITE_API_URL=https://<service>.onrender.com`.
- Render's free tier sleeps when idle. The first request can take ~50 s, which the "Waking up the server…" pill covers.

## Implementation overview

_To come with the search pipeline (phase 2)._

## Assumptions and design decisions

_To come._

## Features

_To come._

## Testing strategy

_To come._

## Future improvements

_To come._
