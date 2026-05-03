import { NavLink } from 'react-router-dom'
import { useImport } from '../context/ImportContext'

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

export default function Navbar() {
  const { isImporting } = useImport()

  const link = ({ isActive }) =>
    `px-4 py-2 rounded-xl text-sm font-medium transition-all duration-150 ${
      isActive
        ? 'bg-violet-500/20 text-violet-300'
        : 'text-slate-400 hover:text-slate-200 hover:bg-white/5'
    } ${isImporting ? 'opacity-40 pointer-events-none' : ''}`

  function blockIfImporting(e) {
    if (isImporting) e.preventDefault()
  }

  return (
    <nav
      className="sticky top-0 z-50 glass-nav border-b"
      style={{ borderColor: 'rgba(255,255,255,0.06)' }}
    >
      <div className="max-w-5xl mx-auto px-4 h-16 flex items-center justify-between">
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
