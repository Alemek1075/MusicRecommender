import { useState, useRef, useEffect } from 'react'
import { createPortal } from 'react-dom'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'

/**
 * Formats the backend UTC timestamp into the short date shown on playlist cards.
 */
function formatDate(dateStr) {
  // Convert ISO/UTC date strings into a stable short US date label.
  return new Date(dateStr).toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  })
}

/**
 * Shortens long playlist URLs so cards remain scannable while still revealing the source.
 */
function truncateUrl(url, max = 52) {
  // Nothing to truncate when URL is empty or already short.
  if (!url || url.length <= max) return url

  // Add ellipsis after slicing to the maximum card-friendly length.
  return url.slice(0, max) + '…'
}

/**
 * Renders the platform badge by inspecting the original external URL.
 */
function PlatformBadge({ url }) {
  // Spotify playlist URLs get a green platform badge.
  if (url?.includes('spotify.com')) {
    return (
      <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium bg-green-500/12 text-green-400 border border-green-500/20">
        <svg className="w-3 h-3" viewBox="0 0 24 24" fill="currentColor">
          <path d="M12 0C5.4 0 0 5.4 0 12s5.4 12 12 12 12-5.4 12-12S18.66 0 12 0zm5.521 17.34c-.24.359-.66.48-1.021.24-2.82-1.74-6.36-2.101-10.561-1.141-.418.122-.779-.179-.899-.539-.12-.421.18-.78.54-.9 4.56-1.021 8.52-.6 11.64 1.32.42.18.479.659.301 1.02zm1.44-3.3c-.301.42-.841.6-1.262.3-3.239-1.98-8.159-2.58-11.939-1.38-.479.12-1.02-.12-1.14-.6-.12-.48.12-1.021.6-1.141C9.6 9.9 15 10.561 18.72 12.84c.361.181.54.78.241 1.2zm.12-3.36C15.24 8.4 8.82 8.16 5.16 9.301c-.6.179-1.2-.181-1.38-.721-.18-.601.18-1.2.72-1.381 4.26-1.26 11.28-1.02 15.721 1.621.539.3.719 1.02.419 1.56-.299.421-1.02.599-1.559.3z" />
        </svg>
        Spotify
      </span>
    )
  }
  // YouTube playlist/short URLs get a red platform badge.
  if (url?.includes('youtube.com') || url?.includes('youtu.be')) {
    return (
      <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium bg-red-500/12 text-red-400 border border-red-500/20">
        <svg className="w-3 h-3" viewBox="0 0 24 24" fill="currentColor">
          <path d="M23.495 6.205a3.007 3.007 0 0 0-2.088-2.088c-1.87-.501-9.396-.501-9.396-.501s-7.507-.01-9.396.501A3.007 3.007 0 0 0 .527 6.205a31.247 31.247 0 0 0-.522 5.805 31.247 31.247 0 0 0 .522 5.783 3.007 3.007 0 0 0 2.088 2.088c1.868.502 9.396.502 9.396.502s7.506 0 9.396-.502a3.007 3.007 0 0 0 2.088-2.088 31.247 31.247 0 0 0 .5-5.783 31.247 31.247 0 0 0-.5-5.805zM9.609 15.601V8.408l6.264 3.602z" />
        </svg>
        YouTube
      </span>
    )
  }
  // Unknown source gets no badge.
  return null
}

/**
 * Modal used to add, update, or clear a playlist display name. It portals to document.body so it
 * is not clipped by card/grid containers.
 */
function RenameModal({ initialName, onClose, onSave, busy, error }) {
  // Local input state starts from the playlist's current display name.
  const [value, setValue] = useState(initialName || '')

  /**
   * Closes the modal when the user clicks the backdrop, but not when they click inside the dialog.
   */
  function handleBackdrop(e) {
    if (e.target === e.currentTarget) onClose()
  }

  /**
   * Sends trimmed text to the parent. An empty value becomes null to clear the backend name field.
   */
  function handleSubmit(e) {
    // Keep the form from refreshing the page.
    e.preventDefault()

    // Save trimmed text, or null when the user clears the input.
    onSave(value.trim() || null)
  }

  return createPortal(
    /* Overlay catches backdrop clicks and visually isolates the modal. */
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      style={{ backgroundColor: 'rgba(0,0,0,0.7)', backdropFilter: 'blur(8px)' }}
      onClick={handleBackdrop}
    >
      {/* Stop clicks inside the dialog from bubbling to the backdrop. */}
      <div
        className="w-full max-w-lg rounded-3xl border border-violet-500/20 p-8 shadow-2xl"
        style={{
          background: 'linear-gradient(180deg, #1a1d2e 0%, #131520 100%)',
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Dialog header with edit icon and title. */}
        <div className="flex items-center gap-3 mb-1">
          <div className="w-10 h-10 rounded-2xl bg-violet-500/15 flex items-center justify-center">
            <svg
              className="w-5 h-5 text-violet-400"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              strokeWidth="2"
            >
              <path
                d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"
                strokeLinecap="round"
                strokeLinejoin="round"
              />
              <path
                d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"
                strokeLinecap="round"
                strokeLinejoin="round"
              />
            </svg>
          </div>
          <h2 className="text-lg font-semibold text-slate-100">Rename playlist</h2>
        </div>
        <p className="text-sm text-slate-500 mb-6 ml-13">
          Give this playlist a custom display name. Leave the field blank to revert to the URL.
        </p>

        {/* Rename form posts through handleSubmit. */}
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-xs font-medium text-slate-500 mb-2 uppercase tracking-wide">
              Display name
            </label>
            <input
              type="text"
              value={value}
              onChange={(e) => setValue(e.target.value)}
              placeholder="e.g. My summer vibes"
              className="w-full rounded-xl px-4 py-3.5 text-base text-slate-100 placeholder-slate-600 focus:outline-none focus:ring-2 focus:ring-violet-500/40 focus:border-violet-500/50 transition-all"
              style={{ backgroundColor: '#0f1119', border: '1px solid rgba(255,255,255,0.08)' }}
              autoFocus
              disabled={busy}
            />
          </div>

          {error && (
            /* Inline backend error area shown if rename fails. */
            <div className="rounded-xl border border-red-500/30 bg-red-500/10 px-4 py-3">
              <p className="text-xs font-semibold text-red-300 mb-1">Couldn't save</p>
              <p className="text-xs text-red-200/80 break-words">{error}</p>
            </div>
          )}

          {/* Modal actions. */}
          <div className="flex gap-3 pt-2">
            <button
              type="button"
              onClick={onClose}
              disabled={busy}
              className="flex-1 py-3 rounded-xl text-slate-300 hover:text-slate-100 text-sm font-medium border border-slate-700/60 hover:border-slate-600 hover:bg-white/5 transition-all disabled:opacity-50"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={busy}
              className="flex-1 py-3 rounded-xl bg-violet-600 hover:bg-violet-500 disabled:opacity-50 text-white text-sm font-semibold shadow-lg shadow-violet-500/20 transition-all flex items-center justify-center gap-2"
            >
              {busy && (
                <span className="w-4 h-4 animate-spin rounded-full border-2 border-white/30 border-t-white" />
              )}
              {busy ? 'Saving…' : 'Save name'}
            </button>
          </div>
        </form>
      </div>
    </div>,
    document.body
  )
}

/**
 * Generic confirmation dialog for destructive card actions.
 */
function ConfirmModal({ title, message, confirmLabel = 'Delete', danger = true, onClose, onConfirm, busy }) {
  /**
   * Treats a click on the overlay as cancel, while preserving clicks inside the dialog body.
   */
  function handleBackdrop(e) {
    if (e.target === e.currentTarget) onClose()
  }
  return createPortal(
    /* Confirmation overlay. */
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      style={{ backgroundColor: 'rgba(0,0,0,0.65)', backdropFilter: 'blur(6px)' }}
      onClick={handleBackdrop}
    >
      {/* Dialog content stops click propagation to avoid accidental cancel. */}
      <div
        className="w-full max-w-md rounded-2xl border border-slate-700/60 p-6 shadow-2xl"
        style={{ backgroundColor: '#131520' }}
        onClick={(e) => e.stopPropagation()}
      >
        <h2 className="text-base font-semibold text-slate-100 mb-2">{title}</h2>
        <p className="text-sm text-slate-500 mb-5">{message}</p>
        {/* Cancel and confirm actions. */}
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
            className={`flex-1 py-2.5 rounded-xl text-white text-sm font-medium disabled:opacity-50 ${
              danger ? 'bg-red-600 hover:bg-red-500' : 'bg-violet-600 hover:bg-violet-500'
            }`}
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
 * Playlist summary card used on Home and Playlists. It owns the rename/delete menus and delegates
 * parent state updates through optional callbacks.
 */
export default function PlaylistCard({ playlist, onRenamed, onDeleted }) {
  // Router navigation opens the playlist detail page from the card body.
  const navigate = useNavigate()

  // Menu/modal/busy/error state is local to each card instance.
  const [menuOpen, setMenuOpen] = useState(false)
  const [showRename, setShowRename] = useState(false)
  const [showDelete, setShowDelete] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)
  const menuRef = useRef(null)

  // Close the overflow menu when the user clicks elsewhere.
  useEffect(() => {
    /**
     * Document-level click handler used only while the menu is open.
     */
    function handler(e) {
      if (menuRef.current && !menuRef.current.contains(e.target)) setMenuOpen(false)
    }
    if (menuOpen) document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [menuOpen])

  /**
   * Persists a new custom playlist name and informs the parent list about the updated entity.
   */
  async function handleRename(name) {
    // Mark modal action as busy and clear any previous error.
    setBusy(true)
    setError(null)
    try {
      // Persist the name change.
      const updated = await api.renamePlaylist(playlist.id, name)

      // Let parent lists merge the updated playlist.
      onRenamed?.(updated)

      // Close the rename modal after success.
      setShowRename(false)
    } catch (err) {
      // Show backend/network failure in the modal.
      setError(err.message)
    }

    // Re-enable modal controls.
    setBusy(false)
  }

  /**
   * Deletes the playlist, then lets the parent remove the card from its local collection.
   */
  async function handleDelete() {
    // Mark delete action as busy and clear stale errors.
    setBusy(true)
    setError(null)
    try {
      // Delete playlist through the backend API.
      await api.deletePlaylist(playlist.id)

      // Remove the card from parent state.
      onDeleted?.(playlist.id)

      // Close the confirmation modal.
      setShowDelete(false)
    } catch (err) {
      // Store failure so the card can surface it if needed.
      setError(err.message)
    }

    // Re-enable modal controls.
    setBusy(false)
  }

  // Custom names take precedence; otherwise the URL itself becomes the display title.
  const renamedName = playlist.name?.trim()
  const displayTitle = renamedName || truncateUrl(playlist.externalUrl)

  return (
    /* Entire card is the visual summary for one playlist. */
    <div
      className="rounded-2xl border border-slate-700/40 p-5 card-hover group relative"
      style={{ backgroundColor: '#131520' }}
    >
      <div className="flex items-center justify-between gap-2 mb-3">
        {/* Left side: optional custom name and platform badge. */}
        <div className="flex items-center gap-2 min-w-0">
          {renamedName && (
            <span className="text-sm font-semibold text-slate-100 truncate">{renamedName}</span>
          )}
          <PlatformBadge url={playlist.externalUrl} />
        </div>
        {/* Right side: processed date and overflow menu. */}
        <div className="flex items-center gap-2 flex-shrink-0">
          <span className="text-slate-600 text-xs">{formatDate(playlist.processedAt)}</span>
          <div className="relative" ref={menuRef}>
            <button
              onClick={(e) => {
                // Keep the menu click from triggering card navigation.
                e.stopPropagation()
                setMenuOpen((v) => !v)
              }}
              className="p-1 rounded-lg text-slate-500 hover:text-slate-200 hover:bg-white/5 transition-colors"
              aria-label="Playlist menu"
            >
              <svg className="w-4 h-4" viewBox="0 0 24 24" fill="currentColor">
                <circle cx="5" cy="12" r="1.6" />
                <circle cx="12" cy="12" r="1.6" />
                <circle cx="19" cy="12" r="1.6" />
              </svg>
            </button>
            {menuOpen && (
              /* Floating menu for rename/delete actions. */
              <div
                className="absolute right-0 mt-1 w-40 rounded-xl border border-slate-700/60 shadow-lg overflow-hidden z-20"
                style={{ backgroundColor: '#1a1d2e' }}
              >
                <button
                  onClick={(e) => {
                    // Open rename modal from the menu.
                    e.stopPropagation()
                    setMenuOpen(false)
                    setShowRename(true)
                  }}
                  className="w-full text-left px-3 py-2 text-sm text-slate-200 hover:bg-white/5 transition-colors"
                >
                  Rename
                </button>
                <button
                  onClick={(e) => {
                    // Open delete confirmation from the menu.
                    e.stopPropagation()
                    setMenuOpen(false)
                    setShowDelete(true)
                  }}
                  className="w-full text-left px-3 py-2 text-sm text-red-400 hover:bg-red-500/10 transition-colors"
                >
                  Delete
                </button>
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Card body navigates to track detail. */}
      <div onClick={() => navigate(`/playlists/${playlist.id}`)} className="cursor-pointer">
        {!renamedName && (
          <p className="text-slate-300 text-sm font-medium group-hover:text-slate-100 transition-colors break-all leading-relaxed">
            {displayTitle}
          </p>
        )}
        {renamedName && (
          <p className="text-xs text-slate-600 break-all">{truncateUrl(playlist.externalUrl)}</p>
        )}

        <div className="flex items-center gap-1.5 mt-4">
          <span className="text-xs text-violet-400 font-medium">View tracks</span>
          <svg
            className="w-3.5 h-3.5 text-violet-400 group-hover:translate-x-0.5 transition-transform"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
            strokeWidth="2.5"
          >
            <path d="M5 12h14M12 5l7 7-7 7" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        </div>
      </div>

      {/* Rename dialog is mounted only when requested. */}
      {showRename && (
        <RenameModal
          initialName={playlist.name || ''}
          onClose={() => {
            setShowRename(false)
            setError(null)
          }}
          onSave={handleRename}
          busy={busy}
          error={error}
        />
      )}
      {/* Delete confirmation is mounted only when requested. */}
      {showDelete && (
        <ConfirmModal
          title="Delete this playlist?"
          message="The playlist, its tracks, and its recommendation history will all be removed. This cannot be undone."
          confirmLabel="Delete"
          onClose={() => setShowDelete(false)}
          onConfirm={handleDelete}
          busy={busy}
        />
      )}
    </div>
  )
}
