import type { RefObject } from 'react'
import { EXAMPLES } from '../lib/examples'
import { PrimaryButton } from './ui'

interface Props {
  /** The large first-visit layout vs the compact one above results. */
  home: boolean
  value: string
  onChange: (value: string) => void
  onSubmit: (query: string) => void
  loading: boolean
  validation: string | null
  showExamples: boolean
  inputRef: RefObject<HTMLInputElement | null>
}

export function SearchForm({ home, value, onChange, onSubmit, loading, validation, showExamples, inputRef }: Props) {
  return (
    <form
      noValidate
      onSubmit={(e) => {
        e.preventDefault()
        onSubmit(value)
      }}
      className={`flex flex-col ${home ? 'gap-3.5' : 'gap-3'}`}
    >
      <label
        htmlFor="q"
        className={
          home
            ? 'font-display text-[34px] leading-[1.05] font-normal tracking-[-0.02em] sm:text-[52px]'
            : 'font-display text-[28px] leading-[1.15] font-normal tracking-[-0.01em] sm:text-[34px]'
        }
      >
        What book are you thinking of?
      </label>
      {home && (
        <p className="text-[15px] text-muted sm:text-[17px]">
          <span className="sm:hidden">Title, author, a character, a year. Messy is fine.</span>
          <span className="hidden sm:inline">Type whatever you remember: a title, an author, a character, a year. Messy is fine.</span>
        </p>
      )}

      <div className={`flex flex-wrap gap-3 ${home ? 'mt-2' : ''}`}>
        <input
          ref={inputRef}
          id="q"
          name="q"
          type="search"
          autoComplete="off"
          enterKeyHint="search"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder="e.g. tolkien hobbit illustrated 1937"
          aria-invalid={validation ? true : undefined}
          aria-describedby={validation ? 'q-error' : 'q-hint'}
          className={`min-w-0 flex-[999_1_420px] border bg-card px-5 text-ink placeholder:text-muted/80 ${
            validation ? 'border-danger' : 'border-line-strong'
          } ${home ? 'h-[60px] rounded-xl text-lg' : 'h-14 rounded-[10px] text-[17px]'}`}
        />
        <PrimaryButton type="submit" busy={loading} className={`flex-[1_0_auto] text-base ${home ? 'h-[60px] rounded-xl px-8' : 'h-14 px-7'}`}>
          {loading ? 'Searching…' : 'Find book'}
        </PrimaryButton>
      </div>
      <span id="q-hint" className="sr-only">
        Press slash to jump here from anywhere on the page.
      </span>

      {validation && (
        <p id="q-error" role="alert" className="text-sm font-medium text-danger">
          {validation}
        </p>
      )}

      {showExamples && (
        <div className="flex flex-wrap items-center gap-2 text-[13px] text-muted">
          <span className="hidden sm:inline">Try</span>
          {EXAMPLES.map((example) => (
            <button
              key={example}
              type="button"
              onClick={() => {
                onChange(example)
                onSubmit(example)
              }}
              className="min-h-11 rounded-full border border-line px-3.5 text-[13px] text-ink transition-colors hover:border-line-strong hover:bg-card sm:min-h-0 sm:py-1.5"
            >
              {example}
            </button>
          ))}
        </div>
      )}
    </form>
  )
}
