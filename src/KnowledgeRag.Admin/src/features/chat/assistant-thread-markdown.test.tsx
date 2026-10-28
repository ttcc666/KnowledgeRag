import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import {
  CITATION_HREF_PREFIX,
  SourceExcerptMarkdown,
  citationNumberFromHref,
  rewriteTrustedCitationTextNodes,
} from './chat-markdown'

afterEach(cleanup)

describe('chat markdown rendering', () => {
  it('renders source markdown structure without executing html or external resources', () => {
    const { container } = render(
      <SourceExcerptMarkdown
        text={'#### 后端代码\n\n- 配置 `Min Height`\n- 保持模块边界\n\n2. 第二项\n3. 第三项\n\n```csharp\n.UnionByKey()\n```\n\n[外部链接](https://example.com)\n\n<script>alert(1)</script>\n\n![远程图片](https://example.com/pixel.png)'}
      />,
    )

    expect(screen.getByRole('heading', { name: '后端代码' })).toBeInTheDocument()
    expect(screen.getAllByRole('listitem')).toHaveLength(4)
    expect(container.querySelector('ol')).toHaveAttribute('start', '2')
    expect(screen.getByText('Min Height').tagName).toBe('CODE')
    expect(screen.getByText(/\.UnionByKey/).tagName).toBe('CODE')
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
    expect(container.querySelector('script')).toBeNull()
    expect(container.querySelector('img')).toBeNull()
  })

  it('rewrites only trusted citation text nodes and leaves code and existing links alone', () => {
    const tree: any = {
      type: 'root',
      children: [
        { type: 'paragraph', children: [{ type: 'text', value: '事实 [1]，越界 [2]' }] },
        { type: 'inlineCode', value: '[1]' },
        { type: 'link', url: 'https://example.com', children: [{ type: 'text', value: '[1]' }] },
      ],
    }

    rewriteTrustedCitationTextNodes(tree, 1)

    const paragraphChildren = tree.children[0].children
    expect(paragraphChildren.some((item: any) => item.type === 'link' && item.url === `${CITATION_HREF_PREFIX}1`)).toBe(true)
    expect(paragraphChildren.some((item: any) => item.type === 'text' && item.value?.includes('[2]'))).toBe(true)
    expect(tree.children[1]).toEqual({ type: 'inlineCode', value: '[1]' })
    expect(tree.children[2].children).toEqual([{ type: 'text', value: '[1]' }])
    expect(citationNumberFromHref(`${CITATION_HREF_PREFIX}1`)).toBe(1)
    expect(citationNumberFromHref('https://example.com')).toBeNull()
  })
})
