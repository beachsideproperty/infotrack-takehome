import { useCallback, useReducer, useRef, useState } from 'react'
import { SearchError, searchBooks } from './api'
import type { SearchResponse } from './types'
import { validateQuery } from './validate'

export type SearchState =
  | { status: 'idle' }
  | { status: 'loading'; query: string; startedAt: number }
  | { status: 'done'; query: string; response: SearchResponse }
  | { status: 'error'; query: string; error: SearchError }

type Action =
  | { type: 'start'; query: string; startedAt: number }
  | { type: 'success'; query: string; response: SearchResponse }
  | { type: 'failure'; query: string; error: SearchError }
  | { type: 'reset' }

function reducer(_: SearchState, action: Action): SearchState {
  switch (action.type) {
    case 'start':
      return { status: 'loading', query: action.query, startedAt: action.startedAt }
    case 'success':
      return { status: 'done', query: action.query, response: action.response }
    case 'failure':
      return { status: 'error', query: action.query, error: action.error }
    case 'reset':
      return { status: 'idle' }
  }
}

/**
 * One search at a time: starting a new one aborts the previous request, so a slow answer can never
 * overwrite a newer one. Validation runs first with the same rules and wording as the API; its message
 * shows under the input and leaves whatever is on screen in place.
 *
 * No abort-on-unmount: the app never unmounts this, and React's dev-mode mount/unmount/mount would
 * otherwise cancel a search started on first render (a ?q= link).
 */
export function useSearch() {
  const [state, dispatch] = useReducer(reducer, { status: 'idle' })
  const [validation, setValidation] = useState<string | null>(null)
  const inFlight = useRef<AbortController | null>(null)

  const search = useCallback(async (raw: string) => {
    const message = validateQuery(raw)
    setValidation(message)
    if (message) return

    const query = raw.trim()
    inFlight.current?.abort()
    const controller = new AbortController()
    inFlight.current = controller
    dispatch({ type: 'start', query, startedAt: Date.now() })

    try {
      const response = await searchBooks(query, controller.signal)
      if (!controller.signal.aborted) dispatch({ type: 'success', query, response })
    } catch (err) {
      if (controller.signal.aborted) return
      const error = err instanceof SearchError ? err : new SearchError('server', 'Something went wrong.')
      if (error.kind === 'validation') {
        // The server disagreed with our check (it's the source of truth): show its message inline.
        setValidation(error.message)
        dispatch({ type: 'reset' })
      } else {
        dispatch({ type: 'failure', query, error })
      }
    }
  }, [])

  const reset = useCallback(() => {
    inFlight.current?.abort()
    setValidation(null)
    dispatch({ type: 'reset' })
  }, [])

  const clearValidation = useCallback(() => setValidation(null), [])

  return { state, validation, search, reset, clearValidation }
}
