import type { SearchResponse } from './types'

export const API_URL = (import.meta.env.VITE_API_URL ?? 'http://localhost:8080').replace(/\/+$/, '')

/** Long enough to ride out a Render free-tier cold start (~50 s). */
const SEARCH_TIMEOUT_MS = 60_000

export interface Health {
  status: string
  version: string
}

export async function getHealth(signal?: AbortSignal): Promise<Health> {
  const res = await fetch(`${API_URL}/health`, { signal })
  if (!res.ok) throw new Error(`Health check failed: ${res.status}`)
  return res.json() as Promise<Health>
}

export type SearchErrorKind =
  | 'validation' // 400: shown inline under the input
  | 'rateLimited' // 429: our per-client limit
  | 'catalogDown' // 502: Open Library down or slow
  | 'catalogBusy' // 503: Open Library rate-limiting us
  | 'network' // no response (offline, server asleep, timeout)
  | 'server' // anything else

export class SearchError extends Error {
  readonly kind: SearchErrorKind
  /** Seconds to wait before retrying, from Retry-After. */
  readonly retryAfter?: number

  constructor(kind: SearchErrorKind, message: string, retryAfter?: number) {
    super(message)
    this.name = 'SearchError'
    this.kind = kind
    this.retryAfter = retryAfter
  }
}

/** The caller's signal plus a timeout (falls back gracefully where AbortSignal.any/timeout are missing). */
function withTimeout(signal: AbortSignal | undefined, ms: number): AbortSignal | undefined {
  const timeout = typeof AbortSignal.timeout === 'function' ? AbortSignal.timeout(ms) : undefined
  if (!signal) return timeout
  if (!timeout || typeof AbortSignal.any !== 'function') return signal
  return AbortSignal.any([signal, timeout])
}

interface ProblemDetails {
  title?: string
  detail?: string
}

/** POST /api/search. Resolves with the response or throws a {@link SearchError}; an aborted search rethrows the AbortError. */
export async function searchBooks(query: string, signal?: AbortSignal): Promise<SearchResponse> {
  let res: Response
  try {
    res = await fetch(`${API_URL}/api/search`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ query }),
      signal: withTimeout(signal, SEARCH_TIMEOUT_MS),
    })
  } catch (err) {
    if (signal?.aborted) throw err // the user started another search; not an error
    throw new SearchError('network', 'Could not reach the search server.')
  }

  if (res.ok) return (await res.json()) as SearchResponse

  const problem = (await res.json().catch(() => ({}))) as ProblemDetails
  const retryAfter = Number(res.headers.get('Retry-After')) || undefined
  const message = problem.detail ?? problem.title ?? `Request failed (${res.status}).`

  switch (res.status) {
    case 400:
      throw new SearchError('validation', message)
    case 429:
      throw new SearchError('rateLimited', message, retryAfter)
    case 502:
      throw new SearchError('catalogDown', message)
    case 503:
      throw new SearchError('catalogBusy', message, retryAfter)
    default:
      throw new SearchError('server', message)
  }
}
