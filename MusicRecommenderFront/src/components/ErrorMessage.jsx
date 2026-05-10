/**
 * Reusable error callout. Pages can pass an optional retry handler when the failed operation can
 * be safely attempted again from the same UI state.
 */
export default function ErrorMessage({ message, onRetry }) {
  return (
    /* Red-tinted container visually separates recoverable errors from normal content. */
    <div className="rounded-2xl border border-red-500/20 bg-red-500/8 p-4 flex items-start gap-3">
      {/* Warning icon anchors the message for quick scanning. */}
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
      {/* Text column takes remaining space and wraps long backend messages. */}
      <div className="flex-1">
        <p className="text-red-300 text-sm leading-relaxed">{message}</p>
        {/* Retry appears only when the parent can safely re-run the failed load. */}
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
