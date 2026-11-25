import { describe, expect, it } from 'vitest'
import type { AiHealth, Health, ProblemDetails } from '../lib/api'
import type { AiIssue, BookResult, InterpretedAs, MatchDetails, Mode, SearchResponse } from '../lib/types'

/**
 * The API saves real responses to contracts/*.json (api/tests/.../ContractSnapshotTests.cs). This test checks
 * those files against the TypeScript types, so the web app and the API can't drift apart unnoticed:
 * - a field the API sends that the types don't know fails ("unexpected field"),
 * - a required field the API stopped sending fails ("missing"),
 * - an enum value the types don't list fails.
 * The shapes below are typed against the interfaces, so TypeScript itself makes them list every field exactly.
 */

type Check = (value: unknown, path: string) => void
type Optional = Check & { optional: true }
type Shape<T> = { [K in keyof Required<T>]-?: undefined extends T[K] ? Optional : Check }

const fail = (path: string, message: string): never => {
  throw new Error(`${path}: ${message}`)
}
const is =
  (test: (v: unknown) => boolean, expected: string): Check =>
  (v, p) => {
    if (!test(v)) fail(p, `expected ${expected}, got ${JSON.stringify(v)}`)
  }
const string = is((v) => typeof v === 'string', 'a string')
const number = is((v) => typeof v === 'number', 'a number')
const boolean = is((v) => typeof v === 'boolean', 'a boolean')
const optional = (check: Check): Optional => Object.assign((v: unknown, p: string) => check(v, p), { optional: true as const })
const oneOf = <T extends string>(values: Record<T, true>): Check =>
  is((v) => typeof v === 'string' && Object.hasOwn(values, v), `one of ${Object.keys(values).join(', ')}`)
const arrayOf =
  (check: Check): Check =>
  (v, p) => {
    if (!Array.isArray(v)) fail(p, 'expected an array')
    ;(v as unknown[]).forEach((item, i) => check(item, `${p}[${i}]`))
  }
const recordOf =
  (check: Check): Check =>
  (v, p) => {
    if (typeof v !== 'object' || v === null) fail(p, 'expected an object')
    Object.entries(v as object).forEach(([k, item]) => check(item, `${p}.${k}`))
  }
function object<T>(shape: Shape<T>): Check {
  return (v, p) => {
    if (typeof v !== 'object' || v === null || Array.isArray(v)) fail(p, 'expected an object')
    const record = v as Record<string, unknown>
    for (const key of Object.keys(record)) if (!(key in shape)) fail(`${p}.${key}`, 'unexpected field (add it to the TypeScript types)')
    for (const [key, check] of Object.entries(shape) as [string, Check & { optional?: true }][]) {
      if (record[key] === undefined) {
        if (!check.optional) fail(`${p}.${key}`, 'missing (required in the TypeScript types)')
      } else check(record[key], `${p}.${key}`)
    }
  }
}

const aiIssue = oneOf<AiIssue>({ notConfigured: true, overloaded: true, timedOut: true, rateLimited: true, dailyQuota: true, error: true })

const matchDetails = object<MatchDetails>({
  title: oneOf<MatchDetails['title']>({ exact: true, near: true, none: true }),
  author: oneOf<MatchDetails['author']>({ primary: true, contributor: true, none: true }),
  yearMatched: boolean,
  seriesMember: boolean,
  collection: boolean,
  derivative: boolean,
  keywordOnly: boolean,
})

const bookResult = object<BookResult>({
  title: string,
  authors: arrayOf(string),
  contributors: arrayOf(string),
  firstPublishYear: optional(number),
  openLibraryId: string,
  url: string,
  coverUrl: optional(string),
  matchTier: oneOf<BookResult['matchTier']>({ A: true, B: true, C: true, D: true, E: true }),
  match: matchDetails,
  explanation: string,
})

const interpretedAs = object<InterpretedAs>({
  title: optional(string),
  author: optional(string),
  keywords: arrayOf(string),
  year: optional(number),
  isSeries: boolean,
  text: optional(string),
})

type DidYouMean = NonNullable<SearchResponse['didYouMean']>
type AuthorLink = NonNullable<SearchResponse['author']>

const searchResponse = object<SearchResponse>({
  query: string,
  interpretedAs: arrayOf(interpretedAs),
  mode: oneOf<Mode>({ match: true, authorOnly: true, noMatch: true }),
  didYouMean: optional(object<DidYouMean>({ title: optional(string), author: optional(string) })),
  author: optional(object<AuthorLink>({ name: string, openLibraryId: string, url: string })),
  results: arrayOf(bookResult),
  hasBestMatch: boolean,
  parser: oneOf<SearchResponse['parser']>({ llm: true, fallback: true }),
  explanations: oneOf<SearchResponse['explanations']>({ llm: true, template: true }),
  aiIssue: optional(aiIssue),
})

type ModelHealth = AiHealth['models'][number]

const health = object<Health>({
  status: string,
  version: string,
  commit: optional(string),
  ai: optional(
    object<AiHealth>({
      status: oneOf<AiHealth['status']>({ ok: true, limited: true, unavailable: true, notConfigured: true }),
      models: arrayOf(
        object<ModelHealth>({
          model: string,
          available: boolean,
          issue: optional(aiIssue),
          retryInSeconds: optional(number),
          requestsLastMinute: number,
        }),
      ),
    }),
  ),
})

const problem = object<ProblemDetails>({
  type: optional(string),
  title: optional(string),
  status: optional(number),
  detail: optional(string),
  errors: optional(recordOf(arrayOf(string))),
})

const files = import.meta.glob<unknown>('../../../contracts/*.json', { eager: true, import: 'default' })
const named = (prefix: string) => Object.entries(files).filter(([path]) => path.split('/').pop()!.startsWith(prefix))

describe('API contract (contracts/*.json from the API tests)', () => {
  it('has the saved responses', () => {
    expect(named('search-').length).toBeGreaterThanOrEqual(6)
    expect(named('health').length).toBe(1)
    expect(named('problem-').length).toBeGreaterThanOrEqual(1)
  })

  it.each(named('search-'))('%s matches SearchResponse', (path, json) => searchResponse(json, path.split('/').pop()!))
  it.each(named('health'))('%s matches Health', (path, json) => health(json, path.split('/').pop()!))
  it.each(named('problem-'))('%s matches ProblemDetails', (path, json) => problem(json, path.split('/').pop()!))

  it('catches drift', () => {
    expect(() => searchResponse({ ...files['../../../contracts/search-best-match.json'] as object, extra: 1 }, 'x')).toThrow(/unexpected field/)
    expect(() => searchResponse({ ...files['../../../contracts/search-best-match.json'] as object, mode: 'other' }, 'x')).toThrow(/expected one of/)
  })
})
