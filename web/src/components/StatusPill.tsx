import type { ApiStatus } from '../lib/useApiStatus'

interface Props {
  status: ApiStatus
  onRetry: () => void
}

export function StatusPill({ status, onRetry }: Props) {
  if (status.state === 'connected') {
    return (
      <span role="status" className="flex items-center gap-2 text-[13px] text-muted">
        <span aria-hidden="true" className="size-2 rounded-full bg-accent" />
        API connected · v{status.version}
      </span>
    )
  }

  if (status.state === 'unreachable') {
    return (
      <span role="status" className="flex items-center gap-2 text-[13px] text-muted">
        <span aria-hidden="true" className="size-2 rounded-full bg-muted" />
        Server unreachable.
        <button
          type="button"
          onClick={onRetry}
          className="text-accent underline underline-offset-2 hover:text-accent-dark focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent rounded"
        >
          Try again
        </button>
      </span>
    )
  }

  return (
    <span role="status" className="flex items-center gap-2 text-[13px] text-muted">
      <span aria-hidden="true" className="size-2 rounded-full bg-amber animate-pulse" />
      Waking up the server…
    </span>
  )
}
