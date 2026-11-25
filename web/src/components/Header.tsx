import type { AiHealth } from '../lib/api'
import type { ApiStatus } from '../lib/useApiStatus'
import { StatusPill } from './StatusPill'

interface Props {
  status: ApiStatus
  onRetryStatus: () => void
  onHome: () => void
  onHowItWorks: () => void
}

export function Header({ status, onRetryStatus, onHome, onHowItWorks }: Props) {
  return (
    <header className="flex flex-wrap items-center justify-between gap-3 border-b border-line px-5 py-[18px] sm:px-8">
      <a
        href="/"
        onClick={(e) => {
          e.preventDefault()
          onHome()
        }}
        className="flex items-baseline gap-2.5 text-ink no-underline hover:text-ink"
      >
        <span className="font-display text-[22px] font-semibold tracking-[-0.01em] sm:text-2xl">Book Finder</span>
        <span className="hidden text-[13px] text-muted sm:inline">find the book you half remember</span>
      </a>
      {status.state === 'connected' ? (
        <div className="flex items-center gap-4">
          <AiNote ai={status.ai} />
          <a
            href="#how"
            onClick={(e) => {
              e.preventDefault()
              onHowItWorks()
            }}
            className="text-sm font-medium"
          >
            How it works
          </a>
        </div>
      ) : (
        <StatusPill status={status} onRetry={onRetryStatus} />
      )}
    </header>
  )
}

/**
 * Warns before anyone searches when every Gemini model is resting (or there's no key), so the
 * basic-matching banner on the results isn't a surprise. "limited" isn't shown: the other model still works.
 */
function AiNote({ ai }: { ai?: AiHealth }) {
  if (ai?.status !== 'unavailable' && ai?.status !== 'notConfigured') return null
  const detail =
    ai.status === 'notConfigured'
      ? 'No Gemini key on this server: searches use basic matching.'
      : ai.models.some((m) => m.issue === 'dailyQuota') && ai.models.every((m) => m.issue === 'dailyQuota')
        ? 'Today’s free AI quota is used up: searches use basic matching until it resets.'
        : 'Google’s AI is resting for a moment: searches use basic matching until it’s back.'
  return (
    <span role="status" title={detail} className="flex items-center gap-2 text-[13px] text-muted">
      <span aria-hidden="true" className="size-2 rounded-full bg-amber" />
      <span className="sr-only">{detail}</span>
      <span aria-hidden="true">Basic matching</span>
    </span>
  )
}
