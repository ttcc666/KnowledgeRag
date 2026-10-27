import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Building2, Database, FileText, MessageSquareText } from 'lucide-react'
import { api } from '@/lib/api'
import { Badge, Button, Card, CardContent, CardDescription, CardHeader, CardTitle, PageHeader, Skeleton } from '@/components/ui'

export function DashboardPage() {
  const kbs = useQuery({ queryKey: ['knowledge-bases'], queryFn: api.knowledgeBases.list })
  const tenants = useQuery({ queryKey: ['tenants'], queryFn: api.tenants.list })
  const conversations = useQuery({ queryKey: ['conversations'], queryFn: () => api.conversations.list() })
  const cards = [
    { label: '知识库', value: kbs.data?.length ?? 0, icon: Database, hint: '当前账号可访问' },
    { label: '租户', value: tenants.data?.length ?? 0, icon: Building2, hint: '组织与个人空间' },
    { label: '对话 Session', value: conversations.data?.length ?? 0, icon: MessageSquareText, hint: '持久化 AgentSession' },
    { label: 'RAG 模式', value: 'Hybrid', icon: FileText, hint: 'Vector + BM25/FTS + RRF' },
  ]
  return <div className='space-y-6'><PageHeader title='总览' description='Knowledge RAG 多租户管理与 Agent 运维入口' actions={<Button asChild><Link to='/knowledge-bases'>管理知识库</Link></Button>} />
    <div className='grid gap-4 sm:grid-cols-2 xl:grid-cols-4'>{cards.map(({ label, value, icon: Icon, hint }) => <Card key={label}><CardHeader className='flex-row items-center justify-between space-y-0 pb-2'><CardTitle className='text-sm font-medium'>{label}</CardTitle><Icon className='size-4 text-muted-foreground' /></CardHeader><CardContent><div className='text-2xl font-bold'>{kbs.isLoading || tenants.isLoading || conversations.isLoading ? <Skeleton className='h-8 w-16' /> : value}</div><p className='mt-1 text-xs text-muted-foreground'>{hint}</p></CardContent></Card>)}</div>
    <div className='grid gap-4 lg:grid-cols-[2fr_1fr]'><Card><CardHeader><CardTitle>最近知识库</CardTitle><CardDescription>按更新时间展示你可以访问的知识库。</CardDescription></CardHeader><CardContent className='space-y-3'>{(kbs.data || []).slice(0, 6).map((kb) => <Link key={kb.id} to='/knowledge-bases/$knowledgeBaseId' params={{ knowledgeBaseId: kb.id }} className='flex items-center justify-between rounded-lg border p-3 transition hover:bg-accent'><div className='min-w-0'><div className='truncate text-sm font-medium'>{kb.name}</div><div className='truncate text-xs text-muted-foreground'>{kb.description || '暂无描述'}</div></div><Badge variant='outline'>{kb.role}</Badge></Link>)}{!kbs.isLoading && !kbs.data?.length && <div className='py-8 text-center text-sm text-muted-foreground'>还没有知识库</div>}</CardContent></Card>
    <Card><CardHeader><CardTitle>平台能力</CardTitle><CardDescription>当前项目已启用的核心能力。</CardDescription></CardHeader><CardContent className='space-y-3 text-sm'>{['Microsoft Agent Framework','PostgreSQL / SQLite','Qdrant / pgvector','BM25 / PostgreSQL FTS','MinIO / S3','OpenTelemetry'].map((x) => <div key={x} className='flex items-center justify-between'><span>{x}</span><Badge>Ready</Badge></div>)}</CardContent></Card></div>
  </div>
}
