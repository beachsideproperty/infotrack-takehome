import * as m from 'motion/react-m'
import { LOADING_STAGES, useLoadingStage } from '../lib/useLoadingStage'

/** Staged progress (client-side timer over one API request) and skeletons shaped like the real results. */
export function LoadingView({ startedAt }: { startedAt: number }) {
  const active = useLoadingStage(startedAt)

  return (
    <div className="flex flex-col gap-8" aria-busy="true">
      <ol aria-label="Search progress" className="flex flex-wrap gap-x-6 gap-y-2 text-sm">
        {LOADING_STAGES.map((label, i) => {
          const state = i < active ? 'done' : i === active ? 'active' : 'pending'
          return (
            <li key={label} className={`flex items-center gap-2 ${state === 'active' ? 'font-semibold' : ''} ${state === 'pending' ? 'text-muted' : ''}`}>
              <StageIcon state={state} />
              {label}
              <span className="sr-only">{state === 'done' ? '(done)' : state === 'active' ? '(in progress)' : ''}</span>
            </li>
          )
        })}
      </ol>

      <m.div
        aria-hidden="true"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ duration: 0.3 }}
        className="flex flex-wrap gap-8 rounded-[14px] border border-line bg-card p-5 sm:p-8"
      >
        <div className="h-[180px] w-[120px] flex-none rounded bg-skeleton sm:h-[330px] sm:w-[220px]" />
        <div className="flex min-w-0 flex-[999_1_320px] flex-col gap-4">
          <div className="h-3.5 w-[120px] rounded bg-skeleton" />
          <div className="h-12 w-[55%] rounded-md bg-skeleton" />
          <div className="h-[18px] w-[30%] rounded bg-skeleton" />
          <div className="h-14 w-full rounded-md bg-skeleton-soft" />
          <div className="h-3.5 w-4/5 rounded bg-skeleton-soft" />
          <div className="h-3.5 w-[70%] rounded bg-skeleton-soft" />
        </div>
      </m.div>

      <div aria-hidden="true" className="grid grid-cols-[repeat(auto-fill,minmax(300px,1fr))] gap-4">
        {[0, 1, 2].map((i) => (
          <div key={i} className="h-[132px] rounded-xl border border-line bg-card" />
        ))}
      </div>
    </div>
  )
}

function StageIcon({ state }: { state: 'done' | 'active' | 'pending' }) {
  if (state === 'done') {
    return (
      <m.span
        aria-hidden="true"
        initial={{ scale: 0.6 }}
        animate={{ scale: 1 }}
        className="flex size-5 items-center justify-center rounded-full bg-accent text-xs text-card"
      >
        ✓
      </m.span>
    )
  }
  return (
    <span
      aria-hidden="true"
      className={`size-5 rounded-full border-2 ${
        state === 'active' ? 'border-accent border-r-skeleton motion-safe:animate-spin' : 'border-line'
      }`}
    />
  )
}
