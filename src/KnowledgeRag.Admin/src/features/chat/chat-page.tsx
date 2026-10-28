import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties, type PointerEvent as ReactPointerEvent, type ReactNode } from 'react'
import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  AssistantRuntimeProvider,
  ThreadListItemPrimitive,
  ThreadListPrimitive,
  useExternalStoreRuntime,
} from '@assistant-ui/react'
import { ArchiveRestore, Bot, ChevronDown, FileText, LoaderCircle, Maximize2, Menu, Minimize2, PanelLeftClose, PanelLeftOpen, PanelRightClose, PanelRightOpen, Trash2, X } from 'lucide-react'
import { motion } from 'motion/react'
import { toast } from 'sonner'
import { Thread } from '@/components/assistant-ui/elements/thread.aui'
import { ThreadList } from '@/components/assistant-ui/elements/thread-list.aui'
import { SourceExcerptMarkdown } from './chat-markdown'
import { flattenMessagePages, resolveRuntimeThreadId } from './chat-runtime-state'
import { mergeProgressEvent } from './progress-events'
import { api, getErrorMessage } from '@/lib/api'
import { cn } from '@/lib/utils'
import type { ChatMessage, ChatSource, RagProgressEvent } from '@/lib/types'
import { Button, EmptyState, PageHeader, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui'

function parseSources(message: ChatMessage): ChatSource[] {
  if (!message.sourcesJson) return []
  try {
    const parsed = JSON.parse(message.sourcesJson)
    return Array.isArray(parsed) ? parsed as ChatSource[] : []
  } catch {
    return []
  }
}

function formatScore(value?: number | null) {
  return typeof value === 'number' && Number.isFinite(value) ? value.toFixed(4) : '—'
}

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value))
}

export function ChatPage() {
  const qc = useQueryClient()
  const kbs = useQuery({ queryKey: ['knowledge-bases'], queryFn: api.knowledgeBases.list })
  const initialKb = new URLSearchParams(window.location.search).get('knowledgeBaseId') || ''
  const [kbId, setKbId] = useState(initialKb)
  const activeKb = kbId || kbs.data?.[0]?.id || ''
  const conversations = useQuery({ queryKey: ['conversations', activeKb, false], queryFn: () => api.conversations.list(activeKb, false), enabled: !!activeKb })
  const archivedConversations = useQuery({ queryKey: ['conversations', activeKb, true], queryFn: () => api.conversations.list(activeKb, true), enabled: !!activeKb })
  const [conversationId, setConversationId] = useState('')
  const runtimeThreadId = resolveRuntimeThreadId(conversationId, conversations.data, archivedConversations.data)
  const selectedConversation = conversations.data?.find((item) => item.id === runtimeThreadId)
    || archivedConversations.data?.find((item) => item.id === runtimeThreadId)
  const selectedArchived = !!runtimeThreadId && !!archivedConversations.data?.some((item) => item.id === runtimeThreadId)

  useEffect(() => {
    if (activeKb && !conversationId && conversations.data?.length) setConversationId(conversations.data[0].id)
  }, [activeKb, conversationId, conversations.data])
  useEffect(() => setConversationId(''), [activeKb])

  const messages = useInfiniteQuery({
    queryKey: ['messages', runtimeThreadId || 'unselected'],
    queryFn: ({ pageParam }) => {
      if (!runtimeThreadId) throw new Error('Conversation is not available in the current thread list.')
      return api.conversations.messagePage(runtimeThreadId, pageParam || undefined, 50)
    },
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.hasEarlier && lastPage.previousCursor ? lastPage.previousCursor : undefined,
    enabled: !!runtimeThreadId,
  })

  const [selectedSource, setSelectedSource] = useState<ChatSource | null>(null)
  const [sourcePanel, setSourcePanel] = useState(false)
  const [conversationsOpen, setConversationsOpen] = useState(() => localStorage.getItem('chat-conversations-open') !== 'false')
  const [focusMode, setFocusMode] = useState(false)
  const [sourceWidth, setSourceWidth] = useState(() => clamp(Number(localStorage.getItem('chat-source-width')) || 380, 320, 520))
  const [mobileConversations, setMobileConversations] = useState(false)
  const [mobileSource, setMobileSource] = useState(false)
  const [liveUser, setLiveUser] = useState('')
  const [liveAnswer, setLiveAnswer] = useState('')
  const [streaming, setStreaming] = useState(false)
  const [progressEvents, setProgressEvents] = useState<RagProgressEvent[]>([])
  const [progressOpen, setProgressOpen] = useState(true)
  const controllerRef = useRef<AbortController | null>(null)

  useEffect(() => {
    localStorage.setItem('chat-conversations-open', String(conversationsOpen))
  }, [conversationsOpen])

  useEffect(() => {
    localStorage.setItem('chat-source-width', String(sourceWidth))
  }, [sourceWidth])

  const invalidateConversationLists = useCallback(async () => {
    await Promise.all([
      qc.invalidateQueries({ queryKey: ['conversations', activeKb, false] }),
      qc.invalidateQueries({ queryKey: ['conversations', activeKb, true] }),
    ])
  }, [activeKb, qc])

  const runStream = useCallback(async (prompt: string) => {
    if (!prompt.trim() || streaming || !activeKb) return
    let id = conversationId
    if (!id) {
      const conversation = await api.conversations.create(activeKb)
      id = conversation.id
      setConversationId(id)
      await invalidateConversationLists()
    }

    const controller = new AbortController()
    controllerRef.current = controller
    setStreaming(true)
    setLiveUser(prompt)
    setLiveAnswer('')
    setProgressEvents([])
    setProgressOpen(true)

    try {
      await api.conversations.stream(
        id,
        prompt,
        (delta) => setLiveAnswer((current) => current + delta),
        controller.signal,
        (event) => setProgressEvents((current) => mergeProgressEvent(current, event)),
      )
      await Promise.all([
        qc.invalidateQueries({ queryKey: ['messages', id] }),
        invalidateConversationLists(),
      ])
      setLiveUser('')
      setLiveAnswer('')
      setProgressOpen(false)
    } catch (error) {
      setLiveUser('')
      setLiveAnswer('')
      setProgressOpen(false)
      if ((error as Error).name === 'AbortError') toast.info('已停止生成；未完成的临时消息已清理')
      else toast.error(getErrorMessage(error))
    } finally {
      setStreaming(false)
      controllerRef.current = null
    }
  }, [activeKb, conversationId, invalidateConversationLists, qc, streaming])

  const loadedMessages = useMemo<ChatMessage[]>(
    () => flattenMessagePages(messages.data?.pages),
    [messages.data],
  )

  const threadMessages = useMemo<ChatMessage[]>(() => {
    const history = [...loadedMessages]
    const now = new Date().toISOString()
    if (liveUser) history.push({ id: 'live-user', role: 'user', content: liveUser, createdAt: now })
    if (streaming || liveAnswer) history.push({ id: 'live-assistant', role: 'assistant', content: liveAnswer, createdAt: now })
    return history
  }, [loadedMessages, liveUser, liveAnswer, streaming])

  const convertMessage = useCallback((message: ChatMessage) => {
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
  }, [])

  const runtime = useExternalStoreRuntime({
    messages: threadMessages,
    isRunning: streaming,
    isLoading: !!runtimeThreadId && messages.isLoading,
    isSendDisabled: streaming || !activeKb || selectedArchived || (!!conversationId && !runtimeThreadId),
    suggestions: [
      { prompt: '总结这个知识库' },
      { prompt: '有哪些关键主题？' },
      { prompt: '帮我比较相关文档' },
    ],
    convertMessage,
    onNew: async (message) => {
      const textPart = message.content.find((part) => part.type === 'text')
      if (!textPart || textPart.type !== 'text' || !textPart.text.trim()) return
      await runStream(textPart.text)
    },
    onCancel: async () => controllerRef.current?.abort(),
    hasEarlier: !!messages.hasNextPage,
    onLoadEarlier: async () => {
      if (messages.hasNextPage) await messages.fetchNextPage()
    },
    adapters: {
      feedback: {
        submit: ({ message, type }) => {
          if (!runtimeThreadId || message.id.startsWith('live-')) return
          void api.conversations.feedback(runtimeThreadId, message.id, type).catch((error) => toast.error(getErrorMessage(error)))
        },
      },
      threadList: {
        threadId: runtimeThreadId,
        isLoading: conversations.isLoading || archivedConversations.isLoading,
        threads: (conversations.data || []).map((item) => ({ id: item.id, status: 'regular' as const, title: item.title, lastMessageAt: new Date(item.updatedAt) })),
        archivedThreads: (archivedConversations.data || []).map((item) => ({ id: item.id, status: 'archived' as const, title: item.title, lastMessageAt: new Date(item.updatedAt) })),
        onSwitchToNewThread: async () => {
          if (streaming || !activeKb) return
          const conversation = await api.conversations.create(activeKb)
          setConversationId(conversation.id)
          setSelectedSource(null)
          setSourcePanel(false)
          setProgressEvents([])
          await invalidateConversationLists()
        },
        onSwitchToThread: (id) => {
          if (streaming) return
          setConversationId(id)
          setSelectedSource(null)
          setSourcePanel(false)
          setProgressEvents([])
          setMobileConversations(false)
        },
        onRename: async (id, title) => {
          if (streaming) return
          await api.conversations.rename(id, title)
          await invalidateConversationLists()
        },
        onArchive: async (id) => {
          if (streaming) return
          await api.conversations.archive(id)
          if (conversationId === id) setConversationId('')
          await invalidateConversationLists()
        },
        onUnarchive: async (id) => {
          if (streaming) return
          await api.conversations.unarchive(id)
          await invalidateConversationLists()
        },
        onDelete: async (id) => {
          if (streaming || !confirm('删除当前会话？')) return
          await api.conversations.remove(id)
          if (conversationId === id) setConversationId('')
          await invalidateConversationLists()
        },
      },
    },
  })

  if (!kbs.isLoading && !kbs.data?.length) {
    return <div className='space-y-6'><PageHeader eyebrow='RAG assistant' title='RAG 对话' description='先创建知识库并上传文档，再开始有来源依据的问答。' /><EmptyState icon={<Bot className='size-5 text-primary' />} title='没有可用知识库' description='创建知识库并完成向量化后，就可以开始对话。' /></div>
  }


  const showConversations = conversationsOpen && !focusMode
  const showSource = sourcePanel && !focusMode

  const beginSourceResize = (event: ReactPointerEvent<HTMLDivElement>) => {
    event.preventDefault()
    const startX = event.clientX
    const startWidth = sourceWidth
    const onMove = (moveEvent: PointerEvent) => setSourceWidth(clamp(startWidth + startX - moveEvent.clientX, 320, 520))
    const onUp = () => {
      window.removeEventListener('pointermove', onMove)
      window.removeEventListener('pointerup', onUp)
    }
    window.addEventListener('pointermove', onMove)
    window.addEventListener('pointerup', onUp)
  }

  const latestAssistantMessage = [...loadedMessages].reverse().find((message) => message.role.toLowerCase() === 'assistant')
  const latestSources = latestAssistantMessage ? parseSources(latestAssistantMessage) : []

  const conversationList = <div className='flex h-full min-h-0 flex-col'>
    <div className='min-h-0 flex-1 overflow-y-auto p-2'>
      <ThreadList />
      {(archivedConversations.data?.length || 0) > 0 && <ArchivedThreads disabled={streaming} />}
    </div>
  </div>

  return <AssistantRuntimeProvider runtime={runtime}>
    <div className='flex h-full min-h-0 flex-col gap-2'>
      <div className='flex min-h-12 shrink-0 items-center gap-2 rounded-2xl border bg-background/78 px-2.5 py-2 shadow-sm backdrop-blur-xl sm:px-3'>
        <div className='flex min-w-0 flex-1 items-center gap-1.5'>
          <Button size='icon-sm' variant='ghost' className='lg:hidden' aria-label='打开会话列表' onClick={() => setMobileConversations(true)}><Menu className='size-4' /></Button>
          <Button size='icon-sm' variant='ghost' className='hidden lg:inline-flex' aria-label={showConversations ? '隐藏会话列表' : '显示会话列表'} onClick={() => setConversationsOpen((current) => !current)}>
            {showConversations ? <PanelLeftClose className='size-4' /> : <PanelLeftOpen className='size-4' />}
          </Button>
          <div className='min-w-0'>
            <div className='flex items-center gap-2'>
              <span className='truncate text-sm font-semibold'>与知识对话</span>
              {focusMode && <span className='hidden rounded-full border bg-primary/5 px-2 py-0.5 text-[9px] font-medium text-primary sm:inline'>Focus</span>}
              {selectedArchived && <span className='hidden rounded-full border bg-muted px-2 py-0.5 text-[9px] font-medium text-muted-foreground sm:inline'>Archived · 只读</span>}
              {streaming && <span className='hidden text-[10px] text-primary sm:inline'>● Agent Workflow 执行中</span>}
            </div>
            <div className='hidden max-w-[38rem] truncate text-[10px] text-muted-foreground sm:block'>{selectedConversation?.title || '新会话'} · Planner → Hybrid Search → Answer → Evidence Review → Citation Gate</div>
          </div>
        </div>

        <div className='flex shrink-0 items-center gap-1'>
          <div className='w-32 sm:w-44 lg:w-52'><Select value={activeKb} onValueChange={setKbId}><SelectTrigger className='h-8'><SelectValue placeholder='选择知识库' /></SelectTrigger><SelectContent>{kbs.data?.map((kb) => <SelectItem value={kb.id} key={kb.id}>{kb.name}</SelectItem>)}</SelectContent></Select></div>
          <Button size='icon-sm' variant={showSource ? 'secondary' : 'ghost'} className='hidden lg:inline-flex' aria-label={showSource ? '关闭来源证据' : '打开来源证据'} onClick={() => { if (!showSource && !selectedSource && latestSources.length) setSelectedSource(latestSources[0]); setSourcePanel((current) => !current) }}>
            {showSource ? <PanelRightClose className='size-4' /> : <PanelRightOpen className='size-4' />}
          </Button>
          {latestSources.length > 0 && <Button size='icon-sm' variant='ghost' aria-label='查看来源证据' className='lg:hidden' onClick={() => { if (!selectedSource) setSelectedSource(latestSources[0]); setMobileSource(true) }}><FileText className='size-4' /></Button>}
          <Button size='icon-sm' variant={focusMode ? 'secondary' : 'ghost'} aria-label={focusMode ? '退出专注模式' : '进入专注模式'} onClick={() => setFocusMode((current) => !current)}>
            {focusMode ? <Minimize2 className='size-4' /> : <Maximize2 className='size-4' />}
          </Button>
        </div>
      </div>

      <div
        className='chat-workspace grid min-h-0 flex-1 overflow-hidden rounded-2xl border bg-card/70 shadow-sm'
        data-conversations={showConversations ? 'true' : 'false'}
        data-source={showSource ? 'true' : 'false'}
        data-focus={focusMode ? 'true' : 'false'}
        style={{ '--source-width': `${sourceWidth}px` } as CSSProperties}
      >
        {showConversations && <aside data-testid='conversation-panel' className='hidden border-r bg-muted/12 lg:block'>{conversationList}</aside>}

        <section className='flex min-w-0 flex-col overflow-hidden bg-background/45'>
          <RunStatusBar
            events={progressEvents}
            running={streaming}
            open={progressOpen}
            onToggle={() => setProgressOpen((current) => !current)}
          />
          <div className='min-h-0 flex-1'>
            <Thread autoFocus={!selectedArchived} />
          </div>
        </section>

        {showSource && <aside data-testid='source-panel' className='source-panel relative hidden border-l bg-muted/8 lg:flex'>
          <div role='separator' aria-label='调整来源面板宽度' className='absolute inset-y-0 -left-1 z-10 w-2 cursor-col-resize touch-none hover:bg-primary/10' onPointerDown={beginSourceResize} />
          <div className='flex h-12 shrink-0 items-center justify-between border-b bg-background/70 px-4'>
            <div><div className='text-xs font-semibold'>来源证据</div><div className='max-w-64 truncate text-[10px] text-muted-foreground'>{selectedSource ? selectedSource.fileName : '选择一条引用查看证据'}</div></div>
            <Button size='icon-sm' variant='ghost' aria-label='关闭来源证据' onClick={() => setSourcePanel(false)}><X className='size-3.5' /></Button>
          </div>
          <div className='source-panel-body min-h-0 flex-1 overflow-y-auto overscroll-contain'>{selectedSource ? <SourcePanel source={selectedSource} sources={latestSources} onSource={setSelectedSource} kbId={activeKb} /> : <SourceEmpty />}</div>
        </aside>}
      </div>

      {mobileConversations && <MobileDrawer title='Conversations' onClose={() => setMobileConversations(false)}>{conversationList}</MobileDrawer>}
      {mobileSource && selectedSource && <MobileDrawer title='来源证据' side='right' onClose={() => setMobileSource(false)}><SourcePanel source={selectedSource} sources={latestSources} onSource={setSelectedSource} kbId={activeKb} /></MobileDrawer>}
    </div>
  </AssistantRuntimeProvider>
}


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
        <span className='min-w-0 flex-1 truncate'>{event.label || event.type}</span>
        <span className='shrink-0'>{event.status}</span>
      </div>)}
    </div>}
  </div>
}

function SourcePanel({ source, sources, onSource, kbId }: { source: ChatSource; sources: ChatSource[]; onSource: (source: ChatSource) => void; kbId: string }) {
  const open = async () => {
    try {
      const blob = await api.documents.download(kbId, source.documentId)
      const url = URL.createObjectURL(blob)
      window.open(url, '_blank', 'noopener,noreferrer')
      setTimeout(() => URL.revokeObjectURL(url), 30000)
    } catch (error) {
      toast.error(getErrorMessage(error))
    }
  }
  const metrics = [
    ['Vector', source.vectorScore],
    ['BM25', source.bm25Score],
    ['RRF', source.fusionScore],
    ['Rerank', source.rerankScore],
  ] as const

  return <div className='space-y-4 p-4'>
    {sources.length > 1 && <div className='flex flex-wrap gap-1.5'>{sources.map((item, index) => <button key={`${item.documentId}-${item.chunkIndex}-${index}`} type='button' onClick={() => onSource(item)} className={cn('rounded-md border px-2 py-1 text-[11px]', item.documentId === source.documentId && item.chunkIndex === source.chunkIndex ? 'border-primary/40 bg-primary/8 text-primary' : 'bg-background text-muted-foreground hover:bg-muted')}>[{index + 1}] {item.fileName || '来源'}</button>)}</div>}
    <div className='rounded-xl border bg-background/70 p-3.5 shadow-sm'><div className='truncate text-sm font-semibold'>{source.fileName || '未命名文档'}</div><div className='mt-1 text-[11px] text-muted-foreground'>chunk #{source.chunkIndex ?? '—'}{source.pageNumber != null ? ` · page ${source.pageNumber}` : ''}</div></div>
    <div><div className='mb-2 text-[10px] font-medium uppercase tracking-wide text-muted-foreground'>证据摘录</div><div className='rounded-xl border bg-background/65 p-3.5'><SourceExcerptMarkdown text={source.excerpt || '历史消息未保存 excerpt；重新提问后会显示完整证据摘录。'} /></div></div>
    <div><div className='mb-2 text-[10px] font-medium uppercase tracking-wide text-muted-foreground'>检索分数</div><div className='grid grid-cols-2 gap-2'>{metrics.map(([label, value]) => <div key={label} className='rounded-xl border bg-background/60 p-3'><div className='text-[9px] uppercase text-muted-foreground'>{label}</div><div className='mt-1 text-sm font-semibold tabular-nums'>{formatScore(value)}</div></div>)}</div></div>
    <div className='rounded-xl border border-primary/15 bg-primary/5 p-3.5'><div className='text-[10px] uppercase tracking-wide text-muted-foreground'>Final score</div><div className='mt-1 text-xl font-semibold tabular-nums'>{formatScore(source.score)}</div></div>
    <Button variant='outline' className='w-full' onClick={() => void open()}>打开原始文档</Button>
  </div>
}

function SourceEmpty() {
  return <div className='grid min-h-full place-items-center p-6 text-center'><div><div className='mx-auto mb-3 flex size-10 items-center justify-center rounded-xl border bg-background'><FileText className='size-4 text-muted-foreground' /></div><div className='text-xs font-medium'>尚未选择引用</div><div className='mt-1 max-w-52 text-[11px] leading-5 text-muted-foreground'>点击回答中的引用或回答下方的来源，来源面板会在需要时打开。</div></div></div>
}

function MobileDrawer({ title, side = 'left', onClose, children }: { title: string; side?: 'left' | 'right'; onClose: () => void; children: ReactNode }) {
  return <div className='fixed inset-0 z-50 lg:hidden'><button aria-label='close' className='absolute inset-0 bg-slate-950/45 backdrop-blur-sm' onClick={onClose} /><motion.aside initial={{ x: side === 'left' ? -360 : 360 }} animate={{ x: 0 }} exit={{ x: side === 'left' ? -360 : 360 }} className={cn('absolute inset-y-0 w-[88vw] max-w-sm bg-background shadow-2xl', side === 'left' ? 'left-0' : 'right-0')}><div className='flex h-14 items-center justify-between border-b px-4 text-sm font-semibold'>{title}<Button variant='ghost' size='icon-sm' onClick={onClose}><X className='size-4' /></Button></div><div className='h-[calc(100%-3.5rem)] overflow-y-auto'>{children}</div></motion.aside></div>
}
