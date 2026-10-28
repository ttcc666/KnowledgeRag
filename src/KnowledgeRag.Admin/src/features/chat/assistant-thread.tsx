import { useCallback, useMemo } from 'react'
import {
  ActionBarPrimitive,
  AssistantRuntimeProvider,
  AuiIf,
  ComposerPrimitive,
  MessagePartPrimitive,
  MessagePrimitive,
  ThreadPrimitive,
  useAui,
  useExternalStoreRuntime,
} from '@assistant-ui/react'
import {
  AlertCircle,
  ArrowUp,
  Check,
  ChevronDown,
  ChevronRight,
  Copy,
  FileText,
  LoaderCircle,
  Search,
  ShieldCheck,
  Sparkles,
  Square,
  Wrench,
} from 'lucide-react'
import { cn } from '@/lib/utils'
import type { ChatMessage, ChatSource, RagProgressEvent } from '@/lib/types'

type AssistantThreadProps = {
  messages: ChatMessage[]
  sourcesByMessageId: Record<string, ChatSource[]>
  isRunning: boolean
  progressEvents: RagProgressEvent[]
  traceMessageId: string
  progressOpen: boolean
  onToggleProgress: () => void
  onSend: (message: string) => Promise<void>
  onCancel: () => void
  onSource: (source: ChatSource) => void
}

export function AssistantThread({
  messages,
  sourcesByMessageId,
  isRunning,
  progressEvents,
  traceMessageId,
  progressOpen,
  onToggleProgress,
  onSend,
  onCancel,
  onSource,
}: AssistantThreadProps) {
  const convertMessage = useCallback((message: ChatMessage) => ({
    id: message.id,
    role: message.role.toLowerCase() === 'user' ? 'user' as const : 'assistant' as const,
    content: [{ type: 'text' as const, text: message.content }],
    createdAt: new Date(message.createdAt),
  }), [])

  const runtime = useExternalStoreRuntime({
    messages,
    isRunning,
    convertMessage,
    onNew: async (message) => {
      const textPart = message.content.find((part) => part.type === 'text')
      if (!textPart || textPart.type !== 'text' || !textPart.text.trim()) return
      await onSend(textPart.text)
    },
    onCancel: async () => onCancel(),
  })

  return (
    <AssistantRuntimeProvider runtime={runtime}>
      <ThreadPrimitive.Root className='flex h-full min-h-0 flex-col'>
        <ThreadPrimitive.Viewport
          turnAnchor='top'
          autoScroll
          scrollToBottomOnRunStart
          className='min-h-0 flex-1 overflow-y-auto overscroll-contain bg-gradient-to-b from-muted/10 via-transparent to-transparent px-4 py-5 sm:px-6 lg:px-8'
        >
          <div className='mx-auto flex w-full max-w-5xl flex-col gap-7 pb-5'>
            <AuiIf condition={(state) => state.thread.isEmpty}>
              <WelcomePrompts />
            </AuiIf>

            <ThreadPrimitive.Messages>
              {({ message }) => message.role === 'user'
                ? <UserMessage />
                : <AssistantMessage
                    messageId={message.id}
                    sources={sourcesByMessageId[message.id] || []}
                    onSource={onSource}
                    progressEvents={progressEvents}
                    showProgress={message.id === traceMessageId}
                    progressOpen={progressOpen}
                    onToggleProgress={onToggleProgress}
                    running={isRunning && message.id === traceMessageId}
                  />}
            </ThreadPrimitive.Messages>

            <ThreadPrimitive.ViewportFooter className='sticky bottom-0 z-10 pt-4'>
              <ChatComposer />
            </ThreadPrimitive.ViewportFooter>
          </div>
        </ThreadPrimitive.Viewport>
      </ThreadPrimitive.Root>
    </AssistantRuntimeProvider>
  )
}

function WelcomePrompts() {
  const aui = useAui()
  const prompts = useMemo(() => ['总结这个知识库', '有哪些关键主题？', '帮我比较相关文档'], [])
  return (
    <div className='grid min-h-[min(52vh,480px)] place-items-center py-10'>
      <div className='w-full max-w-xl text-center'>
        <div className='mx-auto mb-5 flex size-14 items-center justify-center rounded-2xl border border-primary/15 bg-primary/8 text-primary shadow-sm'>
          <Sparkles className='size-6' />
        </div>
        <h2 className='text-xl font-semibold tracking-tight sm:text-2xl'>从知识库开始探索</h2>
        <p className='mx-auto mt-2 max-w-md text-sm leading-6 text-muted-foreground'>回答会附带来源证据。执行时可展开 Agent 过程，查看规划、检索、子 Agent 调用和证据审核状态。</p>
        <div className='mt-6 flex flex-wrap justify-center gap-2'>
          {prompts.map((prompt) => (
            <button
              key={prompt}
              type='button'
              onClick={() => aui.composer().setText(prompt)}
              className='rounded-full border bg-background px-4 py-2 text-xs text-muted-foreground shadow-sm transition hover:border-primary/30 hover:bg-primary/5 hover:text-foreground'
            >
              {prompt}
            </button>
          ))}
        </div>
      </div>
    </div>
  )
}

function ChatComposer() {
  return (
    <div className='mx-auto w-full max-w-4xl rounded-2xl border bg-background/95 p-2 shadow-[0_12px_40px_-24px_rgba(15,23,42,.35)] backdrop-blur-xl sm:p-2.5'>
      <ComposerPrimitive.Root className='flex flex-col gap-2'>
        <ComposerPrimitive.Input
          placeholder='询问当前知识库…'
          submitMode='enter'
          unstable_insertNewlineOnTouchEnter
          className='max-h-40 min-h-12 w-full resize-none bg-transparent px-3 py-2.5 text-sm leading-6 outline-none placeholder:text-muted-foreground/75'
        />
        <div className='flex items-center justify-between gap-3 px-1 pb-0.5'>
          <span className='hidden text-[10px] text-muted-foreground sm:inline'>Enter 发送 · Shift+Enter 换行</span>
          <div className='ml-auto flex items-center gap-2'>
            <AuiIf condition={(state) => state.thread.isRunning}>
              <ComposerPrimitive.Cancel className='inline-flex h-8 items-center gap-1.5 rounded-xl border border-destructive/25 px-3 text-xs font-medium text-destructive transition hover:bg-destructive/5'>
                <Square className='size-3' />停止
              </ComposerPrimitive.Cancel>
            </AuiIf>
            <ComposerPrimitive.Send className='inline-flex size-9 items-center justify-center rounded-xl bg-primary text-primary-foreground shadow-sm transition hover:bg-primary/90 disabled:cursor-not-allowed disabled:opacity-35'>
              <ArrowUp className='size-4' />
              <span className='sr-only'>发送消息</span>
            </ComposerPrimitive.Send>
          </div>
        </div>
      </ComposerPrimitive.Root>
    </div>
  )
}

function UserMessage() {
  return (
    <MessagePrimitive.Root data-role='user' className='flex items-start justify-end gap-3'>
      <div className='max-w-[88%] rounded-2xl rounded-tr-md bg-primary px-4 py-3 text-sm leading-7 text-primary-foreground shadow-sm sm:max-w-[78%]'>
        <MessagePrimitive.Parts>
          {({ part }) => part.type === 'text' ? <MessagePartPrimitive.Text className='whitespace-pre-wrap' /> : null}
        </MessagePrimitive.Parts>
      </div>
      <div className='mt-1 flex size-8 shrink-0 items-center justify-center rounded-xl border bg-secondary text-muted-foreground'>
        <span className='text-[10px] font-semibold'>你</span>
      </div>
    </MessagePrimitive.Root>
  )
}

function AssistantMessage({
  messageId,
  sources,
  onSource,
  progressEvents,
  showProgress,
  progressOpen,
  onToggleProgress,
  running,
}: {
  messageId: string
  sources: ChatSource[]
  onSource: (source: ChatSource) => void
  progressEvents: RagProgressEvent[]
  showProgress: boolean
  progressOpen: boolean
  onToggleProgress: () => void
  running: boolean
}) {
  return (
    <MessagePrimitive.Root data-role='assistant' className='group flex items-start gap-3'>
      <div className='mt-1 flex size-8 shrink-0 items-center justify-center rounded-xl bg-primary text-primary-foreground shadow-sm'>
        <Sparkles className='size-4' />
      </div>
      <div className='min-w-0 flex-1'>
        <div className='max-w-[960px] text-sm leading-7 sm:text-[14px]'>
          <MessagePrimitive.Parts>
            {({ part }) => part.type === 'text'
              ? <div className='whitespace-pre-wrap break-words'><MessagePartPrimitive.Text /><MessagePartPrimitive.InProgress><span className='ml-1 inline-block h-4 w-0.5 animate-pulse bg-primary align-middle' /></MessagePartPrimitive.InProgress></div>
              : part.type === 'tool-call' ? part.toolUI : null}
          </MessagePrimitive.Parts>
        </div>

        {sources.length > 0 && (
          <div className='mt-3 flex flex-wrap items-center gap-1.5'>
            <span className='mr-1 inline-flex items-center gap-1 text-[10px] font-medium uppercase tracking-wide text-muted-foreground'><FileText className='size-3' />引用来源</span>
            {sources.map((source, index) => (
              <button
                key={`${source.documentId}-${source.chunkIndex}-${index}`}
                type='button'
                aria-label={`查看引用 ${index + 1}: ${source.fileName || '未命名文档'}`}
                onClick={() => onSource(source)}
                className='inline-flex max-w-72 items-center gap-1.5 rounded-lg border bg-background/90 px-2.5 py-1.5 text-[11px] text-muted-foreground shadow-sm transition hover:border-primary/30 hover:bg-primary/5 hover:text-foreground'
              >
                <span className='font-semibold text-primary'>[{index + 1}]</span>
                <FileText className='size-3 shrink-0' />
                <span className='truncate'>{source.fileName || `来源 ${index + 1}`}</span>
                {source.pageNumber != null && <span className='shrink-0'>p.{source.pageNumber}</span>}
                <ChevronRight className='size-3 shrink-0' />
              </button>
            ))}
          </div>
        )}

        {showProgress && progressEvents.length > 0 && (
          <ExecutionTimeline events={progressEvents} open={progressOpen} onToggle={onToggleProgress} running={running} />
        )}

        <ActionBarPrimitive.Root hideWhenRunning autohide='always' autohideFloat='always' className='mt-1 flex min-h-7 items-center gap-1 data-[floating]:opacity-0 data-[floating]:transition-opacity group-hover:data-[floating]:opacity-100'>
          <ActionBarPrimitive.Copy aria-label={`复制消息 ${messageId}`} className='group/copy rounded-md p-1.5 text-muted-foreground hover:bg-accent hover:text-foreground'>
            <Copy className='size-3.5 group-data-[copied]/copy:hidden' />
            <Check className='hidden size-3.5 text-emerald-600 group-data-[copied]/copy:block' />
          </ActionBarPrimitive.Copy>
        </ActionBarPrimitive.Root>
      </div>
    </MessagePrimitive.Root>
  )
}

function ExecutionTimeline({ events, open, onToggle, running }: { events: RagProgressEvent[]; open: boolean; onToggle: () => void; running: boolean }) {
  const workflow = events.find((event) => event.stepId === 'workflow')
  const doneCount = events.filter((event) => ['completed', 'skipped'].includes(event.status)).length
  const statusText = running ? '正在执行' : workflow?.status === 'failed' ? '执行失败' : workflow?.status === 'cancelled' ? '已停止' : '已完成'

  return (
    <section className='mt-4 overflow-hidden rounded-2xl border bg-card/70 shadow-sm' aria-label='Agent 执行过程'>
      <button type='button' onClick={onToggle} aria-expanded={open} className='flex w-full items-center gap-3 px-3.5 py-3 text-left transition hover:bg-muted/40 sm:px-4'>
        <span className={cn('flex size-8 shrink-0 items-center justify-center rounded-xl border', running ? 'border-primary/20 bg-primary/8 text-primary' : 'bg-background text-muted-foreground')}>
          {running ? <LoaderCircle className='size-4 animate-spin' /> : workflow?.status === 'failed' ? <AlertCircle className='size-4 text-destructive' /> : <Check className='size-4 text-emerald-600' />}
        </span>
        <span className='min-w-0 flex-1'>
          <span className='flex flex-wrap items-center gap-2 text-xs font-semibold'>执行过程<span className={cn('rounded-full px-2 py-0.5 text-[10px] font-medium', running ? 'bg-primary/8 text-primary' : 'bg-muted text-muted-foreground')}>{statusText}</span></span>
          <span className='mt-0.5 block text-[10px] text-muted-foreground'>{doneCount} 个步骤完成{typeof workflow?.elapsedMs === 'number' ? ` · 总耗时 ${formatDuration(workflow.elapsedMs)}` : ''}</span>
        </span>
        {open ? <ChevronDown className='size-4 text-muted-foreground' /> : <ChevronRight className='size-4 text-muted-foreground' />}
      </button>

      {open && (
        <ol className='border-t px-3.5 py-2 sm:px-4'>
          {events.map((event) => (
            <li key={event.stepId} className='flex gap-3 py-2.5'>
              <div className='flex w-5 shrink-0 justify-center'>
                <span className={cn('mt-0.5 flex size-5 items-center justify-center rounded-full border', event.status === 'running' ? 'border-primary/30 bg-primary/8 text-primary' : event.status === 'failed' || event.status === 'warning' ? 'border-amber-500/30 bg-amber-500/8 text-amber-600' : event.status === 'skipped' ? 'border-border bg-muted text-muted-foreground' : 'border-emerald-500/20 bg-emerald-500/8 text-emerald-600')}>
                  {event.status === 'running' ? <LoaderCircle className='size-3 animate-spin' /> : event.status === 'failed' || event.status === 'warning' ? <AlertCircle className='size-3' /> : event.stage === 'retrieval' ? <Search className='size-3' /> : event.stage === 'review' ? <ShieldCheck className='size-3' /> : event.stage === 'answer' && event.type.startsWith('tool.') ? <Wrench className='size-3' /> : <Check className='size-3' />}
                </span>
              </div>
              <div className='min-w-0 flex-1'>
                <div className='flex flex-wrap items-start justify-between gap-x-3 gap-y-1'>
                  <span className='text-xs font-medium leading-5'>{event.label}</span>
                  <span className='shrink-0 text-[10px] tabular-nums text-muted-foreground'>{typeof event.elapsedMs === 'number' ? formatDuration(event.elapsedMs) : event.status === 'running' ? '进行中' : ''}</span>
                </div>
                <div className='mt-0.5 flex flex-wrap items-center gap-x-2 gap-y-1 text-[10px] text-muted-foreground'>
                  <span>{event.actor}</span>
                  {progressDetail(event) && <span>· {progressDetail(event)}</span>}
                </div>
              </div>
            </li>
          ))}
        </ol>
      )}
    </section>
  )
}

function progressDetail(event: RagProgressEvent) {
  const data = event.data || {}
  const values: string[] = []
  if (typeof data.query === 'string') values.push(data.query)
  if (typeof data.queryCount === 'number' && !('query' in data)) values.push(`${data.queryCount} 个查询`)
  if (typeof data.resultCount === 'number') values.push(`${data.resultCount} 条命中`)
  if (typeof data.evidenceCount === 'number') values.push(`${data.evidenceCount} 条证据`)
  if (typeof data.mergedEvidenceCount === 'number') values.push(`合并后 ${data.mergedEvidenceCount} 条`)
  if (typeof data.confidence === 'number') values.push(`置信度 ${Math.round(data.confidence * 100)}%`)
  if (typeof data.issueCount === 'number' && data.issueCount > 0) values.push(`${data.issueCount} 个待修复项`)
  if (data.fallback === true) values.push('已启用降级策略')
  if (typeof data.retrievalRound === 'number' && data.retrievalRound > 1) values.push(`第 ${data.retrievalRound} 轮`)
  return values.join(' · ')
}

function formatDuration(milliseconds: number) {
  return milliseconds < 1000 ? `${milliseconds} ms` : `${(milliseconds / 1000).toFixed(2)} s`
}
