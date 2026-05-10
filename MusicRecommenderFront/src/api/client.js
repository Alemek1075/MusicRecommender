// Backend API prefix. Vite/proxy or production hosting is expected to route /api to ASP.NET Core.
const BASE = '/api'

/**
 * Shared fetch wrapper for every frontend API call.
 * It attaches JSON headers, preserves caller-provided options like AbortSignal, normalizes backend
 * error bodies into thrown Error objects, and returns null for empty 204-style responses.
 */
async function request(path, options = {}) {
  // Execute the HTTP request against the backend API prefix.
  const res = await fetch(`${BASE}${path}`, {
    // Default every request to JSON while still allowing callers to override/extend headers.
    headers: { 'Content-Type': 'application/json', ...options.headers },
    // Spread method/body/signal/etc. after headers so caller options are preserved.
    ...options,
  })

  // Convert non-2xx responses into thrown errors for page-level catch blocks.
  if (!res.ok) {
    // Backend errors may be plain text or JSON ProblemDetails.
    const text = await res.text()

    // Start with raw text, then replace it if JSON provides a cleaner message.
    let msg = text
    try {
      // Try to parse structured error bodies.
      const json = JSON.parse(text)

      // Prefer common message fields before falling back to the parsed value or text.
      msg = json.message || json.title || json || text
    } catch {}

    // Always throw a normal Error object so UI code can read err.message.
    throw new Error(typeof msg === 'string' ? msg : `HTTP ${res.status}`)
  }

  // Read the response body once.
  const text = await res.text()

  // 204/empty responses become null; JSON responses are parsed.
  return text ? JSON.parse(text) : null
}

/**
 * Typed-ish API facade used by pages/components. Keeping endpoint construction here prevents route
 * strings and query parameter details from leaking throughout the UI.
 */
export const api = {
  // Imports a Spotify or YouTube playlist. signal lets the import modal cancel long-running work.
  submitPlaylist: (url, signal) =>
    request('/playlists', { method: 'POST', body: JSON.stringify({ url }), signal }),

  // Returns all saved playlists for dashboard/recent-list views.
  getPlaylists: () => request('/playlists'),

  // Returns all tracks for a playlist, or a selected subset used by history/favourite popovers.
  getTracks: (id, trackNumbers = []) => {
    // No track filter means request the full playlist track list.
    if (!trackNumbers.length) return request(`/playlists/${id}/tracks`)

    // Repeated query params match ASP.NET Core's List<int> binding.
    const params = new URLSearchParams()

    // Append each requested one-based track number.
    trackNumbers.forEach((n) => params.append('trackNumbers', String(n)))

    // Request only the requested track rows.
    return request(`/playlists/${id}/tracks?${params}`)
  },

  // Saves or clears the playlist's optional display name.
  renamePlaylist: (id, name) =>
    request(`/playlists/${id}`, { method: 'PATCH', body: JSON.stringify({ name }) }),

  // Deletes a playlist and its related stored data.
  deletePlaylist: (id) => request(`/playlists/${id}`, { method: 'DELETE' }),

  // Requests recommendations. selectedTrackNumbers are favourite seed tracks; count controls batch size.
  generateRecommendation: (playlistId, selectedTrackNumbers = [], count = 1) => {
    // playlistId and count are scalar query parameters.
    const params = new URLSearchParams({ playlistId: String(playlistId), count: String(count) })

    // Favourite seed tracks are repeated query parameters.
    selectedTrackNumbers.forEach((n) => params.append('selectedTrackNumbers', String(n)))

    // Call the recommendation generation endpoint.
    return request(`/recommendations/generate?${params}`)
  },

  // Loads grouped recommendation history for the History page.
  getHistory: () => request('/recommendations/history'),

  // Deletes one generated recommendation from history.
  deleteRecommendation: (id) => request(`/recommendations/${id}`, { method: 'DELETE' }),

  // Clears all recommendation history for one playlist while keeping the playlist import.
  deletePlaylistHistory: (playlistId) =>
    request(`/recommendations/playlist/${playlistId}`, { method: 'DELETE' }),

  // Loads aggregate library stats; passing playlistIds narrows stats to those playlists.
  getStatistics: (playlistIds = []) => {
    // Repeated playlistIds narrow statistics to specific playlists.
    const params = new URLSearchParams()
    playlistIds.forEach((id) => params.append('playlistIds', String(id)))

    // Omit the query string entirely for whole-library stats.
    const qs = playlistIds.length ? `?${params}` : ''

    // Request aggregate statistics.
    return request(`/statistics${qs}`)
  },
}
