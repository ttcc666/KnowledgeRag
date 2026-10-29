import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Activity, Bot, Brain, CheckCircle2, CircleDollarSign, FlaskConical, History, Network, Play,
  Plus, RefreshCw, Save, Send, ShieldCheck, SlidersHorizontal, Trash2, Webhook,
} from 'lucide-react'
import { toast } from 'sonner'
import { api, getErrorMessage } from '@/lib/api'
import { formatDate } from '@/lib/utils'
import type { AiModelDeployment, PromptProfile, RagProfileUpdate, RagRun } from '@/lib/types'
import {
  Badge, Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label, PageHeader,
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Skeleton, Textarea,
} from '@/components/ui'

type Tab = 'evaluation' | 'profile' | 'runs' | 'cost' | 'audit' | 'prompts' | 'automation' | 'agents'

const tabs: { id: Tab; label: string; icon: typeof Activity }[] = [
  { id: 'evaluation', label: 'Evaluation', icon: FlaskConical },
  { id: 'profile', label: 'RAG 配置', icon: SlidersHorizontal },
  { id: 'runs', label: 'Run Trace', icon: History },
  { id: 'cost', label: '成本 / 模型', icon: CircleDollarSign },
  { id: 'audit', label: 'Audit Log', icon: ShieldCheck },
  { id: 'prompts', label: 'Prompt / Agent', icon: Brain },
  { id: 'automation', label: 'Automation', icon: Webhook },
  { id: 'agents', label: 'Multi Agent', icon: Network },
]

export function GovernancePage() {
  const [tab, setTab] = useState<Tab>('evaluation')
  const tenants = useQuery({ queryKey: ['tenants'], queryFn: api.tenants.list })
  const knowledgeBases = useQuery({ queryKey: ['knowledge-bases'], queryFn: api.knowledgeBases.list })
  const [tenantId, setTenantId] = useState('')
  const [knowledgeBaseId, setKnowledgeBaseId] = useState('')

  useEffect(() => { if (!tenantId && tenants.data?.length) setTenantId(tenants.data[0].id) }, [tenantId, tenants.data])
  const tenantKnowledgeBases = useMemo(() => knowledgeBases.data?.filter((kb) => !tenantId || kb.tenantId === tenantId) || [], [knowledgeBases.data, tenantId])
  useEffect(() => {
    if (!tenantKnowledgeBases.some((kb) => kb.id === knowledgeBaseId)) setKnowledgeBaseId(tenantKnowledgeBases[0]?.id || '')
  }, [knowledgeBaseId, tenantKnowledgeBases])

  const overview = useQuery({ queryKey: ['governance-overview', tenantId], queryFn: () => api.governance.overview(tenantId), enabled: !!tenantId })

  return <div className='space-y-6'>
    <PageHeader
      eyebrow='Operate & Govern'
      title='AI Governance Center'
      description='统一管理 RAG 质量评测、知识库运行策略、Trace、成本、Prompt、审计、Webhook 与 Specialist Agents。'
      actions={<div className='flex flex-wrap gap-2'><div className='w-44'><Select value={tenantId} onValueChange={setTenantId}><SelectTrigger><SelectValue placeholder='选择租户' /></SelectTrigger><SelectContent>{tenants.data?.map((tenant) => <SelectItem key={tenant.id} value={tenant.id}>{tenant.name}</SelectItem>)}</SelectContent></Select></div><div className='w-52'><Select value={knowledgeBaseId} onValueChange={setKnowledgeBaseId}><SelectTrigger><SelectValue placeholder='选择知识库' /></SelectTrigger><SelectContent>{tenantKnowledgeBases.map((kb) => <SelectItem key={kb.id} value={kb.id}>{kb.name}</SelectItem>)}</SelectContent></Select></div></div>}
    />

    <div className='grid gap-3 sm:grid-cols-2 xl:grid-cols-4'>
      <MetricCard label='RAG Runs' value={overview.data?.runCount} detail='持久化运行记录' />
      <MetricCard label='Avg latency' value={overview.data ? `${Math.round(overview.data.averageLatencyMs)} ms` : undefined} detail='端到端平均耗时' />
      <MetricCard label='Estimated cost' value={overview.data ? `$${overview.data.estimatedCostUsd.toFixed(4)}` : undefined} detail='当前租户累计估算' />
      <MetricCard label='Governance' value={overview.data ? `${overview.data.evaluationRunCount} eval / ${overview.data.activeWebhookCount} hooks` : undefined} detail='评测与自动化' />
    </div>

    <div className='flex gap-1 overflow-x-auto rounded-2xl border bg-muted/20 p-1.5'>
      {tabs.map(({ id, label, icon: Icon }) => <button key={id} onClick={() => setTab(id)} className={`inline-flex h-9 shrink-0 items-center gap-2 rounded-xl px-3 text-xs font-medium transition ${tab === id ? 'bg-background text-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground'}`}><Icon className='size-3.5' />{label}</button>)}
    </div>

    {!tenantId ? <Card className='p-8 text-center text-sm text-muted-foreground'>请先选择租户。</Card> : <>
      {tab === 'evaluation' && <EvaluationPanel tenantId={tenantId} knowledgeBaseId={knowledgeBaseId} />}
      {tab === 'profile' && <RagProfilePanel knowledgeBaseId={knowledgeBaseId} tenantId={tenantId} />}
      {tab === 'runs' && <RunsPanel knowledgeBaseId={knowledgeBaseId} />}
      {tab === 'cost' && <CostPanel tenantId={tenantId} />}
      {tab === 'audit' && <AuditPanel tenantId={tenantId} />}
      {tab === 'prompts' && <PromptPanel tenantId={tenantId} knowledgeBaseId={knowledgeBaseId} />}
      {tab === 'automation' && <AutomationPanel tenantId={tenantId} />}
      {tab === 'agents' && <AgentsPanel knowledgeBaseId={knowledgeBaseId} />}
    </>}
  </div>
}

function MetricCard({ label, value, detail }: { label: string; value?: string | number; detail: string }) {
  return <Card className='p-4'><div className='text-[10px] font-semibold uppercase tracking-[.12em] text-muted-foreground'>{label}</div><div className='mt-2 text-2xl font-semibold tabular-nums'>{value === undefined ? <Skeleton className='h-7 w-24' /> : value}</div><div className='mt-1 text-xs text-muted-foreground'>{detail}</div></Card>
}

function EvaluationPanel({ tenantId, knowledgeBaseId }: { tenantId: string; knowledgeBaseId: string }) {
  const qc = useQueryClient()
  const datasets = useQuery({ queryKey: ['evaluation-datasets', tenantId], queryFn: () => api.governance.datasets(tenantId) })
  const runs = useQuery({ queryKey: ['evaluation-runs', tenantId], queryFn: () => api.governance.evaluationRuns(tenantId) })
  const [datasetId, setDatasetId] = useState('')
  const [name, setName] = useState('Production regression')
  const [query, setQuery] = useState('')
  const [expected, setExpected] = useState('')
  useEffect(() => { if (!datasetId && datasets.data?.length) setDatasetId(datasets.data[0].id) }, [datasetId, datasets.data])
  const cases = useQuery({ queryKey: ['evaluation-cases', tenantId, datasetId], queryFn: () => api.governance.cases(tenantId, datasetId), enabled: !!datasetId })
  const createDataset = useMutation({ mutationFn: () => api.governance.createDataset(tenantId, { knowledgeBaseId, name, description: 'Created from AI Governance Center' }), onSuccess: async (dataset) => { setDatasetId(dataset.id); await qc.invalidateQueries({ queryKey: ['evaluation-datasets', tenantId] }); toast.success('Dataset 已创建') }, onError: (error) => toast.error(getErrorMessage(error)) })
  const addCase = useMutation({ mutationFn: () => api.governance.addCase(tenantId, datasetId, { category: 'fact', query, expectedAnswerContains: expected || undefined, requiresKnowledge: true }), onSuccess: async () => { setQuery(''); setExpected(''); await qc.invalidateQueries({ queryKey: ['evaluation-cases', tenantId, datasetId] }); toast.success('测试用例已加入') }, onError: (error) => toast.error(getErrorMessage(error)) })
  const run = useMutation({ mutationFn: () => api.governance.runEvaluation(tenantId, datasetId), onSuccess: async () => { await Promise.all([qc.invalidateQueries({ queryKey: ['evaluation-runs', tenantId] }), qc.invalidateQueries({ queryKey: ['governance-overview', tenantId] })]); toast.success('Evaluation 完成') }, onError: (error) => toast.error(getErrorMessage(error)) })

  return <div className='grid gap-4 xl:grid-cols-[.85fr_1.15fr]'>
    <div className='space-y-4'>
      <Card><CardHeader><CardTitle className='text-sm'>Evaluation Dataset</CardTitle><CardDescription>把真实失败案例沉淀为可重复运行的回归集。</CardDescription></CardHeader><CardContent className='space-y-3'><div className='flex gap-2'><Input value={name} onChange={(e) => setName(e.target.value)} placeholder='Dataset name' /><Button disabled={!knowledgeBaseId || !name.trim() || createDataset.isPending} onClick={() => createDataset.mutate()}><Plus className='size-3.5' />创建</Button></div>{datasets.data?.length ? <Select value={datasetId} onValueChange={setDatasetId}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent>{datasets.data.map((dataset) => <SelectItem key={dataset.id} value={dataset.id}>{dataset.name}</SelectItem>)}</SelectContent></Select> : <div className='text-xs text-muted-foreground'>还没有 Dataset。</div>}</CardContent></Card>
      <Card><CardHeader><CardTitle className='text-sm'>新增 Case</CardTitle><CardDescription>期望文本可以留空，只验证 grounding / no-answer 基础规则。</CardDescription></CardHeader><CardContent className='space-y-3'><Textarea value={query} onChange={(e) => setQuery(e.target.value)} placeholder='测试问题' /><Input value={expected} onChange={(e) => setExpected(e.target.value)} placeholder='答案应包含（可选）' /><Button disabled={!datasetId || !query.trim() || addCase.isPending} onClick={() => addCase.mutate()}>加入测试集</Button></CardContent></Card>
    </div>
    <div className='space-y-4'>
      <Card><CardHeader className='flex-row items-center justify-between space-y-0'><div><CardTitle className='text-sm'>Regression Run</CardTitle><CardDescription>{cases.data?.length || 0} cases · 在线调用当前 KB Profile</CardDescription></div><Button disabled={!datasetId || !cases.data?.length || run.isPending} onClick={() => run.mutate()}>{run.isPending ? <RefreshCw className='size-3.5 animate-spin' /> : <Play className='size-3.5' />}运行评测</Button></CardHeader><CardContent>{cases.data?.length ? <div className='space-y-2'>{cases.data.slice(0, 8).map((item) => <div key={item.id} className='rounded-xl border p-3 text-xs'><div className='font-medium'>{item.query}</div><div className='mt-1 text-muted-foreground'>{item.category}{item.expectedAnswerContains ? ` · expects “${item.expectedAnswerContains}”` : ''}</div></div>)}</div> : <div className='text-sm text-muted-foreground'>添加测试用例后即可运行。</div>}</CardContent></Card>
      <Card><CardHeader><CardTitle className='text-sm'>Recent evaluation runs</CardTitle></CardHeader><CardContent className='space-y-2'>{runs.data?.slice(0, 8).map((item) => <div key={item.id} className='grid grid-cols-[1fr_auto] gap-3 rounded-xl border p-3'><div><div className='flex items-center gap-2 text-xs font-medium'><Badge variant={item.status === 'completed' ? 'success' : item.status === 'failed' ? 'destructive' : 'warning'}>{item.status}</Badge>{item.passedCount}/{item.caseCount} passed</div><div className='mt-1 text-[10px] text-muted-foreground'>{formatDate(item.createdAt)} · {Math.round(item.averageLatencyMs)} ms avg</div></div><div className='text-right'><div className='text-sm font-semibold'>{Math.round(item.passRate * 100)}%</div><div className='text-[10px] text-muted-foreground'>grounded {Math.round(item.groundednessRate * 100)}%</div></div></div>) || <div className='text-xs text-muted-foreground'>暂无运行记录。</div>}</CardContent></Card>
    </div>
  </div>
}

function RagProfilePanel({ knowledgeBaseId, tenantId }: { knowledgeBaseId: string; tenantId: string }) {
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
}

function RunsPanel({ knowledgeBaseId }: { knowledgeBaseId: string }) {
  const runs = useQuery({ queryKey: ['rag-runs', knowledgeBaseId], queryFn: () => api.governance.runs(knowledgeBaseId), enabled: !!knowledgeBaseId })
  const [selected, setSelected] = useState<RagRun | null>(null)
  const trace = useMemo(() => { try { return selected ? JSON.parse(selected.traceJson) as Array<Record<string, unknown>> : [] } catch { return [] } }, [selected])
  return <div className='grid gap-4 xl:grid-cols-[1.05fr_.95fr]'><Card><CardHeader><CardTitle className='text-sm'>RAG Run history</CardTitle><CardDescription>每一轮 Chat 都保存模型、耗时、成本和前端看到的同源 Workflow Trace。</CardDescription></CardHeader><CardContent className='space-y-2'>{runs.data?.map((run) => <button key={run.id} onClick={() => setSelected(run)} className='grid w-full grid-cols-[1fr_auto] gap-3 rounded-xl border p-3 text-left transition hover:border-primary/30 hover:bg-primary/3'><div className='min-w-0'><div className='truncate text-xs font-medium'>{run.query}</div><div className='mt-1 flex flex-wrap gap-2 text-[10px] text-muted-foreground'><span>{formatDate(run.createdAt)}</span><span>{run.answerModel}</span><span>{run.agentMode}</span><span>{run.sourceCount} sources</span></div></div><div className='text-right'><Badge variant={run.status === 'completed' ? 'success' : run.status === 'failed' ? 'destructive' : 'warning'}>{run.status}</Badge><div className='mt-1 text-[10px] tabular-nums text-muted-foreground'>{run.latencyMs} ms · ${run.estimatedCostUsd.toFixed(5)}</div></div></button>) || <div className='text-xs text-muted-foreground'>暂无 Run。</div>}</CardContent></Card><Card><CardHeader><CardTitle className='text-sm'>Trace inspector</CardTitle><CardDescription>{selected ? `${selected.id.slice(0, 8)} · ${trace.length} events` : '选择一条 Run 查看完整结构化 Trace。'}</CardDescription></CardHeader><CardContent>{selected ? <div className='space-y-2'>{trace.map((event, index) => <div key={String(event.stepId || index) + index} className='rounded-xl border p-3'><div className='flex items-center justify-between gap-3 text-xs'><span className='font-medium'>{String(event.label || event.type || 'event')}</span><span className='text-[10px] text-muted-foreground'>{String(event.status || '')}</span></div><div className='mt-1 text-[10px] text-muted-foreground'>{String(event.actor || '')}{typeof event.elapsedMs === 'number' ? ` · ${event.elapsedMs} ms` : ''}</div></div>)}</div> : <div className='text-sm text-muted-foreground'>Trace 不包含 chain-of-thought；只保存安全的执行状态、Agent/Tool、耗时和检索统计。</div>}</CardContent></Card></div>
}

function CostPanel({ tenantId }: { tenantId: string }) {
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
}

function AuditPanel({ tenantId }: { tenantId: string }) {
  const audit = useQuery({ queryKey: ['audit-log', tenantId], queryFn: () => api.governance.audit(tenantId) })
  return <Card><CardHeader><CardTitle className='text-sm'>Audit Log</CardTitle><CardDescription>自动记录经过认证的 API mutation；审计写入失败不会改变原业务请求结果。</CardDescription></CardHeader><CardContent className='overflow-x-auto'><table className='w-full min-w-[760px] text-left text-xs'><thead><tr className='border-b text-muted-foreground'><th className='pb-2 font-medium'>Time</th><th className='pb-2 font-medium'>Action</th><th className='pb-2 font-medium'>Resource</th><th className='pb-2 font-medium'>Summary</th><th className='pb-2 font-medium'>Status</th><th className='pb-2 font-medium'>IP</th></tr></thead><tbody>{audit.data?.map((row) => <tr key={row.id} className='border-b last:border-0'><td className='py-3'>{formatDate(row.createdAt)}</td><td>{row.action}</td><td>{row.resourceType}</td><td className='max-w-80 truncate'>{row.summary}</td><td><Badge variant={row.statusCode < 400 ? 'success' : 'destructive'}>{row.statusCode}</Badge></td><td className='text-muted-foreground'>{row.ipAddress || '—'}</td></tr>)}</tbody></table></CardContent></Card>
}

function PromptPanel({ tenantId, knowledgeBaseId }: { tenantId: string; knowledgeBaseId: string }) {
  const qc = useQueryClient(); const prompts = useQuery({ queryKey: ['prompt-profiles', tenantId], queryFn: () => api.governance.prompts(tenantId) }); const [name, setName] = useState('Production RAG'); const [planner, setPlanner] = useState(''); const [answer, setAnswer] = useState(''); const [reviewer, setReviewer] = useState('')
  const save = useMutation({ mutationFn: () => api.governance.savePrompt(tenantId, { knowledgeBaseId: knowledgeBaseId || null, name, plannerPrompt: planner, answerPrompt: answer, reviewerPrompt: reviewer, isActive: true }), onSuccess: async () => { await qc.invalidateQueries({ queryKey: ['prompt-profiles', tenantId] }); toast.success('Prompt Profile 新版本已保存') }, onError: (error) => toast.error(getErrorMessage(error)) })
  const load = (profile: PromptProfile) => { setName(profile.name); setPlanner(profile.plannerPrompt); setAnswer(profile.answerPrompt); setReviewer(profile.reviewerPrompt) }
  return <div className='grid gap-4 xl:grid-cols-[.7fr_1.3fr]'><Card><CardHeader><CardTitle className='text-sm'>Versioned Profiles</CardTitle><CardDescription>激活新版本不会覆盖系统安全与 grounding 基线。</CardDescription></CardHeader><CardContent className='space-y-2'>{prompts.data?.map((profile) => <button key={profile.id} onClick={() => load(profile)} className='w-full rounded-xl border p-3 text-left hover:border-primary/30'><div className='flex items-center justify-between'><span className='text-xs font-medium'>{profile.name}</span><Badge variant={profile.isActive ? 'success' : 'outline'}>v{profile.version}</Badge></div><div className='mt-1 text-[10px] text-muted-foreground'>{profile.knowledgeBaseId ? 'Knowledge Base scoped' : 'Tenant scoped'} · {formatDate(profile.updatedAt)}</div></button>)}</CardContent></Card><Card><CardHeader><CardTitle className='text-sm'>Prompt / Agent policy</CardTitle><CardDescription>这里只写管理员附加策略；不可替换平台不可变安全规则。</CardDescription></CardHeader><CardContent className='space-y-3'><Field label='Profile name'><Input value={name} onChange={(e) => setName(e.target.value)} /></Field><Field label='Planner additional policy'><Textarea value={planner} onChange={(e) => setPlanner(e.target.value)} placeholder='例如：优先生成保留产品编号的检索 query' /></Field><Field label='Answer additional policy'><Textarea value={answer} onChange={(e) => setAnswer(e.target.value)} placeholder='例如：回答先给结论，再给依据和风险' /></Field><Field label='Reviewer additional policy'><Textarea value={reviewer} onChange={(e) => setReviewer(e.target.value)} placeholder='例如：政策日期必须有明确证据才通过' /></Field><div className='flex justify-end'><Button disabled={!name.trim() || save.isPending} onClick={() => save.mutate()}><Save className='size-3.5' />保存新版本</Button></div></CardContent></Card></div>
}

function AutomationPanel({ tenantId }: { tenantId: string }) {
  const qc = useQueryClient()
  const providers = useQuery({ queryKey: ['webhook-providers'], queryFn: () => api.governance.webhookProviders() })
  const hooks = useQuery({ queryKey: ['webhooks', tenantId], queryFn: () => api.governance.webhooks(tenantId) })
  const [name, setName] = useState('Operations webhook')
  const [provider, setProvider] = useState('custom')
  const [url, setUrl] = useState('')
  const [events, setEvents] = useState('rag.run.completed,evaluation.completed')
  const [secret, setSecret] = useState('')
  const selectedProvider = providers.data?.find((item) => item.id === provider)
  const save = useMutation({
    mutationFn: () => api.governance.saveWebhook(tenantId, { name, provider, url, eventTypes: events.split(',').map((x) => x.trim()).filter(Boolean), secret: secret || undefined, isActive: true }),
    onSuccess: async () => { setUrl(''); setSecret(''); await qc.invalidateQueries({ queryKey: ['webhooks', tenantId] }); toast.success('通信渠道已保存') },
    onError: (error) => toast.error(getErrorMessage(error)),
  })
  const remove = useMutation({ mutationFn: (id: string) => api.governance.deleteWebhook(tenantId, id), onSuccess: async () => { await qc.invalidateQueries({ queryKey: ['webhooks', tenantId] }); toast.success('通信渠道已删除') } })
  const test = useMutation({ mutationFn: (id: string) => api.governance.testWebhook(tenantId, id), onSuccess: async () => { await qc.invalidateQueries({ queryKey: ['webhooks', tenantId] }); toast.success('测试消息已发送') }, onError: (error) => toast.error(getErrorMessage(error)) })
  return <div className='grid gap-4 xl:grid-cols-[.85fr_1.15fr]'>
    <Card>
      <CardHeader><CardTitle className='text-sm'>Webhook Automation · 通信渠道</CardTitle><CardDescription>选择平台并填写机器人 Webhook URL 即可接入。常见平台自动转换消息格式；自定义 Webhook 保留原始 JSON 与 HMAC-SHA256 签名。</CardDescription></CardHeader>
      <CardContent className='space-y-3'>
        <Field label='接入平台'>
          <Select value={provider} onValueChange={setProvider}>
            <SelectTrigger><SelectValue placeholder='选择通信平台' /></SelectTrigger>
            <SelectContent>{providers.data?.map((item) => <SelectItem key={item.id} value={item.id}>{item.name}</SelectItem>)}</SelectContent>
          </Select>
        </Field>
        {selectedProvider && <p className='rounded-lg bg-muted/50 px-3 py-2 text-xs leading-5 text-muted-foreground'>{selectedProvider.description}</p>}
        <Field label='渠道名称'><Input value={name} onChange={(e) => setName(e.target.value)} placeholder='例如：研发告警群' /></Field>
        <Field label='机器人 Webhook URL'><Input value={url} onChange={(e) => setUrl(e.target.value)} placeholder={selectedProvider?.urlPlaceholder ?? 'https://example.com/hooks/rag'} /></Field>
        <Field label='订阅事件'><Input value={events} onChange={(e) => setEvents(e.target.value)} placeholder='rag.run.completed,evaluation.completed 或 *' /></Field>
        <Field label={selectedProvider?.secretLabel ?? '签名密钥（可选）'}><Input type='password' value={secret} onChange={(e) => setSecret(e.target.value)} placeholder={selectedProvider?.secretHint ?? '可选；仅自定义 / 支持签名的平台需要'} /></Field>
        <div className='flex flex-wrap items-center justify-between gap-2'><span className='text-[10px] text-muted-foreground'>密钥仅用于服务端签名，API 不会返回明文。</span><Button disabled={!name.trim() || !url.trim() || save.isPending} onClick={() => save.mutate()}><Plus className='size-3.5' />创建渠道</Button></div>
      </CardContent>
    </Card>
    <Card>
      <CardHeader><CardTitle className='text-sm'>已配置渠道</CardTitle><CardDescription>每个渠道独立订阅事件、记录投递结果，并可发送测试消息。</CardDescription></CardHeader>
      <CardContent className='space-y-2'>{hooks.data?.map((hook) => <div key={hook.id} className='rounded-xl border p-3'>
        <div className='flex items-start justify-between gap-3'>
          <div className='min-w-0'>
            <div className='flex flex-wrap items-center gap-2 text-xs font-medium'>{hook.name}<Badge variant={hook.isActive ? 'success' : 'outline'}>{hook.isActive ? 'active' : 'disabled'}</Badge><Badge variant='outline'>{providers.data?.find((item) => item.id === hook.provider)?.name ?? hook.provider ?? 'Custom'}</Badge></div>
            <div className='mt-1 truncate text-[10px] text-muted-foreground'>{hook.url}</div>
            <div className='mt-1 text-[10px] text-muted-foreground'>{hook.eventTypes}{hook.lastStatusCode ? ` · last HTTP ${hook.lastStatusCode}` : ''}{hook.lastError ? ` · ${hook.lastError}` : ''}</div>
          </div>
          <div className='flex gap-1'><Button size='icon-sm' variant='outline' title='发送测试' onClick={() => test.mutate(hook.id)}><Send className='size-3.5' /></Button><Button size='icon-sm' variant='ghost' title='删除' onClick={() => remove.mutate(hook.id)}><Trash2 className='size-3.5 text-destructive' /></Button></div>
        </div>
      </div>) || <div className='text-xs text-muted-foreground'>暂无通信渠道。</div>}</CardContent>
    </Card>
  </div>
}
function AgentsPanel({ knowledgeBaseId }: { knowledgeBaseId: string }) {
  const profile = useQuery({ queryKey: ['rag-profile', knowledgeBaseId], queryFn: () => api.governance.ragProfile(knowledgeBaseId), enabled: !!knowledgeBaseId })
  const agents = [
    ['QueryPlannerAgent', '理解意图并生成 multi-query retrieval plan', profile.data?.plannerModel],
    ['KnowledgeAnswerAgent', '持有最终回答权，并选择是否委派 specialist', profile.data?.answerModel],
    ['ComparisonSpecialistAgent', '跨来源比较政策、流程、版本和对象', profile.data?.reviewerModel],
    ['SummarySpecialistAgent', '忠实摘要、主题归纳和行动项', profile.data?.reviewerModel],
    ['ExtractionSpecialistAgent', '结构化字段、实体、日期、编号抽取', profile.data?.reviewerModel],
    ['ResearchSpecialistAgent', '复杂多文档时间线、依赖、冲突与证据缺口', profile.data?.reviewerModel],
    ['EvidenceReviewerAgent', 'grounding、citation、证据覆盖审核', profile.data?.reviewerModel],
  ]
  return <Card><CardHeader><div className='flex items-center justify-between'><div><CardTitle className='text-sm'>Multi-Agent topology</CardTitle><CardDescription>采用确定性 Workflow + Agent-as-Tool，而不是无约束 Group Chat。</CardDescription></div><Badge variant={profile.data?.agentMode === 'single' ? 'outline' : 'info'}>{profile.data?.agentMode || 'loading'}</Badge></div></CardHeader><CardContent className='grid gap-3 md:grid-cols-2 xl:grid-cols-3'>{agents.map(([name, description, model], index) => <div key={name} className='rounded-2xl border bg-background/50 p-4'><div className='flex items-center gap-2'><div className='flex size-8 items-center justify-center rounded-xl bg-primary/8 text-primary'>{index === 0 ? <Brain className='size-4' /> : index === agents.length - 1 ? <ShieldCheck className='size-4' /> : <Bot className='size-4' />}</div><div className='min-w-0'><div className='truncate text-xs font-semibold'>{name}</div><div className='truncate text-[10px] text-muted-foreground'>{model || 'instance default'}</div></div></div><p className='mt-3 text-xs leading-5 text-muted-foreground'>{description}</p>{index > 1 && index < agents.length - 1 && <div className='mt-3 flex items-center gap-1 text-[10px] text-primary'><CheckCircle2 className='size-3' />可在 Chat Trace 实时观察调用</div>}</div>)}</CardContent></Card>
}

function Field({ label, children }: { label: string; children: React.ReactNode }) { return <div className='space-y-1.5'><Label>{label}</Label>{children}</div> }
function Toggle({ label, value, onChange }: { label: string; value: boolean; onChange: (value: boolean) => void }) { return <button type='button' onClick={() => onChange(!value)} className='flex h-10 items-center justify-between rounded-xl border px-3 text-xs'><span>{label}</span><Badge variant={value ? 'success' : 'outline'}>{value ? 'Enabled' : 'Disabled'}</Badge></button> }
