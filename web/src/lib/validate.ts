/** Same rules and wording as the API, checked first so the user gets instant feedback. */
export const MAX_QUERY_LENGTH = 300
export const EMPTY_QUERY_MESSAGE = 'Type a title, an author or a few words you remember.'
export const TOO_LONG_MESSAGE = "That's a lot to go on. Try the few words you're surest about."

export function validateQuery(raw: string): string | null {
  const query = raw.trim()
  if (query.length === 0) return EMPTY_QUERY_MESSAGE
  if (query.length > MAX_QUERY_LENGTH) return TOO_LONG_MESSAGE
  return null
}
