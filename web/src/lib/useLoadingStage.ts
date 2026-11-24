import { useEffect, useState } from 'react'

export const LOADING_STAGES = ['Read your words', 'Searching Open Library', 'Ranking matches'] as const

/** When each stage starts, in ms after the search starts. The API is one request; this paces the story. */
export const STAGE_STARTS_MS = [0, 900, 3500] as const

/** Index of the active stage for a search started at `startedAt` (client-side timer, never ahead of the answer). */
export function useLoadingStage(startedAt: number | null): number {
  const [stage, setStage] = useState(0)

  useEffect(() => {
    if (startedAt === null) return
    const update = () => {
      const elapsed = Date.now() - startedAt
      setStage(STAGE_STARTS_MS.filter((start) => elapsed >= start).length - 1)
    }
    update()
    const timer = setInterval(update, 200)
    return () => clearInterval(timer)
  }, [startedAt])

  return startedAt === null ? 0 : stage
}
