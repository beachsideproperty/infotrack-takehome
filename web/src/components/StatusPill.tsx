import type { ApiStatus } from '../lib/useApiStatus'

interface Props {
  status: ApiStatus
  onRetry: () => void
}

/** Shown in the header until the API answers /health (Render's free tier sleeps when idle). */
export function StatusPill({ status, onRetry }: Props) {
  if (status.state === 'unreachable') {
    return (
      <span role="status" className="flex items-center gap-2 text-[13px] text-muted">
        <span aria-hidden="true" className="size-2 rounded-full bg-muted" />
        Server unreachable.
        <button type="button" onClick={onRetry} className="rounded text-accent underline underline-offset-2 hover:text-accent-dark">
          Try again
        </button>
      </span>
    )
  }

  return (
    <span role="status" className="flex items-center gap-2 text-[13px] text-muted">
      <span aria-hidden="true" className="size-2 rounded-full bg-amber motion-safe:animate-pulse" />
      <span className="sm:hidden">Waking up…</span>
      <span className="hidden sm:inline">Waking up the server…</span>
    </span>
  )
}
