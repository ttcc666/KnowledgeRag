import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Background, Controls, Handle, MiniMap, Position, ReactFlow, type Edge, type Node, type NodeProps } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { api } from '@/lib/api'
import { Card, PageHeader } from '@/components/ui'

const nodeTypes = { product: ProductNode }
export function PipelinePage() {
  const settings = useQuery({ queryKey:['product-settings'], queryFn:api.settings.product }); const s = settings.data
  const nodes = useMemo<Node[]>(() => [
    ['upload','Upload','API + file storage',0,120], ['cap','CAP event',s?.capTransport || 'transport',220,120], ['parse','Parse','PDF / DOCX / text',440,40], ['chunk','Chunk',s ? `${s.chunkSize} / overlap ${s.chunkOverlap}` : 'chunking',440,200], ['embed','Embedding',s?.embeddingModel || 'embedding model',680,200], ['vector','Vector',s?.vectorBackend || 'vector',920,120], ['lexical','Lexical',s?.lexicalBackend || 'lexical',920,280], ['rrf','RRF fusion','rank fusion',1160,200], ['rerank','Reranker',s?.rerankerMode || 'reranker',1380,200], ['agent','Agent Framework',s?.chatModel || 'chat model',1600,200], ['signalr','SignalR','realtime UI',1820,200],
  ].map(([id,label,meta,x,y]) => ({ id:String(id), type:'product', position:{x:Number(x),y:Number(y)}, data:{label,meta} })), [s])
  const edges: Edge[] = [['upload','cap'],['cap','parse'],['parse','chunk'],['chunk','embed'],['embed','vector'],['chunk','lexical'],['vector','rrf'],['lexical','rrf'],['rrf','rerank'],['rerank','agent'],['agent','signalr']].map(([source,target]) => ({id:`${source}-${target}`,source,target,animated:target==='signalr'}))
  return <div className='space-y-6'><PageHeader eyebrow='Operate' title='RAG Pipeline' description='把实际运行配置映射到完整摄取与检索链路，方便排障和架构沟通。' /><Card className='h-[680px] overflow-hidden bg-card/60'><ReactFlow nodes={nodes} edges={edges} nodeTypes={nodeTypes} fitView minZoom={.35} maxZoom={1.5}><Background gap={22} /><MiniMap pannable zoomable /><Controls /></ReactFlow></Card></div>
}
function ProductNode({ data }: NodeProps) { const d=data as {label:string;meta:string}; return <div className='min-w-44 rounded-2xl border bg-background/95 p-4 shadow-lg'><Handle type='target' position={Position.Left} /><div className='text-xs font-semibold'>{d.label}</div><div className='mt-1 text-[10px] text-muted-foreground'>{d.meta}</div><Handle type='source' position={Position.Right} /></div> }
