import { useState, useRef, useLayoutEffect, useEffect } from 'react'
import { createPortal } from 'react-dom'

function parseFavNums(favoriteTrackNumbers) {
  if (Array.isArray(favoriteTrackNumbers)) return favoriteTrackNumbers
  if (typeof favoriteTrackNumbers === 'string')
    return favoriteTrackNumbers
      .split(',')
      .map((n) => parseInt(n.trim(), 10))
      .filter(Boolean)
  return []
}

function FavouritesPanel({ favNums, namesMap }) {
  const [isOpen, setIsOpen] = useState(false)
  const buttonRef = useRef(null)
  const panelRef = useRef(null)
  const [position, setPosition] = useState(null)
  const count = favNums.length

  useLayoutEffect(() => {
    if (!isOpen) { setPosition(null); return }
    function compute() {
      const btn = buttonRef.current
      if (!btn) return
      const rect = btn.getBoundingClientRect()
      const panelW = Math.min(288, window.innerWidth - 16)
      const panelH = panelRef.current?.offsetHeight ?? 240
      const margin = 8
      let top = rect.top - panelH - margin
      if (top < margin) top = Math.min(rect.bottom + margin, window.innerHeight - panelH - margin)
      let left = rect.left
      if (left + panelW + margin > window.innerWidth) left = window.innerWidth - panelW - margin
      if (left < margin) left = margin
      setPosition({ top, left, width: panelW })
    }
    compute()
    const raf = requestAnimationFrame(compute)
    window.addEventListener('scroll', compute, true)
    window.addEventListener('resize', compute)
    return () => {
      cancelAnimationFrame(raf)
      window.removeEventListener('scroll', compute, true)
      window.removeEventListener('resize', compute)
    }
  }, [isOpen, count])

  useEffect(() => {
    if (!isOpen) return
    function handler(e) {
      if (panelRef.current?.contains(e.target)) return
      if (buttonRef.current?.contains(e.target)) return
      setIsOpen(false)
    }
    function onKey(e) { if (e.key === 'Escape') setIsOpen(false) }
    document.addEventListener('mousedown', handler)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', handler)
      document.removeEventListener('keydown', onKey)
    }
  }, [isOpen])

  if (count === 0) {
    return (
      <div className="mt-5 pt-4 border-t border-slate-700/40">
        <p className="text-xs text-slate-600">
          Based on: <span className="text-slate-500">full playlist</span>
        </p>
      </div>
    )
  }

  const list = favNums.map((n) => namesMap?.[n] ?? `Track #${n}`)

  return (
    <div className="mt-5 pt-4 border-t border-slate-700/40">
      <button
        ref={buttonRef}
        type="button"
        onClick={() => setIsOpen(!isOpen)}
        className="inline-flex items-center gap-1.5 text-xs text-slate-500 hover:text-violet-300 transition-colors px-2 py-1 -mx-2 rounded-lg hover:bg-violet-500/10"
        aria-expanded={isOpen}
      >
        <svg className="w-3.5 h-3.5 text-violet-400" viewBox="0 0 24 24" fill="currentColor">
          <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2" />
        </svg>
        <span>{count} chosen favourite{count !== 1 ? 's' : ''}</span>
        <svg
          className={`w-3 h-3 transition-transform ${isOpen ? 'rotate-180' : ''}`}
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
          strokeWidth="2.5"
        >
          <polyline points="6 9 12 15 18 9" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </button>

      {isOpen &&
        createPortal(
          <div
            ref={panelRef}
            className="fixed rounded-2xl border border-violet-500/20 shadow-2xl z-[60] overflow-hidden"
            style={{
              backgroundColor: '#1a1d2e',
              top: position?.top ?? -9999,
              left: position?.left ?? -9999,
              width: position?.width ?? 288,
              visibility: position ? 'visible' : 'hidden',
            }}
          >
            <div className="px-4 py-2.5 border-b border-slate-700/40 flex items-center justify-between">
              <span className="text-xs font-semibold text-violet-300 uppercase tracking-wider">
                Chosen favourites
              </span>
              <span className="text-xs text-slate-600">{count}</span>
            </div>
            <ul className="max-h-60 overflow-y-auto py-1">
              {list.map((name, idx) => (
                <li
                  key={idx}
                  className="flex items-start gap-2.5 px-4 py-1.5 text-xs text-slate-300 hover:bg-white/5"
                >
                  <span className="text-slate-700 flex-shrink-0 w-4 text-right">{idx + 1}</span>
                  <span className="flex-1 break-words leading-snug">{name}</span>
                </li>
              ))}
            </ul>
          </div>,
          document.body
        )}
    </div>
  )
}

export default function RecommendationResult({ recommendations, tracks, onDismiss }) {
  if (!recommendations?.length) return null

  const isSingle = recommendations.length === 1
  const first = recommendations[0]

  const favNums = parseFavNums(first.favoriteTrackNumbers)
  const namesMap = tracks
    ? Object.fromEntries(
        tracks.map((t) => [t.trackNumber, `${t.trackName} — ${t.artistName}`])
      )
    : {}

  return (
    <div
      className="rounded-2xl border border-violet-500/30 p-6"
      style={{
        background:
          'linear-gradient(135deg, rgba(109,40,217,0.12) 0%, rgba(139,92,246,0.06) 100%)',
      }}
    >
      {/* Header */}
      <div className="flex items-start justify-between mb-5">
        <div className="flex items-center gap-2.5">
          <div className="w-8 h-8 rounded-xl bg-violet-500/20 flex items-center justify-center flex-shrink-0">
            <svg
              className="w-4 h-4 text-violet-400"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              strokeWidth="2"
            >
              <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2" />
            </svg>
          </div>
          <span className="text-violet-300 text-xs font-semibold uppercase tracking-widest">
            Recommended for you
          </span>
          {!isSingle && (
            <span className="text-xs px-2 py-0.5 rounded-full bg-violet-500/15 text-violet-400 border border-violet-500/20">
              {recommendations.length} tracks
            </span>
          )}
        </div>
        {onDismiss && (
          <button
            onClick={onDismiss}
            className="text-slate-600 hover:text-slate-300 transition-colors p-1 flex-shrink-0"
          >
            <svg
              className="w-4 h-4"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              strokeWidth="2"
            >
              <line x1="18" y1="6" x2="6" y2="18" strokeLinecap="round" />
              <line x1="6" y1="6" x2="18" y2="18" strokeLinecap="round" />
            </svg>
          </button>
        )}
      </div>

      {isSingle ? (
        /* Single recommendation — original full layout */
        <div className="flex items-center gap-4">
          <div className="w-14 h-14 rounded-2xl bg-violet-500/20 flex items-center justify-center flex-shrink-0">
            <svg
              className="w-7 h-7 text-violet-400"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              strokeWidth="1.5"
            >
              <path d="M9 18V5l12-2v13" strokeLinecap="round" strokeLinejoin="round" />
              <circle cx="6" cy="18" r="3" />
              <circle cx="18" cy="16" r="3" />
            </svg>
          </div>
          <div className="min-w-0">
            <p className="text-xl font-bold text-white leading-tight">
              {first.suggestedTrackName}
            </p>
            <p className="text-slate-400 text-sm mt-1">{first.suggestedArtist}</p>
          </div>
        </div>
      ) : (
        /* Multiple recommendations — compact numbered list */
        <div className="space-y-1.5">
          {recommendations.map((rec, i) => (
            <div
              key={rec.id ?? i}
              className="flex items-center gap-3 px-3 py-2.5 rounded-xl border border-slate-700/30"
              style={{ backgroundColor: 'rgba(255,255,255,0.025)' }}
            >
              <span className="text-xs text-slate-600 w-4 text-right flex-shrink-0 font-mono tabular-nums">
                {i + 1}
              </span>
              <div className="w-7 h-7 rounded-lg bg-violet-500/15 flex items-center justify-center flex-shrink-0">
                <svg
                  className="w-3.5 h-3.5 text-violet-400"
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                  strokeWidth="1.5"
                >
                  <path d="M9 18V5l12-2v13" strokeLinecap="round" strokeLinejoin="round" />
                  <circle cx="6" cy="18" r="3" />
                  <circle cx="18" cy="16" r="3" />
                </svg>
              </div>
              <div className="flex-1 min-w-0">
                <p className="text-sm font-semibold text-white truncate leading-tight">
                  {rec.suggestedTrackName}
                </p>
                <p className="text-xs text-slate-400 truncate mt-0.5">{rec.suggestedArtist}</p>
              </div>
            </div>
          ))}
        </div>
      )}

      <FavouritesPanel favNums={favNums} namesMap={namesMap} />
    </div>
  )
}
