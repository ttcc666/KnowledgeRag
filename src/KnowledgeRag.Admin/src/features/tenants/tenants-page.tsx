import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Activity, Copy, Cpu, KeyRound, Pencil, Plus, Server, ShieldCheck, Trash2, Users } from 'lucide-react'
import { toast } from 'sonner'
import { api, getErrorMessage } from '@/lib/api'
import { formatDate } from '@/lib/utils'
import type { AiModelDeployment, AiModelType, AiProviderConnection, CreatedApiKey, TenantRole } from '@/lib/types'
import { Badge, Button, Card, Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, EmptyState, Input, Label, PageHeader, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui'

const allScopes = ['knowledge:read', 'knowledge:write', 'knowledge:admin', 'tenant:admin']

export function TenantsPage() {
  const qc = useQueryClient()
  const tenants = useQuery({ queryKey: ['tenants'], queryFn: api.tenants.list })
  const [selectedId, setSelectedId] = useState('')
  const selected = useMemo(() => tenants.data?.find((x) => x.id === selectedId) || tenants.data?.[0], [tenants.data, selectedId])
  const [createOpen, setCreateOpen] = useState(false)
  const [tenantName, setTenantName] = useState('')
  const create = useMutation({ mutationFn: () => api.tenants.create(tenantName), onSuccess: async (tenant) => { toast.success('租户已创建'); setSelectedId(tenant.id); setTenantName(''); setCreateOpen(false); await qc.invalidateQueries({ queryKey: ['tenants'] }) }, onError: (e) => toast.error(getErrorMessage(e)) })

  return <div className='space-y-7'>
    <PageHeader eyebrow='Team & access' title='租户与权限' description='Tenant 是知识库、成员、API Key 与 AI Provider 的隔离边界。把人员访问、机器访问和模型连接放在同一工作区管理。' actions={<Button onClick={() => setCreateOpen(true)}><Plus className='size-4' />新建租户</Button>} />
    {tenants.data?.length ? <div className='grid gap-5 xl:grid-cols-[280px_1fr]'>
      <Card className='h-fit bg-card/65 p-2'>
        <div className='px-3 pb-2 pt-2 text-[10px] font-semibold uppercase tracking-[.14em] text-muted-foreground'>Organizations</div>
        {tenants.data.map((tenant) => <button key={tenant.id} onClick={() => setSelectedId(tenant.id)} className={`mb-1 flex w-full items-center gap-3 rounded-xl p-3 text-left transition ${selected?.id === tenant.id ? 'bg-accent' : 'hover:bg-accent/45'}`}>
          <div className='flex size-9 items-center justify-center rounded-xl border bg-background text-xs font-semibold'>{tenant.name.slice(0, 1).toUpperCase()}</div>
          <div className='min-w-0 flex-1'><div className='truncate text-xs font-semibold'>{tenant.name}</div><div className='mt-0.5 truncate text-[10px] text-muted-foreground'>{tenant.slug}</div></div>
          <Badge variant='outline'>{tenant.role}</Badge>
        </button>)}
      </Card>
      {selected && <TenantWorkspace tenantId={selected.id} tenantRole={selected.role} />}
    </div> : !tenants.isLoading && <EmptyState icon={<Users className='size-5 text-primary' />} title='没有租户' description='创建组织租户，开始配置成员、API Key 与知识库隔离。' />}
    <Dialog open={createOpen} onOpenChange={setCreateOpen}><DialogContent><DialogHeader><DialogTitle>新建租户</DialogTitle><DialogDescription>建立一个新的组织级隔离边界。</DialogDescription></DialogHeader><div className='space-y-2'><Label>租户名称</Label><Input autoFocus value={tenantName} onChange={(e) => setTenantName(e.target.value)} placeholder='Acme AI Team' /></div><DialogFooter><Button variant='outline' onClick={() => setCreateOpen(false)}>取消</Button><Button disabled={!tenantName.trim() || create.isPending} onClick={() => create.mutate()}>创建</Button></DialogFooter></Dialog>
  </div>
}

function TenantWorkspace({ tenantId, tenantRole }: { tenantId: string; tenantRole: TenantRole }) {
  const [tab, setTab] = useState<'members' | 'keys' | 'ai'>('members')
  const canAdmin = tenantRole === 'Owner' || tenantRole === 'Admin'
  return <Card className='overflow-hidden bg-card/65'>
    <div className='flex gap-1 overflow-x-auto border-b px-3 pt-2'>
      <button className={`border-b-2 px-3 py-2.5 text-xs font-medium ${tab === 'members' ? 'border-primary text-foreground' : 'border-transparent text-muted-foreground'}`} onClick={() => setTab('members')}><Users className='mr-2 inline size-3.5' />Members</button>
      <button disabled={!canAdmin} className={`border-b-2 px-3 py-2.5 text-xs font-medium disabled:opacity-30 ${tab === 'keys' ? 'border-primary text-foreground' : 'border-transparent text-muted-foreground'}`} onClick={() => setTab('keys')}><KeyRound className='mr-2 inline size-3.5' />API Keys</button>
      <button className={`border-b-2 px-3 py-2.5 text-xs font-medium ${tab === 'ai' ? 'border-primary text-foreground' : 'border-transparent text-muted-foreground'}`} onClick={() => setTab('ai')}><Cpu className='mr-2 inline size-3.5' />AI Providers</button>
    </div>
    <div className='p-4 sm:p-5'>
      {tab === 'members' ? <TenantMembers tenantId={tenantId} canAdmin={canAdmin} /> : tab === 'keys' ? (canAdmin ? <ApiKeys tenantId={tenantId} /> : null) : <TenantAiProviders tenantId={tenantId} canAdmin={canAdmin} />}
    </div>
  </Card>
}

function TenantMembers({ tenantId, canAdmin }: { tenantId: string; canAdmin: boolean }) {
  const qc = useQueryClient()
  const [email, setEmail] = useState('')
  const [role, setRole] = useState<TenantRole>('Member')
  const members = useQuery({ queryKey: ['tenant-members', tenantId], queryFn: () => api.tenants.members(tenantId) })
  const save = useMutation({ mutationFn: () => api.tenants.saveMember(tenantId, email, role), onSuccess: async () => { toast.success('租户成员已更新'); setEmail(''); await qc.invalidateQueries({ queryKey: ['tenant-members', tenantId] }) }, onError: (e) => toast.error(getErrorMessage(e)) })
  const remove = useMutation({ mutationFn: (id: string) => api.tenants.removeMember(tenantId, id), onSuccess: async () => { toast.success('成员已移除'); await qc.invalidateQueries({ queryKey: ['tenant-members', tenantId] }) }, onError: (e) => toast.error(getErrorMessage(e)) })
  return <div className='space-y-5'>
    {canAdmin && <div className='grid gap-2 rounded-2xl border bg-muted/15 p-3 sm:grid-cols-[1fr_150px_auto]'><Input value={email} onChange={(e) => setEmail(e.target.value)} placeholder='member@example.com' /><Select value={role} onValueChange={(v) => setRole(v as TenantRole)}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value='Member'>Member</SelectItem><SelectItem value='Admin'>Admin</SelectItem><SelectItem value='Owner'>Owner</SelectItem></SelectContent></Select><Button disabled={!email.trim() || save.isPending} onClick={() => save.mutate()}><Plus className='size-4' />保存</Button></div>}
    <div className='divide-y rounded-2xl border bg-background/45'>{members.data?.map((member) => <div key={member.id} className='flex items-center gap-3 px-4 py-3.5'><div className='flex size-9 items-center justify-center rounded-full bg-primary/10 text-xs font-semibold text-primary'>{(member.displayName || member.email).slice(0, 1).toUpperCase()}</div><div className='min-w-0 flex-1'><div className='truncate text-xs font-medium'>{member.displayName}</div><div className='truncate text-[10px] text-muted-foreground'>{member.email} · {formatDate(member.addedAt)}</div></div><Badge variant='outline'>{member.role}</Badge>{canAdmin && <Button variant='ghost' size='icon-sm' onClick={() => { if (confirm(`确认移除 ${member.email}？`)) remove.mutate(member.id) }}><Trash2 className='size-3.5 text-destructive' /></Button>}</div>)}</div>
  </div>
}

function ApiKeys({ tenantId }: { tenantId: string }) {
  const qc = useQueryClient()
  const keys = useQuery({ queryKey: ['api-keys', tenantId], queryFn: () => api.tenants.apiKeys(tenantId) })
  const kbs = useQuery({ queryKey: ['knowledge-bases'], queryFn: api.knowledgeBases.list })
  const [open, setOpen] = useState(false)
  const [name, setName] = useState('')
  const [scopes, setScopes] = useState<string[]>(['knowledge:read'])
  const [kbId, setKbId] = useState('')
  const [created, setCreated] = useState<CreatedApiKey | null>(null)
  const create = useMutation({ mutationFn: () => api.tenants.createApiKey(tenantId, { name, scopes, knowledgeBaseId: kbId || null }), onSuccess: async (result) => { setCreated(result); setOpen(false); setName(''); setScopes(['knowledge:read']); setKbId(''); await qc.invalidateQueries({ queryKey: ['api-keys', tenantId] }) }, onError: (e) => toast.error(getErrorMessage(e)) })
  const revoke = useMutation({ mutationFn: (id: string) => api.tenants.revokeApiKey(tenantId, id), onSuccess: async () => { toast.success('API Key 已撤销'); await qc.invalidateQueries({ queryKey: ['api-keys', tenantId] }) }, onError: (e) => toast.error(getErrorMessage(e)) })
  const tenantKbs = kbs.data?.filter((x) => x.tenantId === tenantId) || []
  const toggleScope = (scope: string) => setScopes((current) => current.includes(scope) ? current.filter((x) => x !== scope) : [...current, scope])
  return <div className='space-y-4'>
    <div className='flex justify-between'><div><div className='text-xs font-semibold'>Machine access</div><div className='mt-1 text-[10px] text-muted-foreground'>使用 Scope 和知识库边界限制服务端访问。</div></div><Button size='sm' onClick={() => setOpen(true)}><KeyRound className='size-3.5' />创建 Key</Button></div>
    <div className='divide-y rounded-2xl border bg-background/45'>{keys.data?.length ? keys.data.map((key) => <div key={key.id} className='flex flex-col gap-3 px-4 py-3.5 sm:flex-row sm:items-center'><div className='min-w-0 flex-1'><div className='text-xs font-medium'>{key.name}</div><div className='mt-1 flex flex-wrap items-center gap-1.5'><code className='text-[10px] text-muted-foreground'>{key.keyPrefix}…</code>{key.scopes.map((scope) => <Badge key={scope} variant='outline'>{scope}</Badge>)}</div></div><div className='text-[10px] text-muted-foreground'>{key.knowledgeBaseId ? tenantKbs.find((x) => x.id === key.knowledgeBaseId)?.name || 'Knowledge base' : '整个租户'}</div><Button variant='ghost' size='icon-sm' onClick={() => { if (confirm(`撤销 ${key.name}？`)) revoke.mutate(key.id) }}><Trash2 className='size-3.5 text-destructive' /></Button></div>) : <div className='p-8 text-center text-xs text-muted-foreground'>还没有 API Key</div>}</div>
    <Dialog open={open} onOpenChange={setOpen}><DialogContent><DialogHeader><DialogTitle>创建 API Key</DialogTitle><DialogDescription>明文密钥只显示一次，数据库只保存哈希。</DialogDescription></DialogHeader><div className='space-y-4'><div className='space-y-2'><Label>名称</Label><Input value={name} onChange={(e) => setName(e.target.value)} placeholder='CI / Internal App' /></div><div className='space-y-2'><Label>Scopes</Label><div className='grid gap-2 sm:grid-cols-2'>{allScopes.map((scope) => <label key={scope} className='flex items-center gap-2 rounded-xl border p-3 text-xs'><input type='checkbox' checked={scopes.includes(scope)} onChange={() => toggleScope(scope)} />{scope}</label>)}</div></div><div className='space-y-2'><Label>知识库范围</Label><Select value={kbId || '__all'} onValueChange={(v) => setKbId(v === '__all' ? '' : v)}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value='__all'>整个租户</SelectItem>{tenantKbs.map((kb) => <SelectItem key={kb.id} value={kb.id}>{kb.name}</SelectItem>)}</SelectContent></Select></div></div><DialogFooter><Button variant='outline' onClick={() => setOpen(false)}>取消</Button><Button disabled={!name.trim() || !scopes.length || create.isPending} onClick={() => create.mutate()}>创建</Button></DialogFooter></DialogContent></Dialog>
    <Dialog open={!!created} onOpenChange={(v) => !v && setCreated(null)}><DialogContent><DialogHeader><DialogTitle className='flex items-center gap-2'><ShieldCheck className='size-5 text-success' />API Key 已创建</DialogTitle><DialogDescription>请立即复制，关闭后无法再次查看完整密钥。</DialogDescription></DialogHeader>{created && <div className='rounded-xl border bg-muted/40 p-4'><code className='break-all text-xs'>{created.apiKey}</code></div>}<DialogFooter><Button onClick={() => { if (created) { navigator.clipboard.writeText(created.apiKey); toast.success('已复制') } }}><Copy className='size-4' />复制密钥</Button></DialogFooter></DialogContent></Dialog>
  </div>
}

function TenantAiProviders({ tenantId, canAdmin }: { tenantId: string; canAdmin: boolean }) {
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

  const removeConnection = useMutation({ mutationFn: (id: string) => api.tenants.deleteAiConnection(tenantId, id), onSuccess: async () => { toast.success('Provider Connection 已删除'); await qc.invalidateQueries({ queryKey: ['ai-connections', tenantId] }) }, onError: (e) => toast.error(getErrorMessage(e)) })
  const testConnection = useMutation({ mutationFn: (id: string) => api.tenants.testAiConnection(tenantId, id), onSuccess: (result) => result.success ? toast.success(result.message) : toast.error(result.message), onError: (e) => toast.error(getErrorMessage(e)) })
  const removeDeployment = useMutation({ mutationFn: (id: string) => api.tenants.deleteAiDeployment(tenantId, id), onSuccess: async () => { toast.success('Model Deployment 已删除'); await Promise.all([qc.invalidateQueries({ queryKey: ['ai-deployments', tenantId] }), qc.invalidateQueries({ queryKey: ['ai-defaults', tenantId] })]) }, onError: (e) => toast.error(getErrorMessage(e)) })
  const saveDefaults = useMutation({ mutationFn: () => api.tenants.saveAiDefaults(tenantId, { defaultChatDeploymentId: chatDefault === '__none' ? null : chatDefault, defaultEmbeddingDeploymentId: embeddingDefault === '__none' ? null : embeddingDefault, defaultRerankerDeploymentId: rerankerDefault === '__none' ? null : rerankerDefault }), onSuccess: async () => { toast.success('租户默认模型已保存'); await qc.invalidateQueries({ queryKey: ['ai-defaults', tenantId] }) }, onError: (e) => toast.error(getErrorMessage(e)) })

  const byType = (type: AiModelType) => (deployments.data || []).filter((x) => x.modelType === type && x.isEnabled)
  return <div className='space-y-6'>
    <div className='rounded-2xl border bg-muted/15 p-4 text-xs text-muted-foreground'>
      <div className='font-medium text-foreground'>Tenant AI Control Plane</div>
      <div className='mt-1 leading-5'>Provider 凭据按租户加密存储且不会返回前端。租户自定义 Base URL 必须是公网 HTTPS，并经过 SSRF 安全连接策略。Chat / Planner / Answer / Reviewer 会使用租户默认 Chat Deployment；Embedding 默认值在本阶段只作为后续安全重建索引的目标，不会直接切换已有向量空间。</div>
    </div>

    <section className='space-y-3'>
      <div className='flex items-center justify-between gap-3'><div><div className='text-xs font-semibold'>Provider Connections</div><div className='mt-1 text-[10px] text-muted-foreground'>同一租户可以配置多个 OpenAI-compatible 账号或 Gateway。</div></div>{canAdmin && <Button size='sm' onClick={() => { setEditingConnection(null); setConnectionOpen(true) }}><Plus className='size-3.5' />添加连接</Button>}</div>
      <div className='grid gap-3 lg:grid-cols-2'>{connections.data?.length ? connections.data.map((connection) => <div key={connection.id} className='rounded-2xl border bg-background/45 p-4'><div className='flex items-start gap-3'><div className='flex size-9 shrink-0 items-center justify-center rounded-xl border bg-muted/30'><Server className='size-4 text-primary' /></div><div className='min-w-0 flex-1'><div className='flex flex-wrap items-center gap-2'><span className='text-xs font-semibold'>{connection.name}</span><Badge variant='outline'>{connection.providerType}</Badge>{connection.isEnabled ? <Badge>Enabled</Badge> : <Badge variant='outline'>Disabled</Badge>}</div><div className='mt-1 truncate text-[10px] text-muted-foreground'>{connection.baseUrl}</div><div className='mt-2 text-[10px] text-muted-foreground'>Credential {connection.hasCredential ? 'configured' : 'not configured'} · revision {connection.revision}</div></div></div>{canAdmin && <div className='mt-3 flex flex-wrap gap-2'><Button size='sm' variant='outline' disabled={testConnection.isPending} onClick={() => testConnection.mutate(connection.id)}><Activity className='size-3.5' />测试</Button><Button size='sm' variant='outline' onClick={() => { setEditingConnection(connection); setConnectionOpen(true) }}><Pencil className='size-3.5' />编辑</Button><Button size='sm' variant='ghost' onClick={() => { if (confirm(`删除 Provider Connection “${connection.name}”？`)) removeConnection.mutate(connection.id) }}><Trash2 className='size-3.5 text-destructive' />删除</Button></div>}</div>) : <div className='col-span-full rounded-2xl border border-dashed p-7 text-center text-xs text-muted-foreground'>还没有 Tenant AI Provider Connection。未配置时继续使用平台级 AI fallback。</div>}</div>
    </section>

    <section className='space-y-3'>
      <div className='flex items-center justify-between gap-3'><div><div className='text-xs font-semibold'>Model Deployments</div><div className='mt-1 text-[10px] text-muted-foreground'>Deployment 把模型名绑定到具体 Provider Connection，避免只用裸 model string 识别供应商。</div></div>{canAdmin && <Button size='sm' variant='outline' disabled={!connections.data?.length} onClick={() => { setEditingDeployment(null); setDeploymentOpen(true) }}><Plus className='size-3.5' />添加模型</Button>}</div>
      <div className='divide-y rounded-2xl border bg-background/45'>{deployments.data?.length ? deployments.data.map((deployment) => <div key={deployment.id} className='flex flex-col gap-3 px-4 py-3.5 sm:flex-row sm:items-center'><div className='min-w-0 flex-1'><div className='flex flex-wrap items-center gap-2'><span className='text-xs font-medium'>{deployment.displayName}</span><Badge variant='outline'>{deployment.modelType}</Badge>{deployment.isEnabled ? null : <Badge variant='outline'>Disabled</Badge>}</div><div className='mt-1 text-[10px] text-muted-foreground'>{deployment.connectionName} · <code>{deployment.remoteModelName}</code>{deployment.embeddingDimensions ? ` · ${deployment.embeddingDimensions} dimensions` : ''}</div></div>{canAdmin && <div className='flex gap-1'><Button variant='ghost' size='icon-sm' onClick={() => { setEditingDeployment(deployment); setDeploymentOpen(true) }}><Pencil className='size-3.5' /></Button><Button variant='ghost' size='icon-sm' onClick={() => { if (confirm(`删除 Model Deployment “${deployment.displayName}”？`)) removeDeployment.mutate(deployment.id) }}><Trash2 className='size-3.5 text-destructive' /></Button></div>}</div>) : <div className='p-7 text-center text-xs text-muted-foreground'>还没有 Model Deployment。</div>}</div>
    </section>

    <section className='space-y-3'>
      <div><div className='text-xs font-semibold'>Tenant Defaults</div><div className='mt-1 text-[10px] text-muted-foreground'>KB 未覆盖模型时使用这些默认值。Embedding 的实际激活仍由版本化 Reindex 控制。</div></div>
      <div className='grid gap-3 rounded-2xl border bg-muted/10 p-4 lg:grid-cols-3'>
        <DefaultDeploymentSelect label='Chat' value={chatDefault} onChange={setChatDefault} deployments={byType('chat')} disabled={!canAdmin} />
        <DefaultDeploymentSelect label='Embedding target' value={embeddingDefault} onChange={setEmbeddingDefault} deployments={byType('embedding')} disabled={!canAdmin} />
        <DefaultDeploymentSelect label='Reranker' value={rerankerDefault} onChange={setRerankerDefault} deployments={byType('rerank')} disabled={!canAdmin} />
        {canAdmin && <div className='lg:col-span-3 flex justify-end'><Button size='sm' disabled={saveDefaults.isPending} onClick={() => saveDefaults.mutate()}>保存默认模型</Button></div>}
      </div>
    </section>

    <ConnectionDialog tenantId={tenantId} open={connectionOpen} onOpenChange={setConnectionOpen} connection={editingConnection} providers={providers.data || []} />
    <DeploymentDialog tenantId={tenantId} open={deploymentOpen} onOpenChange={setDeploymentOpen} deployment={editingDeployment} connections={connections.data || []} />
  </div>
}

function DefaultDeploymentSelect({ label, value, onChange, deployments, disabled }: { label: string; value: string; onChange: (value: string) => void; deployments: AiModelDeployment[]; disabled: boolean }) {
  return <div className='space-y-2'><Label>{label}</Label><Select disabled={disabled} value={value} onValueChange={onChange}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value='__none'>Platform fallback / none</SelectItem>{deployments.map((deployment) => <SelectItem key={deployment.id} value={deployment.id}>{deployment.displayName} · {deployment.connectionName}</SelectItem>)}</SelectContent></Select></div>
}

function ConnectionDialog({ tenantId, open, onOpenChange, connection, providers }: { tenantId: string; open: boolean; onOpenChange: (open: boolean) => void; connection: AiProviderConnection | null; providers: { id: string; name: string }[] }) {
  const qc = useQueryClient()
  const [name, setName] = useState('')
  const [providerType, setProviderType] = useState('openai-compatible')
  const [baseUrl, setBaseUrl] = useState('https://api.openai.com/v1')
  const [credential, setCredential] = useState('')
  const [enabled, setEnabled] = useState(true)
  useEffect(() => { if (!open) return; setName(connection?.name || ''); setProviderType(connection?.providerType || providers[0]?.id || 'openai-compatible'); setBaseUrl(connection?.baseUrl || 'https://api.openai.com/v1'); setCredential(''); setEnabled(connection?.isEnabled ?? true) }, [open, connection, providers])
  const save = useMutation({ mutationFn: () => api.tenants.saveAiConnection(tenantId, { id: connection?.id || null, name, providerType, baseUrl, credential: credential || null, isEnabled: enabled }), onSuccess: async () => { toast.success(connection ? 'Provider Connection 已更新' : 'Provider Connection 已创建'); onOpenChange(false); await qc.invalidateQueries({ queryKey: ['ai-connections', tenantId] }) }, onError: (e) => toast.error(getErrorMessage(e)) })
  return <Dialog open={open} onOpenChange={onOpenChange}><DialogContent><DialogHeader><DialogTitle>{connection ? '编辑 Provider Connection' : '添加 Provider Connection'}</DialogTitle><DialogDescription>凭据仅写入服务端加密存储。编辑时留空不会覆盖已有凭据。</DialogDescription></DialogHeader><div className='space-y-4'><div className='space-y-2'><Label>名称</Label><Input value={name} onChange={(e) => setName(e.target.value)} placeholder='OpenAI Production' /></div><div className='space-y-2'><Label>Provider</Label><Select value={providerType} onValueChange={setProviderType}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent>{providers.length ? providers.map((provider) => <SelectItem key={provider.id} value={provider.id}>{provider.name}</SelectItem>) : <SelectItem value='openai-compatible'>OpenAI Compatible</SelectItem>}</SelectContent></Select></div><div className='space-y-2'><Label>Base URL</Label><Input value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} placeholder='https://api.openai.com/v1' /><p className='text-[10px] text-muted-foreground'>Tenant-managed URL 必须是公网 HTTPS；localhost、私网、link-local 等会被拒绝。</p></div><div className='space-y-2'><Label>API Key / Credential</Label><Input type='password' autoComplete='new-password' value={credential} onChange={(e) => setCredential(e.target.value)} placeholder={connection?.hasCredential ? '留空以保留当前凭据' : 'sk-…'} /></div><label className='flex items-center gap-2 text-xs'><input type='checkbox' checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />启用此连接</label></div><DialogFooter><Button variant='outline' onClick={() => onOpenChange(false)}>取消</Button><Button disabled={!name.trim() || !baseUrl.trim() || save.isPending} onClick={() => save.mutate()}>保存</Button></DialogFooter></DialogContent></Dialog>
}

function DeploymentDialog({ tenantId, open, onOpenChange, deployment, connections }: { tenantId: string; open: boolean; onOpenChange: (open: boolean) => void; deployment: AiModelDeployment | null; connections: AiProviderConnection[] }) {
  const qc = useQueryClient()
  const [connectionId, setConnectionId] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [remoteModelName, setRemoteModelName] = useState('')
  const [modelType, setModelType] = useState<AiModelType>('chat')
  const [dimensions, setDimensions] = useState('')
  const [enabled, setEnabled] = useState(true)
  useEffect(() => { if (!open) return; setConnectionId(deployment?.connectionId || connections.find((x) => x.isEnabled)?.id || connections[0]?.id || ''); setDisplayName(deployment?.displayName || ''); setRemoteModelName(deployment?.remoteModelName || ''); setModelType(deployment?.modelType || 'chat'); setDimensions(deployment?.embeddingDimensions ? String(deployment.embeddingDimensions) : ''); setEnabled(deployment?.isEnabled ?? true) }, [open, deployment, connections])
  const save = useMutation({ mutationFn: () => api.tenants.saveAiDeployment(tenantId, { id: deployment?.id || null, connectionId, displayName, remoteModelName, modelType, embeddingDimensions: modelType === 'embedding' ? Number(dimensions) || null : null, isEnabled: enabled }), onSuccess: async () => { toast.success(deployment ? 'Model Deployment 已更新' : 'Model Deployment 已创建'); onOpenChange(false); await qc.invalidateQueries({ queryKey: ['ai-deployments', tenantId] }) }, onError: (e) => toast.error(getErrorMessage(e)) })
  return <Dialog open={open} onOpenChange={onOpenChange}><DialogContent><DialogHeader><DialogTitle>{deployment ? '编辑 Model Deployment' : '添加 Model Deployment'}</DialogTitle><DialogDescription>模型名与具体 Provider Connection 绑定，运行时按 Deployment 解析。</DialogDescription></DialogHeader><div className='space-y-4'><div className='space-y-2'><Label>Provider Connection</Label><Select value={connectionId} onValueChange={setConnectionId}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent>{connections.map((connection) => <SelectItem key={connection.id} value={connection.id}>{connection.name}</SelectItem>)}</SelectContent></Select></div><div className='space-y-2'><Label>显示名称</Label><Input value={displayName} onChange={(e) => setDisplayName(e.target.value)} placeholder='GPT-4.1 Production' /></div><div className='space-y-2'><Label>远端模型名</Label><Input value={remoteModelName} onChange={(e) => setRemoteModelName(e.target.value)} placeholder='gpt-4.1' /></div><div className='space-y-2'><Label>类型</Label><Select value={modelType} onValueChange={(value) => setModelType(value as AiModelType)}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value='chat'>Chat</SelectItem><SelectItem value='embedding'>Embedding</SelectItem><SelectItem value='rerank'>Rerank</SelectItem></SelectContent></Select></div>{modelType === 'embedding' && <div className='space-y-2'><Label>Embedding dimensions</Label><Input inputMode='numeric' value={dimensions} onChange={(e) => setDimensions(e.target.value.replace(/\D/g, ''))} placeholder='1536' /><p className='text-[10px] text-muted-foreground'>本 PR 只保存目标配置；实际切换会在版本化 Reindex 中校验物理向量维度。</p></div>}<label className='flex items-center gap-2 text-xs'><input type='checkbox' checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />启用此 Deployment</label></div><DialogFooter><Button variant='outline' onClick={() => onOpenChange(false)}>取消</Button><Button disabled={!connectionId || !displayName.trim() || !remoteModelName.trim() || (modelType === 'embedding' && !Number(dimensions)) || save.isPending} onClick={() => save.mutate()}>保存</Button></DialogFooter></DialogContent></Dialog>
}
