import type { SearchError } from '../lib/api'
import { useCountdown } from '../lib/useCountdown'
import { PrimaryButton } from './ui'

const COPY: Record<Exclude<SearchError['kind'], 'validation'>, { title: string; body: string }> = {
  catalogDown: {
    title: 'Can’t reach Open Library right now',
    body: 'Your search is fine. The book catalog isn’t responding, so there’s nothing to match against yet. This usually clears up in a minute.',
  },
  catalogBusy: {
    title: 'Open Library is busy',
    body: 'The book catalog is limiting requests right now. Your search is fine; try again in a few seconds.',
  },
  rateLimited: {
    title: 'Lots of searches at once',
    body: 'Try again in a few seconds. The limit keeps the free book and AI services available for everyone.',
  },
  network: {
    title: 'Can’t reach the search server',
    body: 'It may still be waking up (the free server sleeps when idle) or your connection dropped. Try again in a moment.',
  },
  server: {
    title: 'Something went wrong on our side',
    body: 'The search didn’t finish. Trying again usually works.',
  },
}

interface Props {
  error: SearchError
  onRetry: () => void
}

/** Errors that replace the results. Bad input is handled inline under the search box instead. */
export function ErrorCard({ error, onRetry }: Props) {
  const copy = error.kind === 'validation' ? COPY.server : COPY[error.kind]
  const wait = useCountdown(error.retryAfter)

  return (
    <section role="alert" aria-labelledby="error-title" className="flex gap-4 rounded-[14px] border border-line bg-card p-6 sm:gap-6 sm:p-10">
      <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" className="size-8 flex-none text-danger sm:size-10">
        <circle cx="12" cy="12" r="9" />
        <path d="M12 7.5v5.5" />
        <path d="M12 16.5h.01" />
      </svg>
      <div className="flex flex-col gap-3">
        <h1 id="error-title" className="font-display text-2xl leading-[1.15] font-normal sm:text-[32px]">
          {copy.title}
        </h1>
        <p className="max-w-[600px] text-ink-soft">{copy.body}</p>
        <div className="mt-1 flex flex-wrap items-center gap-4">
          <PrimaryButton onClick={onRetry} disabled={wait > 0} className="h-12 text-[15px]">
            {wait > 0 ? `Try again in ${wait}s` : 'Try again'}
          </PrimaryButton>
          <span className="text-[13px] text-muted">Your query is kept, so you won&rsquo;t need to retype it.</span>
        </div>
      </div>
    </section>
  )
}
