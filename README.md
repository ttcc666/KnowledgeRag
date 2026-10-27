# KnowledgeRag.NonAzure

基于 **.NET 10 + Microsoft Agent Framework** 的非 Azure 多租户 RAG 知识库平台，前端使用 **shadcn-admin 风格的 React/Vite 管理后台**。

项目不依赖 Azure OpenAI / Azure AI Search / Azure Blob / Azure Identity / Foundry。AI 使用 OpenAI-compatible API；向量、全文检索、文件存储、数据库均可自行选择。

> 运行时统一使用 **.NET 10 (`net10.0`)**。仓库根目录包含 `global.json`，要求 .NET 10 SDK，并允许在 .NET 10 的更新 feature band 上自动 roll-forward。Docker 构建/运行镜像和 GitHub Actions 也统一使用 .NET 10。

## 当前能力

- Microsoft Agent Framework + `TextSearchProvider`
- OpenAI-compatible Chat / Embeddings（OpenAI、Ollama、vLLM、兼容网关）
- SQLite / PostgreSQL
- EF Core Migration
- JWT Access Token + 旋转式 Refresh Token
- Tenant 多租户
- Tenant `Owner / Admin / Member`
- Knowledge Base `Owner / Editor / Viewer`
- Tenant API Key，支持知识库范围与 `knowledge:read / write / admin`、`tenant:admin` scopes
- Local / MinIO / AWS S3-compatible 文件存储
- Qdrant / PostgreSQL pgvector 可选向量后端
- C# BM25 / PostgreSQL 原生 FTS 可选词法后端
- Hybrid Search：Vector + Lexical + RRF
- `none / local / llm` reranker
- Conversation / ChatMessage / AgentSession 持久化
- OpenTelemetry traces + metrics + OTLP
- xUnit + WebApplicationFactory 集成测试
- PDF / DOCX / TXT / MD / CSV / JSON / XML / HTML
- React 19 + Vite 8 + TypeScript + Tailwind CSS 4 + Shadcn/Radix 管理后台
- TanStack Query / Router，自动 Refresh Token 重试
- Swagger

---

## 架构

后端已整理为 **Pragmatic Clean Architecture + Feature Modules**，不再把实体、EF Core、服务和 Endpoint 混在 `KnowledgeRag.Api`：

```text
src/
├── KnowledgeRag.Domain/          # 实体 / 枚举；无外部实现依赖
├── KnowledgeRag.Application/     # Contracts / ActorContext / 搜索抽象
├── KnowledgeRag.Infrastructure/  # EF Core / AI / Search / Storage / 业务实现
├── KnowledgeRag.Api/             # HTTP Endpoints / Middleware / Security / Composition Root
└── KnowledgeRag.Admin/           # React + shadcn-admin
```

依赖方向：

```text
Api ───────► Infrastructure ───────► Application ───────► Domain
 │                                      │
 └──────────────────────────────────────► Domain
```

`Program.cs` 只负责组合启动；API 路由按业务拆在 `KnowledgeRag.Api/Endpoints`，EF Core migrations 固定在 `KnowledgeRag.Infrastructure/Persistence/Migrations`。详细开发约定见 [`ARCHITECTURE.md`](ARCHITECTURE.md)。

```text
React / Vite / shadcn-admin
      │
      ▼
Client / Admin UI
      │
      ├── Bearer JWT
      └── X-Api-Key
      │
      ▼
ASP.NET Core
      │
      ├── Tenant / RBAC / API Key
      ├── Document / Conversation
      └── Microsoft Agent Framework
                  │
             TextSearchProvider
                  │
                  ▼
             Hybrid Search
       ┌──────────┴──────────┐
       │                     │
 Vector backend         Lexical backend
 Qdrant | pgvector      BM25 | Postgres FTS
       │                     │
       └──────────┬──────────┘
                  ▼
                 RRF
                  ▼
               reranker
                  ▼
              Top-K chunks
                  ▼
        OpenAI-compatible LLM
```

文件存储：

```text
IFileStorage
 ├── LocalFileStorage
 └── S3FileStorage
      ├── MinIO
      └── AWS S3-compatible endpoint
```

业务数据：

```text
Users
Tenants / TenantMembers
ApiKeys
RefreshTokens
KnowledgeBases / KnowledgeBaseMembers
Documents / DocumentChunks
Conversations / ChatMessages
```

---

# 1. 最简单启动：SQLite + Qdrant + Local

```bash
cp .env.example .env
```

至少修改：

```env
AI_API_KEY=your-key
AUTH_SIGNING_KEY=replace-with-a-long-random-secret-at-least-32-characters
```

启动：

```bash
docker compose up -d --build
```

访问：

- UI: `http://localhost:8080`
- Swagger: `http://localhost:8080/swagger`
- Qdrant dashboard: `http://localhost:6333/dashboard`

默认组合：

```env
DATABASE_PROVIDER=Sqlite
SEARCH_VECTOR_BACKEND=qdrant
SEARCH_LEXICAL_BACKEND=bm25
STORAGE_PROVIDER=local
```

## 前端开发（shadcn-admin）

前端独立位于：

```text
src/KnowledgeRag.Admin
```

技术栈与 `satnaing/shadcn-admin` 对齐：React、Vite、TypeScript、Tailwind CSS、Shadcn/Radix、TanStack Query/Router。UI 主题和后台布局按 shadcn-admin 的管理后台模式改造，业务 API 使用本项目自己的 JWT / Refresh Token / Tenant / RBAC。

开发模式先启动 .NET API（8080），再运行：

```bash
cd src/KnowledgeRag.Admin
npm install
npm run dev
```

访问 `http://localhost:5173`。Vite 会把 `/api` 代理到 `http://localhost:8080`。

构建到独立 `dist`：

```bash
npm run build
```

构建并直接写入 ASP.NET Core `wwwroot`：

```bash
npm run build:api
```

Docker 不需要手工执行这些命令：`KnowledgeRag.Api/Dockerfile` 已增加 Node 22 前端构建阶段，最终 React 静态资源会自动进入 .NET 10 镜像。

管理后台已包含：

- 登录 / 注册 / Refresh Token 自动轮换
- 总览 Dashboard
- 知识库列表与创建
- 文档上传、下载、删除与索引状态
- Vector / BM25 / RRF / reranker 检索调试
- KnowledgeBase Owner / Editor / Viewer 成员管理
- Tenant Owner / Admin / Member 管理
- Tenant API Key / Scope / KnowledgeBase 范围管理
- AgentSession 多轮 RAG 对话与来源展示
- Light / Dark mode 与响应式 Sidebar

`shadcn-admin` 为 MIT License，相关归属说明见 `src/KnowledgeRag.Admin/THIRD_PARTY_NOTICES.md`。

---

# 2. PostgreSQL + pgvector + PostgreSQL FTS

项目 Compose 中的 PostgreSQL 使用带 `vector` extension 的镜像。

`.env`：

```env
DATABASE_PROVIDER=PostgreSql
DATABASE_CONNECTION_STRING=Host=postgres;Port=5432;Database=knowledge_rag;Username=knowledge_rag;Password=knowledge_rag_dev_password

SEARCH_VECTOR_BACKEND=pgvector
SEARCH_LEXICAL_BACKEND=postgres_fts
POSTGRES_FTS_CONFIG=simple

POSTGRES_DB=knowledge_rag
POSTGRES_USER=knowledge_rag
POSTGRES_PASSWORD=knowledge_rag_dev_password
```

启动：

```bash
docker compose --profile postgres up -d --build
```

`pgvector` 模式会初始化：

```text
CREATE EXTENSION IF NOT EXISTS vector
rag_chunk_vectors
HNSW vector_cosine_ops index
```

`postgres_fts` 模式会为 `DocumentChunks.Text` 创建 GIN expression index。

> PostgreSQL `simple` FTS 更适合英文、数字和有空格分词的内容。大量中文知识库建议继续使用项目内置 BM25，或者自行安装中文 PostgreSQL parser / 独立全文检索引擎。

可以自由混搭，例如：

```env
DATABASE_PROVIDER=PostgreSql
SEARCH_VECTOR_BACKEND=pgvector
SEARCH_LEXICAL_BACKEND=bm25
```

---

# 3. MinIO / S3

启用 MinIO：

```env
STORAGE_PROVIDER=s3
S3_ENDPOINT=http://minio:9000
S3_REGION=us-east-1
S3_BUCKET=knowledge-rag
S3_ACCESS_KEY=minioadmin
S3_SECRET_KEY=minioadmin
S3_FORCE_PATH_STYLE=true
```

启动：

```bash
docker compose --profile s3 up -d --build
```

- S3 API: `http://localhost:9000`
- MinIO Console: `http://localhost:9001`

`minio-init` 会自动创建 bucket。

`S3FileStorage` 使用 AWS Signature V4，请勿把真实生产密钥写入源码或提交到 Git。

AWS S3 使用 virtual-host style 时可设置：

```env
S3_ENDPOINT=https://s3.us-east-1.amazonaws.com
S3_REGION=us-east-1
S3_FORCE_PATH_STYLE=false
```

---

# 4. EF Core Migration

应用默认：

```json
"Database": {
  "AutoMigrate": true
}
```

启动时执行：

```csharp
await db.Database.MigrateAsync();
```

项目已包含初始 production migration：

```text
src/KnowledgeRag.Infrastructure/Persistence/Migrations/
  202610270001_InitialProduction.cs
  AppDbContextModelSnapshot.cs
```

安装 EF CLI：

```bash
dotnet tool install --global dotnet-ef --version 10.*
```

新增迁移：

```bash
dotnet ef migrations add AddSomething \
  --project src/KnowledgeRag.Infrastructure \
  --startup-project src/KnowledgeRag.Api \
  --output-dir Persistence/Migrations
```

应用：

```bash
dotnet ef database update \
  --project src/KnowledgeRag.Infrastructure \
  --startup-project src/KnowledgeRag.Api
```

PostgreSQL 设计时：

```bash
DATABASE_PROVIDER=PostgreSql \
DATABASE_CONNECTION_STRING='Host=localhost;Database=knowledge_rag;Username=knowledge_rag;Password=...' \
dotnet ef database update --project src/KnowledgeRag.Infrastructure --startup-project src/KnowledgeRag.Api
```

生产建议使用 migration bundle 或经审核 SQL，而不是让应用运行账号长期拥有 schema 修改权限。

> 如果你运行过本项目上一版的 `EnsureCreated` 数据库，该数据库没有 EF migration history，不能直接套用新的 InitialProduction migration。开发环境最简单是备份后重建；生产已有数据请参考 `MIGRATION_GUIDE.md` 做 baseline / 数据迁移。

---

# 5. Access Token / Refresh Token

登录/注册返回：

```json
{
  "accessToken": "...",
  "expiresAt": "...",
  "refreshToken": "...",
  "refreshExpiresAt": "...",
  "user": {}
}
```

刷新：

```http
POST /api/auth/refresh
Content-Type: application/json

{
  "refreshToken": "..."
}
```

行为：

- Refresh Token 使用 64-byte 随机值
- DB 只保存 SHA-256 hash
- 刷新时 rotate：旧 token 立即 revoke，新 token 写入 DB
- 已 revoke token 再次出现会被视为 replay，并撤销该用户剩余有效 refresh tokens
- `/api/auth/logout` 可主动 revoke

配置：

```env
AUTH_ACCESS_TOKEN_MINUTES=60
AUTH_REFRESH_TOKEN_DAYS=30
```

---

# 6. Tenant / API Key / RBAC

注册用户会自动获得一个个人 Tenant，并成为 Owner。

Tenant Role：

| Role | 使用租户 | 成员管理 | API Key 管理 | 删除租户 |
|---|---:|---:|---:|---:|
| Member | ✅ | ❌ | ❌ | ❌ |
| Admin | ✅ | ✅ | ✅ | ❌ |
| Owner | ✅ | ✅ | ✅ | ✅ |

Knowledge Base Role：

| Role | 搜索/Chat/下载 | 上传删除文档 | 修改 KB | 成员管理/删除 KB |
|---|---:|---:|---:|---:|
| Viewer | ✅ | ❌ | ❌ | ❌ |
| Editor | ✅ | ✅ | ✅ | ❌ |
| Owner | ✅ | ✅ | ✅ | ✅ |

API Key scope：

```text
knowledge:read
knowledge:write
knowledge:admin
tenant:admin
```

API Key 可以：

- 绑定 Tenant
- `tenant:admin` 才允许修改 Tenant / 管理 Tenant 成员；`knowledge:admin` 不再隐式拥有租户管理权
- 可选锁定一个 `KnowledgeBaseId`
- 设置过期时间
- 服务端只保存 hash
- 请求使用 `X-Api-Key`

创建：

```http
POST /api/tenants/{tenantId}/api-keys
Authorization: Bearer ...

{
  "name": "backend-service",
  "scopes": ["knowledge:read", "knowledge:write"],
  "knowledgeBaseId": null,
  "expiresAt": null
}
```

返回的 `apiKey` 只应保存一次；后续列表只显示 prefix，不返回明文。

调用：

```http
X-Api-Key: kr_live_xxxxx
```

---

# 7. Hybrid Search

搜索流程：

```text
Query
 ├── Embedding -> Vector backend -> top N
 └── Lexical backend -> top N
               │
               ▼
       Reciprocal Rank Fusion
               │
               ▼
           reranker
               │
               ▼
             Top-K
```

配置：

```env
SEARCH_VECTOR_BACKEND=qdrant     # qdrant | pgvector
SEARCH_LEXICAL_BACKEND=bm25      # bm25 | postgres_fts
RERANKER_MODE=local              # none | local | llm
```

BM25 是跨 SQLite/PostgreSQL 的 C# 实现，并对 CJK 生成 unigram + bigram token。

---

# 8. Conversation / AgentSession

数据库同时保存：

- `ChatMessages`：UI / 审计历史
- `Conversation.AgentSessionJson`：Microsoft Agent Framework session

`AgentSessionJson` 可能包含完整对话上下文和敏感信息，生产数据库应启用磁盘/列级加密、备份加密和严格的数据库访问控制。

API Key 创建的会话还会保存 `ApiKeyId`，因此不会和同一创建者的普通 JWT 会话混在一起。

请求恢复流程：

```text
load conversation
 -> verify actor owns this session namespace
 -> verify actor still has KB permission
 -> Deserialize AgentSession
 -> Agent.RunAsync
 -> Serialize AgentSession
 -> save messages + session
```

---

# 9. OpenTelemetry

默认启用 ASP.NET Core / HttpClient / Runtime instrumentation，并增加：

```text
rag.ingest
rag.search
```

RAG span 带有知识库、候选数量、Top-K、向量后端和词法后端等 tags。

配置 OTLP：

```env
OTEL_ENABLED=true
OTEL_SERVICE_NAME=KnowledgeRag.NonAzure
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
```

启动示例 collector：

```bash
docker compose --profile otel up -d --build
```

Prometheus exporter：

```text
http://localhost:8889/metrics
```

Collector 示例配置位于：

```text
observability/otel-collector.yaml
```

---

# 10. 集成测试

```bash
dotnet test KnowledgeRag.NonAzure.sln
```

测试项目：

```text
tests/KnowledgeRag.Api.IntegrationTests
```

目前覆盖：

1. 注册 -> Refresh Token rotation
2. 旧 Refresh Token 重放 -> 撤销当前有效 refresh session
3. Tenant API Key -> scoped KB access
4. `knowledge:admin` API Key 不会隐式获得 `tenant:admin`
5. 非成员无法访问他人的知识库
6. 外部后端测试：MinIO PUT/GET/DELETE
7. 外部后端测试：pgvector upsert/search/delete
8. 外部后端测试：PostgreSQL FTS 真实检索

普通 API 测试使用临时 SQLite DB，并实际启动 ASP.NET Core `WebApplicationFactory`，所以会覆盖 EF Migration、认证中间件、API 路由和权限服务。

仓库还包含 `.github/workflows/ci.yml`：第一个 job 自动执行 `dotnet restore`、`dotnet build` 和普通集成测试；第二个 job 会启动 PostgreSQL/pgvector + PostgreSQL FTS + MinIO + OTel Collector，启动应用并检查 readiness，然后运行 `Category=ExternalBackend` 测试，真正通过项目代码访问 pgvector、PostgreSQL FTS 和 MinIO。

---

# 11. API 摘要

认证：

```text
POST /api/auth/register
POST /api/auth/login
POST /api/auth/refresh
POST /api/auth/logout
GET  /api/auth/me
```

Tenant：

```text
GET    /api/tenants
POST   /api/tenants
PUT    /api/tenants/{tenantId}
DELETE /api/tenants/{tenantId}
GET    /api/tenants/{tenantId}/members
PUT    /api/tenants/{tenantId}/members
DELETE /api/tenants/{tenantId}/members/{userId}
GET    /api/tenants/{tenantId}/api-keys
POST   /api/tenants/{tenantId}/api-keys
DELETE /api/tenants/{tenantId}/api-keys/{apiKeyId}
```

知识库：

```text
GET    /api/knowledge-bases
POST   /api/knowledge-bases
GET    /api/knowledge-bases/{id}
PUT    /api/knowledge-bases/{id}
DELETE /api/knowledge-bases/{id}
```

文档：

```text
GET    /api/knowledge-bases/{kbId}/documents
POST   /api/knowledge-bases/{kbId}/documents
GET    /api/knowledge-bases/{kbId}/documents/{documentId}/file
DELETE /api/knowledge-bases/{kbId}/documents/{documentId}
```

RAG：

```text
POST /api/search
POST /api/chat
POST /api/conversations
GET  /api/conversations
GET  /api/conversations/{id}/messages
POST /api/conversations/{id}/messages
```

Health：

```text
GET /api/health
GET /api/health/ready
```

---

# 12. 推荐生产组合

中文企业知识库：

```text
ASP.NET Core
PostgreSQL
pgvector
C# BM25（或独立搜索引擎）
MinIO / S3
OpenAI-compatible model gateway
Microsoft Agent Framework
OpenTelemetry Collector
```

英文/有明确空格分词的知识库，可以尝试：

```text
PostgreSQL + pgvector + PostgreSQL FTS
```

这样文档业务数据、全文检索和 dense vector 可以集中到 PostgreSQL，部署组件更少。

---

## 安全提醒

- 生产必须更换 `Auth:SigningKey`
- 不要把 `.env`、S3 secret、模型 API key 提交到 Git
- API Key 和 Refresh Token 明文都只应出现在客户端持有端
- 移出租户成员前会阻止删除仍是某个知识库唯一 Owner 的成员，并同步清理其知识库成员关系
- 知识库检索内容应被视为不可信数据，Agent prompt 中已加入间接 Prompt Injection 防护
- S3 bucket 建议保持私有，通过本 API 做权限检查后流式下载
- 生产迁移建议使用单独 deployment identity
