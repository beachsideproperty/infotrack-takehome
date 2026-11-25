import type { AiIssue } from '../lib/types'

/** Headline for each reason Gemini wasn't used, so people know whether trying again will help. */
const degradedHeadline: Record<AiIssue, string> = {
  overloaded: 'Google’s AI is busy right now, so this used basic matching. Try again in a minute.',
  timedOut: 'Google’s AI was too slow to answer, so this used basic matching. Try again in a minute.',
  rateLimited: 'The AI’s per-minute limit was reached, so this used basic matching. Try again in a minute.',
  dailyQuota: 'Today’s free AI quota is used up, so this used basic matching until it resets.',
  notConfigured: 'AI isn’t set up on this server, so this used basic matching.',
  error: 'AI is unavailable, so this used basic matching.',
}

/** Shown when the rule-based parser handled the query because Gemini was unavailable. */
export function DegradedBanner({ issue }: { issue?: AiIssue }) {
  return (
    <div role="status" className="flex items-start gap-3 rounded-[10px] border border-warn-line bg-warn px-[18px] py-3.5">
      <svg aria-hidden="true" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" className="mt-0.5 flex-none text-warn-icon">
        <path d="M12 3l9.5 16.5h-19z" />
        <path d="M12 10v4" />
        <path d="M12 17h.01" />
      </svg>
      <p className="text-warn-ink">
        <strong className="font-semibold">{degradedHeadline[issue ?? 'error']}</strong> Results come straight from Open Library and the
        reasons are shorter. Ambiguous searches may miss.
      </p>
    </div>
  )
}

/** Shown when the "why" lines came from templates rather than Gemini. */
export function TemplateNote() {
  return (
    <p className="text-[13px] text-muted">
      These reasons come from a template filled with the same fetched fields, so they stay accurate, just plainer.
    </p>
  )
}
