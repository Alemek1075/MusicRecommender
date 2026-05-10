import { useState, useEffect } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import StatsGrid from '../components/StatsGrid'
import PlaylistCard from '../components/PlaylistCard'
import LoadingSpinner from '../components/LoadingSpinner'
import ErrorMessage from '../components/ErrorMessage'

/**
 * Homepage import form. It creates the playlist, optionally applies a custom display name, then
 * redirects the user to the new playlist detail page.
 */
function ImportForm({ onSuccess }) {
  // URL input stores the playlist source to import.
  const [url, setUrl] = useState('')

  // Optional friendly name applied after import succeeds.
  const [name, setName] = useState('')

  // Loading disables form controls during backend processing.
  const [loading, setLoading] = useState(false)

  // Error stores user-facing backend/network failures.
  const [error, setError] = useState(null)

  // Used to jump to the created playlist after import.
  const navigate = useNavigate()

  /**
   * Submits the entered playlist URL to the backend and chains the optional rename request after
   * the playlist exists.
   */
  async function handleSubmit(e) {
    // Prevent browser form submission.
    e.preventDefault()

    // Ignore blank URLs.
    if (!url.trim()) return

    // Lock the form and clear stale errors.
    setLoading(true)
    setError(null)
    try {
      // Import the playlist through the backend.
      const result = await api.submitPlaylist(url.trim())

      // Apply optional display name only after the playlist has an ID.
      if (name.trim()) {
        const renamed = await api.renamePlaylist(result.playlist.id, name.trim())
        result.playlist = { ...result.playlist, ...renamed }
      }

      // Allow parent page to refresh local dashboard state if it wants to.
      onSuccess?.(result)

      // Move directly into the track-selection/detail workflow.
      navigate(`/playlists/${result.playlist.id}`)
    } catch (err) {
      // Show the backend/network error and re-enable the form.
      setError(err.message)
      setLoading(false)
    }
  }

  return (
    /* Homepage import form. */
    <form onSubmit={handleSubmit} className="space-y-3">
      <div className="flex gap-3">
        {/* Playlist URL input. */}
        <input
          type="text"
          value={url}
          onChange={(e) => setUrl(e.target.value)}
          placeholder="Paste a Spotify or YouTube playlist URL…"
          className="flex-1 rounded-xl px-4 py-3 text-sm text-slate-200 placeholder-slate-600 focus:outline-none focus:ring-1 focus:ring-violet-500/50 transition-all"
          style={{
            backgroundColor: '#1a1d2e',
            border: '1px solid rgba(255,255,255,0.08)',
          }}
          disabled={loading}
        />
        {/* Import button shows a spinner while the backend analyzes the playlist. */}
        <button
          type="submit"
          disabled={loading || !url.trim()}
          className="flex items-center gap-2 px-5 py-3 rounded-xl bg-violet-600 hover:bg-violet-500 disabled:opacity-50 disabled:cursor-not-allowed text-white text-sm font-medium transition-all min-w-[120px] justify-center"
        >
          {loading ? (
            <>
              <div className="w-4 h-4 animate-spin rounded-full border-2 border-white/25 border-t-white" />
              Processing…
            </>
          ) : (
            <>
              <svg
                className="w-4 h-4"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
                strokeWidth="2"
              >
                <path
                  d="M12 5v14M5 12l7 7 7-7"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                />
              </svg>
              Import
            </>
          )}
        </button>
      </div>

      {/* Optional name input, saved only if non-empty. */}
      <input
        type="text"
        value={name}
        onChange={(e) => setName(e.target.value)}
        placeholder="Playlist name (optional)"
        className="w-full rounded-xl px-4 py-2.5 text-sm text-slate-200 placeholder-slate-600 focus:outline-none focus:ring-1 focus:ring-violet-500/50 transition-all"
        style={{
          backgroundColor: '#1a1d2e',
          border: '1px solid rgba(255,255,255,0.06)',
        }}
        disabled={loading}
      />

      {/* Long imports get a progress hint. */}
      {loading && (
        <p className="text-xs text-slate-500 text-center">
          Looking up track info — this can take up to a minute for large playlists…
        </p>
      )}
      {/* Backend/import errors appear below the form. */}
      {error && (
        <div className="mt-1">
          <ErrorMessage message={error} />
        </div>
      )}
    </form>
  )
}

/**
 * Landing/dashboard page. It combines the primary import workflow, library statistics, and a few
 * recent playlist cards for quick re-entry.
 */
export default function Home() {
  // Whole-library statistics shown below the hero when available.
  const [stats, setStats] = useState(null)

  // Recent playlist list for the dashboard cards.
  const [playlists, setPlaylists] = useState([])

  // Initial dashboard loading state.
  const [loading, setLoading] = useState(true)

  // Load dashboard data in parallel because stats and playlists are independent API calls.
  useEffect(() => {
    /**
     * Fetches dashboard statistics and recent playlists. Errors are intentionally swallowed here
     * because the homepage can still show the import form without dashboard data.
     */
    async function load() {
      try {
        // Stats and playlists do not depend on each other, so fetch them together.
        const [s, p] = await Promise.all([api.getStatistics(), api.getPlaylists()])
        setStats(s)
        setPlaylists(p || [])
      } catch {}
      // Always stop the loading spinner, even if dashboard data fails.
      setLoading(false)
    }
    load()
  }, [])

  return (
    <div className="space-y-14">
      {/* Hero */}
      <div className="text-center pt-6">
        <span className="inline-flex items-center gap-2 px-3 py-1.5 rounded-full border border-violet-500/25 bg-violet-500/8 text-violet-300 text-xs font-medium mb-7">
          <svg
            className="w-3.5 h-3.5"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
            strokeWidth="2"
          >
            <path d="M9 18V5l12-2v13" strokeLinecap="round" strokeLinejoin="round" />
            <circle cx="6" cy="18" r="3" />
            <circle cx="18" cy="16" r="3" />
          </svg>
          Smart Music Discovery
        </span>

        <h1 className="text-4xl md:text-5xl font-bold tracking-tight text-slate-100 mb-4">
          Find your next
          <span className="text-violet-400"> favourite track</span>
        </h1>
        <p className="text-slate-500 text-lg max-w-lg mx-auto mb-10">
          Import any Spotify or YouTube playlist and get recommendations tailored to your taste.
        </p>

        <div className="max-w-2xl mx-auto">
          <ImportForm />
        </div>
      </div>

      {/* Stats + recents */}
      {loading ? (
        <LoadingSpinner className="py-10" size="lg" />
      ) : (
        <>
          {stats && stats.totalTracks > 0 && (
            <section>
              <p className="text-xs font-semibold text-slate-600 uppercase tracking-widest mb-4">
                Your library
              </p>
              <StatsGrid stats={stats} />
            </section>
          )}

          {playlists.length > 0 && (
            <section>
              <div className="flex items-center justify-between mb-4">
                <p className="text-xs font-semibold text-slate-600 uppercase tracking-widest">
                  Recent playlists
                </p>
                <Link
                  to="/playlists"
                  className="text-xs text-violet-400 hover:text-violet-300 transition-colors"
                >
                  View all →
                </Link>
              </div>
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                {playlists.slice(0, 3).map((p) => (
                  <PlaylistCard key={p.id} playlist={p} />
                ))}
              </div>
            </section>
          )}

          {playlists.length === 0 && (
            <div className="text-center py-20">
              <svg
                className="w-16 h-16 mx-auto mb-4 text-slate-700"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
                strokeWidth="1.5"
              >
                <path
                  d="M9 18V5l12-2v13"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                />
                <circle cx="6" cy="18" r="3" />
                <circle cx="18" cy="16" r="3" />
              </svg>
              <p className="text-slate-500 font-medium mb-1">No playlists yet</p>
              <p className="text-slate-600 text-sm">Paste a URL above to get started</p>
            </div>
          )}
        </>
      )}
    </div>
  )
}
