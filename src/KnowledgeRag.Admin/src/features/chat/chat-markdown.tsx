import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'

export const CITATION_HREF_PREFIX = '#knowledge-rag-citation-'

type MarkdownAstNode = {
  type: string
  value?: string
  url?: string
  children?: MarkdownAstNode[]
}

const citationPattern = /\[(\d{1,3})\]/g
const citationSkipNodes = new Set(['code', 'inlineCode', 'html', 'link', 'linkReference'])

export function citationNumberFromHref(href?: string) {
  if (!href?.startsWith(CITATION_HREF_PREFIX)) return null
  const value = Number(href.slice(CITATION_HREF_PREFIX.length))
  return Number.isInteger(value) && value >= 1 ? value : null
}

export function rewriteTrustedCitationTextNodes(node: MarkdownAstNode, sourceCount: number): void {
  if (citationSkipNodes.has(node.type) || !node.children?.length) return

  const nextChildren: MarkdownAstNode[] = []
  for (const child of node.children) {
    if (child.type === 'text' && typeof child.value === 'string') {
      nextChildren.push(...splitCitationText(child.value, sourceCount))
      continue
    }

    rewriteTrustedCitationTextNodes(child, sourceCount)
    nextChildren.push(child)
  }

  node.children = nextChildren
}

function splitCitationText(value: string, sourceCount: number): MarkdownAstNode[] {
  const result: MarkdownAstNode[] = []
  let cursor = 0
  citationPattern.lastIndex = 0
  let match: RegExpExecArray | null

  while ((match = citationPattern.exec(value)) !== null) {
    if (match.index > cursor) result.push({ type: 'text', value: value.slice(cursor, match.index) })

    const citation = Number(match[1])
    if (citation >= 1 && citation <= sourceCount) {
      result.push({
        type: 'link',
        url: `${CITATION_HREF_PREFIX}${citation}`,
        children: [{ type: 'text', value: `[${citation}]` }],
      })
    } else {
      result.push({ type: 'text', value: match[0] })
    }
    cursor = match.index + match[0].length
  }

  if (cursor < value.length) result.push({ type: 'text', value: value.slice(cursor) })
  return result.length ? result : [{ type: 'text', value }]
}

export function createTrustedCitationRemarkPlugin(sourceCount: number) {
  return () => (tree: MarkdownAstNode) => rewriteTrustedCitationTextNodes(tree, sourceCount)
}

export function SourceExcerptMarkdown({ text }: { text: string }) {
  return (
    <div className='source-markdown break-words text-xs leading-6 text-foreground/85'>
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        skipHtml
        components={{
          h1: ({ children }) => <h3 className='mb-2 mt-4 text-sm font-semibold first:mt-0'>{children}</h3>,
          h2: ({ children }) => <h3 className='mb-2 mt-4 text-sm font-semibold first:mt-0'>{children}</h3>,
          h3: ({ children }) => <h4 className='mb-1.5 mt-3 font-semibold first:mt-0'>{children}</h4>,
          h4: ({ children }) => <h4 className='mb-1.5 mt-3 font-semibold first:mt-0'>{children}</h4>,
          p: ({ children }) => <p className='my-2 first:mt-0 last:mb-0'>{children}</p>,
          ul: ({ children }) => <ul className='my-2 list-disc space-y-1 pl-5 marker:text-muted-foreground'>{children}</ul>,
          ol: ({ children, start }) => <ol start={start} className='my-2 list-decimal space-y-1 pl-5 marker:text-muted-foreground'>{children}</ol>,
          li: ({ children }) => <li className='pl-0.5'>{children}</li>,
          blockquote: ({ children }) => <blockquote className='my-2 border-l-2 border-primary/25 pl-3 text-foreground/75'>{children}</blockquote>,
          pre: ({ children }) => <pre className='my-2 overflow-x-auto rounded-lg border bg-muted/55 p-3 text-[11px] leading-5'>{children}</pre>,
          code: ({ children, className }) => className
            ? <code className={className}>{children}</code>
            : <code className='rounded border bg-muted/60 px-1 py-0.5 font-mono text-[0.92em]'>{children}</code>,
          table: ({ children }) => <div className='my-2 overflow-x-auto'><table className='w-full border-collapse text-left text-[11px]'>{children}</table></div>,
          thead: ({ children }) => <thead className='border-b bg-muted/40'>{children}</thead>,
          th: ({ children }) => <th className='border px-2 py-1.5 font-semibold'>{children}</th>,
          td: ({ children }) => <td className='border px-2 py-1.5 align-top'>{children}</td>,
          a: ({ children }) => <span className='underline decoration-dotted underline-offset-2'>{children}</span>,
          img: ({ alt }) => <span className='text-muted-foreground'>[图片{alt ? `: ${alt}` : ''}]</span>,
        }}
      >
        {text}
      </ReactMarkdown>
    </div>
  )
}
