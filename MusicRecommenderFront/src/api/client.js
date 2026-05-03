const BASE = '/api'

async function request(path, options = {}) {
  const res = await fetch(`${BASE}${path}`, {
    headers: { 'Content-Type': 'application/json', ...options.headers },
    ...options,
  })
  if (!res.ok) {
    const text = await res.text()
    let msg = text
    try {
      const json = JSON.parse(text)
      msg = json.message || json.title || json || text
    } catch {}
    throw new Error(typeof msg === 'string' ? msg : `HTTP ${res.status}`)
  }
  const text = await res.text()
  return text ? JSON.parse(text) : null
}

export const api = {
  submitPlaylist: (url, signal) =>
    request('/playlists', { method: 'POST', body: JSON.stringify({ url }), signal }),

  getPlaylists: () => request('/playlists'),

  getTracks: (id) => request(`/playlists/${id}/tracks`),

  renamePlaylist: (id, name) =>
    request(`/playlists/${id}`, { method: 'PATCH', body: JSON.stringify({ name }) }),

  deletePlaylist: (id) => request(`/playlists/${id}`, { method: 'DELETE' }),

  generateRecommendation: (playlistId, selectedTrackNumbers = []) => {
    const params = new URLSearchParams({ playlistId: String(playlistId) })
    selectedTrackNumbers.forEach((n) => params.append('selectedTrackNumbers', String(n)))
    return request(`/recommendations/generate?${params}`)
  },

  getHistory: () => request('/recommendations/history'),

  deleteRecommendation: (id) => request(`/recommendations/${id}`, { method: 'DELETE' }),

  deletePlaylistHistory: (playlistId) =>
    request(`/recommendations/playlist/${playlistId}`, { method: 'DELETE' }),

  getStatistics: (playlistIds = []) => {
    const params = new URLSearchParams()
    playlistIds.forEach((id) => params.append('playlistIds', String(id)))
    const qs = playlistIds.length ? `?${params}` : ''
    return request(`/statistics${qs}`)
  },
}
