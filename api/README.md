# Book Finder API reference

Details behind the [main README](../README.md): endpoints, response fields, configuration, project layout and maintenance commands.

## Endpoints

| Method | Path | Response |
|---|---|---|
| GET | `/health` | `200 { "status": "ok", "version": "0.1.0", "commit": "abc1234", "ai": { … } }` (`commit` only on Render) |
| POST | `/api/search` | `200` search outcome (below); `400` bad input; `429` too many searches from this client; `502` Open Library down; `503` Open Library rate-limiting |

Real responses for every kind of result are saved in [`contracts/`](../contracts) and kept current by the tests.

```jsonc
// POST /api/search  { "query": "tolkien hobbit illustrated deluxe 1937" }
{
  "query": "tolkien hobbit illustrated deluxe 1937",
  "interpretedAs": [{ "title": "The Hobbit", "author": "J.R.R. Tolkien", "keywords": ["illustrated", "deluxe"], "year": 1937, "isSeries": false }],
  "mode": "match",                 // "match" | "authorOnly" | "noMatch"
  "hasBestMatch": true,            // show a hero card only when true
  "parser": "llm",                 // "llm" | "fallback"
  "explanations": "llm",           // "llm" | "template"
  "results": [
    {
      "title": "The Hobbit",
      "authors": ["J.R.R. Tolkien"],          // primary authors, from the work record
      "contributors": [],                      // other names Open Library attaches to the work
      "firstPublishYear": 1937,                // exactly as Open Library reports it
      "openLibraryId": "OL27482W",
      "url": "https://openlibrary.org/works/OL27482W",
      "coverUrl": "https://covers.openlibrary.org/b/id/14627509-L.jpg",
      "matchTier": "A",
      "match": { "title": "exact", "author": "primary", "yearMatched": true, "seriesMember": false, "collection": false, "derivative": false, "keywordOnly": false },
      "explanation": "Exact title match; Tolkien is the primary author on the work record; first published in 1937, the year you gave."
    }
  ]
}
```

- `match` says what matched (`title`: exact | near | none; `author`: primary | contributor | none; plus flags), so the UI labels results precisely.
- `aiIssue` appears only when Gemini wasn't used, for the parse or else the explanations: `overloaded` (Google's 503), `timedOut`, `rateLimited` (Google's per-minute limit or our own cap), `dailyQuota` (until midnight Pacific), `notConfigured` or `error`. When the two models failed differently it reports the one that clears soonest.
- `noMatch` can carry results: keyword "closest guesses" when the query described a book without naming one. `didYouMean: { title, author }` appears only on `noMatch`; `author: { name, openLibraryId, url }` only on `authorOnly`.
- Fields with no value are left out (a missing year is never `0`).
- Errors are RFC 7807 ProblemDetails. Validation errors carry the message to show under the input in `detail`: "Type a title, an author or a few words you remember." / "That's a lot to go on. Try the few words you're surest about." (over 300 characters).

**`/health` → `ai`** is Gemini as the server currently sees it, from recent calls (it never calls Google, so it's free to check): `{ "status": "ok" | "limited" | "unavailable" | "notConfigured", "models": [{ "model", "available", "issue"?, "retryInSeconds"?, "requestsLastMinute" }] }`. `limited` means one model is resting and the other still works. `/health` stays 200 either way, so Render never restarts the API because Google is busy.

## Configuration

| Variable | Where | Purpose |
|---|---|---|
| `GEMINI_API_KEY` | API | Gemini key. Locally: `dotnet user-secrets` or `.env` for Docker. Optional: without it, basic matching |
| `GEMINI_MODEL` | API | Parsing tries it first. Default `gemini-3.5-flash-lite` |
| `GEMINI_FALLBACK_MODEL` | API | Explanations try it first; each falls back to the other. Default `gemini-3.1-flash-lite` |
| `GEMINI_RPM_PER_MODEL` | API | Our own requests-per-minute cap per model. Default 14 |
| `OPENLIBRARY_CONTACT_EMAIL` | API | Contact in the Open Library User-Agent (3 requests/second instead of 1) |
| `SEARCH_RATE_LIMIT_PER_MINUTE` | API | Searches per client IP per minute before a 429. Default 10 |
| `ALLOWED_ORIGINS` | API | Comma-separated origins allowed by CORS, no trailing slash |
| `PORT` | API | Port to bind on `0.0.0.0`. Render sets it (default 10000); locally 8080 |
| `APP_VERSION` | API | Overrides the version `/health` reports |
| `RENDER_GIT_COMMIT` | API | Set by Render; `/health` shows the first 7 characters as `commit` |
| `VITE_API_URL` | Web | API base URL, baked in at build time (never put secrets here). Default `http://localhost:8080` |

## Deployment

- **API on Render:** Web Service, Docker runtime, root directory `api`, health check `/health`. Env vars: `GEMINI_API_KEY`, `OPENLIBRARY_CONTACT_EMAIL`, `ALLOWED_ORIGINS` (the Vercel URL).
- **Web on Vercel:** root directory `web`, Vite preset, `VITE_API_URL=https://<service>.onrender.com`. Redeploy after changing it.

## Project layout

```
api/
  src/BookFinder.Api             Endpoints, validation, ProblemDetails, CORS, rate limit
  src/BookFinder.Application     Search service, matching hierarchy, ranker, fallback parser, templates (pure code)
  src/BookFinder.Domain          Records: hypotheses, candidates, tiers, outcomes
  src/BookFinder.Infrastructure  Open Library client (rate limit, cache, retry), Gemini client (models, gate, cooldowns)
  tests/BookFinder.Tests         Unit, golden-query, client, API and contract tests + recorded Open Library JSON
  tools/BookFinder.LlmEval       Measures Gemini's reading of the golden queries (run by hand)
web/src/lib                      API client and types, search state, labels, validation
web/src/components               One component per screen element from the designs
contracts/                       Real API responses, checked by both the API and web tests
```

## Maintenance commands

```bash
# After an intended change to the API's JSON: refresh the saved contract, then update web/src/lib/types.ts until npm test passes
cd api && UPDATE_CONTRACTS=1 dotnet test --filter Category=Contract

# Refresh the recorded Open Library responses (calls the live API, politely paced)
cd api && RECORD_FIXTURES=1 dotnet test --filter Category=Golden

# Re-run the Gemini eval (~26 Gemini calls); writes tools/BookFinder.LlmEval/RESULTS.md
cd api && GEMINI_API_KEY=... dotnet run --project tools/BookFinder.LlmEval
```
