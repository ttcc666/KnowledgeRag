import { useEffect, useMemo, useRef, useState, type CSSProperties, type PointerEvent as ReactPointerEvent, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Bot,
  ChevronRight,
  Copy,
  FileText,
  Maximize2,
  Menu,
  MessageSquarePlus,
  Minimize2,
  PanelLeftClose,
  PanelLeftOpen,
  PanelRightClose,
  PanelRightOpen,
  RotateCcw,
  Send,
  Square,
  Trash2,
  User,
  X,
} from 'lucide-react'
import { motion } from 'motion/react'
import { toast } from 'sonner'
import { api, getErrorMessage } from '@/lib/api'
import { cn, formatDate } from '@/lib/utils'
import type { ChatMessage, ChatSource } from '@/lib/types'
import { Button, EmptyState, PageHeader, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Textarea } from '@/components/ui'

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
  const conversations = useQuery({ queryKey: ['conversations', activeKb], queryFn: () => api.conversations.list(activeKb), enabled: !!activeKb })
  const [conversationId, setConversationId] = useState('')

  useEffect(() => {
    if (activeKb && !conversationId && conversations.data?.length) setConversationId(conversations.data[0].id)
  }, [activeKb, conversationId, conversations.data])
  useEffect(() => setConversationId(''), [activeKb])

  const messages = useQuery({ queryKey: ['messages', conversationId], queryFn: () => api.conversations.messages(conversationId), enabled: !!conversationId })
  const [text, setText] = useState('')
  const messageViewportRef = useRef<HTMLDivElement>(null)
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
  const controllerRef = useRef<AbortController | null>(null)

  useEffect(() => {
    localStorage.setItem('chat-conversations-open', String(conversationsOpen))
  }, [conversationsOpen])

  useEffect(() => {
    localStorage.setItem('chat-source-width', String(sourceWidth))
  }, [sourceWidth])

  useEffect(() => {
    const viewport = messageViewportRef.current
    if (!viewport) return
    viewport.scrollTo({ top: viewport.scrollHeight, behavior: 'smooth' })
  }, [messages.data, liveAnswer])

  const create = useMutation({
    mutationFn: () => api.conversations.create(activeKb),
    onSuccess: async (conversation) => {
      setConversationId(conversation.id)
      await qc.invalidateQueries({ queryKey: ['conversations', activeKb] })
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })
  const remove = useMutation({
    mutationFn: (id: string) => api.conversations.remove(id),
    onSuccess: async () => {
      setConversationId('')
      setSelectedSource(null)
      setSourcePanel(false)
      await qc.invalidateQueries({ queryKey: ['conversations', activeKb] })
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })
  const selectedConversation = useMemo(() => conversations.data?.find((item) => item.id === conversationId), [conversations.data, conversationId])

  const runStream = async (prompt: string) => {
    if (!prompt.trim() || streaming || !activeKb) return
    let id = conversationId
    if (!id) {
      const conversation = await api.conversations.create(activeKb)
      id = conversation.id
      setConversationId(id)
    }
    const controller = new AbortController()
    controllerRef.current = controller
    setStreaming(true)
    setLiveUser(prompt)
    setLiveAnswer('')
    setText('')
    try {
      await api.conversations.stream(id, prompt, (delta) => setLiveAnswer((current) => current + delta), controller.signal)
      await Promise.all([
        qc.invalidateQueries({ queryKey: ['messages', id] }),
        qc.invalidateQueries({ queryKey: ['conversations', activeKb] }),
      ])
      setLiveUser('')
      setLiveAnswer('')
    } catch (error) {
      if ((error as Error).name === 'AbortError') toast.info('已停止生成')
      else toast.error(getErrorMessage(error))
    } finally {
      setStreaming(false)
      controllerRef.current = null
    }
  }

  const regenerate = () => {
    const history = messages.data || []
    const lastUser = [...history].reverse().find((item) => item.role.toLowerCase() === 'user')
    if (lastUser) void runStream(lastUser.content)
  }

  if (!kbs.isLoading && !kbs.data?.length) {
    return <div className='space-y-6'><PageHeader eyebrow='RAG assistant' title='RAG 对话' description='先创建知识库并上传文档，再开始有来源依据的问答。' /><EmptyState icon={<Bot className='size-5 text-primary' />} title='没有可用知识库' description='创建知识库并完成向量化后，就可以开始对话。' /></div>
  }

  const conversationList = <ConversationList conversations={conversations.data || []} current={conversationId} onNew={() => create.mutate()} onSelect={(id) => {
    setConversationId(id)
    setSelectedSource(null)
    setSourcePanel(false)
    setMobileConversations(false)
  }} />

  const onSource = (source: ChatSource) => {
    setSelectedSource(source)
    if (window.innerWidth < 1024) setMobileSource(true)
    else setSourcePanel(true)
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

  return <div className='flex h-full min-h-0 flex-col gap-2'>
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
            {streaming && <span className='hidden text-[10px] text-primary sm:inline'>● 正在生成</span>}
          </div>
          <div className='hidden max-w-[38rem] truncate text-[10px] text-muted-foreground sm:block'>{selectedConversation?.title || '新会话'} · Hybrid Search → RRF → Reranker → Agent</div>
        </div>
      </div>

      <div className='flex shrink-0 items-center gap-1'>
        <div className='w-32 sm:w-44 lg:w-52'><Select value={activeKb} onValueChange={setKbId}><SelectTrigger className='h-8'><SelectValue placeholder='选择知识库' /></SelectTrigger><SelectContent>{kbs.data?.map((kb) => <SelectItem value={kb.id} key={kb.id}>{kb.name}</SelectItem>)}</SelectContent></Select></div>
        {conversationId && <Button size='icon-sm' variant='ghost' aria-label='删除当前会话' onClick={() => { if (confirm('删除当前会话？')) remove.mutate(conversationId) }}><Trash2 className='size-3.5 text-destructive' /></Button>}
        <Button size='icon-sm' variant={showSource ? 'secondary' : 'ghost'} className='hidden lg:inline-flex' aria-label={showSource ? '关闭来源证据' : '打开来源证据'} onClick={() => setSourcePanel((current) => !current)}>
          {showSource ? <PanelRightClose className='size-4' /> : <PanelRightOpen className='size-4' />}
        </Button>
        {selectedSource && <Button size='icon-sm' variant='ghost' aria-label='查看来源证据' className='lg:hidden' onClick={() => setMobileSource(true)}><FileText className='size-4' /></Button>}
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

      <section className='flex min-w-0 flex-col bg-background/45'>
        <div ref={messageViewportRef} data-testid='message-viewport' className='min-h-0 flex-1 overflow-y-auto overscroll-contain bg-gradient-to-b from-muted/10 via-transparent to-transparent px-4 py-5 sm:px-6 lg:px-8'>
          <div className='mx-auto w-full max-w-6xl space-y-6'>
            {messages.data?.map((message, index) => <Message key={message.id} message={message} index={index} onSource={onSource} />)}
            {liveUser && <Message message={{ id: 'live-user', role: 'user', content: liveUser, createdAt: new Date().toISOString() }} index={999} onSource={() => {}} />}
            {streaming && <LiveAssistant text={liveAnswer} />}
            {!messages.isLoading && !messages.data?.length && !streaming && <div className='grid min-h-[360px] place-items-center'><div className='max-w-lg text-center'><div className='mx-auto mb-4 flex size-12 items-center justify-center rounded-2xl border bg-primary/8 text-primary shadow-sm'><Bot className='size-5' /></div><div className='text-base font-semibold'>从知识库开始探索</div><div className='mt-2 text-sm leading-6 text-muted-foreground'>回答会实时生成，并保留原文摘录、页码与检索分数。点击回答下方的引用即可按需打开证据。</div><div className='mt-4 flex flex-wrap justify-center gap-2'>{['总结这个知识库', '有哪些关键主题？', '帮我找相关文档'].map((item) => <button key={item} onClick={() => setText(item)} className='rounded-full border bg-background px-3.5 py-1.5 text-xs text-muted-foreground shadow-sm transition hover:border-primary/30 hover:text-foreground'>{item}</button>)}</div></div></div>}
          </div>
        </div>

        <div className='shrink-0 border-t bg-background/86 p-2.5 backdrop-blur sm:p-3'>
          <div className='mx-auto max-w-4xl rounded-2xl border bg-background p-1.5 shadow-sm transition focus-within:border-primary/35 focus-within:shadow-md'>
            <Textarea className='min-h-12 max-h-36 resize-none border-0 bg-transparent px-3 py-2 shadow-none focus-visible:ring-0' value={text} onChange={(event) => setText(event.target.value)} placeholder='询问当前知识库…' disabled={streaming} onKeyDown={(event) => { if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); void runStream(text) } }} />
            <div className='flex items-center justify-between gap-3 px-1 pb-1'>
              <div className='flex min-w-0 items-center gap-1.5'><span className='hidden text-[10px] text-muted-foreground sm:inline'>Enter 发送 · Shift+Enter 换行</span>{conversationId && !streaming && <Button size='sm' variant='ghost' className='h-7 px-2 text-xs' onClick={regenerate}><RotateCcw className='size-3' />重新生成</Button>}</div>
              {streaming ? <Button size='sm' variant='destructive' className='h-8' onClick={() => controllerRef.current?.abort()}><Square className='size-3.5' />停止</Button> : <Button size='icon' aria-label='发送消息' className='size-8 rounded-xl' disabled={!activeKb || !text.trim()} onClick={() => void runStream(text)}><Send className='size-3.5' /></Button>}
            </div>
          </div>
        </div>
      </section>

      {showSource && <aside data-testid='source-panel' className='source-panel relative hidden border-l bg-muted/8 lg:flex'>
        <div role='separator' aria-label='调整来源面板宽度' className='absolute inset-y-0 -left-1 z-10 w-2 cursor-col-resize touch-none hover:bg-primary/10' onPointerDown={beginSourceResize} />
        <div className='flex h-12 shrink-0 items-center justify-between border-b bg-background/70 px-4'>
          <div><div className='text-xs font-semibold'>来源证据</div><div className='max-w-64 truncate text-[10px] text-muted-foreground'>{selectedSource ? selectedSource.fileName : '选择一条引用查看证据'}</div></div>
          <Button size='icon-sm' variant='ghost' aria-label='关闭来源证据' onClick={() => setSourcePanel(false)}><X className='size-3.5' /></Button>
        </div>
        <div className='source-panel-body min-h-0 flex-1 overflow-y-auto overscroll-contain'>{selectedSource ? <SourcePanel source={selectedSource} kbId={activeKb} /> : <SourceEmpty />}</div>
      </aside>}
    </div>

    {mobileConversations && <MobileDrawer title='Conversations' onClose={() => setMobileConversations(false)}>{conversationList}</MobileDrawer>}
    {mobileSource && selectedSource && <MobileDrawer title='来源证据' side='right' onClose={() => setMobileSource(false)}><SourcePanel source={selectedSource} kbId={activeKb} /></MobileDrawer>}
  </div>
}

function ConversationList({ conversations, current, onNew, onSelect }: { conversations: { id: string; title: string; updatedAt: string }[]; current: string; onNew: () => void; onSelect: (id: string) => void }) {
  return <div className='flex h-full min-h-0 flex-col'>
    <div className='flex h-12 shrink-0 items-center justify-between border-b px-3'><div><div className='text-xs font-semibold'>Conversations</div><div className='text-[10px] text-muted-foreground'>{conversations.length} sessions</div></div><Button size='icon-sm' variant='ghost' aria-label='新建会话' onClick={onNew}><MessageSquarePlus className='size-4' /></Button></div>
    <div className='min-h-0 flex-1 overflow-y-auto p-2'>{conversations.map((conversation) => <button key={conversation.id} onClick={() => onSelect(conversation.id)} className={cn('mb-1 w-full rounded-xl px-3 py-2.5 text-left transition', current === conversation.id ? 'bg-accent font-medium shadow-sm' : 'hover:bg-accent/55')}><div className='truncate text-xs'>{conversation.title}</div><div className='mt-1 text-[10px] text-muted-foreground'>{formatDate(conversation.updatedAt)}</div></button>)}</div>
  </div>
}

function LiveAssistant({ text }: { text: string }) {
  return <div className='flex items-start gap-3'><AssistantAvatar /><div className='min-w-0 max-w-[94%] flex-1 xl:max-w-[960px]'><div className='rounded-2xl rounded-tl-md border bg-background px-5 py-4 text-sm leading-7 shadow-sm'>{text ? <MessageBody content={text} /> : <span className='animate-pulse text-muted-foreground'>正在思考…</span>}<span className='ml-1 inline-block h-4 w-0.5 animate-pulse bg-primary align-middle' /></div></div></div>
}

function Message({ message, index, onSource }: { message: ChatMessage; index: number; onSource: (source: ChatSource) => void }) {
  const isUser = message.role.toLowerCase() === 'user'
  const sources = parseSources(message)
  return <motion.div initial={{ opacity: 0, y: 6 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: Math.min(index * .025, .15) }} className={cn('flex items-start gap-3', isUser ? 'justify-end' : 'justify-start')}>
    {!isUser && <AssistantAvatar />}
    <div className={cn('group min-w-0', isUser ? 'max-w-[82%] sm:max-w-[68%]' : 'max-w-[94%] flex-1 xl:max-w-[960px]')}>
      <div className={cn('rounded-2xl px-4 py-3 text-sm leading-7', isUser ? 'rounded-tr-md bg-primary text-primary-foreground shadow-sm' : 'rounded-tl-md border bg-background px-5 py-4 shadow-sm')}>
        {isUser ? <div className='whitespace-pre-wrap break-words'>{message.content}</div> : <MessageBody content={message.content} />}
      </div>
      {sources.length > 0 && <div className='mt-2.5 flex flex-wrap items-center gap-1.5'><span className='mr-1 text-[10px] font-medium uppercase tracking-wide text-muted-foreground'>引用</span>{sources.map((source, sourceIndex) => <button key={`${source.documentId}-${source.chunkIndex}-${sourceIndex}`} aria-label={`查看引用 ${sourceIndex + 1}: ${source.fileName || '未命名文档'}`} onClick={() => onSource(source)} className='inline-flex max-w-72 items-center gap-1.5 rounded-lg border bg-background/90 px-2.5 py-1.5 text-[11px] text-muted-foreground shadow-sm transition hover:border-primary/30 hover:bg-primary/5 hover:text-foreground'><span className='font-semibold text-primary'>[{sourceIndex + 1}]</span><FileText className='size-3' /><span className='truncate'>{source.fileName || `来源 ${sourceIndex + 1}`}</span>{source.pageNumber != null && <span className='shrink-0'>p.{source.pageNumber}</span>}<ChevronRight className='size-3 shrink-0' /></button>)}</div>}
      <div className='mt-1 flex min-h-6 items-center opacity-0 transition group-hover:opacity-100 focus-within:opacity-100'><button aria-label='复制消息' onClick={() => { void navigator.clipboard.writeText(message.content); toast.success('已复制') }} className='rounded-md p-1.5 text-muted-foreground hover:bg-accent'><Copy className='size-3' /></button></div>
    </div>
    {isUser && <div className='mt-1 flex size-8 shrink-0 items-center justify-center rounded-xl border bg-secondary'><User className='size-4' /></div>}
  </motion.div>
}

function AssistantAvatar() {
  return <div className='mt-1 flex size-8 shrink-0 items-center justify-center rounded-xl bg-primary text-primary-foreground shadow-sm'><Bot className='size-4' /></div>
}

function MessageBody({ content }: { content: string }) {
  return <div className='space-y-2.5 break-words'>{content.replace(/\r\n/g, '\n').split('\n').map((line, index) => {
    const trimmed = line.trim()
    if (!trimmed) return <div key={index} className='h-1' />
    const heading = trimmed.match(/^(#{1,4})\s+(.*)$/)
    if (heading) return <div key={index} className={cn('pt-1 font-semibold tracking-tight', heading[1].length <= 2 ? 'text-base' : 'text-sm')}><InlineMarkup text={heading[2]} /></div>
    const bullet = trimmed.match(/^[-*]\s+(.*)$/)
    if (bullet) return <div key={index} className='flex gap-2 pl-1'><span className='mt-[11px] size-1.5 shrink-0 rounded-full bg-primary/70' /><div className='min-w-0'><InlineMarkup text={bullet[1]} /></div></div>
    const numbered = trimmed.match(/^(\d+)[.)]\s+(.*)$/)
    if (numbered) return <div key={index} className='flex gap-2 pl-1'><span className='min-w-5 font-medium text-primary'>{numbered[1]}.</span><div className='min-w-0'><InlineMarkup text={numbered[2]} /></div></div>
    return <p key={index}><InlineMarkup text={trimmed} /></p>
  })}</div>
}

function InlineMarkup({ text }: { text: string }) {
  const parts = text.split(/(\*\*[^*]+\*\*|`[^`]+`)/g).filter(Boolean)
  return <>{parts.map((part, index): ReactNode => {
    if (part.startsWith('**') && part.endsWith('**')) return <strong key={index} className='font-semibold text-foreground'>{part.slice(2, -2)}</strong>
    if (part.startsWith('`') && part.endsWith('`')) return <code key={index} className='rounded bg-muted px-1.5 py-0.5 font-mono text-[.9em]'>{part.slice(1, -1)}</code>
    return <span key={index}>{part}</span>
  })}</>
}

function SourcePanel({ source, kbId }: { source: ChatSource; kbId: string }) {
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
    <div className='rounded-xl border bg-background/70 p-3.5 shadow-sm'><div className='truncate text-sm font-semibold'>{source.fileName || '未命名文档'}</div><div className='mt-1 text-[11px] text-muted-foreground'>chunk #{source.chunkIndex ?? '—'}{source.pageNumber != null ? ` · page ${source.pageNumber}` : ''}</div></div>
    <div><div className='mb-2 text-[10px] font-medium uppercase tracking-wide text-muted-foreground'>证据摘录</div><div className='whitespace-pre-wrap rounded-xl border bg-background/65 p-3.5 text-xs leading-6 text-foreground/85'>{source.excerpt || '历史消息未保存 excerpt；重新提问后会显示完整证据摘录。'}</div></div>
    <div><div className='mb-2 text-[10px] font-medium uppercase tracking-wide text-muted-foreground'>检索分数</div><div className='grid grid-cols-2 gap-2'>{metrics.map(([label, value]) => <div key={label} className='rounded-xl border bg-background/60 p-3'><div className='text-[9px] uppercase text-muted-foreground'>{label}</div><div className='mt-1 text-sm font-semibold tabular-nums'>{formatScore(value)}</div></div>)}</div></div>
    <div className='rounded-xl border border-primary/15 bg-primary/5 p-3.5'><div className='text-[10px] uppercase tracking-wide text-muted-foreground'>Final score</div><div className='mt-1 text-xl font-semibold tabular-nums'>{formatScore(source.score)}</div></div>
    <Button variant='outline' className='w-full' onClick={() => void open()}>打开原始文档</Button>
  </div>
}

function SourceEmpty() {
  return <div className='grid min-h-full place-items-center p-6 text-center'><div><div className='mx-auto mb-3 flex size-10 items-center justify-center rounded-xl border bg-background'><FileText className='size-4 text-muted-foreground' /></div><div className='text-xs font-medium'>尚未选择引用</div><div className='mt-1 max-w-52 text-[11px] leading-5 text-muted-foreground'>点击回答下方的引用，来源面板会在需要时打开。</div></div></div>
}

function MobileDrawer({ title, side = 'left', onClose, children }: { title: string; side?: 'left' | 'right'; onClose: () => void; children: React.ReactNode }) {
  return <div className='fixed inset-0 z-50 lg:hidden'><button aria-label='close' className='absolute inset-0 bg-slate-950/45 backdrop-blur-sm' onClick={onClose} /><motion.aside initial={{ x: side === 'left' ? -360 : 360 }} animate={{ x: 0 }} exit={{ x: side === 'left' ? -360 : 360 }} className={cn('absolute inset-y-0 w-[88vw] max-w-sm bg-background shadow-2xl', side === 'left' ? 'left-0' : 'right-0')}><div className='flex h-14 items-center justify-between border-b px-4 text-sm font-semibold'>{title}<Button variant='ghost' size='icon-sm' onClick={onClose}><X className='size-4' /></Button></div><div className='h-[calc(100%-3.5rem)] overflow-y-auto'>{children}</div></motion.aside></div>
}
