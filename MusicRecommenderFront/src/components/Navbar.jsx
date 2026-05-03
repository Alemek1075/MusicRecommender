import { NavLink } from 'react-router-dom'

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
  const link = ({ isActive }) =>
    `px-4 py-2 rounded-xl text-sm font-medium transition-all duration-150 ${
      isActive
        ? 'bg-violet-500/20 text-violet-300'
        : 'text-slate-400 hover:text-slate-200 hover:bg-white/5'
    }`

  return (
    <nav
      className="sticky top-0 z-50 glass-nav border-b"
      style={{ borderColor: 'rgba(255,255,255,0.06)' }}
    >
      <div className="max-w-5xl mx-auto px-4 h-16 flex items-center justify-between">
        <NavLink
          to="/"
          className="flex items-center gap-2.5 text-violet-400 hover:text-violet-300 transition-colors"
        >
          <MusicIcon />
          <span className="font-semibold text-base tracking-tight text-slate-100">
            Music Recommender
          </span>
        </NavLink>

        <div className="flex items-center gap-1">
          <NavLink to="/" end className={link}>
            Home
          </NavLink>
          <NavLink to="/playlists" className={link}>
            Playlists
          </NavLink>
          <NavLink to="/history" className={link}>
            History
          </NavLink>
        </div>
      </div>
    </nav>
  )
}
