from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected one match, found {count}")
    return text.replace(old, new, 1)


def replace_function(text: str, start_marker: str, end_marker: str, replacement: str, label: str) -> str:
    start = text.find(start_marker)
    end = text.find(end_marker, start + 1)
    if start < 0 or end < 0:
        raise SystemExit(f"{label}: function boundary not found")
    return text[:start] + replacement.rstrip() + "\n\n" + text[end:]

# Frontend contracts.
p = Path('src/KnowledgeRag.Admin/src/lib/types.ts')
s = p.read_text()
s = replace_once(s,
"export interface AiModelDeployment { id: string; tenantId: string; connectionId: string; connectionName: string; displayName: string; remoteModelName: string; modelType: AiModelType; embeddingDimensions?: number | null; isEnabled: boolean; updatedAt: string }",
"export interface AiModelDeployment { id: string; tenantId: string; connectionId: string; connectionName: string; displayName: string; remoteModelName: string; modelType: AiModelType; embeddingDimensions?: number | null; isEnabled: boolean; updatedAt: string; fallbackDeploymentId?: string | null; healthStatus: string; lastHealthCheckedAt?: string | null; lastHealthError?: string | null }",
'ai deployment frontend type')
s = replace_once(s,
"export interface AiConnectionTestResult { success: boolean; message: string }",
"export interface AiConnectionTestResult { success: boolean; message: string }\nexport interface AiDeploymentHealthResult { deploymentId: string; success: boolean; status: string; message: string; checkedAt: string }",
'ai deployment health type')
s = replace_once(s,
"  reviewerModel: string\n  promptProfileId?: string | null",
"  reviewerModel: string\n  plannerDeploymentId?: string | null\n  answerDeploymentId?: string | null\n  reviewerDeploymentId?: string | null\n  rerankerDeploymentId?: string | null\n  promptProfileId?: string | null",
'rag profile deployment ids')
s = replace_once(s,
"export interface RagRun { id: string; tenantId: string; knowledgeBaseId: string; conversationId?: string | null; userId: string; apiKeyId?: string | null; query: string; answer: string; status: string; answerModel: string; agentMode: string; sourceCount: number; estimatedInputTokens: number; estimatedOutputTokens: number; estimatedCostUsd: number; latencyMs: number; traceJson: string; createdAt: string }",
"export interface RagRun { id: string; tenantId: string; knowledgeBaseId: string; conversationId?: string | null; userId: string; apiKeyId?: string | null; query: string; answer: string; status: string; answerModel: string; answerDeploymentId?: string | null; agentMode: string; sourceCount: number; estimatedInputTokens: number; estimatedOutputTokens: number; estimatedCostUsd: number; latencyMs: number; traceJson: string; createdAt: string }",
'rag run deployment id')
s = replace_once(s,
"export interface ModelPrice { id: string; tenantId: string; model: string; inputCostPerMillionTokens: number; outputCostPerMillionTokens: number; isActive: boolean; updatedAt: string }",
"export interface ModelPrice { id: string; tenantId: string; deploymentId?: string | null; model: string; inputCostPerMillionTokens: number; outputCostPerMillionTokens: number; isActive: boolean; updatedAt: string }",
'model price deployment id')
p.write_text(s)

# API client contracts.
p = Path('src/KnowledgeRag.Admin/src/lib/api.ts')
s = p.read_text()
s = replace_once(s,
"  AiConnectionTestResult, AiModelDeployment, AiModelType, AiProviderConnection, AiProviderDescriptor, ApiKeySummary, AuditLog,",
"  AiConnectionTestResult, AiDeploymentHealthResult, AiModelDeployment, AiModelType, AiProviderConnection, AiProviderDescriptor, ApiKeySummary, AuditLog,",
'api type import')
s = replace_once(s,
"    saveAiDeployment: async (tenantId: string, body: { id?: string | null; connectionId: string; displayName: string; remoteModelName: string; modelType: AiModelType; embeddingDimensions?: number | null; isEnabled: boolean }) => (await http.post<AiModelDeployment>(`/api/tenants/${tenantId}/ai/deployments`, body)).data,\n    deleteAiDeployment: async (tenantId: string, id: string) => { await http.delete(`/api/tenants/${tenantId}/ai/deployments/${id}`) },",
"    saveAiDeployment: async (tenantId: string, body: { id?: string | null; connectionId: string; displayName: string; remoteModelName: string; modelType: AiModelType; embeddingDimensions?: number | null; isEnabled: boolean; fallbackDeploymentId?: string | null }) => (await http.post<AiModelDeployment>(`/api/tenants/${tenantId}/ai/deployments`, body)).data,\n    deleteAiDeployment: async (tenantId: string, id: string) => { await http.delete(`/api/tenants/${tenantId}/ai/deployments/${id}`) },\n    testAiDeployment: async (tenantId: string, id: string) => (await http.post<AiDeploymentHealthResult>(`/api/tenants/${tenantId}/ai/deployments/${id}/health`)).data,",
'ai deployment api')
s = replace_once(s,
"    saveModelPrice: async (tenantId: string, body: { model: string; inputCostPerMillionTokens: number; outputCostPerMillionTokens: number; isActive?: boolean }) => (await http.put<ModelPrice>('/api/governance/model-prices', body, { params: { tenantId } })).data,",
"    saveModelPrice: async (tenantId: string, body: { model: string; deploymentId?: string | null; inputCostPerMillionTokens: number; outputCostPerMillionTokens: number; isActive?: boolean }) => (await http.put<ModelPrice>('/api/governance/model-prices', body, { params: { tenantId } })).data,",
'model price api')
p.write_text(s)

# Tenant AI provider operations UI.
p = Path('src/KnowledgeRag.Admin/src/features/tenants/tenants-page.tsx')
s = p.read_text()
start = s.find("function TenantAiProviders(")
end = s.find("function DefaultDeploymentSelect(", start)
if start < 0 or end < 0:
    raise SystemExit('TenantAiProviders boundary not found')
tenant_ai = r'''function TenantAiProviders({ tenantId, canAdmin }: { tenantId: string; canAdmin: boolean }) {
  const qc = useQueryClient()
  const providers = useQuery({ queryKey: ['ai-provider-catalog'], queryFn: api.tenants.aiProviders })
  const connections = useQuery({ queryKey: ['ai-connections', tenantId], queryFn: () => api.tenants.aiConnections(tenantId) })
  const deployments = useQuery({ queryKey: ['ai-deployments', tenantId], queryFn: () => api.tenants.aiDeployments(tenantId) })
  const defaults = useQuery({ queryKey: ['ai-defaults', tenantId], queryFn: () => api.tenants.aiDefaults(tenantId) })
  const [connectionOpen, setConnectionOpen] = useState(false)
  const [editingConnection, setEditingConnection] = useState<AiProviderConnection | null>(null)
  const [deploymentOpen, setDeploymentOpen] = useState(false)
  const [editingDeployment, setEditingDeployment] = useState<AiModelDeployment | null>(null)
  const [chatDefault, setChatDefault] = useState('__none')
  const [embeddingDefault, setEmbeddingDefault] = useState('__none')
  const [rerankerDefault, setRerankerDefault] = useState('__none')

  useEffect(() => {
    setChatDefault(defaults.data?.defaultChatDeploymentId || '__none')
    setEmbeddingDefault(defaults.data?.defaultEmbeddingDeploymentId || '__none')
    setRerankerDefault(defaults.data?.defaultRerankerDeploymentId || '__none')
  }, [defaults.data])

  const refreshDeployments = () => qc.invalidateQueries({ queryKey: ['ai-deployments', tenantId] })
  const removeConnection = useMutation({ mutationFn: (id: string) => api.tenants.deleteAiConnection(tenantId, id), onSuccess: async () => { toast.success('Provider Connection 已删除'); await qc.invalidateQueries({ queryKey: ['ai-connections', tenantId] }) }, onError: (e) => toast.error(getErrorMessage(e)) })
  const testConnection = useMutation({ mutationFn: (id: string) => api.tenants.testAiConnection(tenantId, id), onSuccess: (result) => result.success ? toast.success(result.message) : toast.error(result.message), onError: (e) => toast.error(getErrorMessage(e)) })
  const testDeployment = useMutation({ mutationFn: (id: string) => api.tenants.testAiDeployment(tenantId, id), onSuccess: async (result) => { result.success ? toast.success(result.message) : toast.error(result.message); await refreshDeployments() }, onError: (e) => toast.error(getErrorMessage(e)) })
  const removeDeployment = useMutation({ mutationFn: (id: string) => api.tenants.deleteAiDeployment(tenantId, id), onSuccess: async () => { toast.success('Model Deployment 已删除'); await Promise.all([refreshDeployments(), qc.invalidateQueries({ queryKey: ['ai-defaults', tenantId] })]) }, onError: (e) => toast.error(getErrorMessage(e)) })
  const saveDefaults = useMutation({ mutationFn: () => api.tenants.saveAiDefaults(tenantId, { defaultChatDeploymentId: chatDefault === '__none' ? null : chatDefault, defaultEmbeddingDeploymentId: embeddingDefault === '__none' ? null : embeddingDefault, defaultRerankerDeploymentId: rerankerDefault === '__none' ? null : rerankerDefault }), onSuccess: async () => { toast.success('租户默认模型已保存'); await qc.invalidateQueries({ queryKey: ['ai-defaults', tenantId] }) }, onError: (e) => toast.error(getErrorMessage(e)) })

  const byType = (type: AiModelType) => (deployments.data || []).filter((x) => x.modelType === type && x.isEnabled)
  return <div className='space-y-6'>
    <div className='rounded-2xl border bg-muted/15 p-4 text-xs text-muted-foreground'>
      <div className='font-medium text-foreground'>Tenant AI Control Plane</div>
      <div className='mt-1 leading-5'>Provider 凭据按租户加密存储且不会返回前端。租户自定义 Base URL 必须是公网 HTTPS，并经过 SSRF 安全连接策略。Deployment 可配置同类型显式 fallback 与持久化 health；Planner / Answer / Reviewer / Reranker 的 KB 级绑定在 Governance Center 管理。Embedding 默认值只作为下一次版本化 Reindex 的目标。</div>
    </div>

    <section className='space-y-3'>
      <div className='flex items-center justify-between gap-3'><div><div className='text-xs font-semibold'>Provider Connections</div><div className='mt-1 text-[10px] text-muted-foreground'>同一租户可以配置多个 OpenAI-compatible 账号或 Gateway。</div></div>{canAdmin && <Button size='sm' onClick={() => { setEditingConnection(null); setConnectionOpen(true) }}><Plus className='size-3.5' />添加连接</Button>}</div>
      <div className='grid gap-3 lg:grid-cols-2'>{connections.data?.length ? connections.data.map((connection) => <div key={connection.id} className='rounded-2xl border bg-background/45 p-4'><div className='flex items-start gap-3'><div className='flex size-9 shrink-0 items-center justify-center rounded-xl border bg-muted/30'><Server className='size-4 text-primary' /></div><div className='min-w-0 flex-1'><div className='flex flex-wrap items-center gap-2'><span className='text-xs font-semibold'>{connection.name}</span><Badge variant='outline'>{connection.providerType}</Badge>{connection.isEnabled ? <Badge>Enabled</Badge> : <Badge variant='outline'>Disabled</Badge>}</div><div className='mt-1 truncate text-[10px] text-muted-foreground'>{connection.baseUrl}</div><div className='mt-2 text-[10px] text-muted-foreground'>Credential {connection.hasCredential ? 'configured' : 'not configured'} · revision {connection.revision}</div></div></div>{canAdmin && <div className='mt-3 flex flex-wrap gap-2'><Button size='sm' variant='outline' disabled={testConnection.isPending} onClick={() => testConnection.mutate(connection.id)}><Activity className='size-3.5' />测试</Button><Button size='sm' variant='outline' onClick={() => { setEditingConnection(connection); setConnectionOpen(true) }}><Pencil className='size-3.5' />编辑</Button><Button size='sm' variant='ghost' onClick={() => { if (confirm(`删除 Provider Connection “${connection.name}”？`)) removeConnection.mutate(connection.id) }}><Trash2 className='size-3.5 text-destructive' />删除</Button></div>}</div>) : <div className='col-span-full rounded-2xl border border-dashed p-7 text-center text-xs text-muted-foreground'>还没有 Tenant AI Provider Connection。未配置时继续使用平台级 AI fallback。</div>}</div>
    </section>

    <section className='space-y-3'>
      <div className='flex items-center justify-between gap-3'><div><div className='text-xs font-semibold'>Model Deployments</div><div className='mt-1 text-[10px] text-muted-foreground'>Deployment 是 provider/model/health/fallback/price 的稳定治理身份。</div></div>{canAdmin && <Button size='sm' variant='outline' disabled={!connections.data?.length} onClick={() => { setEditingDeployment(null); setDeploymentOpen(true) }}><Plus className='size-3.5' />添加模型</Button>}</div>
      <div className='divide-y rounded-2xl border bg-background/45'>{deployments.data?.length ? deployments.data.map((deployment) => {
        const fallback = deployment.fallbackDeploymentId ? deployments.data?.find((x) => x.id === deployment.fallbackDeploymentId) : undefined
        const healthVariant = deployment.healthStatus === 'healthy' ? 'success' : deployment.healthStatus === 'unhealthy' ? 'destructive' : 'outline'
        return <div key={deployment.id} className='flex flex-col gap-3 px-4 py-3.5 sm:flex-row sm:items-center'><div className='min-w-0 flex-1'><div className='flex flex-wrap items-center gap-2'><span className='text-xs font-medium'>{deployment.displayName}</span><Badge variant='outline'>{deployment.modelType}</Badge><Badge variant={healthVariant}>{deployment.healthStatus || 'unknown'}</Badge>{deployment.isEnabled ? null : <Badge variant='outline'>Disabled</Badge>}</div><div className='mt-1 text-[10px] text-muted-foreground'>{deployment.connectionName} · <code>{deployment.remoteModelName}</code>{deployment.embeddingDimensions ? ` · ${deployment.embeddingDimensions} dimensions` : ''}{fallback ? ` · fallback → ${fallback.displayName}` : ''}</div>{deployment.lastHealthCheckedAt && <div className='mt-1 text-[10px] text-muted-foreground'>Health checked {formatDate(deployment.lastHealthCheckedAt)}{deployment.lastHealthError ? ` · ${deployment.lastHealthError}` : ''}</div>}</div>{canAdmin && <div className='flex gap-1'><Button variant='ghost' size='icon-sm' title='检查 Deployment health' disabled={testDeployment.isPending} onClick={() => testDeployment.mutate(deployment.id)}><Activity className='size-3.5' /></Button><Button variant='ghost' size='icon-sm' title='编辑 Deployment' onClick={() => { setEditingDeployment(deployment); setDeploymentOpen(true) }}><Pencil className='size-3.5' /></Button><Button variant='ghost' size='icon-sm' title='删除 Deployment' onClick={() => { if (confirm(`删除 Model Deployment “${deployment.displayName}”？`)) removeDeployment.mutate(deployment.id) }}><Trash2 className='size-3.5 text-destructive' /></Button></div>}</div>
      }) : <div className='p-7 text-center text-xs text-muted-foreground'>还没有 Model Deployment。</div>}</div>
    </section>

    <section className='space-y-3'>
      <div><div className='text-xs font-semibold'>Tenant Defaults</div><div className='mt-1 text-[10px] text-muted-foreground'>KB 未显式绑定角色 Deployment 时使用这些租户默认值。Embedding 的实际激活仍由版本化 Reindex 控制。</div></div>
      <div className='grid gap-3 rounded-2xl border bg-muted/10 p-4 lg:grid-cols-3'>
        <DefaultDeploymentSelect label='Chat' value={chatDefault} onChange={setChatDefault} deployments={byType('chat')} disabled={!canAdmin} />
        <DefaultDeploymentSelect label='Embedding target' value={embeddingDefault} onChange={setEmbeddingDefault} deployments={byType('embedding')} disabled={!canAdmin} />
        <DefaultDeploymentSelect label='Reranker' value={rerankerDefault} onChange={setRerankerDefault} deployments={byType('rerank')} disabled={!canAdmin} />
        {canAdmin && <div className='lg:col-span-3 flex justify-end'><Button size='sm' disabled={saveDefaults.isPending} onClick={() => saveDefaults.mutate()}>保存默认模型</Button></div>}
      </div>
    </section>

    <ConnectionDialog tenantId={tenantId} open={connectionOpen} onOpenChange={setConnectionOpen} connection={editingConnection} providers={providers.data || []} />
    <DeploymentDialog tenantId={tenantId} open={deploymentOpen} onOpenChange={setDeploymentOpen} deployment={editingDeployment} connections={connections.data || []} deployments={deployments.data || []} />
  </div>
}'''
s = s[:start] + tenant_ai + "\n\n" + s[end:]

start = s.find("function DeploymentDialog(")
if start < 0:
    raise SystemExit('DeploymentDialog not found')
deployment_dialog = r'''function DeploymentDialog({ tenantId, open, onOpenChange, deployment, connections, deployments }: { tenantId: string; open: boolean; onOpenChange: (open: boolean) => void; deployment: AiModelDeployment | null; connections: AiProviderConnection[]; deployments: AiModelDeployment[] }) {
  const qc = useQueryClient()
  const [connectionId, setConnectionId] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [remoteModelName, setRemoteModelName] = useState('')
  const [modelType, setModelType] = useState<AiModelType>('chat')
  const [dimensions, setDimensions] = useState('')
  const [fallbackId, setFallbackId] = useState('__none')
  const [enabled, setEnabled] = useState(true)
  useEffect(() => {
    if (!open) return
    setConnectionId(deployment?.connectionId || connections.find((x) => x.isEnabled)?.id || connections[0]?.id || '')
    setDisplayName(deployment?.displayName || '')
    setRemoteModelName(deployment?.remoteModelName || '')
    setModelType(deployment?.modelType || 'chat')
    setDimensions(deployment?.embeddingDimensions ? String(deployment.embeddingDimensions) : '')
    setFallbackId(deployment?.fallbackDeploymentId || '__none')
    setEnabled(deployment?.isEnabled ?? true)
  }, [open, deployment, connections])
  const fallbackCandidates = deployments.filter((item) => item.id !== deployment?.id && item.modelType === modelType && item.isEnabled)
  const save = useMutation({ mutationFn: () => api.tenants.saveAiDeployment(tenantId, { id: deployment?.id || null, connectionId, displayName, remoteModelName, modelType, embeddingDimensions: modelType === 'embedding' ? Number(dimensions) || null : null, isEnabled: enabled, fallbackDeploymentId: fallbackId === '__none' ? null : fallbackId }), onSuccess: async () => { toast.success(deployment ? 'Model Deployment 已更新' : 'Model Deployment 已创建'); onOpenChange(false); await qc.invalidateQueries({ queryKey: ['ai-deployments', tenantId] }) }, onError: (e) => toast.error(getErrorMessage(e)) })
  return <Dialog open={open} onOpenChange={onOpenChange}><DialogContent><DialogHeader><DialogTitle>{deployment ? '编辑 Model Deployment' : '添加 Model Deployment'}</DialogTitle><DialogDescription>模型名与具体 Provider Connection 绑定；health 与显式 fallback 共同决定运行开始时的确定性 endpoint snapshot。</DialogDescription></DialogHeader><div className='space-y-4'><div className='space-y-2'><Label id='tenant-ai-deployment-connection-label' htmlFor='tenant-ai-deployment-connection'>Provider Connection</Label><Select value={connectionId} onValueChange={setConnectionId}><SelectTrigger id='tenant-ai-deployment-connection' aria-labelledby='tenant-ai-deployment-connection-label'><SelectValue /></SelectTrigger><SelectContent>{connections.map((connection) => <SelectItem key={connection.id} value={connection.id}>{connection.name}</SelectItem>)}</SelectContent></Select></div><div className='space-y-2'><Label htmlFor='tenant-ai-deployment-display-name'>显示名称</Label><Input id='tenant-ai-deployment-display-name' value={displayName} onChange={(e) => setDisplayName(e.target.value)} placeholder='GPT-4.1 Production' /></div><div className='space-y-2'><Label htmlFor='tenant-ai-deployment-remote-model-name'>远端模型名</Label><Input id='tenant-ai-deployment-remote-model-name' value={remoteModelName} onChange={(e) => setRemoteModelName(e.target.value)} placeholder='gpt-4.1' /></div><div className='space-y-2'><Label id='tenant-ai-deployment-model-type-label' htmlFor='tenant-ai-deployment-model-type'>类型</Label><Select value={modelType} onValueChange={(value) => { setModelType(value as AiModelType); setFallbackId('__none') }}><SelectTrigger id='tenant-ai-deployment-model-type' aria-labelledby='tenant-ai-deployment-model-type-label'><SelectValue /></SelectTrigger><SelectContent><SelectItem value='chat'>Chat</SelectItem><SelectItem value='embedding'>Embedding</SelectItem><SelectItem value='rerank'>Rerank</SelectItem></SelectContent></Select></div>{modelType === 'embedding' && <div className='space-y-2'><Label htmlFor='tenant-ai-deployment-dimensions'>Embedding dimensions</Label><Input id='tenant-ai-deployment-dimensions' type='number' min={1} value={dimensions} onChange={(e) => setDimensions(e.target.value)} placeholder='1536' /></div>}<div className='space-y-2'><Label id='tenant-ai-deployment-fallback-label' htmlFor='tenant-ai-deployment-fallback'>Fallback deployment</Label><Select value={fallbackId} onValueChange={setFallbackId}><SelectTrigger id='tenant-ai-deployment-fallback' aria-labelledby='tenant-ai-deployment-fallback-label'><SelectValue /></SelectTrigger><SelectContent><SelectItem value='__none'>No fallback</SelectItem>{fallbackCandidates.map((item) => <SelectItem key={item.id} value={item.id}>{item.displayName} · {item.connectionName}</SelectItem>)}</SelectContent></Select><p className='text-[10px] text-muted-foreground'>只允许同租户、同类型且无循环的显式 fallback；未配置时不会静默漂移到其他 Provider。</p></div><label className='flex items-center gap-2 text-xs'><input type='checkbox' checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />启用此 Deployment</label></div><DialogFooter><Button variant='outline' onClick={() => onOpenChange(false)}>取消</Button><Button disabled={!connectionId || !displayName.trim() || !remoteModelName.trim() || (modelType === 'embedding' && !(Number(dimensions) > 0)) || save.isPending} onClick={() => save.mutate()}>保存</Button></DialogFooter></DialogContent></Dialog>
}'''
s = s[:start] + deployment_dialog + "\n"
p.write_text(s)

# Governance routing and pricing UI.
p = Path('src/KnowledgeRag.Admin/src/features/governance/governance-page.tsx')
s = p.read_text()
s = replace_once(s,
"import type { PromptProfile, RagProfileUpdate, RagRun } from '@/lib/types'",
"import type { AiModelDeployment, PromptProfile, RagProfileUpdate, RagRun } from '@/lib/types'",
'governance type import')
rag_profile = r'''function RagProfilePanel({ knowledgeBaseId, tenantId }: { knowledgeBaseId: string; tenantId: string }) {
  const qc = useQueryClient()
  const profile = useQuery({ queryKey: ['rag-profile', knowledgeBaseId], queryFn: () => api.governance.ragProfile(knowledgeBaseId), enabled: !!knowledgeBaseId })
  const prompts = useQuery({ queryKey: ['prompt-profiles', tenantId], queryFn: () => api.governance.prompts(tenantId) })
  const deployments = useQuery({ queryKey: ['ai-deployments', tenantId], queryFn: () => api.tenants.aiDeployments(tenantId), enabled: !!tenantId })
  const [form, setForm] = useState<RagProfileUpdate | null>(null)
  useEffect(() => { if (profile.data) { const { knowledgeBaseId: _kb, updatedByUserId: _u, updatedAt: _at, ...rest } = profile.data; void _kb; void _u; void _at; setForm(rest) } }, [profile.data])
  const save = useMutation({ mutationFn: () => api.governance.saveRagProfile(knowledgeBaseId, form!), onSuccess: async () => { await Promise.all([qc.invalidateQueries({ queryKey: ['rag-profile', knowledgeBaseId] }), qc.invalidateQueries({ queryKey: ['governance-overview', tenantId] })]); toast.success('RAG Profile 已保存') }, onError: (error) => toast.error(getErrorMessage(error)) })
  if (!knowledgeBaseId) return <Card className='p-8 text-center text-sm text-muted-foreground'>请选择知识库。</Card>
  if (!form) return <Card className='p-8'><Skeleton className='h-52 w-full' /></Card>
  const number = (key: keyof RagProfileUpdate) => (event: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [key]: Number(event.target.value) })
  const chatDeployments = (deployments.data || []).filter((item) => item.modelType === 'chat' && item.isEnabled)
  const rerankDeployments = (deployments.data || []).filter((item) => item.modelType === 'rerank' && item.isEnabled)
  const routeField = (label: string, key: 'plannerDeploymentId' | 'answerDeploymentId' | 'reviewerDeploymentId' | 'rerankerDeploymentId', items: AiModelDeployment[]) => {
    const id = `rag-route-${key}`
    const labelId = `${id}-label`
    const value = form[key] || '__default'
    return <div className='space-y-1.5'><Label id={labelId} htmlFor={id}>{label}</Label><Select value={value} onValueChange={(next) => setForm({ ...form, [key]: next === '__default' ? null : next })}><SelectTrigger id={id} aria-labelledby={labelId}><SelectValue /></SelectTrigger><SelectContent><SelectItem value='__default'>Tenant / platform default</SelectItem>{items.map((item) => <SelectItem key={item.id} value={item.id}>{item.displayName} · {item.connectionName} · {item.healthStatus || 'unknown'}</SelectItem>)}</SelectContent></Select></div>
  }
  return <div className='grid gap-4 xl:grid-cols-2'>
    <Card><CardHeader><CardTitle className='text-sm'>Retrieval & Review</CardTitle><CardDescription>这些配置会在每一次 RAG Run 开始时解析为不可变 snapshot。</CardDescription></CardHeader><CardContent className='grid gap-4 sm:grid-cols-2'><Toggle label='Planner' value={form.enablePlanner} onChange={(value) => setForm({ ...form, enablePlanner: value })} /><Toggle label='Reviewer' value={form.enableReviewer} onChange={(value) => setForm({ ...form, enableReviewer: value })} /><Field label='Planner max queries'><Input type='number' min={1} max={6} value={form.plannerMaxQueries} onChange={number('plannerMaxQueries')} /></Field><Field label='Multi-query Top K'><Input type='number' min={1} max={30} value={form.multiQueryTopK} onChange={number('multiQueryTopK')} /></Field><Field label='Max merged evidence'><Input type='number' min={1} max={40} value={form.maxMergedEvidence} onChange={number('maxMergedEvidence')} /></Field><Field label='Reviewer retries'><Input type='number' min={0} max={3} value={form.reviewerMaxRetries} onChange={number('reviewerMaxRetries')} /></Field><Field label='Reviewer confidence'><Input type='number' min={0} max={1} step='.05' value={form.reviewerMinConfidence} onChange={number('reviewerMinConfidence')} /></Field><Field label='Agent mode'><Select value={form.agentMode} onValueChange={(agentMode) => setForm({ ...form, agentMode })}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value='single'>Single Agent</SelectItem><SelectItem value='specialists'>Specialists</SelectItem></SelectContent></Select></Field></CardContent></Card>
    <Card><CardHeader><CardTitle className='text-sm'>Model & Budget Governance</CardTitle><CardDescription>优先使用 Deployment 绑定；运行开始时按 health + 显式 fallback 解析一次。未绑定时才使用租户/平台默认或 legacy raw model override。</CardDescription></CardHeader><CardContent className='grid gap-4 sm:grid-cols-2'>{routeField('Planner deployment', 'plannerDeploymentId', chatDeployments)}{routeField('Answer deployment', 'answerDeploymentId', chatDeployments)}{routeField('Reviewer / specialist deployment', 'reviewerDeploymentId', chatDeployments)}{routeField('Reranker deployment', 'rerankerDeploymentId', rerankDeployments)}<div className='sm:col-span-2 rounded-xl border bg-muted/20 p-3 text-[10px] leading-5 text-muted-foreground'>Legacy raw model 字段只为旧配置兼容。新配置建议只绑定 Deployment，避免同名模型跨 Provider 无法区分。</div><Field label='Legacy planner model'><Input value={form.plannerModel} onChange={(e) => setForm({ ...form, plannerModel: e.target.value })} /></Field><Field label='Legacy answer model'><Input value={form.answerModel} onChange={(e) => setForm({ ...form, answerModel: e.target.value })} /></Field><Field label='Legacy reviewer model'><Input value={form.reviewerModel} onChange={(e) => setForm({ ...form, reviewerModel: e.target.value })} /></Field><Field label='Prompt Profile'><Select value={form.promptProfileId || 'auto'} onValueChange={(value) => setForm({ ...form, promptProfileId: value === 'auto' ? null : value })}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value='auto'>Auto / active profile</SelectItem>{prompts.data?.map((item) => <SelectItem key={item.id} value={item.id}>{item.name} · v{item.version}</SelectItem>)}</SelectContent></Select></Field><Field label='Monthly budget USD'><Input type='number' min={0} step='.01' value={form.monthlyBudgetUsd} onChange={number('monthlyBudgetUsd')} /></Field><Field label='Daily run limit'><Input type='number' min={0} value={form.dailyRunLimit} onChange={number('dailyRunLimit')} /></Field><div className='sm:col-span-2 flex justify-end'><Button disabled={save.isPending} onClick={() => save.mutate()}><Save className='size-3.5' />保存 Profile</Button></div></CardContent></Card>
  </div>
}'''
s = replace_function(s, 'function RagProfilePanel(', 'function RunsPanel(', rag_profile, 'RagProfilePanel')

cost_panel = r'''function CostPanel({ tenantId }: { tenantId: string }) {
  const qc = useQueryClient()
  const prices = useQuery({ queryKey: ['model-prices', tenantId], queryFn: () => api.governance.modelPrices(tenantId) })
  const deployments = useQuery({ queryKey: ['ai-deployments', tenantId], queryFn: () => api.tenants.aiDeployments(tenantId) })
  const overview = useQuery({ queryKey: ['governance-overview', tenantId], queryFn: () => api.governance.overview(tenantId) })
  const [deploymentId, setDeploymentId] = useState('__legacy')
  const [model, setModel] = useState('')
  const [input, setInput] = useState('0')
  const [output, setOutput] = useState('0')
  const selectedDeployment = deploymentId === '__legacy' ? undefined : deployments.data?.find((item) => item.id === deploymentId)
  const save = useMutation({ mutationFn: () => api.governance.saveModelPrice(tenantId, { model: deploymentId === '__legacy' ? model : '', deploymentId: deploymentId === '__legacy' ? null : deploymentId, inputCostPerMillionTokens: Number(input), outputCostPerMillionTokens: Number(output), isActive: true }), onSuccess: async () => { setModel(''); await qc.invalidateQueries({ queryKey: ['model-prices', tenantId] }); toast.success('模型价格已保存') }, onError: (error) => toast.error(getErrorMessage(error)) })
  return <div className='grid gap-4 xl:grid-cols-[.8fr_1.2fr]'><Card><CardHeader><CardTitle className='text-sm'>Cost policy</CardTitle><CardDescription>优先按 Deployment 身份计价；同名模型位于不同 Provider 时不会串价。旧 model-name 价格仍保留兼容 fallback。</CardDescription></CardHeader><CardContent><div className='text-3xl font-semibold tabular-nums'>${overview.data?.estimatedCostUsd.toFixed(4) || '0.0000'}</div><div className='mt-1 text-xs text-muted-foreground'>{overview.data?.runCount || 0} persisted runs</div></CardContent></Card><Card><CardHeader><CardTitle className='text-sm'>Deployment pricing</CardTitle><CardDescription>Budget Reservation 与实际 usage settle 都使用 resolved DeploymentId 优先查价。</CardDescription></CardHeader><CardContent className='space-y-4'><div className='grid gap-2 md:grid-cols-[1.4fr_1fr_1fr_auto]'><Select value={deploymentId} onValueChange={setDeploymentId}><SelectTrigger aria-label='Pricing identity'><SelectValue /></SelectTrigger><SelectContent><SelectItem value='__legacy'>Legacy model-name price</SelectItem>{deployments.data?.map((item) => <SelectItem key={item.id} value={item.id}>{item.displayName} · {item.connectionName} · {item.modelType}</SelectItem>)}</SelectContent></Select>{deploymentId === '__legacy' ? <Input placeholder='legacy model name' value={model} onChange={(e) => setModel(e.target.value)} /> : <Input readOnly value={selectedDeployment?.remoteModelName || ''} aria-label='Resolved model name' />}<Input type='number' step='.01' placeholder='input / 1M' value={input} onChange={(e) => setInput(e.target.value)} /><div className='flex gap-2'><Input type='number' step='.01' placeholder='output / 1M' value={output} onChange={(e) => setOutput(e.target.value)} /><Button disabled={(deploymentId === '__legacy' && !model.trim()) || save.isPending} onClick={() => save.mutate()}><Save className='size-3.5' /></Button></div></div><div className='space-y-2'>{prices.data?.map((price) => { const deployment = price.deploymentId ? deployments.data?.find((item) => item.id === price.deploymentId) : undefined; return <div key={price.id} className='grid gap-2 rounded-xl border p-3 text-xs sm:grid-cols-[1fr_auto] sm:items-center'><div><div className='font-medium'>{deployment ? deployment.displayName : price.model}</div><div className='mt-1 text-[10px] text-muted-foreground'>{deployment ? `${deployment.connectionName} · ${price.model} · deployment pricing` : `${price.model} · legacy model-name fallback`}</div></div><div className='text-right text-[10px] tabular-nums text-muted-foreground'>${price.inputCostPerMillionTokens}/1M in · ${price.outputCostPerMillionTokens}/1M out</div></div> }) || <div className='text-xs text-muted-foreground'>暂无价格配置。</div>}</div></CardContent></Card></div>
}'''
s = replace_function(s, 'function CostPanel(', 'function AuditPanel(', cost_panel, 'CostPanel')
p.write_text(s)

# Playwright governance mocks/assertions.
p = Path('src/KnowledgeRag.Admin/e2e/governance.spec.ts')
s = p.read_text()
s = replace_once(s,
"    if (path === '/api/knowledge-bases') return route.fulfill({ json: [{ id: 'kb1', tenantId: 't1', name: 'Governance KB', description: 'Policies', role: 'Owner', createdAt: '2026-10-28T00:00:00Z', updatedAt: '2026-10-28T00:00:00Z' }] })",
"    if (path === '/api/knowledge-bases') return route.fulfill({ json: [{ id: 'kb1', tenantId: 't1', name: 'Governance KB', description: 'Policies', role: 'Owner', createdAt: '2026-10-28T00:00:00Z', updatedAt: '2026-10-28T00:00:00Z' }] })\n    if (path === '/api/tenants/t1/ai/deployments') return route.fulfill({ json: [\n      { id: 'dep-planner', tenantId: 't1', connectionId: 'conn1', connectionName: 'OpenAI Prod', displayName: 'Planner Prod', remoteModelName: 'gpt-small', modelType: 'chat', isEnabled: true, healthStatus: 'healthy', fallbackDeploymentId: null, updatedAt: '2026-10-28T00:00:00Z' },\n      { id: 'dep-answer', tenantId: 't1', connectionId: 'conn1', connectionName: 'OpenAI Prod', displayName: 'Answer Prod', remoteModelName: 'gpt-strong', modelType: 'chat', isEnabled: true, healthStatus: 'healthy', fallbackDeploymentId: 'dep-planner', updatedAt: '2026-10-28T00:00:00Z' },\n      { id: 'dep-rerank', tenantId: 't1', connectionId: 'conn1', connectionName: 'OpenAI Prod', displayName: 'Rerank Prod', remoteModelName: 'rerank-v1', modelType: 'rerank', isEnabled: true, healthStatus: 'unknown', fallbackDeploymentId: null, updatedAt: '2026-10-28T00:00:00Z' },\n    ] })",
'governance deployment mock')
s = replace_once(s,
"    if (path === '/api/governance/knowledge-bases/kb1/rag-profile' && method === 'GET') return route.fulfill({ json: { knowledgeBaseId: 'kb1', enablePlanner: true, plannerMaxQueries: 3, multiQueryTopK: 6, maxMergedEvidence: 10, enableReviewer: true, reviewerMaxRetries: 1, reviewerMinConfidence: .7, plannerModel: 'small-model', answerModel: 'strong-model', reviewerModel: 'small-model', promptProfileId: null, agentMode: 'specialists', monthlyBudgetUsd: 25, dailyRunLimit: 500, updatedByUserId: 'u1', updatedAt: '2026-10-28T00:00:00Z' } })",
"    if (path === '/api/governance/knowledge-bases/kb1/rag-profile' && method === 'GET') return route.fulfill({ json: { knowledgeBaseId: 'kb1', enablePlanner: true, plannerMaxQueries: 3, multiQueryTopK: 6, maxMergedEvidence: 10, enableReviewer: true, reviewerMaxRetries: 1, reviewerMinConfidence: .7, plannerModel: '', answerModel: '', reviewerModel: '', plannerDeploymentId: 'dep-planner', answerDeploymentId: 'dep-answer', reviewerDeploymentId: 'dep-planner', rerankerDeploymentId: 'dep-rerank', promptProfileId: null, agentMode: 'specialists', monthlyBudgetUsd: 25, dailyRunLimit: 500, updatedByUserId: 'u1', updatedAt: '2026-10-28T00:00:00Z' } })",
'governance profile mock')
s = replace_once(s,
"    if (path === '/api/governance/runs') return route.fulfill({ json: [{ id: 'r1', tenantId: 't1', knowledgeBaseId: 'kb1', conversationId: 'c1', userId: 'u1', query: 'What is the policy?', answer: 'Policy answer', status: 'completed', answerModel: 'strong-model', agentMode: 'specialists', sourceCount: 4, estimatedInputTokens: 500, estimatedOutputTokens: 120, estimatedCostUsd: .0034, latencyMs: 1800, traceJson: JSON.stringify([{ stepId: 'planner', label: '问题分析完成', actor: 'QueryPlannerAgent', status: 'completed', elapsedMs: 120 }]), createdAt: '2026-10-28T00:00:00Z' }] })",
"    if (path === '/api/governance/runs') return route.fulfill({ json: [{ id: 'r1', tenantId: 't1', knowledgeBaseId: 'kb1', conversationId: 'c1', userId: 'u1', query: 'What is the policy?', answer: 'Policy answer', status: 'completed', answerModel: 'gpt-strong', answerDeploymentId: 'dep-answer', agentMode: 'specialists', sourceCount: 4, estimatedInputTokens: 500, estimatedOutputTokens: 120, estimatedCostUsd: .0034, latencyMs: 1800, traceJson: JSON.stringify([{ stepId: 'planner', label: '问题分析完成', actor: 'QueryPlannerAgent', status: 'completed', elapsedMs: 120 }]), createdAt: '2026-10-28T00:00:00Z' }] })",
'governance run mock')
s = replace_once(s,
"    if (path === '/api/governance/model-prices') return route.fulfill({ json: [{ id: 'mp1', tenantId: 't1', model: 'strong-model', inputCostPerMillionTokens: 2.5, outputCostPerMillionTokens: 10, isActive: true, updatedAt: '2026-10-28T00:00:00Z' }] })",
"    if (path === '/api/governance/model-prices') return route.fulfill({ json: [{ id: 'mp1', tenantId: 't1', deploymentId: 'dep-answer', model: 'gpt-strong', inputCostPerMillionTokens: 2.5, outputCostPerMillionTokens: 10, isActive: true, updatedAt: '2026-10-28T00:00:00Z' }] })",
'governance price mock')
s = replace_once(s,
"  const saveRequest = page.waitForRequest((request) => request.url().includes('/api/governance/knowledge-bases/kb1/rag-profile') && request.method() === 'PUT')\n  await page.getByRole('button', { name: '保存 Profile' }).click()\n  await saveRequest",
"  await expect(page.getByRole('combobox', { name: 'Planner deployment' })).toBeVisible()\n  await expect(page.getByRole('combobox', { name: 'Answer deployment' })).toBeVisible()\n  const saveRequest = page.waitForRequest((request) => request.url().includes('/api/governance/knowledge-bases/kb1/rag-profile') && request.method() === 'PUT')\n  await page.getByRole('button', { name: '保存 Profile' }).click()\n  const saved = await saveRequest\n  const body = saved.postDataJSON() as Record<string, unknown>\n  expect(body.plannerDeploymentId).toBe('dep-planner')\n  expect(body.answerDeploymentId).toBe('dep-answer')\n  expect(body.reviewerDeploymentId).toBe('dep-planner')\n  expect(body.rerankerDeploymentId).toBe('dep-rerank')",
'governance routing e2e assertions')
s = replace_once(s,
"  await expect(page.getByText('strong-model')).toBeVisible()",
"  await expect(page.getByText('Answer Prod')).toBeVisible()",
'governance pricing identity assertion')
p.write_text(s)

# Tenant provider dialog e2e exposes explicit fallback control.
p = Path('src/KnowledgeRag.Admin/e2e/tenant-ai-providers.spec.ts')
s = p.read_text()
s = replace_once(s,
"  await expect(page.getByRole('combobox', { name: '类型' })).toBeVisible()\n  await page.getByRole('combobox', { name: '类型' }).click()",
"  await expect(page.getByRole('combobox', { name: '类型' })).toBeVisible()\n  await expect(page.getByRole('combobox', { name: 'Fallback deployment' })).toBeVisible()\n  await page.getByRole('combobox', { name: '类型' }).click()",
'tenant fallback e2e assertion')
p.write_text(s)

# Documentation: replace legacy model-routing section with deployment-first semantics.
p = Path('docs/RAG_AGENT_WORKFLOW.md')
s = p.read_text()
start = s.find('## Model routing\n')
end = s.find('## Workflow controls\n', start)
if start < 0 or end < 0:
    raise SystemExit('model routing docs boundary not found')
section = '''## Model routing\n\nRAG role routing is deployment-first. A Knowledge Base may bind Planner, Answer, Reviewer/Specialist, and Reranker to tenant-scoped Model Deployment IDs. Each run resolves those bindings once at request start into immutable endpoint snapshots.\n\nA Deployment owns provider connection identity, remote model name, health state, and an optional explicit same-type fallback. If the selected Deployment is disabled or persistently unhealthy, resolution may follow only that configured fallback chain; it never silently jumps to another Provider. Fallback cycles are rejected.\n\nWhen a KB role has no Deployment binding, routing falls back to the tenant default Deployment and then to the platform `AI` configuration. Existing raw `PlannerModel` / `AnswerModel` / `ReviewerModel` fields remain only as backward-compatible model-name overrides when no Deployment is bound.\n\nCost governance follows the same identity model: deployment-specific prices are preferred over legacy `(tenant, model-name)` prices, so two Providers exposing the same model string can be priced independently. Budget Reservation and usage Settle carry the resolved `DeploymentId` / `ProviderType` snapshot.\n\nRecommended production routing:\n\n- Planner: small/fast chat Deployment with reliable structured output.\n- Answer: strongest chat Deployment justified by quality/cost targets.\n- Reviewer/Specialists: chat Deployment optimized for structured verification.\n- Reranker: explicit rerank Deployment when LLM reranking is enabled.\n\nHealth checks are administrative observations, not an unconstrained runtime load balancer. A successful check stores `healthy`; a failed check stores `unhealthy`; edits that change provider/model/type/enablement reset health to `unknown`.\n\n'''
s = s[:start] + section + s[end:]
p.write_text(s)
