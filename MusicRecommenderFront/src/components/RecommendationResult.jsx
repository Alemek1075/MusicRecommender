export default function RecommendationResult({ recommendation, onDismiss }) {
  if (!recommendation) return null

  const trackNums =
    typeof recommendation.favoriteTrackNumbers === 'string'
      ? recommendation.favoriteTrackNumbers
      : recommendation.favoriteTrackNumbers?.join(', ') || 'all tracks'

  return (
    <div
      className="rounded-2xl border border-violet-500/30 p-6"
      style={{
        background:
          'linear-gradient(135deg, rgba(109,40,217,0.12) 0%, rgba(139,92,246,0.06) 100%)',
      }}
    >
      <div className="flex items-start justify-between mb-5">
        <div className="flex items-center gap-2.5">
          <div className="w-8 h-8 rounded-xl bg-violet-500/20 flex items-center justify-center">
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
          <span className="text-violet-300 text-xs font-semibold uppercase tracking-widest">
            Recommended for you
          </span>
        </div>
        {onDismiss && (
          <button
            onClick={onDismiss}
            className="text-slate-600 hover:text-slate-300 transition-colors p-1"
          >
            <svg
              className="w-4 h-4"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              strokeWidth="2"
            >
              <line x1="18" y1="6" x2="6" y2="18" strokeLinecap="round" />
              <line x1="6" y1="6" x2="18" y2="18" strokeLinecap="round" />
            </svg>
          </button>
        )}
      </div>

      <div className="flex items-center gap-4">
        <div
          className="w-14 h-14 rounded-2xl bg-violet-500/20 flex items-center justify-center flex-shrink-0"
        >
          <svg
            className="w-7 h-7 text-violet-400"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
            strokeWidth="1.5"
          >
            <path d="M9 18V5l12-2v13" strokeLinecap="round" strokeLinejoin="round" />
            <circle cx="6" cy="18" r="3" />
            <circle cx="18" cy="16" r="3" />
          </svg>
        </div>
        <div className="min-w-0">
          <p className="text-xl font-bold text-white leading-tight">
            {recommendation.suggestedTrackName}
          </p>
          <p className="text-slate-400 text-sm mt-1">{recommendation.suggestedArtist}</p>
        </div>
      </div>

      <div className="mt-5 pt-4 border-t border-slate-700/40">
        <p className="text-xs text-slate-600">
          Based on favorites: <span className="text-slate-500">{trackNums}</span>
        </p>
      </div>
    </div>
  )
}
