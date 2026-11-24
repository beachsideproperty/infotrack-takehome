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
      ) : (
        <StatusPill status={status} onRetry={onRetryStatus} />
      )}
    </header>
  )
}
