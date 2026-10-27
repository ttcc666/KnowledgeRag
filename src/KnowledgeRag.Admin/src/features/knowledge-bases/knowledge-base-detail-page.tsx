import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useParams } from '@tanstack/react-router'
import { Download, FileText, MessageSquareText, Plus, Search, Trash2, Upload, Users } from 'lucide-react'
import { toast } from 'sonner'
import { api, getErrorMessage } from '@/lib/api'
import { formatBytes, formatDate } from '@/lib/utils'
import type { KnowledgeBaseRole, SearchHit } from '@/lib/types'
import { Badge, Button, Card, CardContent, CardDescription, CardHeader, CardTitle, DataTable, EmptyState, Input, Label, PageHeader, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Td, Textarea, Th } from '@/components/ui'

const tabs = [{ id: 'documents', label: '文档', icon: FileText }, { id: 'search', label: 'Hybrid Search', icon: Search }, { id: 'members', label: '成员', icon: Users }] as const
type Tab = (typeof tabs)[number]['id']

export function KnowledgeBaseDetailPage() {
  const { knowledgeBaseId } = useParams({ strict: false }) as { knowledgeBaseId: string }
  const qc = useQueryClient(); const [tab, setTab] = useState<Tab>('documents')
  const kbs = useQuery({ queryKey: ['knowledge-bases'], queryFn: api.knowledgeBases.list })
  const kb = useMemo(() => kbs.data?.find((x) => x.id === knowledgeBaseId), [kbs.data, knowledgeBaseId])
  const canEdit = kb?.role === 'Editor' || kb?.role === 'Owner'; const isOwner = kb?.role === 'Owner'
  return <div className='space-y-6'><PageHeader title={kb?.name || '知识库'} description={kb?.description || '文档、检索、权限与对话管理'} actions={<Button asChild><a href={`/chat?knowledgeBaseId=${knowledgeBaseId}`}><MessageSquareText className='size-4' />进入 RAG 对话</a></Button>} />
    <div className='flex flex-wrap gap-2 border-b pb-3'>{tabs.map(({ id, label, icon: Icon }) => <Button key={id} variant={tab === id ? 'default' : 'ghost'} size='sm' onClick={() => setTab(id)} disabled={id === 'members' && !isOwner}><Icon className='size-4' />{label}</Button>)}</div>
    {tab === 'documents' && <DocumentsPanel kbId={knowledgeBaseId} canEdit={canEdit} onChanged={() => qc.invalidateQueries({ queryKey: ['documents', knowledgeBaseId] })} />}
    {tab === 'search' && <SearchPanel kbId={knowledgeBaseId} />}
    {tab === 'members' && isOwner && <MembersPanel kbId={knowledgeBaseId} />}
  </div>
}

function DocumentsPanel({ kbId, canEdit }: { kbId: string; canEdit: boolean; onChanged: () => void }) {
  const qc = useQueryClient(); const [file, setFile] = useState<File | null>(null)
  const docs = useQuery({ queryKey: ['documents', kbId], queryFn: () => api.documents.list(kbId) })
  const upload = useMutation({ mutationFn: () => api.documents.upload(kbId, file!), onSuccess: async () => { toast.success('文档已完成解析和索引'); setFile(null); await qc.invalidateQueries({ queryKey: ['documents', kbId] }) }, onError: (e) => toast.error(getErrorMessage(e)) })
  const remove = useMutation({ mutationFn: (id: string) => api.documents.remove(kbId, id), onSuccess: async () => { toast.success('文档已删除'); await qc.invalidateQueries({ queryKey: ['documents', kbId] }) }, onError: (e) => toast.error(getErrorMessage(e)) })
  const download = async (id: string, fileName: string) => { try { const blob = await api.documents.download(kbId, id); const url = URL.createObjectURL(blob); const a = document.createElement('a'); a.href = url; a.download = fileName; a.click(); URL.revokeObjectURL(url) } catch (e) { toast.error(getErrorMessage(e)) } }
  return <div className='space-y-4'>{canEdit && <Card><CardHeader><CardTitle className='text-base'>上传并索引文档</CardTitle><CardDescription>支持 PDF、DOCX、TXT、Markdown、CSV、JSON、XML、HTML；上传后自动 Chunk、Embedding 并写入向量后端。</CardDescription></CardHeader><CardContent><div className='flex flex-col gap-3 sm:flex-row'><Input type='file' onChange={(e) => setFile(e.target.files?.[0] || null)} /><Button disabled={!file || upload.isPending} onClick={() => upload.mutate()}><Upload className='size-4' />{upload.isPending ? '索引中…' : '上传并索引'}</Button></div></CardContent></Card>}
    {docs.data?.length ? <DataTable><thead><tr><Th>文件</Th><Th>状态</Th><Th>Chunks</Th><Th>大小</Th><Th>更新时间</Th><Th className='w-28'>操作</Th></tr></thead><tbody>{docs.data.map((doc) => <tr key={doc.id}><Td><div className='max-w-[420px] truncate font-medium'>{doc.fileName}</div>{doc.lastError && <div className='mt-1 max-w-[420px] truncate text-xs text-destructive'>{doc.lastError}</div>}</Td><Td><Badge variant={doc.status === 'Ready' ? 'default' : doc.status === 'Failed' ? 'destructive' : 'secondary'}>{doc.status}</Badge></Td><Td>{doc.chunkCount}</Td><Td>{formatBytes(doc.sizeBytes)}</Td><Td className='text-muted-foreground'>{formatDate(doc.updatedAt)}</Td><Td><div className='flex gap-1'><Button variant='ghost' size='icon' onClick={() => download(doc.id, doc.fileName)}><Download className='size-4' /></Button>{canEdit && <Button variant='ghost' size='icon' onClick={() => { if (confirm(`确认删除 ${doc.fileName}？`)) remove.mutate(doc.id) }}><Trash2 className='size-4 text-destructive' /></Button>}</div></Td></tr>)}</tbody></DataTable> : !docs.isLoading && <EmptyState title='还没有文档' description={canEdit ? '上传第一个文档开始构建知识库。' : '当前知识库还没有可检索文档。'} />}</div>
}

function SearchPanel({ kbId }: { kbId: string }) {
  const [query, setQuery] = useState(''); const [topK, setTopK] = useState('5'); const [hits, setHits] = useState<SearchHit[]>([])
  const search = useMutation({ mutationFn: () => api.search(kbId, query, Number(topK)), onSuccess: setHits, onError: (e) => toast.error(getErrorMessage(e)) })
  return <div className='grid gap-4 xl:grid-cols-[360px_1fr]'><Card><CardHeader><CardTitle className='text-base'>检索调试</CardTitle><CardDescription>查看 Vector / BM25 / RRF / reranker 的最终召回结果。</CardDescription></CardHeader><CardContent className='space-y-4'><div className='space-y-2'><Label>查询</Label><Textarea value={query} onChange={(e) => setQuery(e.target.value)} placeholder='输入要检索的问题…' /></div><div className='space-y-2'><Label>Top K</Label><Input type='number' min={1} max={20} value={topK} onChange={(e) => setTopK(e.target.value)} /></div><Button className='w-full' disabled={!query.trim() || search.isPending} onClick={() => search.mutate()}><Search className='size-4' />{search.isPending ? '检索中…' : '执行 Hybrid Search'}</Button></CardContent></Card><div className='space-y-3'>{hits.map((hit, index) => <Card key={hit.pointId}><CardHeader className='pb-3'><div className='flex flex-wrap items-center justify-between gap-2'><CardTitle className='text-sm'>#{index + 1} · {hit.fileName} / chunk {hit.chunkIndex}</CardTitle><Badge>{hit.score.toFixed(4)}</Badge></div><div className='flex flex-wrap gap-2 text-xs text-muted-foreground'><span>vector {(hit.vectorScore ?? 0).toFixed(4)}</span><span>bm25 {(hit.bm25Score ?? 0).toFixed(4)}</span><span>rrf {(hit.fusionScore ?? 0).toFixed(4)}</span>{hit.rerankScore != null && <span>rerank {hit.rerankScore.toFixed(4)}</span>}</div></CardHeader><CardContent><pre className='whitespace-pre-wrap break-words font-sans text-sm leading-6'>{hit.text}</pre></CardContent></Card>)}{!hits.length && !search.isPending && <EmptyState title='等待检索' description='输入问题后可以直接观察 Hybrid Search 每个阶段的得分。' />}</div></div>
}

function MembersPanel({ kbId }: { kbId: string }) {
  const qc = useQueryClient(); const [email, setEmail] = useState(''); const [role, setRole] = useState<KnowledgeBaseRole>('Viewer')
  const members = useQuery({ queryKey: ['kb-members', kbId], queryFn: () => api.knowledgeBases.members(kbId) })
  const save = useMutation({ mutationFn: () => api.knowledgeBases.saveMember(kbId, email, role), onSuccess: async () => { toast.success('成员权限已更新'); setEmail(''); await qc.invalidateQueries({ queryKey: ['kb-members', kbId] }) }, onError: (e) => toast.error(getErrorMessage(e)) })
  const remove = useMutation({ mutationFn: (id: string) => api.knowledgeBases.removeMember(kbId, id), onSuccess: async () => { toast.success('成员已移除'); await qc.invalidateQueries({ queryKey: ['kb-members', kbId] }) }, onError: (e) => toast.error(getErrorMessage(e)) })
  return <div className='space-y-4'><Card><CardHeader><CardTitle className='text-base'>添加或修改知识库成员</CardTitle><CardDescription>Viewer 可检索/聊天，Editor 可管理文档，Owner 可管理成员和知识库。</CardDescription></CardHeader><CardContent className='flex flex-col gap-3 sm:flex-row'><Input value={email} onChange={(e) => setEmail(e.target.value)} placeholder='member@example.com' /><Select value={role} onValueChange={(v) => setRole(v as KnowledgeBaseRole)}><SelectTrigger className='sm:w-40'><SelectValue /></SelectTrigger><SelectContent><SelectItem value='Viewer'>Viewer</SelectItem><SelectItem value='Editor'>Editor</SelectItem><SelectItem value='Owner'>Owner</SelectItem></SelectContent></Select><Button disabled={!email.trim() || save.isPending} onClick={() => save.mutate()}><Plus className='size-4' />保存成员</Button></CardContent></Card>
  {members.data?.length ? <DataTable><thead><tr><Th>成员</Th><Th>角色</Th><Th>加入时间</Th><Th className='w-20'>操作</Th></tr></thead><tbody>{members.data.map((m) => <tr key={m.id}><Td><div className='font-medium'>{m.displayName}</div><div className='text-xs text-muted-foreground'>{m.email}</div></Td><Td><Badge variant='outline'>{m.role}</Badge></Td><Td>{formatDate(m.addedAt)}</Td><Td><Button variant='ghost' size='icon' onClick={() => { if (confirm(`移除 ${m.email}？`)) remove.mutate(m.id) }}><Trash2 className='size-4 text-destructive' /></Button></Td></tr>)}</tbody></DataTable> : null}</div>
}
