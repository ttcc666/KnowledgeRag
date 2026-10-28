import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { GroundedText } from './assistant-thread'
import type { ChatSource } from '@/lib/types'

const source: ChatSource = {
  documentId: 'd1',
  fileName: 'handbook.pdf',
  chunkIndex: 3,
  sourceLink: '/file',
  score: 0.94,
  excerpt: 'Evidence',
}

afterEach(cleanup)

describe('GroundedText', () => {
  it('renders indented lists and inline code as structured content', () => {
    const { container } = render(
      <GroundedText
        text={'3. **后端代码编写结构：**\n\n  - 描述分层方法，并说明 `Min Height` 配置 [1]\n  - 保持模块边界\n\n4. **组件信息：**'}
        sources={[source]}
        onSource={() => undefined}
      />,
    )

    const orderedLists = container.querySelectorAll('ol')
    expect(orderedLists).toHaveLength(2)
    expect(orderedLists[0]).toHaveAttribute('start', '3')
    expect(orderedLists[1]).toHaveAttribute('start', '4')

    const unordered = container.querySelector('ul')
    expect(unordered).not.toBeNull()
    expect(within(unordered as HTMLUListElement).getAllByRole('listitem')).toHaveLength(2)

    const inlineCode = screen.getByText('Min Height')
    expect(inlineCode.tagName).toBe('CODE')
    expect(container.textContent).not.toContain('`Min Height`')
  })

  it('keeps citation trust boundaries while improving markdown presentation', () => {
    const onSource = vi.fn()
    const { container } = render(
      <GroundedText
        text={'可信引用 [1]，不存在的引用 [2]，以及 <script>alert(1)</script>。'}
        sources={[source]}
        onSource={onSource}
      />,
    )

    const trusted = screen.getByRole('button', { name: '查看引用 1: handbook.pdf' })
    fireEvent.click(trusted)
    expect(onSource).toHaveBeenCalledWith(source)
    expect(screen.queryByRole('button', { name: /查看引用 2/ })).not.toBeInTheDocument()
    expect(screen.getByText(/不存在的引用 \[2\]/)).toBeInTheDocument()
    expect(container.querySelector('script')).toBeNull()
  })
})
