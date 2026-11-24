import { useEffect, useState } from 'react'

/**
 * Seconds left from `seconds`, ticking down to 0. Starts when the component mounts; each new error card
 * mounts fresh, so there's nothing to reset.
 */
export function useCountdown(seconds: number | undefined): number {
  const [left, setLeft] = useState(seconds ?? 0)

  useEffect(() => {
    if (!seconds) return
    const timer = setInterval(() => setLeft((s) => Math.max(0, s - 1)), 1000)
    return () => clearInterval(timer)
  }, [seconds])

  return left
}
