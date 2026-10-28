export type TenantRole = 'Member' | 'Admin' | 'Owner'
export type KnowledgeBaseRole = 'Viewer' | 'Editor' | 'Owner'
export type DocumentStatus = 'Queued' | 'Processing' | 'Ready' | 'Failed' | 'Cancelled'

export interface UserSummary { id: string; email: string; displayName: string }
export interface AuthResponse { accessToken: string; expiresAt: string; refreshToken: string; refreshExpiresAt: string; user: UserSummary }
export interface TenantSummary { id: string; name: string; slug: string; role: TenantRole; createdAt: string; updatedAt: string }
export interface TenantMember { id: string; email: string; displayName: string; role: TenantRole; addedAt: string }
export interface KnowledgeBaseSummary { id: string; tenantId: string; name: string; description?: string | null; role: KnowledgeBaseRole; createdAt: string; updatedAt: string }
export interface KnowledgeBaseMember { id: string; email: string; displayName: string; role: KnowledgeBaseRole; addedAt: string }
export interface KnowledgeDocument { id: string; fileName: string; contentType: string; sizeBytes: number; chunkCount: number; sha256: string; status: DocumentStatus; lastError?: string | null; createdAt: string; updatedAt: string }
export interface UploadResponse { documentId: string; fileName: string; chunkCount: number; sha256: string; status: DocumentStatus }
export interface DocumentProcessingNotification { documentId: string; knowledgeBaseId: string; userId: string; fileName: string; status: DocumentStatus; progress: number; stage: string; chunkCount: number; error?: string | null; updatedAt: string }
export interface SearchHit { pointId: string; documentId: string; fileName: string; chunkIndex: number; text: string; score: number; sourceLink: string; vectorScore?: number | null; bm25Score?: number | null; fusionScore?: number | null; rerankScore?: number | null; pageNumber?: number | null }
export interface Conversation { id: string; knowledgeBaseId: string; title: string; createdAt: string; updatedAt: string }
export interface ChatSource { documentId: string; fileName: string; chunkIndex: number; sourceLink: string; score: number; excerpt: string; pageNumber?: number | null; vectorScore?: number | null; bm25Score?: number | null; fusionScore?: number | null; rerankScore?: number | null }
export interface ChatResponse { conversationId: string; answer: string; sources: ChatSource[] }
export interface RagProgressEvent {
  runId: string
  sequence: number
  stepId: string
  stage: string
  type: string
  actor: string
  label: string
  status: string
  elapsedMs?: number | null
  data?: Record<string, unknown> | null
}
export interface ChatMessage { id: string; role: string; content: string; sourcesJson?: string | null; createdAt: string }
export interface ApiKeySummary { id: string; name: string; keyPrefix: string; scopes: string[]; knowledgeBaseId?: string | null; isActive: boolean; createdAt: string; expiresAt?: string | null; lastUsedAt?: string | null }
export interface CreatedApiKey { id: string; name: string; apiKey: string; scopes: string[]; knowledgeBaseId?: string | null; expiresAt?: string | null }

export interface DailyActivityPoint { date: string; documents: number; conversations: number }
export interface IngestionJobSummary { documentId: string; knowledgeBaseId: string; knowledgeBaseName: string; fileName: string; status: DocumentStatus; chunkCount: number; lastError?: string | null; createdAt: string; updatedAt: string; elapsedSeconds: number; queuePosition?: number | null }
export interface RetrievalMetricsSummary { queryCount: number; p50LatencyMs: number; p95LatencyMs: number; noResultRate: number; averageTopScore: number; averageRerankScore: number; estimatedInputTokens: number; estimatedOutputTokens: number; estimatedModelCostUsd?: number | null; chatTurns: number }
export interface DashboardSummary { knowledgeBaseCount: number; readyDocumentCount: number; processingDocumentCount: number; failedDocumentCount: number; cancelledDocumentCount: number; totalChunkCount: number; conversationCount: number; activity: DailyActivityPoint[]; recentIngestion: IngestionJobSummary[]; retrieval: RetrievalMetricsSummary }
export interface ProductSettingsSummary { databaseProvider: string; chatModel: string; embeddingModel: string; vectorBackend: string; lexicalBackend: string; rerankerMode: string; storageProvider: string; capTransport: string; capFailedRetryCount: number; chunkSize: number; chunkOverlap: number; topK: number; maxUploadMegabytes: number; openTelemetryEnabled: boolean }

export interface GovernanceOverview { runCount: number; averageLatencyMs: number; estimatedCostUsd: number; auditCount: number; promptProfileCount: number; activeWebhookCount: number; evaluationRunCount: number }
export interface RagProfile {
  knowledgeBaseId: string
  enablePlanner: boolean
  plannerMaxQueries: number
  multiQueryTopK: number
  maxMergedEvidence: number
  enableReviewer: boolean
  reviewerMaxRetries: number
  reviewerMinConfidence: number
  plannerModel: string
  answerModel: string
  reviewerModel: string
  promptProfileId?: string | null
  agentMode: string
  monthlyBudgetUsd: number
  dailyRunLimit: number
  updatedByUserId: string
  updatedAt: string
}
export type RagProfileUpdate = Omit<RagProfile, 'knowledgeBaseId' | 'updatedByUserId' | 'updatedAt'>
export interface RagRun { id: string; tenantId: string; knowledgeBaseId: string; conversationId?: string | null; userId: string; apiKeyId?: string | null; query: string; answer: string; status: string; answerModel: string; agentMode: string; sourceCount: number; estimatedInputTokens: number; estimatedOutputTokens: number; estimatedCostUsd: number; latencyMs: number; traceJson: string; createdAt: string }
export interface ModelPrice { id: string; tenantId: string; model: string; inputCostPerMillionTokens: number; outputCostPerMillionTokens: number; isActive: boolean; updatedAt: string }
export interface AuditLog { id: string; tenantId?: string | null; userId: string; apiKeyId?: string | null; action: string; resourceType: string; resourceId?: string | null; summary: string; detailsJson?: string | null; ipAddress?: string | null; userAgent?: string | null; statusCode: number; createdAt: string }
export interface PromptProfile { id: string; tenantId: string; knowledgeBaseId?: string | null; name: string; version: number; plannerPrompt: string; answerPrompt: string; reviewerPrompt: string; isActive: boolean; updatedByUserId: string; createdAt: string; updatedAt: string }
export interface WebhookProvider { id: string; name: string; description: string; urlPlaceholder: string; secretLabel: string; secretHint: string }
export interface WebhookSubscription { id: string; tenantId: string; name: string; provider: string; url: string; eventTypes: string; isActive: boolean; lastStatusCode?: number | null; lastError?: string | null; lastDeliveredAt?: string | null; createdByUserId: string; createdAt: string; updatedAt: string }
export interface EvaluationDataset { id: string; tenantId: string; knowledgeBaseId: string; name: string; description?: string | null; createdByUserId: string; createdAt: string; updatedAt: string }
export interface EvaluationCase { id: string; datasetId: string; category: string; query: string; expectedAnswerContains?: string | null; requiresKnowledge: boolean; createdAt: string }
export interface EvaluationRun { id: string; datasetId: string; knowledgeBaseId: string; status: string; caseCount: number; passedCount: number; passRate: number; groundednessRate: number; averageLatencyMs: number; averageSourceCount: number; resultsJson: string; createdByUserId: string; createdAt: string; completedAt?: string | null }
