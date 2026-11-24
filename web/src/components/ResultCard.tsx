import * as m from 'motion/react-m'
import type { ReactNode } from 'react'
import type { BookResult } from '../lib/types'
import { authorsLine } from '../lib/labels'
import { Cover } from './Cover'
import { Badge, OpenLibraryLink } from './ui'

interface Props {
  result: BookResult
  rank: number
  label: string
  /** Position in the grid, for a gentle stagger. */
  index: number
}

/** A compact result: cover, title, authors · year, label, reason, link. */
export function ResultCard({ result, rank, label, index }: Props) {
  return (
    <m.article
      initial={{ opacity: 0, y: 6 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.25, delay: Math.min(index, 6) * 0.04 }}
      className="flex flex-col gap-1.5 rounded-xl border border-line bg-card p-[18px]"
    >
      <div className="flex items-start gap-3">
        <Cover id={result.openLibraryId} title={result.title} url={result.coverUrl} size="card" />
        <div className="min-w-0 flex-auto">
          <h3 className="font-display text-xl leading-tight font-semibold">{result.title}</h3>
          <p className="mt-0.5 text-sm text-ink-soft">
            {authorsLine(result)}
            {result.firstPublishYear && ` · ${result.firstPublishYear}`}
          </p>
        </div>
        <span className="text-[13px] font-medium text-muted" aria-label={`Rank ${rank}`}>
          #{rank}
        </span>
      </div>
      <Badge>{label}</Badge>
      <p className="text-sm text-ink-soft">{result.explanation}</p>
      <OpenLibraryLink url={result.url} title={result.title} />
    </m.article>
  )
}

export function ResultGrid({ children }: { children: ReactNode }) {
  return <div className="grid grid-cols-[repeat(auto-fill,minmax(300px,1fr))] gap-4">{children}</div>
}
