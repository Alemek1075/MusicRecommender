import { useState, useEffect, useRef } from 'react'
import { api } from '../api/client'
import PlaylistCard from '../components/PlaylistCard'
import LoadingSpinner from '../components/LoadingSpinner'
import ErrorMessage from '../components/ErrorMessage'
import { useImport } from '../context/ImportContext'

/**
 * Modal import workflow for the Playlists page. It supports cancelling the network request with an
 * AbortController and asks for confirmation before interrupting an active import.
 */
function ImportModal({ onClose, onSuccess }) {
  // Playlist URL entered by the user.
  const [url, setUrl] = useState('')

  // Optional custom playlist display name.
  const [name, setName] = useState('')

  // Loading indicates an import request is in flight.
  const [loading, setLoading] = useState(false)

  // Error stores backend/network failures for the modal.
  const [error, setError] = useState(null)

  // Confirmation state for stopping an active import.
  const [confirmInterrupt, setConfirmInterrupt] = useState(false)

  // Holds the AbortController for the current import request.
  const abortRef = useRef(null)

  // Global import setter locks navbar navigation while processing.
  const { setIsImporting } = useImport()

  /**
   * Starts playlist import, stores the AbortController for cancellation, optionally renames the
   * saved playlist, and reports success back to the parent list.
   */
  async function handleSubmit(e) {
    // Keep the form from causing a page reload.
    e.preventDefault()

    // Do not submit empty URLs.
    if (!url.trim()) return

    // Enter loading state locally and globally.
    setLoading(true)
    setIsImporting(true)
    setError(null)

    // Create a cancellable request controller for this import.
    const ctrl = new AbortController()
    abortRef.current = ctrl
    try {
      // Submit the playlist URL and pass the abort signal to fetch.
      const result = await api.submitPlaylist(url.trim(), ctrl.signal)

      // Apply the optional display name after import creates the playlist.
      if (name.trim()) {
        const renamed = await api.renamePlaylist(result.playlist.id, name.trim())
        result.playlist = { ...result.playlist, ...renamed }
      }

      // Tell the parent page to insert the new playlist and close the modal.
      onSuccess(result)
    } catch (err) {
      // AbortError is expected when the user intentionally interrupts.
      if (err.name !== 'AbortError') setError(err.message)
      setLoading(false)
    }

    // Clear import lock and controller reference after completion/cancel.
    setIsImporting(false)
    abortRef.current = null
  }

  /**
   * Closes immediately when idle, or opens the interrupt confirmation while import work is active.
   */
  function attemptClose() {
    // Active imports require confirmation so the user does not cancel accidentally.
    if (loading) {
      setConfirmInterrupt(true)
    } else {
      onClose()
    }
  }

  /**
   * Cancels the in-flight fetch request and clears global import state before closing the modal.
   */
  function confirmStop() {
    // Cancel the fetch request if it is still active.
    abortRef.current?.abort()

    // Clear the controller and UI state.
    abortRef.current = null
    setConfirmInterrupt(false)
    setLoading(false)
    setIsImporting(false)

    // Close the import modal after cancelling.
    onClose()
  }

  /**
   * Treats a backdrop click as a close attempt without closing when the dialog body is clicked.
   */
  function handleBackdrop(e) {
    // Only backdrop clicks, not dialog clicks, attempt to close.
    if (e.target === e.currentTarget) attemptClose()
  }

  return (
    /* Import modal overlay. */
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      style={{ backgroundColor: 'rgba(0,0,0,0.65)', backdropFilter: 'blur(6px)' }}
      onClick={handleBackdrop}
    >
      <div
        className="w-full max-w-md rounded-2xl border border-slate-700/60 p-6"
        style={{ backgroundColor: '#131520' }}
      >
        {/* Modal title and close button. */}
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

        {/* Import form. */}
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
            // Inform the user that genre lookup and track extraction can take time.
            <p className="text-xs text-slate-500 text-center">
              Analyzing tracks and looking up genres — may take a moment…
            </p>
          )}
          {/* Show import errors directly inside the modal. */}
          {error && <ErrorMessage message={error} />}

          {/* Form actions. */}
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

      {/* Nested confirmation modal appears only when interrupting an active import. */}
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

/**
 * Full playlist library page. It loads all imports, opens the import modal, and keeps local list
 * state synchronized after rename/delete/import actions.
 */
export default function Playlists() {
  // Full imported playlist collection.
  const [playlists, setPlaylists] = useState([])

  // Page-level loading/error state.
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  // Controls import modal visibility.
  const [showModal, setShowModal] = useState(false)

  // Global import flag disables the "Import New" button during another import.
  const { isImporting } = useImport()

  /**
   * Fetches the current playlist list and exposes any backend/network error to the retryable error
   * callout.
   */
  async function load() {
    // Start/restart loading state and clear old errors.
    setLoading(true)
    setError(null)
    try {
      // Fetch all saved playlists.
      setPlaylists((await api.getPlaylists()) || [])
    } catch (err) {
      // Surface load failures in the page error callout.
      setError(err.message)
    }

    // End loading state.
    setLoading(false)
  }

  useEffect(() => {
    load()
  }, [])

  /**
   * Adds a newly imported playlist to the top of the list and closes the import modal.
   */
  function handleImportSuccess(result) {
    // Close the modal after successful import.
    setShowModal(false)

    // Optimistically put the new playlist first, matching backend sort order.
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
