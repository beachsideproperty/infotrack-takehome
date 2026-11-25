/** Mirrors the API contract (README "API" section). */

export type Mode = 'match' | 'authorOnly' | 'noMatch'

export interface InterpretedAs {
  title?: string
  author?: string
  keywords: string[]
  year?: number
  isSeries: boolean
  /** Set only when the rule-based fallback parsed the query: the cleaned words. */
  text?: string
}

export interface MatchDetails {
  title: 'exact' | 'near' | 'none'
  author: 'primary' | 'contributor' | 'none'
  yearMatched: boolean
  seriesMember: boolean
  collection: boolean
  derivative: boolean
  keywordOnly: boolean
}

export interface BookResult {
  title: string
  authors: string[]
  contributors: string[]
  firstPublishYear?: number
  openLibraryId: string
  url: string
  coverUrl?: string
  matchTier: 'A' | 'B' | 'C' | 'D' | 'E'
  match: MatchDetails
  explanation: string
}

export interface SearchResponse {
  query: string
  interpretedAs: InterpretedAs[]
  mode: Mode
  didYouMean?: { title?: string; author?: string }
  author?: { name: string; openLibraryId: string; url: string }
  results: BookResult[]
  hasBestMatch: boolean
  parser: 'llm' | 'fallback'
  explanations: 'llm' | 'template'
  /** Why Gemini wasn't used (for the parse, or else the explanations). Absent when it answered. */
  aiIssue?: AiIssue
}

export type AiIssue = 'notConfigured' | 'overloaded' | 'timedOut' | 'rateLimited' | 'dailyQuota' | 'error'

