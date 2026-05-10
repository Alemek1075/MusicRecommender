import { describe, it, expect, vi, beforeEach } from 'vitest'
import { api } from '../api/client'

// Helper that creates a fake successful fetch response.
function mockResponse(data, status = 200) {
  return Promise.resolve({
    ok: status >= 200 && status < 300,
    status,
    text: () => Promise.resolve(data !== null ? JSON.stringify(data) : ''),
  })
}

const fetchMock = vi.fn()
vi.stubGlobal('fetch', fetchMock)

beforeEach(() => {
  fetchMock.mockClear()
})

describe('api.getTracks', () => {
  it('calls the correct path with no query params when trackNumbers is empty', async () => {
    fetchMock.mockReturnValueOnce(mockResponse([]))
    await api.getTracks(42)
    expect(fetchMock.mock.calls[0][0]).toBe('/api/playlists/42/tracks')
  })

  it('appends each track number as a repeated trackNumbers param', async () => {
    fetchMock.mockReturnValueOnce(mockResponse([]))
    await api.getTracks(7, [2, 4])
    const url = fetchMock.mock.calls[0][0]
    expect(url).toContain('trackNumbers=2')
    expect(url).toContain('trackNumbers=4')
  })
})

describe('api.getStatistics', () => {
  it('omits the query string entirely when called with no IDs', async () => {
    fetchMock.mockReturnValueOnce(mockResponse({}))
    await api.getStatistics()
    expect(fetchMock.mock.calls[0][0]).toBe('/api/statistics')
  })

  it('appends each playlist ID as a repeated playlistIds param', async () => {
    fetchMock.mockReturnValueOnce(mockResponse({}))
    await api.getStatistics([1, 3])
    const url = fetchMock.mock.calls[0][0]
    expect(url).toContain('playlistIds=1')
    expect(url).toContain('playlistIds=3')
  })
})

describe('api.generateRecommendation', () => {
  it('builds the correct URL with playlistId, count, and seed tracks', async () => {
    fetchMock.mockReturnValueOnce(mockResponse([]))
    await api.generateRecommendation(5, [1, 2], 3)
    const url = fetchMock.mock.calls[0][0]
    expect(url).toContain('playlistId=5')
    expect(url).toContain('count=3')
    expect(url).toContain('selectedTrackNumbers=1')
    expect(url).toContain('selectedTrackNumbers=2')
  })

  it('defaults count to 1 when not provided', async () => {
    fetchMock.mockReturnValueOnce(mockResponse([]))
    await api.generateRecommendation(5, [])
    const url = fetchMock.mock.calls[0][0]
    expect(url).toContain('count=1')
  })
})

describe('api error handling', () => {
  it('throws an Error with the backend message on non-2xx responses', async () => {
    fetchMock.mockReturnValueOnce(
      Promise.resolve({
        ok: false,
        status: 400,
        text: () => Promise.resolve(JSON.stringify({ message: 'Bad playlist URL' })),
      })
    )
    await expect(api.getPlaylists()).rejects.toThrow('Bad playlist URL')
  })
})
