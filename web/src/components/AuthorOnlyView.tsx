import * as m from 'motion/react-m'
import type { SearchResponse } from '../lib/types'
import { authorsLine } from '../lib/labels'
import { Cover } from './Cover'
import { Badge, OpenLibraryLink } from './ui'

/** mode "authorOnly": the author's most-read works, no hero card. */
export function AuthorOnlyView({ response }: { response: SearchResponse }) {
  const name = response.author?.name ?? response.interpretedAs[0]?.author ?? response.results[0]?.authors[0] ?? 'this author'

  return (
    <section aria-labelledby="author-title" className="flex flex-col gap-[18px]">
      <div className="flex flex-col gap-1.5">
        <Badge tone="accent">Author only · no title in your search</Badge>
        <h1 id="author-title" className="font-display text-[32px] leading-[1.1] font-normal tracking-[-0.01em] sm:text-[44px]">
          Top works by {name}
        </h1>
        <p className="max-w-[620px] text-ink-soft">
          Most-read first, by Open Library reading logs. Add a title or a few words from the book to get a single best match.
        </p>
      </div>

      <ol className="flex flex-col gap-3">
        {response.results.map((r, i) => (
          <m.li
            key={r.openLibraryId}
            initial={{ opacity: 0, y: 6 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.25, delay: i * 0.04 }}
            className="flex flex-wrap items-center gap-x-[18px] gap-y-2 rounded-xl border border-line bg-card px-5 py-[18px]"
          >
            <span className="w-6 flex-none font-display text-[22px] text-muted sm:w-8" aria-label={`Rank ${i + 1}`}>
              {i + 1}
            </span>
            <Cover id={r.openLibraryId} title={r.title} url={r.coverUrl} size="row" />
            <div className="flex min-w-0 flex-[999_1_240px] flex-col gap-1">
              <h2 className="font-display text-[21px] leading-tight font-semibold">{r.title}</h2>
              <p className="text-sm text-ink-soft">
                {authorsLine(r)}
                {r.firstPublishYear && ` · first published ${r.firstPublishYear} (Open Library)`}
              </p>
              <p className="text-sm text-ink-soft">{r.explanation}</p>
            </div>
            <OpenLibraryLink url={r.url} title={r.title} />
          </m.li>
        ))}
      </ol>

      {response.author && (
        <a href={response.author.url} target="_blank" rel="noreferrer" className="self-start text-sm font-semibold">
          All works by {response.author.name} on Open Library →
        </a>
      )}
    </section>
  )
}
