import { useEffect, useMemo, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Bot, MessageSquarePlus, Send, Trash2, User } from 'lucide-react'
import { toast } from 'sonner'
import { api, getErrorMessage } from '@/lib/api'
import { formatDate } from '@/lib/utils'
import type { ChatMessage, ChatSource } from '@/lib/types'
import { Badge, Button, Card, EmptyState, PageHeader, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Textarea } from '@/components/ui'

function parseSources(message: ChatMessage): ChatSource[] {
  if (!message.sourcesJson) return []
  try { return JSON.parse(message.sourcesJson) as ChatSource[] } catch { return [] }
}

export function ChatPage() {
  const qc = useQueryClient(); const kbs = useQuery({ queryKey: ['knowledge-bases'], queryFn: api.knowledgeBases.list })
  const initialKb = new URLSearchParams(window.location.search).get('knowledgeBaseId') || ''
  const [kbId, setKbId] = useState(initialKb); const activeKb = kbId || kbs.data?.[0]?.id || ''
  const conversations = useQuery({ queryKey: ['conversations', activeKb], queryFn: () => api.conversations.list(activeKb), enabled: !!activeKb })
  const [conversationId, setConversationId] = useState('')
  useEffect(() => { if (activeKb && !conversationId && conversations.data?.length) setConversationId(conversations.data[0].id) }, [activeKb, conversationId, conversations.data])
  useEffect(() => { setConversationId('') }, [activeKb])
  const messages = useQuery({ queryKey: ['messages', conversationId], queryFn: () => api.conversations.messages(conversationId), enabled: !!conversationId })
  const [text, setText] = useState(''); const endRef = useRef<HTMLDivElement>(null)
  useEffect(() => { endRef.current?.scrollIntoView({ behavior: 'smooth' }) }, [messages.data])
  const create = useMutation({ mutationFn: () => api.conversations.create(activeKb), onSuccess: async (c) => { setConversationId(c.id); await qc.invalidateQueries({ queryKey: ['conversations', activeKb] }); toast.success('已创建新会话') }, onError: (e) => toast.error(getErrorMessage(e)) })
  const send = useMutation({ mutationFn: async () => { let id = conversationId; if (!id) id = (await api.conversations.create(activeKb)).id; setConversationId(id); return api.conversations.send(id, text) }, onSuccess: async (result) => { setText(''); await Promise.all([qc.invalidateQueries({ queryKey: ['messages', result.conversationId] }), qc.invalidateQueries({ queryKey: ['conversations', activeKb] })]) }, onError: (e) => toast.error(getErrorMessage(e)) })
  const remove = useMutation({ mutationFn: (id: string) => api.conversations.remove(id), onSuccess: async () => { setConversationId(''); await qc.invalidateQueries({ queryKey: ['conversations', activeKb] }) }, onError: (e) => toast.error(getErrorMessage(e)) })
  const selectedConversation = useMemo(() => conversations.data?.find((x) => x.id === conversationId), [conversations.data, conversationId])

  if (!kbs.isLoading && !kbs.data?.length) return <div className='space-y-6'><PageHeader title='RAG 对话' description='使用 Agent Framework 与知识库进行多轮问答。' /><EmptyState title='没有可用知识库' description='先创建知识库并上传文档，再开始 RAG 对话。' /></div>

  return <div className='space-y-5'><PageHeader title='RAG 对话' description='AgentSession 持久化，多轮上下文与来源引用都会保存。' actions={<div className='w-64'><Select value={activeKb} onValueChange={setKbId}><SelectTrigger><SelectValue placeholder='选择知识库' /></SelectTrigger><SelectContent>{kbs.data?.map((kb) => <SelectItem value={kb.id} key={kb.id}>{kb.name}</SelectItem>)}</SelectContent></Select></div>} />
    <div className='grid min-h-[calc(100svh-190px)] gap-4 lg:grid-cols-[280px_1fr]'><Card className='overflow-hidden'><div className='flex items-center justify-between border-b p-3'><div className='text-sm font-semibold'>对话 Session</div><Button size='icon' variant='ghost' onClick={() => create.mutate()} disabled={!activeKb || create.isPending}><MessageSquarePlus className='size-4' /></Button></div><div className='max-h-[calc(100svh-250px)] overflow-y-auto p-2'>{conversations.data?.map((c) => <button key={c.id} onClick={() => setConversationId(c.id)} className={`mb-1 w-full rounded-md p-3 text-left transition ${conversationId === c.id ? 'bg-accent' : 'hover:bg-accent/60'}`}><div className='truncate text-sm font-medium'>{c.title}</div><div className='mt-1 text-xs text-muted-foreground'>{formatDate(c.updatedAt)}</div></button>)}{!conversations.isLoading && !conversations.data?.length && <div className='p-6 text-center text-xs text-muted-foreground'>还没有会话</div>}</div></Card>
      <Card className='flex min-h-[650px] flex-col overflow-hidden'><div className='flex h-14 items-center justify-between border-b px-4'><div><div className='text-sm font-semibold'>{selectedConversation?.title || '新会话'}</div><div className='text-xs text-muted-foreground'>Hybrid Search → reranker → TextSearchProvider → Agent</div></div>{conversationId && <Button size='icon' variant='ghost' onClick={() => { if (confirm('删除当前会话？')) remove.mutate(conversationId) }}><Trash2 className='size-4 text-destructive' /></Button>}</div>
        <div className='flex-1 space-y-5 overflow-y-auto bg-muted/20 p-4 sm:p-6'>{messages.data?.map((m) => <Message key={m.id} message={m} />)}{!messages.isLoading && !messages.data?.length && <div className='grid h-full min-h-80 place-items-center'><div className='max-w-md text-center'><div className='mx-auto mb-4 flex size-12 items-center justify-center rounded-full bg-primary/10'><Bot className='size-6' /></div><div className='font-semibold'>开始询问知识库</div><div className='mt-2 text-sm text-muted-foreground'>回答会优先依据当前知识库的检索结果，并带回文档来源。</div></div></div>}<div ref={endRef} /></div>
        <div className='border-t bg-background p-4'><div className='flex items-end gap-2'><Textarea className='min-h-20 resize-none' value={text} onChange={(e) => setText(e.target.value)} placeholder='输入问题，Enter 发送，Shift+Enter 换行…' onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); if (text.trim() && !send.isPending) send.mutate() } }} /><Button size='icon' className='size-10 shrink-0' disabled={!activeKb || !text.trim() || send.isPending} onClick={() => send.mutate()}><Send className='size-4' /></Button></div></div>
      </Card></div>
  </div>
}

function Message({ message }: { message: ChatMessage }) {
  const isUser = message.role.toLowerCase() === 'user'; const sources = parseSources(message)
  return <div className={`flex gap-3 ${isUser ? 'justify-end' : 'justify-start'}`}>{!isUser && <div className='flex size-8 shrink-0 items-center justify-center rounded-full bg-primary text-primary-foreground'><Bot className='size-4' /></div>}<div className={`max-w-[85%] rounded-xl px-4 py-3 text-sm leading-6 ${isUser ? 'bg-primary text-primary-foreground' : 'border bg-card'}`}><div className='whitespace-pre-wrap break-words'>{message.content}</div>{sources.length > 0 && <div className='mt-3 border-t pt-3'><div className='mb-2 text-xs font-medium opacity-70'>来源</div><div className='flex flex-wrap gap-2'>{sources.map((s, i) => <Badge key={`${s.documentId}-${s.chunkIndex}-${i}`} variant='outline'>{s.fileName} #{s.chunkIndex}</Badge>)}</div></div>}</div>{isUser && <div className='flex size-8 shrink-0 items-center justify-center rounded-full bg-secondary'><User className='size-4' /></div>}</div>
}
