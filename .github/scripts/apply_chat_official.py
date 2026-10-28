from pathlib import Path
import re

root = Path('src/KnowledgeRag.Admin/src')
page = root / 'features/chat/chat-page.tsx'
text = page.read_text()

text = text.replace(
    "import { Archive, ArchiveRestore, Bot, FileText, Maximize2, Menu, MessageSquarePlus, Minimize2, PanelLeftClose, PanelLeftOpen, PanelRightClose, PanelRightOpen, Pencil, Trash2, X } from 'lucide-react'",
    "import { ArchiveRestore, Bot, ChevronDown, FileText, LoaderCircle, Maximize2, Menu, Minimize2, PanelLeftClose, PanelLeftOpen, PanelRightClose, PanelRightOpen, Trash2, X } from 'lucide-react'",
)
text = text.replace(
    "import { AssistantThread } from './assistant-thread'",
    "import { Thread } from '@/components/assistant-ui/elements/thread.aui'\nimport { ThreadList } from '@/components/assistant-ui/elements/thread-list.aui'",
)
text = text.replace("import { cn, formatDate } from '@/lib/utils'", "import { cn } from '@/lib/utils'")
text = text.replace(
    "import type { ChatMessage, ChatSource, Conversation, RagProgressEvent } from '@/lib/types'",
    "import type { ChatMessage, ChatSource, RagProgressEvent } from '@/lib/types'",
)

old_convert = """  const convertMessage = useCallback((message: ChatMessage) => ({
    id: message.id,
    role: message.role.toLowerCase() === 'user' ? 'user' as const : 'assistant' as const,
    content: [{ type: 'text' as const, text: message.content }],
    createdAt: new Date(message.createdAt),
  }), [])"""
new_convert = """  const convertMessage = useCallback((message: ChatMessage) => {
    const role = message.role.toLowerCase() === 'user' ? 'user' as const : 'assistant' as const
    const sources = role === 'assistant' ? parseSources(message) : []
    return {
      id: message.id,
      role,
      content: [
        { type: 'text' as const, text: message.content },
        ...sources.map((source, index) => ({
          type: 'source' as const,
          sourceType: 'document' as const,
          id: `${source.documentId}:${source.chunkIndex ?? index}`,
          title: source.fileName || `来源 ${index + 1}`,
          mediaType: 'application/octet-stream',
          filename: source.fileName || undefined,
        })),
      ],
      createdAt: new Date(message.createdAt),
    }
  }, [])"""
if old_convert not in text:
    raise SystemExit('convertMessage block not found')
text = text.replace(old_convert, new_convert)

marker = "    isSendDisabled: streaming || !activeKb || selectedArchived || (!!conversationId && !runtimeThreadId),\n"
if marker not in text:
    raise SystemExit('runtime marker not found')
text = text.replace(marker, marker + "    suggestions: [\n      { prompt: '总结这个知识库' },\n      { prompt: '有哪些关键主题？' },\n      { prompt: '帮我比较相关文档' },\n    ],\n", 1)
text = text.replace(
    "threads: (conversations.data || []).map((item) => ({ id: item.id, status: 'regular' as const, title: item.title })),",
    "threads: (conversations.data || []).map((item) => ({ id: item.id, status: 'regular' as const, title: item.title, lastMessageAt: new Date(item.updatedAt) })),",
)
text = text.replace(
    "archivedThreads: (archivedConversations.data || []).map((item) => ({ id: item.id, status: 'archived' as const, title: item.title })),",
    "archivedThreads: (archivedConversations.data || []).map((item) => ({ id: item.id, status: 'archived' as const, title: item.title, lastMessageAt: new Date(item.updatedAt) })),",
)

old_sources = """  const sourcesByMessageId = useMemo(() => Object.fromEntries(loadedMessages.map((message) => [message.id, parseSources(message)])), [loadedMessages])
  const traceMessageId = streaming
    ? 'live-assistant'
    : [...loadedMessages].reverse().find((message) => message.role.toLowerCase() === 'assistant')?.id || ''"""
new_sources = """  const latestAssistantMessage = [...loadedMessages].reverse().find((message) => message.role.toLowerCase() === 'assistant')
  const latestSources = latestAssistantMessage ? parseSources(latestAssistantMessage) : []"""
if old_sources not in text:
    raise SystemExit('source index block not found')
text = text.replace(old_sources, new_sources)

conversation_pattern = re.compile(r"  const conversationList = <ConversationList\n.*?\n  />", re.S)
conversation_replacement = """  const conversationList = <div className='flex h-full min-h-0 flex-col'>
    <div className='min-h-0 flex-1 overflow-y-auto p-2'>
      <ThreadList />
      {(archivedConversations.data?.length || 0) > 0 && <ArchivedThreads disabled={streaming} />}
    </div>
  </div>"""
text, count = conversation_pattern.subn(conversation_replacement, text, count=1)
if count != 1:
    raise SystemExit(f'conversationList replacement count={count}')

old_thread = """          <AssistantThread
            sourcesByMessageId={sourcesByMessageId}
            isRunning={streaming}
            progressEvents={progressEvents}
            traceMessageId={traceMessageId}
            progressOpen={progressOpen}
            onToggleProgress={() => setProgressOpen((current) => !current)}
            onSource={onSource}
          />"""
new_thread = """          <RunStatusBar
            events={progressEvents}
            running={streaming}
            open={progressOpen}
            onToggle={() => setProgressOpen((current) => !current)}
          />
          <div className='min-h-0 flex-1'>
            <Thread autoFocus={!selectedArchived} />
          </div>"""
if old_thread not in text:
    raise SystemExit('AssistantThread render block not found')
text = text.replace(old_thread, new_thread)

text = text.replace(
    "onClick={() => setSourcePanel((current) => !current)}",
    "onClick={() => { if (!showSource && !selectedSource && latestSources.length) setSelectedSource(latestSources[0]); setSourcePanel((current) => !current) }}",
    1,
)
text = text.replace(
    "{selectedSource && <Button size='icon-sm' variant='ghost' aria-label='查看来源证据' className='lg:hidden' onClick={() => setMobileSource(true)}><FileText className='size-4' /></Button>}",
    "{latestSources.length > 0 && <Button size='icon-sm' variant='ghost' aria-label='查看来源证据' className='lg:hidden' onClick={() => { if (!selectedSource) setSelectedSource(latestSources[0]); setMobileSource(true) }}><FileText className='size-4' /></Button>}",
)
text = text.replace(
    "<SourcePanel source={selectedSource} kbId={activeKb} />",
    "<SourcePanel source={selectedSource} sources={latestSources} onSource={setSelectedSource} kbId={activeKb} />",
)

custom_lists = re.compile(r"\nfunction ConversationList\(.*?\nfunction SourcePanel", re.S)
archived = r'''
function ArchivedThreads({ disabled }: { disabled: boolean }) {
  return <div className='mt-4 border-t pt-3'>
    <div className='px-2.5 pb-1 text-xs font-medium text-muted-foreground'>Archived</div>
    <ThreadListPrimitive.Root className='flex flex-col gap-0.5'>
      <ThreadListPrimitive.Items archived>
        {() => <ThreadListItemPrimitive.Root className='group relative flex h-8 items-center rounded-md hover:bg-muted data-active:bg-muted'>
          <ThreadListItemPrimitive.Trigger disabled={disabled} className='flex h-full min-w-0 flex-1 items-center rounded-md px-2.5 text-start text-sm outline-none disabled:opacity-50'>
            <span className='min-w-0 flex-1 truncate'><ThreadListItemPrimitive.Title fallback='Archived chat' /></span>
          </ThreadListItemPrimitive.Trigger>
          <ThreadListItemPrimitive.Unarchive disabled={disabled} aria-label='恢复会话' className='me-1 inline-flex size-6 items-center justify-center rounded-md text-muted-foreground opacity-0 hover:bg-accent hover:text-foreground group-hover:opacity-100 focus:opacity-100 disabled:hidden'>
            <ArchiveRestore className='size-3.5' />
          </ThreadListItemPrimitive.Unarchive>
          <ThreadListItemPrimitive.Delete disabled={disabled} aria-label='删除会话' className='me-1 inline-flex size-6 items-center justify-center rounded-md text-muted-foreground opacity-0 hover:bg-destructive/10 hover:text-destructive group-hover:opacity-100 focus:opacity-100 disabled:hidden'>
            <Trash2 className='size-3.5' />
          </ThreadListItemPrimitive.Delete>
        </ThreadListItemPrimitive.Root>}
      </ThreadListPrimitive.Items>
    </ThreadListPrimitive.Root>
  </div>
}

function RunStatusBar({ events, running, open, onToggle }: { events: RagProgressEvent[]; running: boolean; open: boolean; onToggle: () => void }) {
  if (!events.length) return null
  const queryCount = events.filter((event) => event.type === 'query.completed').length
  const retrieval = [...events].reverse().find((event) => event.stage === 'retrieval' && event.status === 'completed')
  const evidenceCount = typeof retrieval?.data?.evidenceCount === 'number' ? retrieval.data.evidenceCount : typeof retrieval?.data?.mergedEvidenceCount === 'number' ? retrieval.data.mergedEvidenceCount : undefined
  const review = [...events].reverse().find((event) => event.stage === 'review' && event.type === 'agent.completed')
  const workflow = [...events].reverse().find((event) => event.stepId === 'workflow')
  const summary = [queryCount ? `${queryCount} 个查询` : null, evidenceCount != null ? `${evidenceCount} 条证据` : null, review ? `Reviewer ${review.status === 'completed' ? '完成' : review.status}` : null].filter(Boolean).join(' · ')
  return <div className='shrink-0 border-b bg-background/85 px-3 py-2 backdrop-blur'>
    <button type='button' onClick={onToggle} className='mx-auto flex w-full max-w-3xl items-center gap-2 rounded-md px-2 py-1 text-left text-xs text-muted-foreground hover:bg-muted/60'>
      {running ? <LoaderCircle className='size-3.5 animate-spin text-primary' /> : <span className='size-2 rounded-full bg-emerald-500' />}
      <span className='font-medium text-foreground'>{running ? 'Agent workflow running' : workflow?.status === 'failed' ? 'Agent workflow failed' : 'Agent workflow complete'}</span>
      <span className='min-w-0 flex-1 truncate'>{summary}</span>
      <ChevronDown className={cn('size-3.5 transition-transform', open && 'rotate-180')} />
    </button>
    {open && <div className='mx-auto mt-1 max-h-40 w-full max-w-3xl overflow-y-auto rounded-md border bg-muted/20 px-3 py-2 text-[11px] text-muted-foreground'>
      {events.map((event, index) => <div key={`${event.stepId}-${event.type}-${index}`} className='flex gap-2 py-0.5'>
        <span className='w-20 shrink-0 font-medium text-foreground/75'>{event.stage}</span>
        <span className='min-w-0 flex-1 truncate'>{event.tool || event.type}</span>
        <span className='shrink-0'>{event.status}</span>
      </div>)}
    </div>}
  </div>
}

function SourcePanel'''
text, count = custom_lists.subn('\n' + archived, text, count=1)
if count != 1:
    raise SystemExit(f'custom list block replacement count={count}')

text = text.replace(
    "function SourcePanel({ source, kbId }: { source: ChatSource; kbId: string }) {",
    "function SourcePanel({ source, sources, onSource, kbId }: { source: ChatSource; sources: ChatSource[]; onSource: (source: ChatSource) => void; kbId: string }) {",
)
source_root = "  return <div className='space-y-4 p-4'>\n"
source_nav = """  return <div className='space-y-4 p-4'>
    {sources.length > 1 && <div className='flex flex-wrap gap-1.5'>{sources.map((item, index) => <button key={`${item.documentId}-${item.chunkIndex}-${index}`} type='button' onClick={() => onSource(item)} className={cn('rounded-md border px-2 py-1 text-[11px]', item.documentId === source.documentId && item.chunkIndex === source.chunkIndex ? 'border-primary/40 bg-primary/8 text-primary' : 'bg-background text-muted-foreground hover:bg-muted')}>[{index + 1}] {item.fileName || '来源'}</button>)}</div>}
"""
if source_root not in text:
    raise SystemExit('SourcePanel root not found')
text = text.replace(source_root, source_nav, 1)
page.write_text(text)

markdown = root / 'features/chat/chat-markdown.tsx'
markdown.write_text("""import ReactMarkdown from 'react-markdown'\nimport remarkGfm from 'remark-gfm'\n\nexport function SourceExcerptMarkdown({ text }: { text: string }) {\n  return (\n    <div className='source-markdown break-words text-xs leading-6 text-foreground/85'>\n      <ReactMarkdown\n        remarkPlugins={[remarkGfm]}\n        skipHtml\n        components={{\n          h1: ({ children }) => <h3 className='mb-2 mt-4 text-sm font-semibold first:mt-0'>{children}</h3>,\n          h2: ({ children }) => <h3 className='mb-2 mt-4 text-sm font-semibold first:mt-0'>{children}</h3>,\n          h3: ({ children }) => <h4 className='mb-1.5 mt-3 font-semibold first:mt-0'>{children}</h4>,\n          h4: ({ children }) => <h4 className='mb-1.5 mt-3 font-semibold first:mt-0'>{children}</h4>,\n          p: ({ children }) => <p className='my-2 first:mt-0 last:mb-0'>{children}</p>,\n          ul: ({ children }) => <ul className='my-2 list-disc space-y-1 pl-5 marker:text-muted-foreground'>{children}</ul>,\n          ol: ({ children, start }) => <ol start={start} className='my-2 list-decimal space-y-1 pl-5 marker:text-muted-foreground'>{children}</ol>,\n          li: ({ children }) => <li className='pl-0.5'>{children}</li>,\n          blockquote: ({ children }) => <blockquote className='my-2 border-l-2 border-primary/25 pl-3 text-foreground/75'>{children}</blockquote>,\n          pre: ({ children }) => <pre className='my-2 overflow-x-auto rounded-lg border bg-muted/55 p-3 text-[11px] leading-5'>{children}</pre>,\n          code: ({ children, className }) => className ? <code className={className}>{children}</code> : <code className='rounded border bg-muted/60 px-1 py-0.5 font-mono text-[0.92em]'>{children}</code>,\n          table: ({ children }) => <div className='my-2 overflow-x-auto'><table className='w-full border-collapse text-left text-[11px]'>{children}</table></div>,\n          thead: ({ children }) => <thead className='border-b bg-muted/40'>{children}</thead>,\n          th: ({ children }) => <th className='border px-2 py-1.5 font-semibold'>{children}</th>,\n          td: ({ children }) => <td className='border px-2 py-1.5 align-top'>{children}</td>,\n          a: ({ children }) => <span className='underline decoration-dotted underline-offset-2'>{children}</span>,\n          img: ({ alt }) => <span className='text-muted-foreground'>[图片{alt ? `: ${alt}` : ''}]</span>,\n        }}\n      >\n        {text}\n      </ReactMarkdown>\n    </div>\n  )\n}\n""")

test = root / 'features/chat/assistant-thread-markdown.test.tsx'
test.write_text("""import { render, screen } from '@testing-library/react'\nimport { describe, expect, it } from 'vitest'\nimport { SourceExcerptMarkdown } from './chat-markdown'\n\ndescribe('SourceExcerptMarkdown', () => {\n  it('renders evidence markdown structurally without active remote links, images, or raw html', () => {\n    const { container } = render(<SourceExcerptMarkdown text={'## 标题\\n\\n3. 第三项\\n4. 第四项\\n\\n```csharp\\nvar x = 1;\\n```\\n\\n[外链](https://example.com) ![远程图](https://example.com/a.png) <script>alert(1)</script>'} />)\n    expect(screen.getByRole('heading', { name: '标题' })).toBeInTheDocument()\n    expect(container.querySelector('ol')?.getAttribute('start')).toBe('3')\n    expect(container.querySelector('pre code')?.textContent).toContain('var x = 1;')\n    expect(container.querySelector('a')).toBeNull()\n    expect(container.querySelector('img')).toBeNull()\n    expect(container.querySelector('script')).toBeNull()\n  })\n})\n""")

thread = root / 'components/assistant-ui/elements/thread.aui.tsx'
thread_text = thread.read_text()
if 'elements/sources.aui' not in thread_text:
    thread_text = thread_text.replace(
        'import { MarkdownText } from "@/components/assistant-ui/elements/markdown-text";\n',
        'import { MarkdownText } from "@/components/assistant-ui/elements/markdown-text";\nimport { Sources } from "@/components/assistant-ui/elements/sources.aui";\n',
    )
if 'case "source":' not in thread_text:
    thread_text = thread_text.replace(
        '              case "text":\n                return <MarkdownText />;\n',
        '              case "text":\n                return <MarkdownText />;\n              case "source":\n                return <Sources {...part} />;\n',
    )
thread.write_text(thread_text)

old_thread_file = root / 'features/chat/assistant-thread.tsx'
if old_thread_file.exists():
    old_thread_file.unlink()

changelog = Path('CHANGELOG.md')
changelog_text = changelog.read_text()
needle = '### Changed\n\n'
entry = '- Chat 主界面改为 assistant-ui 官方 registry Elements：直接使用官方 `Thread` / `ThreadList` / `MarkdownText` / `Sources`，移除 KnowledgeRag 自建消息气泡、Composer、ActionBar、Welcome、消息 Markdown 覆盖和来源 chips；TanStack Query + SSE 仅作为 External Store 数据适配层，Agent Timeline 与深度 Evidence Panel 移到 Thread 外部，避免继续改写官方消息布局。\n'
if entry not in changelog_text:
    changelog_text = changelog_text.replace(needle, needle + entry, 1)
changelog.write_text(changelog_text)
