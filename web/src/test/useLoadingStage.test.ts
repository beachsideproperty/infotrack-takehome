import { act, renderHook } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { useLoadingStage } from '../lib/useLoadingStage'

describe('useLoadingStage', () => {
  it('walks through Read → Search → Rank on a timer', () => {
    vi.useFakeTimers()
    const startedAt = Date.now()
    const { result } = renderHook(() => useLoadingStage(startedAt))
    expect(result.current).toBe(0)

    act(() => vi.advanceTimersByTime(1000))
    expect(result.current).toBe(1)

    act(() => vi.advanceTimersByTime(3000))
    expect(result.current).toBe(2)

    act(() => vi.advanceTimersByTime(60_000))
    expect(result.current).toBe(2) // waits on the last stage until the answer arrives
  })
})
