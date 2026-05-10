import { NavLink } from 'react-router-dom'
import { useImport } from '../context/ImportContext'

/**
 * Inline app mark shown in the navbar. It is kept local because it is only used here and avoids an
 * extra asset request.
 */
function MusicIcon() {
  return (
    <svg
      width="22"
      height="22"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <path d="M9 18V5l12-2v13" />
      <circle cx="6" cy="18" r="3" />
      <circle cx="18" cy="16" r="3" />
    </svg>
  )
}

/**
 * Persistent top navigation. While a playlist import is running, links are visually disabled and
 * click-blocked so the user does not accidentally abandon the in-flight import.
 */
export default function Navbar() {
  // Read global import lock state.
  const { isImporting } = useImport()

  /**
   * Builds NavLink classes from React Router's active-route state and the global import lock.
   */
  const link = ({ isActive }) =>
    `px-4 py-2 rounded-xl text-sm font-medium transition-all duration-150 ${
      isActive
        ? 'bg-violet-500/20 text-violet-300'
        : 'text-slate-400 hover:text-slate-200 hover:bg-white/5'
    } ${isImporting ? 'opacity-40 pointer-events-none' : ''}`

  /**
   * Prevents navigation while an import is active. CSS pointer-events handles normal pointer
   * interaction, and this handler covers keyboard/assistive activation.
   */
  function blockIfImporting(e) {
    // Stop route changes while an import request is active.
    if (isImporting) e.preventDefault()
  }

  return (
    /* Sticky translucent navbar keeps primary navigation visible. */
    <nav
      className="sticky top-0 z-50 glass-nav border-b"
      style={{ borderColor: 'rgba(255,255,255,0.06)' }}
    >
      <div className="max-w-5xl mx-auto px-4 h-16 flex items-center justify-between">
        {/* Brand link returns home unless import locking is active. */}
        <NavLink
          to="/"
          onClick={blockIfImporting}
          className={`flex items-center gap-2.5 text-violet-400 hover:text-violet-300 transition-colors ${
            isImporting ? 'opacity-40 pointer-events-none' : ''
          }`}
        >
          <MusicIcon />
          <span className="font-semibold text-base tracking-tight text-slate-100">
            Music Recommender
          </span>
        </NavLink>

        {/* Main page links. */}
        <div className="flex items-center gap-1">
          <NavLink to="/" end className={link} onClick={blockIfImporting}>
            Home
          </NavLink>
          <NavLink to="/playlists" className={link} onClick={blockIfImporting}>
            Playlists
          </NavLink>
          <NavLink to="/history" className={link} onClick={blockIfImporting}>
            History
          </NavLink>
          {/* Import status chip appears while a playlist is being processed. */}
          {isImporting && (
            <span className="ml-2 flex items-center gap-2 text-xs text-violet-300 px-3 py-1.5 rounded-lg bg-violet-500/10 border border-violet-500/20">
              <span className="w-3 h-3 animate-spin rounded-full border-2 border-violet-400/30 border-t-violet-300" />
              Importing…
            </span>
          )}
        </div>
      </div>
    </nav>
  )
}
