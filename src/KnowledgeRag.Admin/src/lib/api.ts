import axios, { AxiosError, type AxiosRequestConfig } from 'axios'
import { useAuthStore } from '@/stores/auth-store'
import type { ApiKeySummary, AuthResponse, ChatMessage, ChatResponse, Conversation, CreatedApiKey, KnowledgeBaseMember, KnowledgeBaseSummary, KnowledgeDocument, SearchHit, TenantMember, TenantSummary, UserSummary } from './types'

const baseURL = import.meta.env.VITE_API_BASE_URL || ''
export const http = axios.create({ baseURL })
const refreshHttp = axios.create({ baseURL })
let refreshPromise: Promise<string> | null = null

http.interceptors.request.use((config) => {
  const token = useAuthStore.getState().accessToken
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

http.interceptors.response.use((response) => response, async (error: AxiosError) => {
  const original = error.config as (AxiosRequestConfig & { _retry?: boolean }) | undefined
  const state = useAuthStore.getState()
  if (error.response?.status !== 401 || !original || original._retry || !state.refreshToken || String(original.url).includes('/api/auth/refresh')) throw error
  original._retry = true
  try {
    refreshPromise ??= refreshHttp.post<AuthResponse>('/api/auth/refresh', { refreshToken: state.refreshToken })
      .then((r) => { useAuthStore.getState().setSession(r.data); return r.data.accessToken })
      .finally(() => { refreshPromise = null })
    const token = await refreshPromise
    original.headers = { ...(original.headers || {}), Authorization: `Bearer ${token}` }
    return http.request(original)
  } catch (refreshError) {
    useAuthStore.getState().reset()
    window.location.assign('/sign-in')
    throw refreshError
  }
})

export function getErrorMessage(error: unknown) {
  if (axios.isAxiosError(error)) {
    const data = error.response?.data as { error?: string; title?: string } | undefined
    return data?.error || data?.title || error.message
  }
  return error instanceof Error ? error.message : '请求失败'
}

export const api = {
  auth: {
    register: async (body: { email: string; password: string; displayName?: string }) => (await http.post<AuthResponse>('/api/auth/register', body)).data,
    login: async (body: { email: string; password: string }) => (await http.post<AuthResponse>('/api/auth/login', body)).data,
    me: async () => (await http.get<UserSummary>('/api/auth/me')).data,
    logout: async (refreshToken: string) => { if (refreshToken) await http.post('/api/auth/logout', { refreshToken }) },
  },
  tenants: {
    list: async () => (await http.get<TenantSummary[]>('/api/tenants')).data,
    create: async (name: string) => (await http.post<TenantSummary>('/api/tenants', { name })).data,
    members: async (tenantId: string) => (await http.get<TenantMember[]>(`/api/tenants/${tenantId}/members`)).data,
    saveMember: async (tenantId: string, email: string, role: string) => { await http.put(`/api/tenants/${tenantId}/members`, { email, role }) },
    removeMember: async (tenantId: string, userId: string) => { await http.delete(`/api/tenants/${tenantId}/members/${userId}`) },
    apiKeys: async (tenantId: string) => (await http.get<ApiKeySummary[]>(`/api/tenants/${tenantId}/api-keys`)).data,
    createApiKey: async (tenantId: string, body: { name: string; scopes: string[]; knowledgeBaseId?: string | null; expiresAt?: string | null }) => (await http.post<CreatedApiKey>(`/api/tenants/${tenantId}/api-keys`, body)).data,
    revokeApiKey: async (tenantId: string, id: string) => { await http.delete(`/api/tenants/${tenantId}/api-keys/${id}`) },
  },
  knowledgeBases: {
    list: async () => (await http.get<KnowledgeBaseSummary[]>('/api/knowledge-bases')).data,
    create: async (body: { name: string; description?: string; tenantId?: string }) => (await http.post<KnowledgeBaseSummary>('/api/knowledge-bases', body)).data,
    update: async (id: string, body: { name: string; description?: string }) => { await http.put(`/api/knowledge-bases/${id}`, body) },
    remove: async (id: string) => { await http.delete(`/api/knowledge-bases/${id}`) },
    members: async (id: string) => (await http.get<KnowledgeBaseMember[]>(`/api/knowledge-bases/${id}/members`)).data,
    saveMember: async (id: string, email: string, role: string) => { await http.put(`/api/knowledge-bases/${id}/members`, { email, role }) },
    removeMember: async (id: string, userId: string) => { await http.delete(`/api/knowledge-bases/${id}/members/${userId}`) },
  },
  documents: {
    list: async (kbId: string) => (await http.get<KnowledgeDocument[]>(`/api/knowledge-bases/${kbId}/documents`)).data,
    upload: async (kbId: string, file: File) => { const form = new FormData(); form.append('file', file); return (await http.post(`/api/knowledge-bases/${kbId}/documents`, form)).data },
    remove: async (kbId: string, id: string) => { await http.delete(`/api/knowledge-bases/${kbId}/documents/${id}`) },
    download: async (kbId: string, id: string) => (await http.get(`/api/knowledge-bases/${kbId}/documents/${id}/file`, { responseType: 'blob' })).data as Blob,
  },
  search: async (knowledgeBaseId: string, query: string, topK = 5) => (await http.post<SearchHit[]>('/api/search', { knowledgeBaseId, query, topK })).data,
  conversations: {
    list: async (knowledgeBaseId?: string) => (await http.get<Conversation[]>('/api/conversations', { params: knowledgeBaseId ? { knowledgeBaseId } : {} })).data,
    create: async (knowledgeBaseId: string, title?: string) => (await http.post<Conversation>('/api/conversations', { knowledgeBaseId, title })).data,
    messages: async (id: string) => (await http.get<ChatMessage[]>(`/api/conversations/${id}/messages`)).data,
    send: async (id: string, message: string) => (await http.post<ChatResponse>(`/api/conversations/${id}/messages`, { message })).data,
    remove: async (id: string) => { await http.delete(`/api/conversations/${id}`) },
  },
}
