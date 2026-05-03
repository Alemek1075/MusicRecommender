export default function ErrorMessage({ message, onRetry }) {
  return (
    <div className="rounded-2xl border border-red-500/20 bg-red-500/8 p-4 flex items-start gap-3">
      <svg
        className="w-5 h-5 text-red-400 flex-shrink-0 mt-0.5"
        fill="none"
        stroke="currentColor"
        viewBox="0 0 24 24"
        strokeWidth="2"
      >
        <circle cx="12" cy="12" r="10" />
        <line x1="12" y1="8" x2="12" y2="12" strokeLinecap="round" />
        <line x1="12" y1="16" x2="12.01" y2="16" strokeLinecap="round" />
      </svg>
      <div className="flex-1">
        <p className="text-red-300 text-sm leading-relaxed">{message}</p>
        {onRetry && (
          <button
            onClick={onRetry}
            className="mt-2 text-xs text-red-400 hover:text-red-300 underline transition-colors"
          >
            Try again
          </button>
        )}
      </div>
    </div>
  )
}
