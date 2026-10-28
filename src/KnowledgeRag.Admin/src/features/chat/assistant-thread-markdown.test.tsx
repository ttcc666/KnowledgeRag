import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { SourceExcerptMarkdown } from './chat-markdown'

describe('SourceExcerptMarkdown', () => {
  it('renders evidence markdown structurally without active remote links, images, or raw html', () => {
    const { container } = render(<SourceExcerptMarkdown text={'## 标题\n\n3. 第三项\n4. 第四项\n\n```csharp\nvar x = 1;\n```\n\n[外链](https://example.com) ![远程图](https://example.com/a.png) <script>alert(1)</script>'} />)
    expect(screen.getByRole('heading', { name: '标题' })).toBeInTheDocument()
    expect(container.querySelector('ol')?.getAttribute('start')).toBe('3')
    expect(container.querySelector('pre code')?.textContent).toContain('var x = 1;')
    expect(container.querySelector('a')).toBeNull()
    expect(container.querySelector('img')).toBeNull()
    expect(container.querySelector('script')).toBeNull()
  })
})
