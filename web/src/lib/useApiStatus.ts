import { useCallback, useEffect, useState } from 'react'
import { getHealth } from './api'

export type ApiStatus =
  | { state: 'waking' }
  | { state: 'connected'; version: string }
  | { state: 'unreachable' }

// Render's free tier sleeps; a cold start can take ~50 s, so keep retrying for a minute.
const GIVE_UP_AFTER_MS = 60_000
const RETRY_EVERY_MS = 3_000
const REQUEST_TIMEOUT_MS = 10_000

export function useApiStatus() {
  const [status, setStatus] = useState<ApiStatus>({ state: 'waking' })
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let cancelled = false
    const startedAt = Date.now()
    let timer: ReturnType<typeof setTimeout> | undefined
    let controller: AbortController | undefined

    const ping = async () => {
      controller = new AbortController()
      const timeout = setTimeout(() => controller?.abort(), REQUEST_TIMEOUT_MS)
      try {
        const health = await getHealth(controller.signal)
        if (!cancelled) setStatus({ state: 'connected', version: health.version })
      } catch {
        if (cancelled) return
        if (Date.now() - startedAt >= GIVE_UP_AFTER_MS) setStatus({ state: 'unreachable' })
        else timer = setTimeout(ping, RETRY_EVERY_MS)
      } finally {
        clearTimeout(timeout)
      }
    }
    void ping()

    return () => {
      cancelled = true
      clearTimeout(timer)
      controller?.abort()
    }
  }, [attempt])

  const retry = useCallback(() => {
    setStatus({ state: 'waking' })
    setAttempt((n) => n + 1)
  }, [])
  return { status, retry }
}
