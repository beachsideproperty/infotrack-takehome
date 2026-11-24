import type { SearchResponse } from '../lib/types'
import { matchLabel, queryHadAuthor } from '../lib/labels'
import { BestMatchCard } from './BestMatchCard'
import { ResultCard, ResultGrid } from './ResultCard'

/** mode "match": a hero card when there's a clear winner, otherwise a ranked list with no winner. */
export function ResultsView({ response }: { response: SearchResponse }) {
  const hadAuthor = queryHadAuthor(response)
  const [first, ...rest] = response.results

  if (response.hasBestMatch && first) {
    return (
      <>
        <BestMatchCard result={first} label={matchLabel(first, hadAuthor)} />
        {rest.length > 0 && (
          <section aria-labelledby="others-title" className="flex flex-col gap-3.5">
            <h2 id="others-title" className="font-display text-[22px] font-normal">
              Other possible matches
            </h2>
            <ResultGrid>
              {rest.map((r, i) => (
                <ResultCard key={r.openLibraryId} result={r} rank={i + 2} label={matchLabel(r, hadAuthor)} index={i} />
              ))}
            </ResultGrid>
          </section>
        )}
      </>
    )
  }

  const series = response.interpretedAs[0]?.isSeries ? response.interpretedAs[0].title : undefined
  return (
    <section aria-labelledby="list-title" className="flex flex-col gap-3.5">
      <div className="flex flex-col gap-1.5">
        <h1 id="list-title" className="font-display text-[28px] leading-tight font-normal sm:text-[34px]">
          {series ? `Books in the ${series} series` : 'Possible matches'}
        </h1>
        <p className="max-w-[620px] text-ink-soft">
          {series
            ? 'A series has no single answer, so here are its books, strongest match first. Add a title or a character to pick one.'
            : 'No single result clearly beats the rest, so here they are in order. Adding an author, a character or a year usually settles it.'}
        </p>
      </div>
      <ResultGrid>
        {response.results.map((r, i) => (
          <ResultCard key={r.openLibraryId} result={r} rank={i + 1} label={matchLabel(r, hadAuthor)} index={i} />
        ))}
      </ResultGrid>
    </section>
  )
}
