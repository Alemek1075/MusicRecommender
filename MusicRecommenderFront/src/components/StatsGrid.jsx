function StatCard({ label, value, icon, accentClass, bgClass }) {
  return (
    <div
      className="rounded-2xl border border-slate-700/40 p-5"
      style={{ backgroundColor: '#131520' }}
    >
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="text-slate-500 text-xs font-medium uppercase tracking-wider mb-1.5">
            {label}
          </p>
          <p className={`text-2xl font-bold truncate ${accentClass}`}>{value}</p>
        </div>
        <div className={`p-2.5 rounded-xl flex-shrink-0 ${bgClass}`}>{icon}</div>
      </div>
    </div>
  )
}

export default function StatsGrid({ stats }) {
  if (!stats) return null

  const duration =
    stats.totalHours > 0
      ? `${stats.totalHours}h ${stats.totalMinutes}m`
      : `${stats.totalMinutes}m`

  return (
    <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
      <StatCard
        label="Total Tracks"
        value={stats.totalTracks}
        accentClass="text-violet-400"
        bgClass="bg-violet-500/12"
        icon={
          <svg
            className="w-5 h-5 text-violet-400"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
            strokeWidth="2"
          >
            <path d="M9 18V5l12-2v13" strokeLinecap="round" strokeLinejoin="round" />
            <circle cx="6" cy="18" r="3" />
            <circle cx="18" cy="16" r="3" />
          </svg>
        }
      />
      <StatCard
        label="Duration"
        value={duration}
        accentClass="text-sky-400"
        bgClass="bg-sky-500/12"
        icon={
          <svg
            className="w-5 h-5 text-sky-400"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
            strokeWidth="2"
          >
            <circle cx="12" cy="12" r="10" />
            <polyline points="12 6 12 12 16 14" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        }
      />
      <StatCard
        label="Top Artist"
        value={stats.topArtist || '—'}
        accentClass="text-emerald-400"
        bgClass="bg-emerald-500/12"
        icon={
          <svg
            className="w-5 h-5 text-emerald-400"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
            strokeWidth="2"
          >
            <path
              d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"
              strokeLinecap="round"
              strokeLinejoin="round"
            />
            <circle cx="12" cy="7" r="4" />
          </svg>
        }
      />
      <StatCard
        label="Top Genre"
        value={stats.topGenre || '—'}
        accentClass="text-amber-400"
        bgClass="bg-amber-500/12"
        icon={
          <svg
            className="w-5 h-5 text-amber-400"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
            strokeWidth="2"
          >
            <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2" />
          </svg>
        }
      />
    </div>
  )
}
