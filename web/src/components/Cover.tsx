import { useState } from 'react'

/** Muted placeholder colours from the mocks; one per work, picked by its id so it never changes. */
const PALETTE = ['#3B4A3F', '#6B4F3A', '#4A5A6B', '#7A6A4F', '#5A3E36', '#3E4F5A', '#6B3A3A', '#4F5A3E', '#2F4A5F', '#5F6B4F']

function colorFor(id: string): string {
  let hash = 0
  for (const ch of id) hash = (hash * 31 + ch.charCodeAt(0)) >>> 0
  return PALETTE[hash % PALETTE.length]
}

const SIZES = {
  // 2:3, like most covers. The hero cover is about as tall as the text beside it; a card's sits
  // alongside its title and author line instead of looking like an icon.
  hero: 'h-[180px] w-[120px] sm:h-[330px] sm:w-[220px]',
  card: 'h-24 w-16',
  row: 'h-[72px] w-12',
} as const

interface Props {
  id: string
  title: string
  url?: string
  size: keyof typeof SIZES
}

/**
 * Open Library cover, or a coloured block with the title when there's no cover or it fails to load.
 * Lists use the medium image; the hero uses the large one.
 */
export function Cover({ id, title, url, size }: Props) {
  const [failed, setFailed] = useState(false)
  const src = url && size !== 'hero' ? url.replace(/-L\.jpg$/, '-M.jpg') : url
  // A soft shadow so a pale cover doesn't blend into the card.
  const frame = `${SIZES[size]} flex-none overflow-hidden ${
    size === 'hero' ? 'rounded shadow-[0_2px_4px_rgb(42_36_32/0.12),0_8px_24px_rgb(42_36_32/0.12)]' : 'rounded-[3px] shadow-[0_1px_3px_rgb(42_36_32/0.18)]'
  }`

  if (src && !failed) {
    return (
      <img
        src={src}
        alt={`Cover of ${title}`}
        loading={size === 'hero' ? 'eager' : 'lazy'}
        className={`${frame} bg-skeleton object-cover`}
        onError={() => setFailed(true)}
        // Open Library answers a missing cover with a 1×1 image rather than an error.
        onLoad={(e) => e.currentTarget.naturalWidth <= 1 && setFailed(true)}
      />
    )
  }

  return (
    <div
      role="img"
      aria-label={`No cover on Open Library for ${title}`}
      className={`${frame} flex flex-col justify-between text-paper ${size === 'hero' ? 'p-3 sm:p-5' : ''}`}
      style={{ background: colorFor(id) }}
    >
      {size === 'hero' && (
        <>
          <span className="line-clamp-5 font-display text-lg leading-tight font-semibold sm:text-[28px]">{title}</span>
          <span className="text-[10px] tracking-[0.1em] uppercase opacity-85 sm:text-[11px]">No cover on Open Library</span>
        </>
      )}
    </div>
  )
}
