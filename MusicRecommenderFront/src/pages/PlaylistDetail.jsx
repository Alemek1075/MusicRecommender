import { useState, useEffect } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import StatsGrid from '../components/StatsGrid'
import RecommendationResult from '../components/RecommendationResult'
import LoadingSpinner from '../components/LoadingSpinner'
import ErrorMessage from '../components/ErrorMessage'

function formatMs(ms) {
  const min = Math.floor(ms / 60000)
  const sec = Math.floor((ms % 60000) / 1000)
  return `${min}:${sec.toString().padStart(2, '0')}`
}

function TrackRow({ track, selected, onToggle }) {
  return (
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

export default function PlaylistDetail() {
  const { id } = useParams()
  const navigate = useNavigate()

  const [tracks, setTracks] = useState([])
  const [stats, setStats] = useState(null)
  const [playlist, setPlaylist] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  const [selected, setSelected] = useState(new Set())
  const [recommendation, setRecommendation] = useState(null)
  const [recLoading, setRecLoading] = useState(false)
  const [recError, setRecError] = useState(null)

  useEffect(() => {
    async function load() {
      try {
        const [t, s, all] = await Promise.all([
          api.getTracks(id),
          api.getStatistics([parseInt(id, 10)]),
          api.getPlaylists(),
        ])
        setTracks(t || [])
        setStats(s)
        setPlaylist((all || []).find((p) => p.id === parseInt(id, 10)) || null)
      } catch (err) {
        setError(err.message)
      }
      setLoading(false)
    }
    load()
  }, [id])

  function toggleTrack(num) {
    setSelected((prev) => {
      const next = new Set(prev)
      next.has(num) ? next.delete(num) : next.add(num)
      return next
    })
  }

  const allSelected = tracks.length > 0 && selected.size === tracks.length

  async function generateRec() {
    setRecLoading(true)
    setRecError(null)
    setRecommendation(null)
    try {
      const rec = await api.generateRecommendation(parseInt(id, 10), [...selected])
      setRecommendation(rec)
    } catch (err) {
      setRecError(err.message)
    }
    setRecLoading(false)
  }

  if (loading) return <LoadingSpinner className="py-24" size="lg" />

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
      {recommendation && (
        <RecommendationResult
          recommendation={recommendation}
          onDismiss={() => setRecommendation(null)}
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
              <span className="text-xs px-2 py-0.5 rounded-full bg-violet-500/15 text-violet-300 border border-violet-500/20">
                {selected.size} selected
              </span>
            )}
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={() =>
                allSelected
                  ? setSelected(new Set())
                  : setSelected(new Set(tracks.map((t) => t.trackNumber)))
              }
              className="text-xs text-slate-500 hover:text-slate-300 px-3 py-1.5 rounded-lg hover:bg-white/5 transition-all"
            >
              {allSelected ? 'Clear all' : 'Select all'}
            </button>

            <button
              onClick={generateRec}
              disabled={recLoading}
              className="flex items-center gap-2 px-4 py-2 rounded-xl bg-violet-600 hover:bg-violet-500 disabled:opacity-60 disabled:cursor-not-allowed text-white text-xs font-semibold transition-all"
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
                  Get Recommendation
                </>
              )}
            </button>
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
