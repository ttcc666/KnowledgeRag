import { useMemo } from 'react'
import { Link } from '@tanstack/react-router'
import { useQueries, useQuery } from '@tanstack/react-query'
import { Activity, ArrowRight, Database, FileText, Layers3, MessageSquareText, Sparkles } from 'lucide-react'
import { Area, AreaChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api } from '@/lib/api'
import { formatDate } from '@/lib/utils'
import { Badge, Button, Card, CardContent, PageHeader, Skeleton } from '@/components/ui'
import { MetricTile, SectionHeading, SystemBadge } from '@/components/product'

export function DashboardPage() {
  const kbs = useQuery({ queryKey: ['knowledge-bases'], queryFn: api.knowledgeBases.list })
  const tenants = useQuery({ queryKey: ['tenants'], queryFn: api.tenants.list })
  const conversations = useQuery({ queryKey: ['conversations'], queryFn: () => api.conversations.list() })
  const docQueries = useQueries({ queries: (kbs.data || []).slice(0, 12).map((kb) => ({ queryKey: ['documents', kb.id], queryFn: () => api.documents.list(kb.id), staleTime: 20_000 })) })
  const documents = useMemo(() => docQueries.flatMap((query) => query.data || []), [docQueries])
  const totalChunks = documents.reduce((sum, item) => sum + item.chunkCount, 0)
  const processing = documents.filter((item) => item.status === 'Queued' || item.status === 'Processing').length
  const failed = documents.filter((item) => item.status === 'Failed').length
  const loading = kbs.isLoading || tenants.isLoading || conversations.isLoading
  const activityData = useMemo(() => {
    const today = new Date(); return Array.from({ length: 7 }, (_, index) => {
      const date = new Date(today); date.setDate(today.getDate() - (6 - index)); const key = date.toISOString().slice(0, 10)
      const docs = documents.filter((x) => x.updatedAt.slice(0, 10) === key).length
      const chats = (conversations.data || []).filter((x) => x.updatedAt.slice(0, 10) === key).length
      return { day: date.toLocaleDateString('zh-CN', { weekday: 'short' }), docs, chats }
    })
  }, [documents, conversations.data])
  const recentDocs = [...documents].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)).slice(0, 6)

  return <div className='space-y-7'>
    <PageHeader eyebrow='Workspace overview' title='你的知识正在发生什么' description='查看摄取状态、知识资产与对话活跃度。重点关注正在处理和失败的任务，而不是静态计数。' actions={<><Button variant='outline' asChild><Link to='/chat'><MessageSquareText className='size-4' />开始对话</Link></Button><Button asChild><Link to='/knowledge-bases'>管理知识库<ArrowRight className='size-4' /></Link></Button></>} />
    <div className='grid gap-4 sm:grid-cols-2 xl:grid-cols-4'>
      <MetricTile label='知识库' value={loading ? <Skeleton className='h-9 w-14' /> : kbs.data?.length ?? 0} hint='可访问工作空间' icon={<Database className='size-4.5' />} />
      <MetricTile label='已索引文档' value={loading ? <Skeleton className='h-9 w-14' /> : documents.filter((x) => x.status === 'Ready').length} hint={`${totalChunks.toLocaleString()} chunks`} icon={<FileText className='size-4.5' />} accent='success' />
      <MetricTile label='活跃会话' value={loading ? <Skeleton className='h-9 w-14' /> : conversations.data?.length ?? 0} hint='持久化 AgentSession' icon={<MessageSquareText className='size-4.5' />} accent='info' />
      <MetricTile label='处理中' value={loading ? <Skeleton className='h-9 w-14' /> : processing} hint={failed ? `${failed} 个任务需要处理` : '当前无失败任务'} icon={<Activity className='size-4.5' />} accent={failed ? 'warning' : 'primary'} />
    </div>

    <div className='grid gap-5 xl:grid-cols-[minmax(0,1.7fr)_minmax(320px,.8fr)]'>
      <Card className='overflow-hidden bg-card/75'><div className='flex items-start justify-between gap-4 border-b p-5 sm:p-6'><div><div className='text-base font-semibold'>7 日知识活动</div><div className='mt-1 text-sm text-muted-foreground'>文档变更与会话活动，全部来自当前工作区真实数据。</div></div><Badge variant='outline'>Live workspace</Badge></div><CardContent className='h-[310px] pt-5'><ResponsiveContainer width='100%' height='100%'><AreaChart data={activityData} margin={{ left: -18, right: 8, top: 8, bottom: 0 }}><defs><linearGradient id='docsGradient' x1='0' y1='0' x2='0' y2='1'><stop offset='0%' stopColor='var(--primary)' stopOpacity='.28' /><stop offset='100%' stopColor='var(--primary)' stopOpacity='0' /></linearGradient></defs><CartesianGrid vertical={false} stroke='var(--border)' strokeDasharray='3 5' /><XAxis dataKey='day' axisLine={false} tickLine={false} tick={{ fill: 'var(--muted-foreground)', fontSize: 11 }} /><YAxis allowDecimals={false} axisLine={false} tickLine={false} tick={{ fill: 'var(--muted-foreground)', fontSize: 11 }} /><Tooltip contentStyle={{ borderRadius: 12, border: '1px solid var(--border)', background: 'var(--popover)', color: 'var(--popover-foreground)', fontSize: 12 }} /><Area type='monotone' dataKey='docs' name='文档' stroke='var(--primary)' fill='url(#docsGradient)' strokeWidth={2.25} /><Area type='monotone' dataKey='chats' name='会话' stroke='var(--chart-2)' fill='transparent' strokeWidth={2} /></AreaChart></ResponsiveContainer></CardContent></Card>
      <Card className='bg-card/75 p-5 sm:p-6'><SectionHeading title='平台运行栈' description='当前已启用能力' /><div className='mt-5 grid gap-2.5'><SystemBadge label='Agent runtime' value='Microsoft Agent Framework' /><SystemBadge label='Async ingestion' value='CAP + RabbitMQ' /><SystemBadge label='Realtime' value='SignalR' /><SystemBadge label='Vector' value='Qdrant / pgvector' /><SystemBadge label='Lexical' value='BM25 / PostgreSQL FTS' /><SystemBadge label='Observability' value='OpenTelemetry' /></div><div className='mt-5 rounded-xl border border-primary/10 bg-primary/6 p-4'><div className='flex items-start gap-3'><div className='flex size-8 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary'><Sparkles className='size-4' /></div><div><div className='text-sm font-medium'>Hybrid retrieval is ready</div><div className='mt-1 text-xs leading-5 text-muted-foreground'>Vector + lexical + RRF + reranker 已构成完整检索链路。</div></div></div></div></Card>
    </div>

    <div className='space-y-3'><SectionHeading title='最近摄取' description='优先展示最新文档和后台处理状态' action={<Button variant='ghost' size='sm' asChild><Link to='/knowledge-bases'>查看全部<ArrowRight className='size-3.5' /></Link></Button>} />
      <Card className='overflow-hidden bg-card/70'>{recentDocs.length ? <div className='divide-y'>{recentDocs.map((doc) => <div key={doc.id} className='group flex flex-col gap-3 px-4 py-3.5 transition hover:bg-accent/35 sm:flex-row sm:items-center'><div className='flex min-w-0 flex-1 items-center gap-3'><div className='flex size-9 shrink-0 items-center justify-center rounded-xl border bg-background'><FileText className='size-4 text-muted-foreground' /></div><div className='min-w-0'><div className='truncate text-sm font-medium'>{doc.fileName}</div><div className='mt-0.5 text-xs text-muted-foreground'>{doc.chunkCount.toLocaleString()} chunks · {formatDate(doc.updatedAt)}</div></div></div><div className='flex items-center gap-2'><Badge variant={doc.status === 'Ready' ? 'success' : doc.status === 'Failed' ? 'destructive' : 'info'}>{doc.status}</Badge>{doc.status === 'Processing' && <span className='flex items-center gap-1.5 text-[11px] text-info'><span className='size-1.5 animate-pulse rounded-full bg-info' />Live</span>}</div></div>)}</div> : <div className='flex min-h-44 items-center justify-center text-sm text-muted-foreground'><Layers3 className='mr-2 size-4' />上传文档后，这里会显示实时摄取状态</div>}</Card>
    </div>
  </div>
}
