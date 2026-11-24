import { StatusPill } from './components/StatusPill'
import { useApiStatus } from './lib/useApiStatus'

export default function App() {
  const { status, retry } = useApiStatus()

  return (
    <div className="flex min-h-dvh flex-col">
      <header className="flex flex-wrap items-center justify-between gap-3 border-b border-line px-4 py-[18px] sm:px-8">
        <div className="flex items-baseline gap-2.5">
          <span className="font-display text-2xl font-semibold tracking-[-0.01em]">Book Finder</span>
          <span className="text-[13px] text-muted">find the book you half remember</span>
        </div>
        <StatusPill status={status} onRetry={retry} />
      </header>

      <main className="mx-auto flex w-full max-w-[816px] flex-1 flex-col gap-4 px-4 py-16 sm:py-24">
        <h1 className="font-display text-[36px] font-normal leading-[1.05] tracking-[-0.02em] sm:text-[52px]">
          What book are you thinking of?
        </h1>
        <p className="text-[17px] text-muted">
          Type whatever you remember: a title, an author, a character, a year. Messy is fine.
        </p>
        <div className="mt-4 rounded-xl border border-line bg-card p-5 text-ink-soft">
          Search is coming in the next phase. For now this page just checks that the API is reachable.
        </div>
      </main>

      <footer className="flex flex-wrap justify-between gap-2 border-t border-line px-4 py-5 text-[13px] text-muted sm:px-8">
        <span>
          Book data from{' '}
          <a className="text-accent underline underline-offset-2" href="https://openlibrary.org">
            Open Library
          </a>{' '}
          · AI by Gemini
        </span>
        <a className="text-accent underline underline-offset-2" href="https://github.com/beachsideproperty/infotrack-takehome">
          Source on GitHub
        </a>
      </footer>
    </div>
  )
}
