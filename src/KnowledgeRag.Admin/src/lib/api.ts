import axios, { AxiosError, type AxiosRequestConfig } from 'axios'
import { useAuthStore } from '@/stores/auth-store'
import type {
  ApiKeySummary, AuditLog, AuthResponse, ChatMessage, ChatResponse, Conversation, CreatedApiKey,
  DashboardSummary, EvaluationCase, EvaluationDataset, EvaluationRun, GovernanceOverview, IngestionJobSummary,
  KnowledgeBaseMember, KnowledgeBaseSummary, KnowledgeDocument, ModelPrice, ProductSettingsSummary, PromptProfile,
  RagProfile, RagProfileUpdate, RagProgressEvent, RagRun, SearchHit, TenantMember, TenantSummary, UploadResponse,
  UserSummary, WebhookSubscription,
} from './types'

const baseURL = import.meta.env.VITE_API_BASE_URL || ''
export const http = axios.create({ baseURL })
const refreshHttp = axios.create({ baseURL })
let refreshPromise: Promise<string> | null = null

http.interceptors.request.use((config) => { const token = useAuthStore.getState().accessToken; if (token) config.headers.Authorization = `Bearer ${token}`; return config })
http.interceptors.response.use((response) => response, async (error: AxiosError) => {
  const original = error.config as (AxiosRequestConfig & { _retry?: boolean }) | undefined; const state = useAuthStore.getState()
  if (error.response?.status !== 401 || !original || original._retry || !state.refreshToken || String(original.url).includes('/api/auth/refresh')) throw error
  original._retry = true
  try {
    refreshPromise ??= refreshHttp.post<AuthResponse>('/api/auth/refresh', { refreshToken: state.refreshToken }).then((r) => { useAuthStore.getState().setSession(r.data); return r.data.accessToken }).finally(() => { refreshPromise = null })
    const token = await refreshPromise; original.headers = { ...(original.headers || {}), Authorization: `Bearer ${token}` }; return http.request(original)
  } catch (refreshError) { useAuthStore.getState().reset(); window.location.assign('/sign-in'); throw refreshError }
})

export function getErrorMessage(error: unknown) { if (axios.isAxiosError(error)) { const data = error.response?.data as { error?: string; title?: string } | undefined; return data?.error || data?.title || error.message } return error instanceof Error ? error.message : '请求失败' }

async function streamConversation(
  id: string,
  message: string,
  onDelta: (text: string) => void,
  signal?: AbortSignal,
  onProgress?: (event: RagProgressEvent) => void,
): Promise<ChatResponse> {
  const response = await fetch(`${baseURL}/api/conversations/${id}/messages/stream`, { method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${useAuthStore.getState().accessToken}` }, body: JSON.stringify({ message }), signal })
  if (!response.ok || !response.body) throw new Error(`Streaming request failed (${response.status})`)
  const reader = response.body.getReader(); const decoder = new TextDecoder(); let buffer = ''; let final: ChatResponse | null = null
  while (true) {
    const { done, value } = await reader.read(); if (done) break; buffer += decoder.decode(value, { stream: true })
    let boundary = buffer.indexOf('\n\n')
    while (boundary >= 0) {
      const frame = buffer.slice(0, boundary); buffer = buffer.slice(boundary + 2); boundary = buffer.indexOf('\n\n')
      const event = frame.split('\n').find((x) => x.startsWith('event:'))?.slice(6).trim(); const data = frame.split('\n').filter((x) => x.startsWith('data:')).map((x) => x.slice(5).trim()).join('\n')
      if (!data) continue
      if (event === 'delta') onDelta((JSON.parse(data) as { text: string }).text)
      if (event === 'progress') onProgress?.(JSON.parse(data) as RagProgressEvent)
      if (event === 'complete') final = JSON.parse(data) as ChatResponse
    }
  }
  if (!final) throw new Error('Streaming response ended without completion event')
  return final
}

export const api = {
  auth: { register: async (body: { email: string; password: string; displayName?: string }) => (await http.post<AuthResponse>('/api/auth/register', body)).data, login: async (body: { email: string; password: string }) => (await http.post<AuthResponse>('/api/auth/login', body)).data, me: async () => (await http.get<UserSummary>('/api/auth/me')).data, logout: async (refreshToken: string) => { if (refreshToken) await http.post('/api/auth/logout', { refreshToken }) } },
  tenants: { list: async () => (await http.get<TenantSummary[]>('/api/tenants')).data, create: async (name: string) => (await http.post<TenantSummary>('/api/tenants', { name })).data, members: async (tenantId: string) => (await http.get<TenantMember[]>(`/api/tenants/${tenantId}/members`)).data, saveMember: async (tenantId: string, email: string, role: string) => { await http.put(`/api/tenants/${tenantId}/members`, { email, role }) }, removeMember: async (tenantId: string, userId: string) => { await http.delete(`/api/tenants/${tenantId}/members/${userId}`) }, apiKeys: async (tenantId: string) => (await http.get<ApiKeySummary[]>(`/api/tenants/${tenantId}/api-keys`)).data, createApiKey: async (tenantId: string, body: { name: string; scopes: string[]; knowledgeBaseId?: string | null; expiresAt?: string | null }) => (await http.post<CreatedApiKey>(`/api/tenants/${tenantId}/api-keys`, body)).data, revokeApiKey: async (tenantId: string, id: string) => { await http.delete(`/api/tenants/${tenantId}/api-keys/${id}`) } },
  knowledgeBases: { list: async () => (await http.get<KnowledgeBaseSummary[]>('/api/knowledge-bases')).data, create: async (body: { name: string; description?: string; tenantId?: string }) => (await http.post<KnowledgeBaseSummary>('/api/knowledge-bases', body)).data, update: async (id: string, body: { name: string; description?: string }) => { await http.put(`/api/knowledge-bases/${id}`, body) }, remove: async (id: string) => { await http.delete(`/api/knowledge-bases/${id}`) }, members: async (id: string) => (await http.get<KnowledgeBaseMember[]>(`/api/knowledge-bases/${id}/members`)).data, saveMember: async (id: string, email: string, role: string) => { await http.put(`/api/knowledge-bases/${id}/members`, { email, role }) }, removeMember: async (id: string, userId: string) => { await http.delete(`/api/knowledge-bases/${id}/members/${userId}`) } },
  documents: { list: async (kbId: string) => (await http.get<KnowledgeDocument[]>(`/api/knowledge-bases/${kbId}/documents`)).data, upload: async (kbId: string, file: File) => { const form = new FormData(); form.append('file', file); return (await http.post<UploadResponse>(`/api/knowledge-bases/${kbId}/documents`, form)).data }, remove: async (kbId: string, id: string) => { await http.delete(`/api/knowledge-bases/${kbId}/documents/${id}`) }, retry: async (kbId: string, id: string) => { await http.post(`/api/knowledge-bases/${kbId}/documents/${id}/retry`) }, reindex: async (kbId: string, id: string) => { await http.post(`/api/knowledge-bases/${kbId}/documents/${id}/reindex`) }, cancel: async (kbId: string, id: string) => { await http.post(`/api/knowledge-bases/${kbId}/documents/${id}/cancel`) }, download: async (kbId: string, id: string) => (await http.get(`/api/knowledge-bases/${kbId}/documents/${id}/file`, { responseType: 'blob' })).data as Blob },
  dashboard: async () => (await http.get<DashboardSummary>('/api/dashboard')).data,
  ingestion: { jobs: async () => (await http.get<IngestionJobSummary[]>('/api/ingestion/jobs')).data },
  settings: { product: async () => (await http.get<ProductSettingsSummary>('/api/settings/product')).data },
  search: async (knowledgeBaseId: string, query: string, topK = 5) => (await http.post<SearchHit[]>('/api/search', { knowledgeBaseId, query, topK })).data,
  conversations: { list: async (knowledgeBaseId?: string) => (await http.get<Conversation[]>('/api/conversations', { params: knowledgeBaseId ? { knowledgeBaseId } : {} })).data, create: async (knowledgeBaseId: string, title?: string) => (await http.post<Conversation>('/api/conversations', { knowledgeBaseId, title })).data, messages: async (id: string) => (await http.get<ChatMessage[]>(`/api/conversations/${id}/messages`)).data, send: async (id: string, message: string) => (await http.post<ChatResponse>(`/api/conversations/${id}/messages`, { message })).data, stream: streamConversation, remove: async (id: string) => { await http.delete(`/api/conversations/${id}`) } },
  governance: {
    overview: async (tenantId: string) => (await http.get<GovernanceOverview>('/api/governance/overview', { params: { tenantId } })).data,
    ragProfile: async (knowledgeBaseId: string) => (await http.get<RagProfile>(`/api/governance/knowledge-bases/${knowledgeBaseId}/rag-profile`)).data,
    saveRagProfile: async (knowledgeBaseId: string, body: RagProfileUpdate) => (await http.put<RagProfile>(`/api/governance/knowledge-bases/${knowledgeBaseId}/rag-profile`, body)).data,
    runs: async (knowledgeBaseId: string) => (await http.get<RagRun[]>('/api/governance/runs', { params: { knowledgeBaseId } })).data,
    audit: async (tenantId: string) => (await http.get<AuditLog[]>('/api/governance/audit', { params: { tenantId } })).data,
    modelPrices: async (tenantId: string) => (await http.get<ModelPrice[]>('/api/governance/model-prices', { params: { tenantId } })).data,
    saveModelPrice: async (tenantId: string, body: { model: string; inputCostPerMillionTokens: number; outputCostPerMillionTokens: number; isActive?: boolean }) => (await http.put<ModelPrice>('/api/governance/model-prices', body, { params: { tenantId } })).data,
    prompts: async (tenantId: string) => (await http.get<PromptProfile[]>('/api/governance/prompts', { params: { tenantId } })).data,
    savePrompt: async (tenantId: string, body: { id?: string | null; knowledgeBaseId?: string | null; name: string; plannerPrompt: string; answerPrompt: string; reviewerPrompt: string; isActive: boolean }) => (await http.post<PromptProfile>('/api/governance/prompts', body, { params: { tenantId } })).data,
    webhooks: async (tenantId: string) => (await http.get<WebhookSubscription[]>('/api/governance/webhooks', { params: { tenantId } })).data,
    saveWebhook: async (tenantId: string, body: { id?: string | null; name: string; url: string; eventTypes: string[]; secret?: string | null; isActive: boolean }) => (await http.post<WebhookSubscription>('/api/governance/webhooks', body, { params: { tenantId } })).data,
    deleteWebhook: async (tenantId: string, id: string) => { await http.delete(`/api/governance/webhooks/${id}`, { params: { tenantId } }) },
    testWebhook: async (tenantId: string, id: string) => { await http.post(`/api/governance/webhooks/${id}/test`, null, { params: { tenantId } }) },
    datasets: async (tenantId: string) => (await http.get<EvaluationDataset[]>('/api/governance/evaluation/datasets', { params: { tenantId } })).data,
    createDataset: async (tenantId: string, body: { knowledgeBaseId: string; name: string; description?: string }) => (await http.post<EvaluationDataset>('/api/governance/evaluation/datasets', body, { params: { tenantId } })).data,
    cases: async (tenantId: string, datasetId: string) => (await http.get<EvaluationCase[]>(`/api/governance/evaluation/datasets/${datasetId}/cases`, { params: { tenantId } })).data,
    addCase: async (tenantId: string, datasetId: string, body: { category: string; query: string; expectedAnswerContains?: string; requiresKnowledge: boolean }) => (await http.post<EvaluationCase>(`/api/governance/evaluation/datasets/${datasetId}/cases`, body, { params: { tenantId } })).data,
    evaluationRuns: async (tenantId: string, datasetId?: string) => (await http.get<EvaluationRun[]>('/api/governance/evaluation/runs', { params: { tenantId, datasetId } })).data,
    runEvaluation: async (tenantId: string, datasetId: string) => (await http.post<EvaluationRun>(`/api/governance/evaluation/datasets/${datasetId}/run`, null, { params: { tenantId } })).data,
  },
}
