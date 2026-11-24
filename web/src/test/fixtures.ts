import type { BookResult, MatchDetails, SearchResponse } from '../lib/types'

const noMatch: MatchDetails = {
  title: 'none',
  author: 'none',
  yearMatched: false,
  seriesMember: false,
  collection: false,
  derivative: false,
  keywordOnly: false,
}

export function book(title: string, overrides: Omit<Partial<BookResult>, 'match'> & { match?: Partial<MatchDetails> } = {}): BookResult {
  const id = `OL${title.length}${title.charCodeAt(0)}W`
  return {
    title,
    authors: ['J.R.R. Tolkien'],
    contributors: [],
    firstPublishYear: 1937,
    openLibraryId: id,
    url: `https://openlibrary.org/works/${id}`,
    matchTier: 'A',
    explanation: `Why ${title}.`,
    ...overrides,
    match: { ...noMatch, ...overrides.match },
  }
}

export function response(overrides: Partial<SearchResponse> = {}): SearchResponse {
  return {
    query: 'tolkien hobbit',
    interpretedAs: [{ title: 'The Hobbit', author: 'J.R.R. Tolkien', keywords: [], isSeries: false }],
    mode: 'match',
    results: [],
    hasBestMatch: false,
    parser: 'llm',
    explanations: 'llm',
    ...overrides,
  }
}

type Reply = { status?: number; body?: unknown; headers?: Record<string, string> }

/**
 * Stubs fetch: /health answers ok, /api/search answers with the queued replies in order
 * (the last one repeats). Returns the list of search queries sent.
 */
export function mockApi(...replies: Reply[]): string[] {
  const sent: string[] = []
  const fetchMock = async (url: string, init?: RequestInit) => {
    if (url.endsWith('/health')) return json({ status: 'ok', version: '0.1.0' })
    sent.push((JSON.parse(String(init?.body)) as { query: string }).query)
    const reply = replies[Math.min(sent.length - 1, replies.length - 1)]
    return json(reply.body ?? {}, reply.status ?? 200, reply.headers)
  }
  ;(globalThis as { fetch: unknown }).fetch = fetchMock
  return sent
}

function json(body: unknown, status = 200, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })
}
