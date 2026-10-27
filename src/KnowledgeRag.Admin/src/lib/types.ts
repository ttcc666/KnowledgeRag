export type TenantRole = 'Member' | 'Admin' | 'Owner'
export type KnowledgeBaseRole = 'Viewer' | 'Editor' | 'Owner'
export type DocumentStatus = 'Processing' | 'Ready' | 'Failed'

export interface UserSummary { id: string; email: string; displayName: string }
export interface AuthResponse { accessToken: string; expiresAt: string; refreshToken: string; refreshExpiresAt: string; user: UserSummary }
export interface TenantSummary { id: string; name: string; slug: string; role: TenantRole; createdAt: string; updatedAt: string }
export interface TenantMember { id: string; email: string; displayName: string; role: TenantRole; addedAt: string }
export interface KnowledgeBaseSummary { id: string; tenantId: string; name: string; description?: string | null; role: KnowledgeBaseRole; createdAt: string; updatedAt: string }
export interface KnowledgeBaseMember { id: string; email: string; displayName: string; role: KnowledgeBaseRole; addedAt: string }
export interface KnowledgeDocument { id: string; fileName: string; contentType: string; sizeBytes: number; chunkCount: number; sha256: string; status: DocumentStatus; lastError?: string | null; createdAt: string; updatedAt: string }
export interface SearchHit { pointId: string; documentId: string; fileName: string; chunkIndex: number; text: string; score: number; sourceLink: string; vectorScore?: number | null; bm25Score?: number | null; fusionScore?: number | null; rerankScore?: number | null }
export interface Conversation { id: string; knowledgeBaseId: string; title: string; createdAt: string; updatedAt: string }
export interface ChatSource { documentId: string; fileName: string; chunkIndex: number; sourceLink: string; score: number }
export interface ChatResponse { conversationId: string; answer: string; sources: ChatSource[] }
export interface ChatMessage { id: string; role: string; content: string; sourcesJson?: string | null; createdAt: string }
export interface ApiKeySummary { id: string; name: string; keyPrefix: string; scopes: string[]; knowledgeBaseId?: string | null; isActive: boolean; createdAt: string; expiresAt?: string | null; lastUsedAt?: string | null }
export interface CreatedApiKey { id: string; name: string; apiKey: string; scopes: string[]; knowledgeBaseId?: string | null; expiresAt?: string | null }
