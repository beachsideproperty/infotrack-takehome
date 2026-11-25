import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import App from '../App'
import { EMPTY_QUERY_MESSAGE } from '../lib/validate'
import { book, mockApi, mockApiWithHealth, response } from './fixtures'

const hobbit = response({
  hasBestMatch: true,
  results: [
    book('The Hobbit', { match: { title: 'exact', author: 'primary', yearMatched: true } }),
    book('The Hobbit (graphic novel)', {
      authors: ['Charles Dixon', 'J.R.R. Tolkien'],
      matchTier: 'B',
      match: { title: 'exact', author: 'contributor', derivative: true },
    }),
  ],
})

async function searchFor(query: string) {
  const user = userEvent.setup()
  render(<App />)
  await user.type(screen.getByLabelText('What book are you thinking of?'), query)
  await user.click(screen.getByRole('button', { name: 'Find book' }))
  return user
}

describe('App', () => {
  it('shows a best-match card and the other matches', async () => {
    const sent = mockApi({ body: hobbit })
    await searchFor('tolkien hobbit')

    expect(await screen.findByText('Best match')).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1, name: 'The Hobbit' })).toBeInTheDocument()
    expect(screen.getByText('Exact title · primary author')).toBeInTheDocument()
    expect(screen.getByText('First published (Open Library)')).toBeInTheDocument()
    const others = screen.getByRole('region', { name: 'Other possible matches' })
    expect(within(others).getByText('Exact title · another version')).toBeInTheDocument()
    expect(sent).toEqual(['tolkien hobbit'])
    expect(window.location.search).toBe('?q=tolkien%20hobbit')
  })

  it('shows a ranked list with no winner for a series', async () => {
    mockApi({
      body: response({
        interpretedAs: [{ title: 'Harry Potter', author: 'J. K. Rowling', keywords: [], isSeries: true }],
        results: [book("Harry Potter and the Philosopher's Stone", { match: { title: 'near', author: 'primary', seriesMember: true } })],
      }),
    })
    await searchFor('harry potter')

    expect(await screen.findByRole('heading', { name: 'Books in the Harry Potter series' })).toBeInTheDocument()
    expect(screen.queryByText('Best match')).not.toBeInTheDocument()
  })

  it('lists an author’s top works with a link to all of them', async () => {
    mockApi({
      body: response({
        mode: 'authorOnly',
        interpretedAs: [{ author: 'Charles Dickens', keywords: [], isSeries: false }],
        author: { name: 'Charles Dickens', openLibraryId: 'OL24638A', url: 'https://openlibrary.org/authors/OL24638A' },
        results: [book('A Tale of Two Cities', { authors: ['Charles Dickens'], matchTier: 'D', firstPublishYear: 1800 })],
      }),
    })
    await searchFor('dickens')

    expect(await screen.findByRole('heading', { name: 'Top works by Charles Dickens' })).toBeInTheDocument()
    expect(screen.getByText(/first published 1800 \(Open Library\)/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /All works by Charles Dickens/ })).toHaveAttribute('href', 'https://openlibrary.org/authors/OL24638A')
  })

  it('shows keyword-only guesses without pretending they match', async () => {
    mockApi({
      body: response({
        mode: 'noMatch',
        interpretedAs: [{ keywords: ['whale', 'lighthouse'], isSeries: false }],
        results: [book('Heroes of White Whale Lighthouse', { matchTier: 'E', match: { keywordOnly: true } })],
      }),
    })
    await searchFor('a whale and a lighthouse')

    expect(await screen.findByRole('heading', { name: 'No confident match yet' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Closest guesses' })).toBeInTheDocument()
    expect(screen.getByText('Keyword only')).toBeInTheDocument()
  })

  it('runs "Did you mean" as a new search', async () => {
    const sent = mockApi({ body: response({ mode: 'noMatch', didYouMean: { title: 'Moby-Dick', author: 'Herman Melville' } }) }, { body: hobbit })
    const user = await searchFor('mobby dik')

    await user.click(await screen.findByRole('button', { name: 'Moby-Dick by Herman Melville' }))

    expect(sent).toEqual(['mobby dik', 'Moby-Dick Herman Melville'])
  })

  it('validates empty input inline without calling the API', async () => {
    const sent = mockApi({ body: hobbit })
    const user = userEvent.setup()
    render(<App />)

    await user.click(screen.getByRole('button', { name: 'Find book' }))

    expect(screen.getByRole('alert')).toHaveTextContent(EMPTY_QUERY_MESSAGE)
    expect(screen.getByLabelText('What book are you thinking of?')).toHaveAttribute('aria-invalid', 'true')
    expect(sent).toEqual([])
  })

  it('shows the API’s 400 message inline', async () => {
    mockApi({ status: 400, body: { title: 'One or more validation errors occurred.', detail: 'Server says no.' } })
    await searchFor('x')

    expect(await screen.findByRole('alert')).toHaveTextContent('Server says no.')
  })

  it('shows the rate-limit card with a countdown', async () => {
    mockApi({ status: 429, body: { title: 'Lots of searches at once' }, headers: { 'Retry-After': '5' } })
    await searchFor('dickens')

    expect(await screen.findByRole('heading', { name: 'Lots of searches at once' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Try again in 5s' })).toBeDisabled()
  })

  it('shows the Open Library card and retries the same query', async () => {
    const sent = mockApi({ status: 502, body: { title: 'Can’t reach Open Library right now' } }, { body: hobbit })
    const user = await searchFor('austen bennet')

    expect(await screen.findByRole('heading', { name: 'Can’t reach Open Library right now' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByText('Best match')).toBeInTheDocument()
    expect(sent).toEqual(['austen bennet', 'austen bennet'])
  })

  it('shows the AI-unavailable banner for rule-based results', async () => {
    mockApi({
      body: { ...hobbit, parser: 'fallback', explanations: 'template', interpretedAs: [{ text: 'tolkien hobbit', keywords: [], isSeries: false }] },
    })
    await searchFor('tolkien hobbit')

    expect(await screen.findByText(/Something unexpected went wrong with the AI/)).toBeInTheDocument()
  })

  it('says why the AI was unavailable when the API knows', async () => {
    mockApi({
      body: { ...hobbit, parser: 'fallback', explanations: 'template', aiIssue: 'dailyQuota', interpretedAs: [{ text: 'tolkien hobbit', keywords: [], isSeries: false }] },
    })
    await searchFor('tolkien hobbit')

    expect(await screen.findByText(/Today’s free AI quota is used up/)).toBeInTheDocument()
  })

  it('says when Google refused the request rather than the search failing', async () => {
    mockApi({
      body: { ...hobbit, parser: 'fallback', explanations: 'template', aiIssue: 'rejected', interpretedAs: [{ text: 'tolkien hobbit', keywords: [], isSeries: false }] },
    })
    await searchFor('tolkien hobbit')

    expect(await screen.findByText(/a setup problem on our side rather than your search/)).toBeInTheDocument()
  })

  it('warns in the header when every Gemini model is resting, but not when one still works', async () => {
    const resting = (issue: 'overloaded' | 'dailyQuota') => ({ model: 'm', available: false, issue, requestsLastMinute: 0 })
    mockApiWithHealth({ status: 'ok', version: '1', ai: { status: 'unavailable', models: [resting('dailyQuota'), resting('dailyQuota')] } })
    const { unmount } = render(<App />)
    expect(await screen.findByText('Today’s free AI quota is used up: searches use basic matching until it resets.')).toBeInTheDocument()
    unmount()

    mockApiWithHealth({
      status: 'ok',
      version: '1',
      ai: { status: 'limited', models: [resting('overloaded'), { model: 'n', available: true, requestsLastMinute: 1 }] },
    })
    render(<App />)
    expect(await screen.findByRole('link', { name: 'How it works' })).toBeInTheDocument()
    expect(screen.queryByText('Basic matching')).not.toBeInTheDocument()
  })

  it('"How it works" goes home and highlights the section, even when there is nothing to scroll', async () => {
    const scroll = vi.fn()
    Element.prototype.scrollIntoView = scroll
    mockApi({ body: hobbit })
    await searchFor('tolkien hobbit')
    await screen.findByText('Best match')

    await userEvent.click(screen.getByRole('link', { name: 'How it works' }))

    const section = await screen.findByRole('region', { name: 'How it works' })
    await waitFor(() => expect(section).toHaveClass('flash'))
    expect(section).toHaveFocus()
    expect(scroll).toHaveBeenCalled()
    expect(screen.queryByText('Best match')).not.toBeInTheDocument()
  })

  it('runs an example chip and focuses the search box on "/"', async () => {
    const sent = mockApi({ body: hobbit })
    const user = userEvent.setup()
    render(<App />)

    await user.click(screen.getByRole('button', { name: 'mark huckleberry' }))
    await waitFor(() => expect(sent).toEqual(['mark huckleberry']))
    expect(screen.getByLabelText('What book are you thinking of?')).toHaveValue('mark huckleberry')

    screen.getByLabelText('What book are you thinking of?').blur()
    await user.keyboard('/')
    expect(screen.getByLabelText('What book are you thinking of?')).toHaveFocus()
  })

  it('runs the search in a ?q= link once on load', async () => {
    window.history.replaceState(null, '', '/?q=austen%20bennet')
    const sent = mockApi({ body: hobbit })
    render(<App />)

    expect(await screen.findByText('Best match')).toBeInTheDocument()
    expect(sent).toEqual(['austen bennet'])
  })

  it('ignores a slow answer once a newer search has started', async () => {
    let releaseFirst: () => void = () => {}
    const sent: string[] = []
    ;(globalThis as { fetch: unknown }).fetch = vi.fn(async (url: string, init?: RequestInit) => {
      if (url.endsWith('/health')) return new Response('{"status":"ok","version":"0"}')
      const query = (JSON.parse(String(init?.body)) as { query: string }).query
      sent.push(query)
      if (query === 'first') {
        await new Promise<void>((resolve) => (releaseFirst = resolve))
        if (init?.signal?.aborted) throw new DOMException('Aborted', 'AbortError')
      }
      const body = response({ hasBestMatch: true, results: [book(query === 'first' ? 'Old Answer' : 'New Answer')] })
      return new Response(JSON.stringify(body))
    })
    const user = userEvent.setup()
    render(<App />)
    const input = screen.getByLabelText('What book are you thinking of?')

    await user.type(input, 'first{Enter}')
    await user.clear(input)
    await user.type(input, 'second{Enter}')
    expect(await screen.findByRole('heading', { level: 1, name: 'New Answer' })).toBeInTheDocument()

    await act(async () => releaseFirst())
    expect(screen.queryByText('Old Answer')).not.toBeInTheDocument()
    expect(sent).toEqual(['first', 'second'])
  })
})
