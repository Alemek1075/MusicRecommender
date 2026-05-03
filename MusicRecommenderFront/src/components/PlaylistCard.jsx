import { useNavigate } from 'react-router-dom'

function formatDate(dateStr) {
  return new Date(dateStr).toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  })
}

function truncateUrl(url, max = 52) {
  if (!url || url.length <= max) return url
  return url.slice(0, max) + '…'
}

function PlatformBadge({ url }) {
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
  return null
}

export default function PlaylistCard({ playlist }) {
  const navigate = useNavigate()

  return (
    <div
      onClick={() => navigate(`/playlists/${playlist.id}`)}
      className="rounded-2xl border border-slate-700/40 p-5 cursor-pointer card-hover group"
      style={{ backgroundColor: '#131520' }}
    >
      <div className="flex items-center justify-between gap-2 mb-3">
        <PlatformBadge url={playlist.externalUrl} />
        <span className="text-slate-600 text-xs flex-shrink-0">
          {formatDate(playlist.processedAt)}
        </span>
      </div>

      <p className="text-slate-300 text-sm font-medium group-hover:text-slate-100 transition-colors break-all leading-relaxed">
        {truncateUrl(playlist.externalUrl)}
      </p>

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
  )
}
