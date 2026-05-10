import { useState, useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import { useParams, useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import StatsGrid from '../components/StatsGrid'
import RecommendationResult from '../components/RecommendationResult'
import LoadingSpinner from '../components/LoadingSpinner'
import ErrorMessage from '../components/ErrorMessage'

/**
 * Converts millisecond durations from the backend into the M:SS format used in track rows.
 */
function formatMs(ms) {
  // Convert total milliseconds into whole minutes.
  const min = Math.floor(ms / 60000)

  // Convert the remainder into seconds.
  const sec = Math.floor((ms % 60000) / 1000)

  // Pad seconds so durations render like 3:07 instead of 3:7.
  return `${min}:${sec.toString().padStart(2, '0')}`
}

/**
 * Clickable track row. It shows metadata and selection state, then delegates toggle behavior to
 * the parent so the selected Set remains centralized.
 */
function TrackRow({ track, selected, onToggle }) {
  return (
    /* One selectable track row. */
    <div
      onClick={() => onToggle(track.trackNumber)}
      className={`flex items-center gap-3.5 px-4 py-3 rounded-xl cursor-pointer transition-all select-none group ${
        selected
          ? 'bg-violet-500/12 border border-violet-500/25'
          : 'border border-transparent hover:bg-white/3 hover:border-slate-700/40'
      }`}
    >
      {/* Checkbox */}
      <div
        className={`w-5 h-5 rounded-md border-2 flex items-center justify-center flex-shrink-0 transition-all ${
          selected
            ? 'bg-violet-500 border-violet-500'
            : 'border-slate-600 group-hover:border-slate-400'
        }`}
      >
        {selected && (
          <svg
            className="w-3 h-3 text-white"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
            strokeWidth="3"
          >
            <polyline points="20 6 9 17 4 12" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        )}
      </div>

      {/* Number */}
      <span className="text-slate-600 text-xs w-5 text-right flex-shrink-0">
        {track.trackNumber}
      </span>

      {/* Track info */}
      <div className="flex-1 min-w-0">
        <p
          className={`text-sm font-medium truncate transition-colors ${
            selected ? 'text-violet-200' : 'text-slate-200'
          }`}
        >
          {track.trackName}
        </p>
        <p className="text-xs text-slate-500 truncate mt-0.5">{track.artistName}</p>
      </div>

      {/* Genre */}
      {track.genre && (
        <span className="hidden sm:block text-xs px-2.5 py-1 rounded-full bg-slate-800/80 text-slate-500 border border-slate-700/40 flex-shrink-0">
          {track.genre}
        </span>
      )}

      {/* Duration */}
      <span className="text-xs text-slate-600 flex-shrink-0 w-10 text-right font-mono">
        {formatMs(track.durationMs)}
      </span>
    </div>
  )
}

/**
 * Playlist detail and recommendation workspace. Users can select favourite tracks, choose how many
 * suggestions to request, generate recommendations, and inspect imported track metadata.
 */
export default function PlaylistDetail() {
  // Playlist ID comes from /playlists/:id.
  const { id } = useParams()

  // Navigation is used for the back button and error fallback.
  const navigate = useNavigate()

  // Imported track rows for this playlist.
  const [tracks, setTracks] = useState([])

  // Scoped stats for this playlist.
  const [stats, setStats] = useState(null)

  // Playlist metadata, mainly custom display name.
  const [playlist, setPlaylist] = useState(null)

  // Page-level loading/error state.
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  // Set of selected favourite track numbers.
  const [selected, setSelected] = useState(new Set())

  // Latest generated recommendation batch.
  const [recommendations, setRecommendations] = useState(null)

  // Recommendation request loading/error state.
  const [recLoading, setRecLoading] = useState(false)
  const [recError, setRecError] = useState(null)

  // Number of recommendations to request in one batch.
  const [recCount, setRecCount] = useState(1)

  // Controls the count dropdown.
  const [showCountPicker, setShowCountPicker] = useState(false)
  const countPickerRef = useRef(null)

  // Hover state for the selected-tracks badge tooltip.
  const [badgeHovered, setBadgeHovered] = useState(false)
  const badgeRef = useRef(null)
  const [badgePos, setBadgePos] = useState(null)

  /**
   * Captures the badge position before showing the selected-tracks tooltip, because the tooltip is
   * rendered through a portal and needs viewport coordinates.
   */
  function onBadgeEnter() {
    // Measure the badge before rendering the portal tooltip.
    const r = badgeRef.current?.getBoundingClientRect()

    // Store coordinates only when the DOM node exists.
    if (r) setBadgePos(r)

    // Show the tooltip.
    setBadgeHovered(true)
  }

  // Load tracks, scoped statistics, and playlist display metadata together for the detail view.
  useEffect(() => {
    /**
     * Fetches all data required for the detail page. Playlist metadata is read from the playlist
     * list because there is no dedicated "get playlist by id" endpoint yet.
     */
    async function load() {
      try {
        // Fetch tracks, stats, and playlist list concurrently.
        const [t, s, all] = await Promise.all([
          api.getTracks(id),
          api.getStatistics([parseInt(id, 10)]),
          api.getPlaylists(),
        ])

        // Store track rows for the table.
        setTracks(t || [])

        // Store scoped statistics.
        setStats(s)

        // Find this playlist's metadata from the list response.
        setPlaylist((all || []).find((p) => p.id === parseInt(id, 10)) || null)
      } catch (err) {
        // Capture any failed request as a page error.
        setError(err.message)
      }

      // Finish initial loading.
      setLoading(false)
    }
    load()
  }, [id])

  /**
   * Toggles one track number inside a Set while preserving React state immutability.
   */
  function toggleTrack(num) {
    // Use functional state so rapid clicks compose correctly.
    setSelected((prev) => {
      // Clone the Set because React state should not be mutated in place.
      const next = new Set(prev)

      // Toggle the requested track number.
      next.has(num) ? next.delete(num) : next.add(num)

      // Return the updated selection.
      return next
    })
  }

  // Recommendation count is capped at 20% of the playlist so batch generation stays reasonable.
  // allSelected is available for future UI behavior and documents the full-selection state.
  const allSelected = tracks.length > 0 && selected.size === tracks.length

  // At least one recommendation is always allowed.
  const maxCount = Math.max(1, Math.floor(tracks.length * 0.2))

  // Close the recommendation-count menu when the user clicks outside it.
  useEffect(() => {
    if (!showCountPicker) return
    /**
     * Closes the count picker unless the click happened inside the picker wrapper.
     */
    function handler(e) {
      // Ignore clicks inside the picker.
      if (countPickerRef.current?.contains(e.target)) return

      // Close the dropdown on outside clicks.
      setShowCountPicker(false)
    }

    // Register the outside-click listener while the picker is open.
    document.addEventListener('mousedown', handler)

    // Remove the listener on close/unmount.
    return () => document.removeEventListener('mousedown', handler)
  }, [showCountPicker])

  /**
   * Calls the backend recommendation endpoint with the current favourite selection and requested
   * count, then normalizes the response to an array for RecommendationResult.
   */
  async function generateRec() {
    // Show recommendation spinner and clear previous result/error.
    setRecLoading(true)
    setRecError(null)
    setRecommendations(null)
    try {
      // Send selected track numbers as favourite seeds.
      const recs = await api.generateRecommendation(parseInt(id, 10), [...selected], recCount)

      // Backend returns an array, but normalize defensively.
      setRecommendations(Array.isArray(recs) ? recs : [recs])
    } catch (err) {
      // Show recommendation-specific failure without losing the playlist page.
      setRecError(err.message)
    }

    // Hide recommendation spinner.
    setRecLoading(false)
  }

  // Initial loading state for the entire detail page.
  if (loading) return <LoadingSpinner className="py-24" size="lg" />

  // If initial data failed, show an error with a button back to playlists.
  if (error)
    return (
      <div className="py-8 max-w-lg">
        <ErrorMessage message={error} onRetry={() => navigate('/playlists')} />
      </div>
    )

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center gap-3">
        <button
          onClick={() => navigate('/playlists')}
          className="p-2 rounded-xl text-slate-600 hover:text-slate-300 hover:bg-white/5 transition-all"
        >
          <svg
            className="w-5 h-5"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
            strokeWidth="2"
          >
            <path d="M19 12H5M12 19l-7-7 7-7" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        </button>
        <div>
          <h1 className="text-xl font-bold text-slate-100">
            {playlist?.name?.trim() || `Playlist #${id}`}
          </h1>
          <p className="text-slate-600 text-sm">{tracks.length} tracks</p>
        </div>
      </div>

      {/* Stats */}
      {stats && stats.totalTracks > 0 && <StatsGrid stats={stats} />}

      {/* Recommendation */}
      {recommendations && (
        <RecommendationResult
          recommendations={recommendations}
          onDismiss={() => setRecommendations(null)}
          tracks={tracks}
        />
      )}
      {recError && <ErrorMessage message={recError} />}

      {/* Track list panel */}
      <div
        className="rounded-2xl border border-slate-700/40 overflow-hidden"
        style={{ backgroundColor: '#131520' }}
      >
        {/* Panel header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-700/40">
          <div className="flex items-center gap-3">
            <span className="text-sm font-semibold text-slate-200">Tracks</span>
            {selected.size > 0 && (
              <div
                ref={badgeRef}
                className="relative"
                onMouseEnter={onBadgeEnter}
                onMouseLeave={() => setBadgeHovered(false)}
              >
                <span className="text-xs px-2 py-0.5 rounded-full bg-violet-500/15 text-violet-300 border border-violet-500/20 cursor-default">
                  {selected.size} selected
                </span>
                {badgeHovered && badgePos && createPortal(
                  <div
                    className="fixed z-50 rounded-xl border border-violet-500/20 shadow-2xl overflow-hidden pointer-events-none"
                    style={{
                      backgroundColor: '#1a1d2e',
                      minWidth: '180px',
                      maxWidth: '260px',
                      top: badgePos.top - 8,
                      left: badgePos.left,
                      transform: 'translateY(-100%)',
                    }}
                  >
                    <div className="px-3 py-2 border-b border-slate-700/40">
                      <span className="text-xs font-semibold text-violet-300 uppercase tracking-wider">Selected</span>
                    </div>
                    <ul className="py-1 max-h-48 overflow-y-auto">
                      {tracks
                        .filter((t) => selected.has(t.trackNumber))
                        .map((t) => (
                          <li key={t.trackNumber} className="flex items-center gap-2 px-3 py-1.5 text-xs">
                            <span className="text-slate-600 w-4 text-right flex-shrink-0">{t.trackNumber}</span>
                            <span className="text-slate-300 flex-1 truncate">{t.trackName}</span>
                          </li>
                        ))}
                    </ul>
                  </div>,
                  document.body
                )}
              </div>
            )}
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={() =>
                selected.size > 0
                  ? setSelected(new Set())
                  : setSelected(new Set(tracks.map((t) => t.trackNumber)))
              }
              className="text-xs text-slate-500 hover:text-slate-300 px-3 py-1.5 rounded-lg hover:bg-white/5 transition-all"
            >
              {selected.size > 0 ? 'Clear' : 'Select all'}
            </button>

            {/* Split button: action + count picker */}
            <div className="flex items-stretch rounded-xl bg-violet-600 overflow-visible">
              {/* Main action */}
              <button
                onClick={generateRec}
                disabled={recLoading}
                className="flex items-center gap-2 pl-4 pr-3 py-2 rounded-l-xl hover:bg-violet-500 disabled:opacity-60 disabled:cursor-not-allowed text-white text-xs font-semibold transition-all"
              >
                {recLoading ? (
                  <>
                    <div className="w-3.5 h-3.5 animate-spin rounded-full border-2 border-white/25 border-t-white" />
                    Generating…
                  </>
                ) : (
                  <>
                    <svg
                      className="w-3.5 h-3.5"
                      fill="none"
                      stroke="currentColor"
                      viewBox="0 0 24 24"
                      strokeWidth="2"
                    >
                      <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2" />
                    </svg>
                    Get Recommendation{recCount > 1 ? 's' : ''}
                  </>
                )}
              </button>

              {/* Separator */}
              <div className="w-px bg-white/20 my-1.5 flex-shrink-0" />

              {/* Count picker trigger */}
              <div className="relative" ref={countPickerRef}>
                <button
                  onClick={() => setShowCountPicker((v) => !v)}
                  disabled={recLoading}
                  className="flex items-center gap-1 px-2.5 py-2 rounded-r-xl hover:bg-violet-500 disabled:opacity-60 text-white text-xs font-semibold transition-all h-full"
                  title="Number of recommendations"
                >
                  <span className="tabular-nums w-3 text-center">{recCount}</span>
                  <svg
                    className={`w-3 h-3 transition-transform ${showCountPicker ? 'rotate-180' : ''}`}
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                    strokeWidth="2.5"
                  >
                    <polyline
                      points="6 9 12 15 18 9"
                      strokeLinecap="round"
                      strokeLinejoin="round"
                    />
                  </svg>
                </button>

                {showCountPicker && (
                  <div
                    className="absolute right-0 top-full mt-1.5 z-50 rounded-xl border border-violet-500/25 shadow-2xl overflow-hidden py-1"
                    style={{ backgroundColor: '#1a1d2e', minWidth: '56px' }}
                  >
                    {Array.from({ length: maxCount }, (_, i) => i + 1).map((n) => (
                      <button
                        key={n}
                        onClick={() => {
                          setRecCount(n)
                          setShowCountPicker(false)
                        }}
                        className={`w-full px-3 py-1.5 text-xs text-center transition-colors ${
                          recCount === n
                            ? 'bg-violet-500/20 text-violet-300'
                            : 'text-slate-400 hover:bg-white/5 hover:text-slate-200'
                        }`}
                      >
                        {n}
                      </button>
                    ))}
                  </div>
                )}
              </div>
            </div>
          </div>
        </div>

        {/* Track rows */}
        <div className="p-2 space-y-0.5 max-h-[58vh] overflow-y-auto">
          {tracks.length === 0 ? (
            <p className="text-center py-10 text-slate-600 text-sm">No tracks found</p>
          ) : (
            tracks.map((track) => (
              <TrackRow
                key={track.trackNumber}
                track={track}
                selected={selected.has(track.trackNumber)}
                onToggle={toggleTrack}
              />
            ))
          )}
        </div>

        {/* Footer hint */}
        {tracks.length > 0 && (
          <div className="px-5 py-3 border-t border-slate-700/40">
            <p className="text-xs text-slate-700">
              Tick your favourite tracks, then hit "Get Recommendation" — or leave all unchecked to
              pick from the full playlist
            </p>
          </div>
        )}
      </div>
    </div>
  )
}
