import type { BookResult, InterpretedAs, SearchResponse } from './types'

/** Short label for a result, from what matched: "Exact title · primary author", "Keyword only", … */
export function matchLabel(result: BookResult, queryHadAuthor: boolean): string {
  const m = result.match
  if (m.keywordOnly) return 'Keyword only'
  if (m.collection) return 'Collection or box set'
  if (m.derivative) return 'Exact title · another version'

  const title = m.seriesMember ? 'In the series' : { exact: 'Exact title', near: 'Near title', none: 'Different title' }[m.title]
  const author =
    m.author === 'primary'
      ? 'primary author'
      : m.author === 'contributor'
        ? 'author as contributor'
        : queryHadAuthor
          ? 'different author'
          : null
  return author ? `${title} · ${author}` : title
}

const quote = (s: string) => `“${s}”`

/** The "Searched for …" line: how the query was read. */
export function searchedFor(response: SearchResponse): string | null {
  const [first, ...rest] = response.interpretedAs
  if (!first) return null

  if (first.text !== undefined) {
    const words = [...first.text.split(' '), ...first.keywords, ...(first.year ? [String(first.year)] : [])]
    return `Searched for words ${words.map(quote).join(', ')} · parsed by rules`
  }

  const parts = describe(first)
  if (!first.title && !first.author) return `Searched for keywords ${first.keywords.map(quote).join(', ')} · no title or author found`
  if (!first.title) parts.push('no title found')

  const alternatives = rest.filter((h) => h.title).map((h) => quote(h.title!))
  if (alternatives.length > 0) parts.push(`also tried ${alternatives.join(', ')}`)
  return `Searched for ${parts.join(' · ')}`
}

function describe(h: InterpretedAs): string[] {
  const parts: string[] = []
  if (h.title) parts.push(`${h.isSeries ? 'series' : 'title'} ${quote(h.title)}`)
  if (h.author) parts.push(`author ${quote(h.author)}`)
  if (h.year) parts.push(`year ${h.year}`)
  return parts
}

export function queryHadAuthor(response: SearchResponse): boolean {
  return response.interpretedAs.some((h) => !!h.author)
}

/** "J.R.R. Tolkien" or "Brian Herbert, Kevin J. Anderson"; never empty. */
export function authorsLine(result: BookResult): string {
  return result.authors.length > 0 ? result.authors.join(', ') : 'Unknown author'
}

const ordinalSuffix = (n: number) =>
  n % 100 >= 11 && n % 100 <= 13 ? 'th' : ({ 1: 'st', 2: 'nd', 3: 'rd' } as Record<number, string>)[n % 10] ?? 'th'

export const ordinal = (n: number) => `${n}${ordinalSuffix(n)}`
