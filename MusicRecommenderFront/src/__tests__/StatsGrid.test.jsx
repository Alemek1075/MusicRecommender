import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import StatsGrid from '../components/StatsGrid'

const baseStats = {
  totalTracks: 10,
  totalHours: 1,
  totalMinutes: 30,
  topArtist: 'The Beatles',
  topGenre: 'Rock',
}

describe('StatsGrid', () => {
  it('renders nothing when stats is null', () => {
    const { container } = render(<StatsGrid stats={null} />)
    expect(container.firstChild).toBeNull()
  })

  it('renders nothing when stats is undefined', () => {
    const { container } = render(<StatsGrid />)
    expect(container.firstChild).toBeNull()
  })

  it('displays hours and minutes when totalHours > 0', () => {
    render(<StatsGrid stats={{ ...baseStats, totalHours: 2, totalMinutes: 15 }} />)
    expect(screen.getByText('2h 15m')).toBeInTheDocument()
  })

  it('displays only minutes when totalHours is 0', () => {
    render(<StatsGrid stats={{ ...baseStats, totalHours: 0, totalMinutes: 45 }} />)
    expect(screen.getByText('45m')).toBeInTheDocument()
    expect(screen.queryByText(/h /)).not.toBeInTheDocument()
  })

  it('renders the total track count', () => {
    render(<StatsGrid stats={{ ...baseStats, totalTracks: 42 }} />)
    expect(screen.getByText('42')).toBeInTheDocument()
  })

  it('renders the top artist name', () => {
    render(<StatsGrid stats={baseStats} />)
    expect(screen.getByText('The Beatles')).toBeInTheDocument()
  })

  it('renders the top genre name', () => {
    render(<StatsGrid stats={baseStats} />)
    expect(screen.getByText('Rock')).toBeInTheDocument()
  })

  it('shows dash placeholder when topArtist is null', () => {
    render(<StatsGrid stats={{ ...baseStats, topArtist: null }} />)
    // At least one "—" appears (artist; genre may also be null depending on data)
    expect(screen.getAllByText('—').length).toBeGreaterThanOrEqual(1)
  })

  it('shows dash placeholder when topGenre is null', () => {
    render(<StatsGrid stats={{ ...baseStats, topGenre: null }} />)
    expect(screen.getAllByText('—').length).toBeGreaterThanOrEqual(1)
  })
})
