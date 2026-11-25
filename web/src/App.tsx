import { useEffect, useRef, useState } from 'react'
import { AuthorOnlyView } from './components/AuthorOnlyView'
import { ErrorCard } from './components/ErrorCard'
import { Footer } from './components/Footer'
import { Header } from './components/Header'
import { HowItWorks } from './components/HowItWorks'
import { LoadingView } from './components/LoadingView'
import { NoMatchView } from './components/NoMatchView'
import { DegradedBanner } from './components/Notices'
import { ResultsView } from './components/ResultsView'
import { SearchForm } from './components/SearchForm'
import { searchedFor } from './lib/labels'
import type { SearchResponse } from './lib/types'
import { useApiStatus } from './lib/useApiStatus'
import { useSearch, type SearchState } from './lib/useSearch'
import { useSlashToFocus } from './lib/useSlashToFocus'

export default function App() {
  const { status, retry: retryStatus } = useApiStatus()
  const { state, validation, search, reset, clearValidation } = useSearch()
  // A ?q= link pre-fills the box (and is searched once on load, below).
  const [query, setQuery] = useState(() => new URLSearchParams(window.location.search).get('q') ?? '')
  const inputRef = useRef<HTMLInputElement>(null)
  useSlashToFocus(inputRef)

  const run = (q: string) => {
    setQuery(q)
    void search(q)
    // Shareable link to this search (replace, not push: the back button isn't wired to search state).
    if (q.trim()) window.history.replaceState(null, '', `?q=${encodeURIComponent(q.trim())}`)
  }

  // Opening a ?q= link runs that search once on load (the ref stops React's dev double-run from
  // sending two requests, which would spend Gemini quota twice).
  const ranInitial = useRef(false)
  useEffect(() => {
    if (ranInitial.current) return
    ranInitial.current = true
    if (query) void search(query)
    // eslint-disable-next-line react-hooks/exhaustive-deps -- run once on mount
  }, [])

  const home = state.status === 'idle'
  const showHowItWorks = () => {
    reset()
    // After the home layout renders, bring the section into view and move focus there.
    requestAnimationFrame(() => {
      const section = document.getElementById('how')
      if (!section) return
      section.scrollIntoView({ behavior: 'smooth', block: 'start' })
      section.focus({ preventScroll: true })
      // On a tall screen the section is already in view and there's nothing to scroll, so briefly
      // highlight it; otherwise the click looks like it did nothing. Re-adding restarts the animation.
      section.classList.remove('flash')
      void section.offsetWidth
      section.classList.add('flash')
    })
  }

  return (
    <div className="flex min-h-dvh flex-col">
      <Header
        status={status}
        onRetryStatus={retryStatus}
        onHome={() => {
          reset()
          setQuery('')
          window.history.replaceState(null, '', window.location.pathname)
          inputRef.current?.focus()
        }}
        onHowItWorks={showHowItWorks}
      />

      <main
        className={`mx-auto flex w-full flex-1 flex-col ${
          home ? 'max-w-[880px] gap-10 px-5 pt-14 pb-16 sm:px-8 sm:pt-24' : 'max-w-[1120px] gap-8 px-5 pt-8 pb-16 sm:px-8 sm:pt-12'
        }`}
      >
        <SearchForm
          home={home}
          value={query}
          onChange={(v) => {
            setQuery(v)
            if (validation) clearValidation()
          }}
          onSubmit={run}
          loading={state.status === 'loading'}
          validation={validation}
          showExamples={state.status === 'idle' || (state.status === 'done' && state.response.mode !== 'noMatch')}
          inputRef={inputRef}
        />

        <Content state={state} onSearch={run} onRetry={() => state.status === 'error' && run(state.query)} />

        {home && <HowItWorks />}
      </main>

      <Footer />
      <Announcer state={state} />
    </div>
  )
}

function Content({ state, onSearch, onRetry }: { state: SearchState; onSearch: (q: string) => void; onRetry: () => void }) {
  switch (state.status) {
    case 'idle':
      return null
    case 'loading':
      return <LoadingView startedAt={state.startedAt} />
    case 'error':
      return <ErrorCard error={state.error} onRetry={onRetry} />
    case 'done':
      return <Results response={state.response} onSearch={onSearch} />
  }
}

function Results({ response, onSearch }: { response: SearchResponse; onSearch: (q: string) => void }) {
  const line = searchedFor(response)
  return (
    <>
      {response.parser === 'fallback' && <DegradedBanner issue={response.aiIssue} />}
      {line && <p className="-mt-4 text-[13px] text-muted">{line}</p>}
      {response.mode === 'authorOnly' && <AuthorOnlyView response={response} />}
      {response.mode === 'noMatch' && <NoMatchView response={response} onSearch={onSearch} />}
      {response.mode === 'match' && <ResultsView response={response} />}
    </>
  )
}

/** Screen-reader summary of what just happened (results replace each other without a page load). */
function Announcer({ state }: { state: SearchState }) {
  let message = ''
  if (state.status === 'loading') message = 'Searching…'
  if (state.status === 'error') message = 'Search failed.'
  if (state.status === 'done') {
    const r = state.response
    const count = `${r.results.length} result${r.results.length === 1 ? '' : 's'}`
    message =
      r.mode === 'noMatch'
        ? `No confident match. ${r.results.length > 0 ? `${count} as guesses.` : ''}`
        : r.hasBestMatch
          ? `Best match: ${r.results[0].title}. ${count}.`
          : `${count}.`
  }
  return (
    <div aria-live="polite" className="sr-only">
      {message}
    </div>
  )
}
