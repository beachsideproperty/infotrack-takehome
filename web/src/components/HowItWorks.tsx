const STEPS = [
  { title: 'Read', text: 'Gemini pulls a likely title, author and keywords out of whatever you type.' },
  { title: 'Search', text: 'We search Open Library and check each book’s primary author, not just contributors.' },
  { title: 'Rank', text: 'Exact title and author first, then close matches, each with a one-line reason.' },
]

export function HowItWorks() {
  return (
    <section id="how" aria-labelledby="how-title" tabIndex={-1} className="flex flex-col gap-4 rounded-sm border-t border-line pt-7">
      <h2 id="how-title" className="text-xs font-semibold tracking-[0.08em] text-muted uppercase">
        How it works
      </h2>
      <ol className="grid gap-6 sm:grid-cols-3">
        {STEPS.map((step, i) => (
          <li key={step.title} className="flex flex-col gap-1.5">
            <span className="font-display text-[22px] font-semibold">
              {i + 1} · {step.title}
            </span>
            <span className="text-ink-soft">{step.text}</span>
          </li>
        ))}
      </ol>
    </section>
  )
}
