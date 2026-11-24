import * as m from 'motion/react-m'
import type { BookResult } from '../lib/types'
import { authorsLine } from '../lib/labels'
import { Cover } from './Cover'
import { Badge } from './ui'

/** The hero card, shown only when the API says there's a clear winner (`hasBestMatch`). */
export function BestMatchCard({ result, label }: { result: BookResult; label: string }) {
  return (
    <m.article
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.3 }}
      aria-labelledby="best-title"
      className="flex flex-wrap gap-5 rounded-[14px] border border-line bg-card p-5 sm:gap-8 sm:p-8"
    >
      <Cover id={result.openLibraryId} title={result.title} url={result.coverUrl} size="hero" />

      <div className="flex min-w-0 flex-[999_1_320px] flex-col gap-[18px]">
        <div className="flex flex-wrap items-center gap-3">
          <span className="text-xs font-semibold tracking-[0.08em] text-muted uppercase">Best match</span>
          <Badge tone="accent">{label}</Badge>
        </div>
        <div>
          <h1 id="best-title" className="font-display text-4xl leading-[1.05] font-semibold tracking-[-0.02em] sm:text-[52px]">
            {result.title}
          </h1>
          <p className="mt-2 text-base sm:text-lg">
            {authorsLine(result)}
            {result.firstPublishYear && <span className="sm:hidden"> · {result.firstPublishYear}</span>}
          </p>
        </div>

        <dl className="hidden grid-cols-3 border-y border-line sm:grid">
          <Fact term="First published (Open Library)" value={result.firstPublishYear?.toString() ?? 'Not listed'} />
          <Fact term={result.authors.length > 1 ? 'Primary authors' : 'Primary author'} value={authorsLine(result)} />
          <Fact term="Contributors" value={result.contributors.length > 0 ? result.contributors.join(', ') : 'None listed'} />
        </dl>

        <div>
          <h2 className="mb-2 text-sm font-semibold">Why this match</h2>
          <p className="max-w-[560px] text-ink-soft">{result.explanation}</p>
        </div>
        <a
          href={result.url}
          target="_blank"
          rel="noreferrer"
          className="flex h-12 items-center justify-center rounded-[10px] border border-accent font-semibold no-underline sm:h-auto sm:justify-start sm:self-start sm:border-0 sm:underline"
        >
          View on Open Library
        </a>
      </div>
    </m.article>
  )
}

function Fact({ term, value }: { term: string; value: string }) {
  return (
    <div className="py-3 pr-3">
      <dt className="text-xs text-muted">{term}</dt>
      <dd className="mt-0.5 font-medium">{value}</dd>
    </div>
  )
}
