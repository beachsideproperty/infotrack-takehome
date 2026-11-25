# Book Finder

[![CI](https://github.com/beachsideproperty/infotrack-takehome/actions/workflows/ci.yml/badge.svg)](https://github.com/beachsideproperty/infotrack-takehome/actions/workflows/ci.yml)

Paste whatever you remember about a book (a title, an author, a character, a year, typos and all) and get back the book it most likely means, with a one-line reason grounded in Open Library data.

InfoTrack "Find That Book" take-home: React + TypeScript frontend, .NET 10 Web API, Gemini for reading queries and writing explanations, Open Library for book data.

- **Live demo:** https://infotrack-takehome.vercel.app
- **API health:** https://infotrack-takehome.onrender.com/health (Render free tier: the first request after idling can take ~50 s)

**Contents:** [Approach and priorities](#approach-and-priorities) · [Run locally](#run-locally) · [How it works](#how-it-works) (matching hierarchy) · [Failures and unclear results](#failures-and-unclear-results) · [Assumptions and trade-offs](#assumptions-and-trade-offs) · [Testing strategy](#testing-strategy) · [Future improvements](#future-improvements) · [API reference](api/README.md)

## Approach and priorities

The heart of the brief is deciding which book a messy query means, and saying why. I put most of the time there and into keeping it reliable, and kept everything else simple.

**Order of work**

1. **Deploy first.** A hello-world API and web page went live on Render and Vercel before any feature, so hosting, CORS and environment variables couldn't surprise me at the end.
2. **A deterministic core with tests.** Normalising, the A–E matching hierarchy and ranking are plain code, tested with golden queries over recorded Open Library data, before any LLM was involved.
3. **Gemini in a narrow role.** It reads the query and phrases the explanations; code decides everything else. A rule-based parser and template explanations sit behind it, so the app works without it.
4. **Hardening from production evidence.** Render logs showed the real failures were Google overloads and bursts against the free tier's per-minute limit, not bad answers. That led to our own rate cap, cooldowns, splitting the two calls across models, caching, specific failure reasons, and an eval that measures how well Gemini reads queries.
5. **The UI from the designs,** with every state built, not just the happy path: loading, ranked lists, author pages, no match, guesses, the fallback banner and each error.
6. **Making it easy to review:** this README, CI, and contract tests that keep the API and web app in step.

**Where I went deep**

- **Matching and data quality:** primary authors vs contributors from work records, detecting adaptations without role data, collapsing duplicate works, series, box sets, typos, accents, and name variants.
- **LLM integration and resilience:** schema-constrained output with code-side checks, measured model choice, staying inside the free tier, and a fallback that still finds most books.

**Deliberately left out:** chips to refine a search (the "Searched for …" line and an editable query cover most of the need); decades for unknown years (not in the data); real streaming progress (a timed loader over one request is honest enough at 3–6 s); a database or shared cache (one instance, in memory is enough); accounts and search history (out of scope).

**.NET 10, not 8:** .NET 8 reached end of support on Nov 10, 2026 and .NET 10 is the current LTS. The recruiter approved the change.

## Run locally

You need **Docker**, or the **.NET 10 SDK** and **Node.js 22+**.

**Gemini key (optional).** Create a free key at https://aistudio.google.com/app/apikey. Without one, everything still runs, using basic matching (the rule-based parser and template reasons), and the app says so.

### With Docker

```bash
cp .env.example .env          # optionally fill in GEMINI_API_KEY
docker compose up --build
```

Web: http://localhost:5173 · API: http://localhost:8080/health

The first start installs the website's packages inside the container, so give it a minute: the page is ready once the log shows `VITE … ready`. Before that the browser shows a connection reset.

### Without Docker

```bash
# Terminal 1: API on http://localhost:8080
cd api
dotnet user-secrets set GEMINI_API_KEY "<your key>" --project src/BookFinder.Api   # optional
dotnet run --project src/BookFinder.Api

# Terminal 2: web on http://localhost:5173
cd web
npm install
npm run dev
```

Try `curl -s localhost:8080/api/search -H 'Content-Type: application/json' -d '{"query":"tolkien hobbit 1937"}'`, or share any search as a link: `http://localhost:5173/?q=austen%20bennet`.

### Tests

```bash
cd api && dotnet test        # 223 tests, offline, ~1 s
cd web && npm test           # 45 tests, API mocked
cd web && npm run build      # type-check + production build
```

CI runs these plus lint and a Docker build on every push. Settings (`GEMINI_MODEL`, `OPENLIBRARY_CONTACT_EMAIL`, `ALLOWED_ORIGINS`, rate limits…) are in `.env.example` and the [API reference](api/README.md#configuration).

## How it works

**The LLM reads and explains; code decides.** Gemini turns the messy text into hypotheses and writes the one-line reasons. Plain, deterministic code searches Open Library, applies the matching hierarchy and ranks, so ranking is unit-tested and still works when Gemini is down.

```
query ─▶ validate (1–300 chars)
      ─▶ Gemini: 0–3 hypotheses { title?, author?, keywords[], year?, isSeries }   ── fails? ─▶ rule-based parser
      ─▶ nothing about a book?     ─▶ noMatch ("pizza recipe"), no Open Library calls
      ─▶ described, not named?     ─▶ keyword search ─▶ "closest guesses", never a match
      ─▶ author only?              ─▶ that author's most-read works (tier D)
      ─▶ otherwise: search.json per hypothesis ─▶ dedupe ─▶ provisional rank
                    ─▶ works/{id}.json for the top 5 (primary authors) ─▶ classify A–E ─▶ rank
      ─▶ Gemini: one batched call writes all explanations                          ── fails? ─▶ templates
```

**Matching hierarchy** (`TierClassifier`, `Ranker`):

| Tier | Meaning | Example |
|---|---|---|
| A | Exact title and a primary author from the work record | "tolkien hobbit 1937" → *The Hobbit* |
| B | Exact title, but the author is only a contributor (or an adaptation lists them later) | Charles Dixon's graphic-novel *Hobbit* in the same search |
| C | Near title (typos, reordering, series member) and the author matches | "harry potter" → each book in the series |
| D | Author-only query: that author's most-read works | "dickens" |
| E | Weaker: only the title or only the author matches; box sets; keyword-only guesses | Tolkien box sets; "a whale and a lighthouse" |

Within a tier: the year named in the query → the matched author's place on the work record → exact over near title → likelier hypothesis → Open Library reading-log count → edition count → work key. The same input always produces the same order (tested by shuffling). The best-match card appears only when the top result is A–C and clearly beats the runner-up; never for series, author-only or weak matches.

**Normalizing.** Accents and punctuation stripped, lowercase, numbers spelled out ("1984" = "Nineteen Eighty-Four"), leading article dropped; title variants split on subtitles and alternatives ("The Hobbit, or There and Back Again" matches "the hobbit"). Author names match on surname plus compatible given names or initials ("J.R.R. Tolkien" = "John Ronald Reuel Tolkien"; "Charles Dickens" ≠ "Monica Dickens"). **Without Gemini**, the classifier works out which typed words were the author (a surname, or a first name when the other words match that book's title: "mark huckleberry" → Mark Twain), and a title containing every typed word counts as near.

**Open Library and caching.** One `search.json` per hypothesis (max 3; a title with a number is also searched spelled out, because Open Library's "1984" search misses Orwell's main record), work records for the top 5 only. A token bucket keeps us at 3 requests/second with an identified User-Agent, responses are cached by URL, and each call times out at 6 s with one retry. Results are cached by normalized query for 6 hours, so repeats (like the example chips) cost no Gemini quota; a degraded result is kept for only 2 minutes so a better one replaces it. A new query takes about 6–8 Open Library calls, two Gemini calls and 3–6 s.

**Web app.** One page, a `useReducer` state machine (`idle → loading → done | error`). A new search aborts the one in flight, so a slow answer can't overwrite a newer one. The three loading steps are paced by a timer over the single request. Badges come from the API's per-result `match` details, never guessed from the tier letter. Keyboard and screen-reader friendly, reduced motion respected.

## Failures and unclear results

Every case has a defined behaviour, and the query is never lost:

| Situation | What the reader sees |
|---|---|
| Empty or over 300 characters | Inline message under the search box (checked before sending; the API returns the same 400) |
| Several plausible books | Ranked list, no best-match card |
| Series ("harry potter") | "Books in the Harry Potter series", no winner |
| Author only ("dickens") | "Top works by Charles Dickens" + link to all their works on Open Library |
| Described but not named ("a whale and a lighthouse") | "No confident match yet" + keyword guesses labelled "Keyword only" |
| Not a book ("pizza recipe") | "No confident match yet", no Open Library calls |
| Gemini down, slow or out of quota | Basic matching, with a banner that says why and whether retrying helps ("Google's AI is busy right now… Try again in a minute", "Today's free AI quota is used up…"; a refused request or an unusable answer is named too). The API reports it as `aiIssue`; `/health` shows each model's state, and the header warns when every model is resting |
| Basic matching's known limits | "1984 by orwell" reads 1984 as a year (Orwell's works); "tale two cities" puts an obscure book literally titled *A tale two cities* above Dickens; "pizza recipe" shows pizza books (never as a best match); a described book finds nothing. All pinned by tests |
| Open Library down or slow | One retry, then "Can't reach Open Library right now" + Try again (502) |
| Open Library rate-limiting us | "Open Library is busy" + countdown (503 + `Retry-After`) |
| Too many searches from one visitor | "Lots of searches at once" + countdown (429, 10 a minute per IP) |
| API asleep (Render free tier) | "Waking up the server…" in the header; a 60 s timeout, then "Can't reach the search server" + Try again |
| No cover / wrong year in Open Library | Coloured block with the title / the year as Open Library has it, labelled "First published (Open Library)" |

## Assumptions and trade-offs

- **Accuracy over speed.** Each top candidate's work record is fetched to separate primary authors from contributors, as the brief requires. It costs a couple of seconds under Open Library's rate limit; caching and the staged loader cover it.
- **Adaptations without role data.** Open Library lists Charles Dixon, Sean Deming and Tolkien as equal authors of the graphic-novel *Hobbit*. Rule: when another work with the same title lists the queried author *first*, versions listing them later are someone else's adaptation, so the author counts as a contributor there (tier B). That reproduces the brief's example without guessing roles, and leaves real co-authors (*Good Omens*) at A.
- **Duplicates, series, box sets.** Duplicate work records (five *Huckleberry Finn*s) collapse to the most-read one. Series queries list the books with no winner. Box sets and omnibuses are capped at E because the brief asks for works.
- **Every fact comes from Open Library.** Gemini only proposes what to search for and phrases the reason; its output is schema-constrained and checked by code (a year survives only if the reader typed it and it isn't the title, "1984" or "Nineteen Eighty-Four"). The explanation prompt only sees fetched fields. Open Library's years are sometimes wrong (*A Tale of Two Cities*: 1800); we show them labelled as theirs rather than let the LLM "correct" them.
- **Prompt injection.** The query is passed as data, the prompt says to ignore instructions in it, and the answer must fit a schema. The worst case is an odd hypothesis, which still has to match real Open Library records.
- **Model choice, measured.** `gemini-3.8-flash` failed or was rate-limited on nearly every free-tier call; `gemini-3.5-flash-lite` answered in about 1 s. The eval below confirmed the order.
- **Staying inside the free tier** (15 requests/minute per model, two calls per search). Parsing tries 3.5 Flash-Lite first and explanations try 3.1 first, spreading the load over two quotas. Our own cap (14/minute per model) means we never cause a 429. After a failure the model is skipped for a cooldown (Google's retry delay or midnight Pacific for a 429, 20 s after a 503, 10 s after a timeout), so a degraded search takes about 3 s, not 20. Time budgets come from measured latency (parse 9 s, explain 6 s), and explanations are skipped when parsing fell back.
- **Per-client rate limit.** 10 searches a minute per IP (from `X-Forwarded-For` behind Render's proxy), so one visitor can't spend the shared quotas.
- **API shape.** `authors[]` (a work can have several) and camelCase instead of the brief's `author` and `first_publish_year`. No database: in-memory caches per instance.

## Testing strategy

No test calls Gemini or the network; Open Library is replayed from recorded JSON.

| Layer | What's covered |
|---|---|
| Unit | Normalizing (including numbers in titles), title and author matching, every tier, adaptation demotion, collections, series, ranking order and determinism, the best-match rule, the fallback parser and its free-text matching, year checks, template sentences |
| Golden queries | The spec's 11 queries end to end through the real search service and Open Library client, with Gemini replaced by the readings it actually returned, so the tests pin what *code* decides. Every query also runs with Gemini "down", including the known limits |
| Clients | Gemini: model fallback, hanging models, cooldowns and the daily reset (fake clock), our rate cap, failure reasons, error-body parsing, junk JSON. Open Library: parsing, work-record authors, caching, retry, timeouts |
| API | Response shape, `/health`, validation messages, malformed JSON, 429/502/503 ProblemDetails, fallback flags, CORS |
| Contract | The API tests save real responses for every result kind to `contracts/*.json`; a web test checks those files against the TypeScript types, so the two sides can't drift apart unnoticed |
| Web | Every result mode, "Did you mean", validation, error cards and countdowns, Try again, fallback banner and header note, chips, `/` shortcut, `?q=` links, a slow older answer never overwriting a newer one |

**Evaluating Gemini's reading.** The tests fake Gemini, so how well it *reads* queries is measured separately by [`api/tools/BookFinder.LlmEval`](api/tools/BookFinder.LlmEval/RESULTS.md): the golden queries, a not-a-book query and a prompt injection, through the production prompt on each model. Run by hand (it calls Gemini). Results, Nov 25, 2026:

| Model | Right (of answered) | Answered first try | Median |
|---|---|---|---|
| `gemini-3.5-flash-lite` (parsing tries it first) | 12/13 | 10/13 | 0.8 s |
| `gemini-3.1-flash-lite` | 7/10 | 6/13 (3 never answered) | 1.5 s |

It found a bug (both models returned 1984 as a year next to *Nineteen Eighty-Four*; now fixed), confirmed the model order (3.1 obeyed the injection and guessed *Moby-Dick* for the vague query; both contained because every result must be a real Open Library record), and showed that availability, not accuracy, is the real risk.

## Future improvements

In rough priority order:

1. **Real progress and partial results** over server-sent events, instead of a timed loader.
2. **A third LLM option** (a paid tier or second provider behind the same port): overload, not accuracy, is the main failure.
3. **A persistent, shared cache** (Redis), so restarts don't start cold.
4. **A bigger eval on a schedule:** more vague, multilingual and adversarial queries; compare prompt changes before shipping.
5. **Smarter basic matching:** tell "1984" the title from the year; prefer well-read works over obscure literal matches.
6. **Editions** for queries like "illustrated deluxe", alongside the work.
7. **Refining a search:** "Did you mean" for keyword guesses; chips to add an author or year.
8. **End-to-end browser tests** against `docker compose` in CI, and **observability** (dashboards, alerts on fallback rate).
9. **No cold starts:** a paid instance or a host that doesn't sleep.
