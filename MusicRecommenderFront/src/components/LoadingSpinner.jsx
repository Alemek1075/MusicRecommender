/**
 * Small shared loading indicator. size maps to Tailwind dimensions while className lets each page
 * add contextual spacing without duplicating spinner markup.
 */
export default function LoadingSpinner({ size = 'md', className = '' }) {
  // Accepted visual sizes for all current loading states.
  const sizes = { sm: 'w-5 h-5', md: 'w-8 h-8', lg: 'w-12 h-12' }
  return (
    /* Outer wrapper lets callers add vertical spacing/alignment. */
    <div className={`flex items-center justify-center ${className}`}>
      {/* Inner circle uses border-top color difference to create the spin effect. */}
      <div
        className={`${sizes[size]} animate-spin rounded-full border-2 border-slate-700 border-t-violet-500`}
      />
    </div>
  )
}
