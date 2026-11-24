import type { ReactNode } from 'react'

/** Pill labels: `accent` for the best match and the author page, `neutral` for list cards. */
export function Badge({ tone = 'neutral', children }: { tone?: 'accent' | 'neutral'; children: ReactNode }) {
  return tone === 'accent' ? (
    <span className="self-start rounded-full bg-tint px-2.5 py-[3px] text-[13px] font-semibold text-accent">{children}</span>
  ) : (
    <span className="self-start rounded-md bg-tag px-2 py-0.5 text-xs font-medium text-ink-soft">{children}</span>
  )
}

export function Card({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <div className={`rounded-xl border border-line bg-card ${className}`}>{children}</div>
}

export function PrimaryButton({
  children,
  onClick,
  disabled,
  busy,
  type = 'button',
  className = '',
}: {
  children: ReactNode
  onClick?: () => void
  disabled?: boolean
  /** Looks inactive but still works, so Enter can start a new search while one is running. */
  busy?: boolean
  type?: 'button' | 'submit'
  className?: string
}) {
  return (
    <button
      type={type}
      onClick={onClick}
      disabled={disabled}
      aria-disabled={busy || undefined}
      className={`rounded-[10px] bg-accent px-6 font-semibold text-card transition-colors hover:bg-accent-dark disabled:cursor-default disabled:bg-accent-busy aria-disabled:bg-accent-busy ${className}`}
    >
      {children}
    </button>
  )
}

/** "Open Library →" link used on every result. */
export function OpenLibraryLink({ url, title }: { url: string; title: string }) {
  return (
    <a href={url} target="_blank" rel="noreferrer" className="text-sm font-semibold" aria-label={`Open ${title} on Open Library (new tab)`}>
      Open Library →
    </a>
  )
}
