import { useState, useEffect, useRef, useLayoutEffect } from 'react'
import { createPortal } from 'react-dom'
import { api } from '../api/client'
import LoadingSpinner from '../components/LoadingSpinner'
import ErrorMessage from '../components/ErrorMessage'

/**
 * Formats recommendation timestamps for the compact history row date label.
 */
function formatDate(dateStr) {
  // Convert backend UTC timestamp into local short date/time text.
  return new Date(dateStr).toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

/**
 * Keeps long playlist URLs from dominating the history card header.
 */
function truncateUrl(url, max = 45) {
  // Keep empty/short URLs unchanged.
  if (!url || url.length <= max) return url

  // Slice long URLs and add ellipsis for compact card headers.
  return url.slice(0, max) + '…'
}

/**
 * Confirmation dialog used for deleting one suggestion or clearing a whole playlist's history.
 */
function ConfirmModal({ title, message, confirmLabel = 'Delete', onClose, onConfirm, busy }) {
  return createPortal(
    /* Modal overlay for destructive history actions. */
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      style={{ backgroundColor: 'rgba(0,0,0,0.65)', backdropFilter: 'blur(6px)' }}
      onClick={(e) => e.target === e.currentTarget && onClose()}
    >
      <div
        className="w-full max-w-md rounded-2xl border border-slate-700/60 p-6 shadow-2xl"
        style={{ backgroundColor: '#131520' }}
      >
        <h2 className="text-base font-semibold text-slate-100 mb-2">{title}</h2>
        <p className="text-sm text-slate-500 mb-5">{message}</p>
        <div className="flex gap-3">
          <button
            type="button"
            onClick={onClose}
            disabled={busy}
            className="flex-1 py-2.5 rounded-xl text-slate-400 hover:text-slate-200 text-sm font-medium border border-slate-700/60 hover:border-slate-600 disabled:opacity-50"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={onConfirm}
            disabled={busy}
            className="flex-1 py-2.5 rounded-xl bg-red-600 hover:bg-red-500 disabled:opacity-50 text-white text-sm font-medium"
          >
            {busy ? 'Working…' : confirmLabel}
          </button>
        </div>
      </div>
    </div>,
    document.body
  )
}

/**
 * History popover that lists the favourite tracks used to produce a suggestion. It receives open
 * state from the parent so only one suggestion panel is expanded at a time.
 */
function FavouritesPanel({ names, numbers, namesMap, isOpen, onToggle }) {
  // Trigger/panel refs support popover positioning and outside click handling.
  const buttonRef = useRef(null)
  const panelRef = useRef(null)

  // Fixed viewport coordinates used by the portal.
  const [position, setPosition] = useState(null)

  // Favourite track numbers from the suggestion, or an empty list.
  const nums = numbers || []

  // Resolve each number into a display name using fresh namesMap first, then stored names.
  const list = nums.length
    ? nums.map((n, i) => namesMap?.[n] ?? names?.[i] ?? `Track #${n}`)
    : []

  // Count controls label text and empty-state behavior.
  const count = list.length

  // Recompute portal placement while open so the popover stays attached to its trigger during
  // scroll and resize.
  useLayoutEffect(() => {
    if (!isOpen) {
      setPosition(null)
      return
    }
    /**
     * Calculates fixed portal coordinates for the favourites popover while keeping it inside the
     * viewport.
     */
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

  // Close on outside click and Escape to match standard popover behavior.
  useEffect(() => {
    if (!isOpen) return
    /**
     * Closes the popover when a click starts outside both the trigger and the panel.
     */
    function handler(e) {
      if (panelRef.current?.contains(e.target)) return
      if (buttonRef.current?.contains(e.target)) return
      onToggle(false)
    }
    /**
     * Keyboard escape handler for dismissing the favourites popover.
     */
    function onKey(e) {
      if (e.key === 'Escape') onToggle(false)
    }
    document.addEventListener('mousedown', handler)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', handler)
      document.removeEventListener('keydown', onKey)
    }
  }, [isOpen, onToggle])

  // No favourite numbers means the backend generated the suggestion from the full playlist.
  if (count === 0) {
    return (
      <p className="text-xs text-slate-700 mt-1.5">
        Favourites: <span className="text-slate-600">none — full playlist used</span>
      </p>
    )
  }

  return (
    /* Compact favourites control below a history suggestion. */
    <div className="mt-1.5">
      <button
        ref={buttonRef}
        type="button"
        onClick={() => onToggle(!isOpen)}
        className="inline-flex items-center gap-1.5 text-xs text-slate-500 hover:text-violet-300 transition-colors px-2 py-1 -mx-2 rounded-lg hover:bg-violet-500/10"
        aria-expanded={isOpen}
      >
        <svg className="w-3.5 h-3.5 text-violet-400" viewBox="0 0 24 24" fill="currentColor">
          <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2" />
        </svg>
        <span>
          {count} favourite{count !== 1 ? 's' : ''}
        </span>
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
          /* Portal popover with resolved favourite track names. */
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

/**
 * One recommendation row inside a playlist history card. It owns deletion confirmation and reports
 * successful deletion to the parent so the row disappears immediately.
 */
function SuggestionRow({ suggestion, namesMap, isPanelOpen, onPanelToggle, onDelete }) {
  // Confirmation modal visibility for this row.
  const [confirm, setConfirm] = useState(false)

  // Busy state disables delete confirmation while request is in flight.
  const [busy, setBusy] = useState(false)

  // Per-row delete error.
  const [error, setError] = useState(null)

  /**
   * Deletes this recommendation history item from the backend and updates the local history list.
   */
  async function handleDelete() {
    // Disable controls and clear stale row errors.
    setBusy(true)
    setError(null)
    try {
      // Delete this history item through the API.
      await api.deleteRecommendation(suggestion.id)

      // Notify parent so it removes the row immediately.
      onDelete?.(suggestion.id)
    } catch (err) {
      // Keep modal open and show the failure.
      setError(err.message)
      setBusy(false)
    }
  }

  return (
    <div className="flex items-start gap-4 px-5 py-4 hover:bg-white/2 transition-colors group">
      <div className="w-8 h-8 rounded-xl bg-violet-500/14 flex items-center justify-center flex-shrink-0 mt-0.5">
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

      <div className="flex-1 min-w-0">
        <p className="text-sm font-semibold text-slate-200 leading-tight">
          {suggestion.suggestedTrackName}
        </p>
        <p className="text-xs text-slate-500 mt-0.5">{suggestion.suggestedArtist}</p>
        <FavouritesPanel
          names={suggestion.favoriteTrackNames}
          numbers={suggestion.favoriteTrackNumbers}
          namesMap={namesMap}
          isOpen={isPanelOpen}
          onToggle={onPanelToggle}
        />
        {error && <p className="text-xs text-red-400 mt-1">{error}</p>}
      </div>

      <div className="flex items-center gap-3 flex-shrink-0">
        <span className="text-xs text-slate-700 text-right leading-tight">
          {formatDate(suggestion.createdAt)}
        </span>
        <button
          onClick={() => setConfirm(true)}
          className="p-1.5 rounded-lg text-slate-600 hover:text-red-400 hover:bg-red-500/10 opacity-0 group-hover:opacity-100 transition-all"
          aria-label="Delete suggestion"
        >
          <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" strokeWidth="2">
            <polyline points="3 6 5 6 21 6" strokeLinecap="round" strokeLinejoin="round" />
            <path
              d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"
              strokeLinecap="round"
              strokeLinejoin="round"
            />
          </svg>
        </button>
      </div>

      {confirm && (
        <ConfirmModal
          title="Delete this suggestion?"
          message="This single recommendation entry will be removed from history."
          onClose={() => setConfirm(false)}
          onConfirm={handleDelete}
          busy={busy}
        />
      )}
    </div>
  )
}

/**
 * Grouped history card for all suggestions generated from one playlist. It also resolves favourite
 * track numbers back to names when the backend did not already include them.
 */
function PlaylistHistoryCard({ entry, openPanelId, onPanelToggle, onSuggestionDeleted, onPlaylistCleared }) {
  // Infer source platform from the stored playlist URL.
  const isSpotify = entry.playlistUrl?.includes('spotify.com')
  const isYoutube =
    entry.playlistUrl?.includes('youtube.com') || entry.playlistUrl?.includes('youtu.be')

  const badgeClass = isSpotify
    ? 'bg-green-500/12 text-green-400 border-green-500/20'
    : isYoutube
    ? 'bg-red-500/12 text-red-400 border-red-500/20'
    : 'bg-slate-700/50 text-slate-400 border-slate-600/30'

  const platform = isSpotify ? 'Spotify' : isYoutube ? 'YouTube' : 'Playlist'

  // Confirmation state for clearing this playlist's full history.
  const [confirmClear, setConfirmClear] = useState(false)

  // Busy state for clear-all request.
  const [busy, setBusy] = useState(false)

  // Map of favourite track number -> readable track label.
  const [namesMap, setNamesMap] = useState(null)

  // Fetch only the favourite tracks referenced by this card, not the entire playlist.
  useEffect(() => {
    const allNumbers = [
      // Flatten all favourite numbers and de-duplicate them.
      ...new Set(entry.suggestions.flatMap((s) => s.favoriteTrackNumbers || [])),
    ]

    // No favourite numbers means there is nothing to resolve.
    if (!allNumbers.length) {
      setNamesMap({})
      return
    }

    // Cancellation flag prevents state updates after unmount or dependency change.
    let cancelled = false
    api
      .getTracks(entry.playlistId, allNumbers)
      .then((tracks) => {
        // Ignore late responses after cleanup.
        if (cancelled) return

        // Build number -> "title - artist" lookup.
        const map = {}
        for (const t of tracks || []) {
          map[t.trackNumber] = `${t.trackName} — ${t.artistName}`
        }
        setNamesMap(map)
      })
      .catch(() => {
        // If lookup fails, fallback labels are still usable.
        if (!cancelled) setNamesMap({})
      })
    return () => {
      // Mark the request as obsolete.
      cancelled = true
    }
  }, [entry.playlistId, entry.suggestions])

  /**
   * Clears all recommendation rows for this playlist and notifies the parent to remove the card.
   */
  async function handleClear() {
    // Disable clear-all button while request is running.
    setBusy(true)
    try {
      // Delete all recommendation history for this playlist.
      await api.deletePlaylistHistory(entry.playlistId)

      // Remove the card from parent state.
      onPlaylistCleared?.(entry.playlistId)
    } catch (err) {
      // Keep clear-all errors in console because the modal has no local error display.
      console.error(err)
    }

    // Re-enable controls if the card is still mounted.
    setBusy(false)
  }

  // Custom playlist name, if present, is shown before the platform badge.
  const renamedName = entry.playlistName?.trim()

  return (
    <div
      className="rounded-2xl border border-slate-700/40 overflow-hidden"
      style={{ backgroundColor: '#131520' }}
    >
      {/* Card header */}
      <div className="flex items-center justify-between px-5 py-3.5 border-b border-slate-700/40">
        <div className="flex items-center gap-3 min-w-0">
          {renamedName && (
            <span className="text-sm font-semibold text-slate-100 truncate">{renamedName}</span>
          )}
          <span
            className={`inline-flex items-center px-2.5 py-1 rounded-full text-xs font-medium border flex-shrink-0 ${badgeClass}`}
          >
            {platform}
          </span>
          <span className="text-xs text-slate-400 truncate font-medium">
            {truncateUrl(entry.playlistUrl)}
          </span>
        </div>
        <div className="flex items-center gap-3 flex-shrink-0 ml-3">
          <span className="text-xs text-slate-700">
            {entry.suggestions.length} suggestion{entry.suggestions.length !== 1 ? 's' : ''}
          </span>
          <button
            onClick={() => setConfirmClear(true)}
            className="text-xs text-slate-600 hover:text-red-400 px-2.5 py-1 rounded-lg hover:bg-red-500/10 transition-all"
          >
            Clear all
          </button>
        </div>
      </div>

      {/* Suggestions */}
      <div className="divide-y divide-slate-800/50">
        {entry.suggestions.map((s) => (
          <SuggestionRow
            key={s.id}
            suggestion={s}
            namesMap={namesMap}
            isPanelOpen={openPanelId === s.id}
            onPanelToggle={(open) => onPanelToggle(open ? s.id : null)}
            onDelete={(id) => onSuggestionDeleted?.(entry.playlistId, id)}
          />
        ))}
      </div>

      {confirmClear && (
        <ConfirmModal
          title="Clear all history for this playlist?"
          message={`All ${entry.suggestions.length} recommendation${
            entry.suggestions.length !== 1 ? 's' : ''
          } for this playlist will be deleted. The playlist itself stays.`}
          confirmLabel="Clear all"
          onClose={() => setConfirmClear(false)}
          onConfirm={handleClear}
          busy={busy}
        />
      )}
    </div>
  )
}

/**
 * Recommendation history page. It loads grouped history, coordinates the single open favourite
 * popover, and updates state after row/card deletion.
 */
export default function History() {
  // Grouped history entries from the backend.
  const [history, setHistory] = useState([])

  // Page-level loading and error state.
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  // Tracks which suggestion's favourites popover is open.
  const [openPanelId, setOpenPanelId] = useState(null)

  /**
   * Loads all history groups and exposes errors through the shared retryable error component.
   */
  async function load() {
    // Start/restart loading and clear stale errors.
    setLoading(true)
    setError(null)
    try {
      // Fetch grouped recommendation history.
      setHistory((await api.getHistory()) || [])
    } catch (err) {
      // Surface load failure in the page error state.
      setError(err.message)
    }

    // Stop loading after success or failure.
    setLoading(false)
  }

  useEffect(() => {
    load()
  }, [])

  /**
   * Removes a deleted suggestion from local grouped history and drops empty playlist groups.
   */
  function handleSuggestionDeleted(playlistId, suggestionId) {
    setHistory((prev) =>
      prev
        .map((entry) =>
          entry.playlistId === playlistId
            ? { ...entry, suggestions: entry.suggestions.filter((s) => s.id !== suggestionId) }
            : entry
        )
        .filter((entry) => entry.suggestions.length > 0)
    )
  }

  /**
   * Removes a playlist group after all of its recommendation history has been cleared.
   */
  function handlePlaylistCleared(playlistId) {
    setHistory((prev) => prev.filter((entry) => entry.playlistId !== playlistId))
  }

  // Used only for the summary sentence under the page title.
  const totalSuggestions = history.reduce((sum, e) => sum + e.suggestions.length, 0)

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-slate-100">History</h1>
        <p className="text-slate-500 text-sm mt-1">
          {totalSuggestions} recommendation{totalSuggestions !== 1 ? 's' : ''} across{' '}
          {history.length} playlist{history.length !== 1 ? 's' : ''}
        </p>
      </div>

      {loading ? (
        <LoadingSpinner className="py-20" size="lg" />
      ) : error ? (
        <ErrorMessage message={error} onRetry={load} />
      ) : history.length === 0 ? (
        <div className="text-center py-24">
          <svg
            className="w-16 h-16 mx-auto mb-4 text-slate-700"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
            strokeWidth="1.5"
          >
            <circle cx="12" cy="12" r="10" />
            <polyline points="12 6 12 12 16 14" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
          <p className="text-slate-500 font-medium mb-1">No history yet</p>
          <p className="text-slate-600 text-sm">
            Generate a recommendation from a playlist to see it here
          </p>
        </div>
      ) : (
        <div className="space-y-4">
          {history.map((entry) => (
            <PlaylistHistoryCard
              key={entry.playlistId}
              entry={entry}
              openPanelId={openPanelId}
              onPanelToggle={setOpenPanelId}
              onSuggestionDeleted={handleSuggestionDeleted}
              onPlaylistCleared={handlePlaylistCleared}
            />
          ))}
        </div>
      )}
    </div>
  )
}
