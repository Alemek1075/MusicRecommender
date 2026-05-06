import { useState, useEffect, useRef } from 'react'
import { api } from '../api/client'
import PlaylistCard from '../components/PlaylistCard'
import LoadingSpinner from '../components/LoadingSpinner'
import ErrorMessage from '../components/ErrorMessage'
import { useImport } from '../context/ImportContext'

function ImportModal({ onClose, onSuccess }) {
  const [url, setUrl] = useState('')
  const [name, setName] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState(null)
  const [confirmInterrupt, setConfirmInterrupt] = useState(false)
  const abortRef = useRef(null)
  const { setIsImporting } = useImport()

  async function handleSubmit(e) {
    e.preventDefault()
    if (!url.trim()) return
    setLoading(true)
    setIsImporting(true)
    setError(null)
    const ctrl = new AbortController()
    abortRef.current = ctrl
    try {
      const result = await api.submitPlaylist(url.trim(), ctrl.signal)
      if (name.trim()) {
        const renamed = await api.renamePlaylist(result.playlist.id, name.trim())
        result.playlist = { ...result.playlist, ...renamed }
      }
      onSuccess(result)
    } catch (err) {
      if (err.name !== 'AbortError') setError(err.message)
      setLoading(false)
    }
    setIsImporting(false)
    abortRef.current = null
  }

  function attemptClose() {
    if (loading) {
      setConfirmInterrupt(true)
    } else {
      onClose()
    }
  }

  function confirmStop() {
    abortRef.current?.abort()
    abortRef.current = null
    setConfirmInterrupt(false)
    setLoading(false)
    setIsImporting(false)
    onClose()
  }

  function handleBackdrop(e) {
    if (e.target === e.currentTarget) attemptClose()
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      style={{ backgroundColor: 'rgba(0,0,0,0.65)', backdropFilter: 'blur(6px)' }}
      onClick={handleBackdrop}
    >
      <div
        className="w-full max-w-md rounded-2xl border border-slate-700/60 p-6"
        style={{ backgroundColor: '#131520' }}
      >
        <div className="flex items-center justify-between mb-6">
          <h2 className="text-base font-semibold text-slate-100">Import Playlist</h2>
          <button
            onClick={attemptClose}
            className="text-slate-600 hover:text-slate-300 transition-colors p-1"
          >
            <svg
              className="w-5 h-5"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              strokeWidth="2"
            >
              <line x1="18" y1="6" x2="6" y2="18" strokeLinecap="round" />
              <line x1="6" y1="6" x2="18" y2="18" strokeLinecap="round" />
            </svg>
          </button>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-xs font-medium text-slate-500 mb-2">
              Playlist URL
            </label>
            <input
              type="text"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              placeholder="https://open.spotify.com/playlist/… or https://youtube.com/playlist?list=…"
              className="w-full rounded-xl px-4 py-3 text-sm text-slate-200 placeholder-slate-600 focus:outline-none focus:ring-1 focus:ring-violet-500/50 transition-all"
              style={{
                backgroundColor: '#1a1d2e',
                border: '1px solid rgba(255,255,255,0.07)',
              }}
              autoFocus
              disabled={loading}
            />
          </div>

          <div>
            <label className="block text-xs font-medium text-slate-500 mb-2">
              Name <span className="text-slate-700 font-normal">(optional)</span>
            </label>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="My playlist"
              className="w-full rounded-xl px-4 py-3 text-sm text-slate-200 placeholder-slate-600 focus:outline-none focus:ring-1 focus:ring-violet-500/50 transition-all"
              style={{
                backgroundColor: '#1a1d2e',
                border: '1px solid rgba(255,255,255,0.07)',
              }}
              disabled={loading}
            />
          </div>

          {loading && (
            <p className="text-xs text-slate-500 text-center">
              Analyzing tracks and looking up genres — may take a moment…
            </p>
          )}
          {error && <ErrorMessage message={error} />}

          <div className="flex gap-3 pt-1">
            <button
              type="button"
              onClick={attemptClose}
              className="flex-1 py-2.5 rounded-xl text-slate-400 hover:text-slate-200 text-sm font-medium transition-all border border-slate-700/60 hover:border-slate-600"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={loading || !url.trim()}
              className="flex-1 py-2.5 rounded-xl bg-violet-600 hover:bg-violet-500 disabled:opacity-50 disabled:cursor-not-allowed text-white text-sm font-medium transition-all flex items-center justify-center gap-2"
            >
              {loading ? (
                <>
                  <div className="w-4 h-4 animate-spin rounded-full border-2 border-white/25 border-t-white" />
                  Processing…
                </>
              ) : (
                'Import Playlist'
              )}
            </button>
          </div>
        </form>
      </div>

      {confirmInterrupt && (
        <div
          className="fixed inset-0 z-[60] flex items-center justify-center p-4"
          style={{ backgroundColor: 'rgba(0,0,0,0.7)', backdropFilter: 'blur(8px)' }}
          onClick={(e) => e.target === e.currentTarget && setConfirmInterrupt(false)}
        >
          <div
            className="w-full max-w-sm rounded-2xl border border-slate-700/60 p-6"
            style={{ backgroundColor: '#131520' }}
            onClick={(e) => e.stopPropagation()}
          >
            <h3 className="text-base font-semibold text-slate-100 mb-2">Interrupt analysis?</h3>
            <p className="text-sm text-slate-500 mb-5">
              The playlist is still being processed. Stop now?
            </p>
            <div className="flex gap-3">
              <button
                type="button"
                onClick={() => setConfirmInterrupt(false)}
                className="flex-1 py-2.5 rounded-xl text-slate-400 hover:text-slate-200 text-sm font-medium border border-slate-700/60 hover:border-slate-600"
              >
                Keep going
              </button>
              <button
                type="button"
                onClick={confirmStop}
                className="flex-1 py-2.5 rounded-xl bg-red-600 hover:bg-red-500 text-white text-sm font-medium"
              >
                Interrupt
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

export default function Playlists() {
  const [playlists, setPlaylists] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const [showModal, setShowModal] = useState(false)
  const { isImporting } = useImport()

  async function load() {
    setLoading(true)
    setError(null)
    try {
      setPlaylists((await api.getPlaylists()) || [])
    } catch (err) {
      setError(err.message)
    }
    setLoading(false)
  }

  useEffect(() => {
    load()
  }, [])

  function handleImportSuccess(result) {
    setShowModal(false)
    setPlaylists((prev) => [result.playlist, ...prev])
  }

  return (
    <div className="space-y-6">
      <div className="flex items-end justify-between">
        <div>
          <h1 className="text-2xl font-bold text-slate-100">Playlists</h1>
          <p className="text-slate-500 text-sm mt-1">
            {playlists.length} playlist{playlists.length !== 1 ? 's' : ''} imported
          </p>
        </div>
        <button
          onClick={() => setShowModal(true)}
          disabled={isImporting}
          className="flex items-center gap-2 px-4 py-2.5 rounded-xl bg-violet-600 hover:bg-violet-500 disabled:opacity-50 disabled:cursor-not-allowed text-white text-sm font-medium transition-all"
        >
          <svg
            className="w-4 h-4"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
            strokeWidth="2.5"
          >
            <line x1="12" y1="5" x2="12" y2="19" strokeLinecap="round" />
            <line x1="5" y1="12" x2="19" y2="12" strokeLinecap="round" />
          </svg>
          Import New
        </button>
      </div>

      {loading ? (
        <LoadingSpinner className="py-20" size="lg" />
      ) : error ? (
        <ErrorMessage message={error} onRetry={load} />
      ) : playlists.length === 0 ? (
        <div className="text-center py-24">
          <svg
            className="w-16 h-16 mx-auto mb-4 text-slate-700"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
            strokeWidth="1.5"
          >
            <path d="M9 18V5l12-2v13" strokeLinecap="round" strokeLinejoin="round" />
            <circle cx="6" cy="18" r="3" />
            <circle cx="18" cy="16" r="3" />
          </svg>
          <p className="text-slate-500 font-medium mb-1">No playlists yet</p>
          <p className="text-slate-600 text-sm">Click "Import New" to add your first playlist</p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {playlists.map((p) => (
            <PlaylistCard
              key={p.id}
              playlist={p}
              onRenamed={(updated) =>
                setPlaylists((prev) => prev.map((x) => (x.id === updated.id ? { ...x, ...updated } : x)))
              }
              onDeleted={(id) => setPlaylists((prev) => prev.filter((x) => x.id !== id))}
            />
          ))}
        </div>
      )}

      {showModal && (
        <ImportModal onClose={() => setShowModal(false)} onSuccess={handleImportSuccess} />
      )}
    </div>
  )
}
