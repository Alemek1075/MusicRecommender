import { useState, useEffect } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import StatsGrid from '../components/StatsGrid'
import PlaylistCard from '../components/PlaylistCard'
import LoadingSpinner from '../components/LoadingSpinner'
import ErrorMessage from '../components/ErrorMessage'

function ImportForm({ onSuccess }) {
  const [url, setUrl] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState(null)
  const navigate = useNavigate()

  async function handleSubmit(e) {
    e.preventDefault()
    if (!url.trim()) return
    setLoading(true)
    setError(null)
    try {
      const result = await api.submitPlaylist(url.trim())
      onSuccess?.(result)
      navigate(`/playlists/${result.playlist.id}`)
    } catch (err) {
      setError(err.message)
      setLoading(false)
    }
  }

  return (
    <form onSubmit={handleSubmit}>
      <div className="flex gap-3">
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

      {loading && (
        <p className="mt-3 text-xs text-slate-500 text-center">
          Looking up track info — this can take up to a minute for large playlists…
        </p>
      )}
      {error && (
        <div className="mt-3">
          <ErrorMessage message={error} />
        </div>
      )}
    </form>
  )
}

export default function Home() {
  const [stats, setStats] = useState(null)
  const [playlists, setPlaylists] = useState([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    async function load() {
      try {
        const [s, p] = await Promise.all([api.getStatistics(), api.getPlaylists()])
        setStats(s)
        setPlaylists(p || [])
      } catch {}
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
