# Changes from the Azure-oriented architecture

当前项目继续保留 Microsoft Agent Framework，但基础设施全部可自托管或接标准兼容接口。

| Azure-oriented component | Current implementation |
|---|---|
| Azure OpenAI / Foundry | OpenAI-compatible Chat + Embeddings |
| Azure AI Search | Qdrant or PostgreSQL pgvector + BM25/PostgreSQL FTS |
| Azure Blob Storage | Local filesystem or MinIO/AWS S3-compatible storage |
| Azure Identity | JWT users + tenant API keys |
| Azure-hosted telemetry | OpenTelemetry OTLP |
| ad-hoc schema creation | EF Core migrations |

新增生产化能力：

- Tenant / TenantMember
- KB RBAC
- API Key scope + optional KB binding
- rotating Refresh Token + replay detection
- PostgreSQL native FTS
- pgvector HNSW
- S3 Signature V4 storage client
- OpenTelemetry traces/metrics
- WebApplicationFactory integration tests

Azure SDK、`DefaultAzureCredential`、`AIProjectClient`、Azure Search 和 Azure Blob 均不在项目依赖中。


## Admin frontend

旧的单文件 `wwwroot/index.html` 演示 UI 已替换为独立的 `src/KnowledgeRag.Admin` React/Vite 管理后台。

前端采用 `satnaing/shadcn-admin` 的技术栈与视觉/布局模式：Shadcn/Radix、Tailwind CSS、TanStack Query/Router，并连接本项目自己的 JWT + Refresh Token、Tenant/RBAC、KnowledgeBase、Document、Hybrid Search、API Key 与 Conversation API。

Dockerfile 已使用 Node 22 多阶段构建，在 .NET 10 publish 前将前端 `dist` 注入 `wwwroot`。

## Backend structure refactor

后端进一步从单一 `KnowledgeRag.Api` 项目整理为：

- `KnowledgeRag.Domain`：领域实体与枚举
- `KnowledgeRag.Application`：contracts、actor context、搜索抽象
- `KnowledgeRag.Infrastructure`：EF Core、Migration、AI、Search、Storage、业务实现
- `KnowledgeRag.Api`：HTTP endpoints、middleware、authentication composition、OpenTelemetry/Swagger composition

原来的 `Api/Data`、`Api/Models`、`Api/Options`、`Api/Services`、`Api/Migrations` 已移除。详细规则见 `ARCHITECTURE.md`。
