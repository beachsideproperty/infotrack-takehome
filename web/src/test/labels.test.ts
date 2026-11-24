import { describe, expect, it } from 'vitest'
import { matchLabel, searchedFor } from '../lib/labels'
import { validateQuery, TOO_LONG_MESSAGE, EMPTY_QUERY_MESSAGE } from '../lib/validate'
import { book, response } from './fixtures'

describe('matchLabel', () => {
  it.each([
    [{ title: 'exact', author: 'primary' }, true, 'Exact title · primary author'],
    [{ title: 'exact', author: 'contributor' }, true, 'Exact title · author as contributor'],
    [{ title: 'near', author: 'primary' }, true, 'Near title · primary author'],
    [{ title: 'exact', author: 'none' }, true, 'Exact title · different author'],
    [{ title: 'exact', author: 'none' }, false, 'Exact title'],
    [{ title: 'near', author: 'primary', seriesMember: true }, true, 'In the series · primary author'],
    [{ collection: true }, true, 'Collection or box set'],
    [{ derivative: true, title: 'exact', author: 'contributor' }, true, 'Exact title · another version'],
    [{ keywordOnly: true }, false, 'Keyword only'],
  ] as const)('%o (query had author: %s) → %s', (match, hadAuthor, expected) => {
    expect(matchLabel(book('X', { match }), hadAuthor)).toBe(expected)
  })
})

describe('searchedFor', () => {
  it('describes an LLM reading with alternatives', () => {
    const r = response({
      interpretedAs: [
        { title: "Harry Potter and the Philosopher's Stone", author: 'J. K. Rowling', keywords: [], isSeries: false },
        { title: "Harry Potter and the Sorcerer's Stone", author: 'J. K. Rowling', keywords: [], isSeries: false },
      ],
    })
    expect(searchedFor(r)).toBe(
      "Searched for title “Harry Potter and the Philosopher's Stone” · author “J. K. Rowling” · also tried “Harry Potter and the Sorcerer's Stone”",
    )
  })

  it('describes author-only, keyword-only and rule-based readings', () => {
    expect(searchedFor(response({ interpretedAs: [{ author: 'Charles Dickens', keywords: [], isSeries: false }] }))).toBe(
      'Searched for author “Charles Dickens” · no title found',
    )
    expect(searchedFor(response({ interpretedAs: [{ keywords: ['whale', 'lighthouse'], isSeries: false }] }))).toBe(
      'Searched for keywords “whale”, “lighthouse” · no title or author found',
    )
    expect(
      searchedFor(response({ parser: 'fallback', interpretedAs: [{ text: 'tolkien hobbit', keywords: ['illustrated'], year: 1937, isSeries: false }] })),
    ).toBe('Searched for words “tolkien”, “hobbit”, “illustrated”, “1937” · parsed by rules')
  })

  it('says nothing when there was no reading at all', () => {
    expect(searchedFor(response({ interpretedAs: [] }))).toBeNull()
  })
})

describe('validateQuery (same rules as the API)', () => {
  it.each([
    ['', EMPTY_QUERY_MESSAGE],
    ['   ', EMPTY_QUERY_MESSAGE],
    ['a'.repeat(301), TOO_LONG_MESSAGE],
    [`  ${'a'.repeat(300)}  `, null],
    ['dickens', null],
  ])('%j → %s', (input, expected) => {
    expect(validateQuery(input)).toBe(expected)
  })
})
