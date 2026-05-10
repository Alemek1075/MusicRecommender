import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import ErrorMessage from '../components/ErrorMessage'

describe('ErrorMessage', () => {
  it('renders the message text', () => {
    render(<ErrorMessage message="Something went wrong" />)
    expect(screen.getByText('Something went wrong')).toBeInTheDocument()
  })

  it('does not show retry button when onRetry is not provided', () => {
    render(<ErrorMessage message="Error" />)
    expect(screen.queryByText('Try again')).not.toBeInTheDocument()
  })

  it('shows retry button when onRetry is provided', () => {
    render(<ErrorMessage message="Error" onRetry={() => {}} />)
    expect(screen.getByText('Try again')).toBeInTheDocument()
  })

  it('calls onRetry when the retry button is clicked', () => {
    const onRetry = vi.fn()
    render(<ErrorMessage message="Error" onRetry={onRetry} />)
    fireEvent.click(screen.getByText('Try again'))
    expect(onRetry).toHaveBeenCalledOnce()
  })

  it('renders an SVG icon for visual indication', () => {
    const { container } = render(<ErrorMessage message="Error" />)
    expect(container.querySelector('svg')).toBeInTheDocument()
  })
})
