import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import axe from 'axe-core'
import { StatusBadge } from './product'
import { Button, Input } from './ui'

describe('product UI primitives', () => {
  it('renders cancelled ingestion status', () => {
    render(<StatusBadge status='Cancelled' />)
    expect(screen.getByText('Cancelled')).toBeInTheDocument()
  })

  it('has no basic accessibility violations', async () => {
    const { container } = render(<main><label htmlFor='q'>Query</label><Input id='q' /><Button>Search</Button></main>)
    const result = await axe.run(container)
    expect(result.violations).toHaveLength(0)
  })
})
