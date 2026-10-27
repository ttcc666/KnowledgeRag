import { useQuery } from '@tanstack/react-query'
import { Brain, Database, FileSearch, HardDrive, Radio, Settings2 } from 'lucide-react'
import { api } from '@/lib/api'
import { Badge, Card, PageHeader, Skeleton } from '@/components/ui'

export function SettingsPage() {
  const settings = useQuery({ queryKey: ['product-settings'], queryFn: api.settings.product }); const s = settings.data
  const groups = [
    { title:'Models', icon:Brain, items:[['Chat model',s?.chatModel],['Embedding model',s?.embeddingModel],['Reranker',s?.rerankerMode]] },
    { title:'Retrieval', icon:FileSearch, items:[['Vector backend',s?.vectorBackend],['Lexical backend',s?.lexicalBackend],['Top K',s?.topK],['Chunk size',s?.chunkSize],['Chunk overlap',s?.chunkOverlap]] },
    { title:'Data & storage', icon:HardDrive, items:[['Database',s?.databaseProvider],['File storage',s?.storageProvider],['Max upload',s ? `${s.maxUploadMegabytes} MB` : undefined]] },
    { title:'Async ingestion', icon:Radio, items:[['CAP transport',s?.capTransport],['CAP retry count',s?.capFailedRetryCount],['Realtime','SignalR']] },
    { title:'Observability', icon:Database, items:[['OpenTelemetry',s ? (s.openTelemetryEnabled ? 'Enabled' : 'Disabled') : undefined],['Runtime metrics','Enabled']] },
  ]
  return <div className='space-y-6'><PageHeader eyebrow='Manage' title='Product Settings' description='查看当前实例的非敏感运行配置。密钥、连接串和密码不会暴露到前端。' actions={<Badge variant='outline'><Settings2 className='mr-1 size-3' />read-only runtime view</Badge>} /><div className='grid gap-4 lg:grid-cols-2'>{groups.map(({title,icon:Icon,items}) => <Card key={title} className='p-5'><div className='flex items-center gap-2 text-sm font-semibold'><Icon className='size-4 text-primary' />{title}</div><div className='mt-4 divide-y'>{items.map(([label,value]) => <div key={String(label)} className='flex items-center justify-between gap-4 py-3 text-xs'><span className='text-muted-foreground'>{label}</span>{value === undefined ? <Skeleton className='h-4 w-20' /> : <span className='max-w-64 truncate font-medium'>{String(value)}</span>}</div>)}</div></Card>)}</div></div>
}
