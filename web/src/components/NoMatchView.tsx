import type { SearchResponse } from '../lib/types'
import { ResultCard, ResultGrid } from './ResultCard'

interface Props {
  response: SearchResponse
  onSearch: (query: string) => void
}

/**
 * mode "noMatch". Three flavours: keyword-only guesses ("whale lighthouse"), not a book query ("pizza recipe"),
 * or a title/author Open Library doesn't have. Never pretends a guess is a match.
 */
export function NoMatchView({ response, onSearch }: Props) {
  const guesses = response.results
  const notABook = response.interpretedAs.length === 0
  const dym = response.didYouMean
  const dymText = dym && [dym.title, dym.author && `by ${dym.author}`].filter(Boolean).join(' ')

  const message =
    guesses.length > 0
      ? 'Nothing matched a title or an author, so we won’t pretend one is right. A few keywords match thousands of books. Adding an author, a character or a rough year usually does it.'
      : response.parser === 'fallback'
        ? 'Without the AI reader we can only match your words directly, and they didn’t match a title or an author closely enough. Try a title or an author, or search again in a moment.'
        : notABook
          ? 'That doesn’t look like a book we can search for, so we won’t guess. Try a title, an author, a character or a few words you remember from the book.'
          : 'Open Library has nothing close enough to that title or author, so we won’t pretend one is right. Check the spelling, or add an author, a character or a rough year.'

  return (
    <>
      <section aria-labelledby="nomatch-title" className="flex flex-col gap-3.5 rounded-[14px] border border-line bg-card p-6 sm:p-10">
        <h1 id="nomatch-title" className="font-display text-[30px] leading-[1.1] font-normal tracking-[-0.01em] sm:text-[40px]">
          No confident match yet
        </h1>
        <p className="max-w-[620px] text-ink-soft">{message}</p>
        {dym && dymText && (
          <p className="mt-1.5 text-base">
            Did you mean{' '}
            <button
              type="button"
              onClick={() => onSearch([dym.title, dym.author].filter(Boolean).join(' '))}
              className="font-display text-lg font-semibold text-accent underline underline-offset-2 hover:text-accent-dark"
            >
              {dymText}
            </button>
            ?
          </p>
        )}
      </section>

      {guesses.length > 0 && (
        <section aria-labelledby="guesses-title" className="flex flex-col gap-3.5">
          <h2 id="guesses-title" className="font-display text-[22px] font-normal">
            Closest guesses
          </h2>
          <ResultGrid>
            {guesses.map((r, i) => (
              <ResultCard key={r.openLibraryId} result={r} rank={i + 1} label="Keyword only" index={i} />
            ))}
          </ResultGrid>
        </section>
      )}
    </>
  )
}
