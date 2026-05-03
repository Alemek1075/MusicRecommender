import { useState, useEffect } from 'react'
import { api } from '../api/client'
import LoadingSpinner from '../components/LoadingSpinner'
import ErrorMessage from '../components/ErrorMessage'

function formatDate(dateStr) {
  return new Date(dateStr).toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function truncateUrl(url, max = 45) {
  if (!url || url.length <= max) return url
  return url.slice(0, max) + '…'
}

function SuggestionRow({ suggestion }) {
  const trackNums =
    Array.isArray(suggestion.favoriteTrackNumbers)
      ? suggestion.favoriteTrackNumbers.join(', ')
      : suggestion.favoriteTrackNumbers || '—'

  return (
    <div className="flex items-start gap-4 px-5 py-4 hover:bg-white/2 transition-colors">
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
        <p className="text-xs text-slate-700 mt-1.5">
          Favourites: <span className="text-slate-600">{trackNums}</span>
        </p>
      </div>

      <span className="text-xs text-slate-700 flex-shrink-0 text-right leading-tight">
        {formatDate(suggestion.createdAt)}
      </span>
    </div>
  )
}

function PlaylistHistoryCard({ entry }) {
  const isSpotify = entry.playlistUrl?.includes('spotify.com')
  const isYoutube =
    entry.playlistUrl?.includes('youtube.com') || entry.playlistUrl?.includes('youtu.be')

  const badgeClass = isSpotify
    ? 'bg-green-500/12 text-green-400 border-green-500/20'
    : isYoutube
    ? 'bg-red-500/12 text-red-400 border-red-500/20'
    : 'bg-slate-700/50 text-slate-400 border-slate-600/30'

  const platform = isSpotify ? 'Spotify' : isYoutube ? 'YouTube' : 'Playlist'

  return (
    <div
      className="rounded-2xl border border-slate-700/40 overflow-hidden"
      style={{ backgroundColor: '#131520' }}
    >
      {/* Card header */}
      <div className="flex items-center justify-between px-5 py-3.5 border-b border-slate-700/40">
        <div className="flex items-center gap-3 min-w-0">
          <span
            className={`inline-flex items-center px-2.5 py-1 rounded-full text-xs font-medium border flex-shrink-0 ${badgeClass}`}
          >
            {platform}
          </span>
          <span className="text-xs text-slate-600 truncate">
            {truncateUrl(entry.playlistUrl)}
          </span>
        </div>
        <span className="text-xs text-slate-700 flex-shrink-0 ml-3">
          {entry.suggestions.length} suggestion{entry.suggestions.length !== 1 ? 's' : ''}
        </span>
      </div>

      {/* Suggestions */}
      <div className="divide-y divide-slate-800/50">
        {entry.suggestions.map((s) => (
          <SuggestionRow key={s.id} suggestion={s} />
        ))}
      </div>
    </div>
  )
}

export default function History() {
  const [history, setHistory] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  async function load() {
    setLoading(true)
    setError(null)
    try {
      setHistory((await api.getHistory()) || [])
    } catch (err) {
      setError(err.message)
    }
    setLoading(false)
  }

  useEffect(() => {
    load()
  }, [])

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
            <PlaylistHistoryCard key={entry.playlistId} entry={entry} />
          ))}
        </div>
      )}
    </div>
  )
}
