# KnowledgeRag.NonAzure

基于 **.NET 10 + Microsoft Agent Framework** 的非 Azure 多租户 RAG 知识库平台，前端使用 **shadcn-admin 风格的 React/Vite 管理后台**。

项目不依赖 Azure OpenAI / Azure AI Search / Azure Blob / Azure Identity / Foundry。AI 使用 OpenAI-compatible API；向量、全文检索、文件存储、数据库均可自行选择。

> 运行时统一使用 **.NET 10 (`net10.0`)**。仓库根目录包含 `global.json`，要求 .NET 10 SDK，并允许在 .NET 10 的更新 feature band 上自动 roll-forward。Docker 构建/运行镜像和 GitHub Actions 也统一使用 .NET 10。

## 当前能力

- **RAG Agent Workflow**：Planner → Multi-query Retrieval → Answer → Reviewer / bounded Repair → Final Citation Gate
- **确定性检索链路**：Vector Search + BM25 / PostgreSQL FTS + RRF + `none / local / llm` reranker
- **多租户与权限**：Tenant `Owner / Admin / Member`、Knowledge Base `Owner / Editor / Viewer`、JWT / rotating Refresh Token、带 scope / KB binding 的 Tenant API Key
- **文档处理**：PDF / DOCX / TXT / MD / CSV / JSON / XML / HTML，CAP 异步索引，SignalR 进度通知、retry / reindex / cancel
- **可选基础设施**：SQLite / PostgreSQL；Qdrant / pgvector；C# BM25 / PostgreSQL FTS；Local / MinIO / S3-compatible storage
- **Tenant AI Provider 治理**：加密 Provider Connection、Chat / Embedding / Rerank Deployment、健康状态、显式 fallback、Tenant Defaults 与 KB 级角色路由
- **Embedding Index Generation**：知识库级版本化重建、staging generation、成功后原子切换、失败时保留旧 active generation
- **RAG Governance**：Evaluation Dataset / Run、KB RAG Profile、RAG Run Trace、Token / Cost / Budget / Daily quota、Model Pricing、Audit Log
- **Prompt / Agent 管理**：版本化 Prompt Profile、Planner / Answer / Reviewer、Specialist Agent-as-Tool
- **Webhook Automation**：事件订阅、签名/Secret 保护、HTTP 与 Slack / Discord / Teams / 飞书 / 钉钉 / 企业微信等渠道适配
- **Chat UI**：assistant-ui External Store Runtime、SSE Agent Timeline、message-scoped citations / Evidence Panel、feedback、rename、archive / restore、bounded history pagination
- **可观测性**：OpenTelemetry traces + metrics + OTLP；健康与 readiness endpoints
- **验证**：xUnit + WebApplicationFactory、Vitest / accessibility tests、Playwright E2E、PostgreSQL / RabbitMQ / CAP / pgvector / PostgreSQL FTS native smoke
- **管理后台**：React 19 + Vite 8 + TypeScript + Tailwind CSS 4 + Shadcn/Radix、TanStack Query / Router、自动 Refresh Token 轮换处理
- **.NET 10 / EF Core 10**：多 migration、SQLite / PostgreSQL 兼容处理、Swagger

详细专项说明见 [文档索引](docs/README.md)。

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

`Program.cs` 只负责组合启动；API 路由按业务拆在 `src/KnowledgeRag.Api/Endpoints`，EF Core migrations 固定在 `KnowledgeRag.Infrastructure/Persistence/Migrations`。详细开发约定见 [`ARCHITECTURE.md`](ARCHITECTURE.md)。

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

访问 `http://localhost:5173`。Vite 会把 `/api` 与 SignalR 的 `/hubs` 代理到 `http://localhost:8080`；可用 `VITE_DEV_API_PROXY` 覆盖代理目标。

构建到独立 `dist`：

```bash
npm run build
```

构建并直接写入 ASP.NET Core `wwwroot`：

```bash
npm run build:api
```

Docker 不需要手工执行这些命令：`src/KnowledgeRag.Api/Dockerfile` 已增加 Node 22 前端构建阶段，最终 React 静态资源会自动进入 .NET 10 镜像。

管理后台当前包含：

- 登录 / 注册 / Refresh Token 自动轮换
- Dashboard 与文档摄取任务状态
- Knowledge Base CRUD、成员权限、文档上传 / 下载 / 删除 / retry / reindex / cancel
- Knowledge Base Embedding Index generation 状态与版本化 Reindex
- Vector / BM25 / RRF / reranker 检索调试
- Tenant / Tenant Member / API Key / scopes / KB binding
- Tenant AI Provider Connections、Model Deployments、health check、fallback、Tenant Defaults
- Chat：assistant-ui、实时 Agent Timeline、可信引用与 Evidence Panel、feedback、rename、archive / restore、删除
- Governance：RAG Profile、Run Trace、Evaluation、Model Pricing / Cost、Audit、Prompt Profile、Webhook Automation
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

启动时建议先等 PostgreSQL 健康，再启动应用，避免应用 migration 早于数据库 ready：

```bash
docker compose --profile postgres up -d postgres
docker compose --profile postgres up -d --build app
```

如需 CAP 使用 RabbitMQ，还需设置 `CAP_TRANSPORT=rabbitmq` 并启用 `cap` profile：

```bash
docker compose --profile postgres --profile cap up -d postgres rabbitmq
# 等待 PostgreSQL / RabbitMQ ready 后
docker compose --profile postgres --profile cap up -d --build app
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

建议分两步启动，先等 MinIO 可用，再启动应用：

```bash
docker compose --profile s3 up -d minio
docker compose --profile s3 up -d --build app
```

- S3 API: `http://localhost:9000`
- MinIO Console: `http://localhost:9001`

当前 Compose **没有** `minio-init` bucket 初始化服务，`S3FileStorage` 也不会自动创建 bucket。首次使用前，请打开 MinIO Console，使用 `S3_ACCESS_KEY` / `S3_SECRET_KEY` 登录，并手动创建名称与 `S3_BUCKET` 一致的 bucket（默认 `knowledge-rag`）。

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

当 `Database:AutoMigrate=true`（默认值）时，应用启动会执行 EF Core migrations。生产环境也可以设置 `DATABASE_AUTO_MIGRATE=false`，改由发布流水线中的 migration bundle 执行 schema 更新。

当前 migration 不止初始 schema，包含：

```text
202610270001_InitialProduction
20261027050438_SyncCurrentModel
20261028100000_PlatformGovernanceSuite
20261028120000_WebhookChannelProviders
20261028164203_TenantAiProviderFoundation
20261029003620_TenantAiEmbeddingIndexGenerations
20261029021050_AiDeploymentRoutingGovernance
AppDbContextModelSnapshot.cs
```

旧版 `EnsureCreated` 数据库不要直接执行最新 migration；升级步骤见 [MIGRATION_GUIDE.md](MIGRATION_GUIDE.md)。

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
6. 外部后端测试：MinIO PUT/GET/DELETE（需单独启用 `RUN_EXTERNAL_S3_TESTS=1` 并提供 MinIO）
7. 外部后端测试：pgvector upsert/search/delete
8. 外部后端测试：PostgreSQL FTS 真实检索

普通 API 测试使用临时 SQLite DB，并实际启动 ASP.NET Core `WebApplicationFactory`，所以会覆盖 EF Migration、认证中间件、API 路由和权限服务。

仓库的 `.github/workflows/ci.yml` 当前分为两个主要验证 job：

- `build-test`：前端执行 `npm ci`、production build、Vitest、Playwright；后端执行 .NET restore / Release build / integration tests。
- `native-backends-smoke`：启动 PostgreSQL（pgvector）、RabbitMQ 与 OpenTelemetry Collector，使用 PostgreSQL + pgvector + PostgreSQL FTS + CAP/RabbitMQ 配置启动应用，检查 readiness 与 CAP schema，并运行 `Category=ExternalBackend` 测试。

当前 native CI 使用 Local 文件存储，并不启动 MinIO；MinIO / S3 round-trip 测试需要单独提供 MinIO 并设置 `RUN_EXTERNAL_S3_TESTS=1`。

---

# 11. API 摘要

所有需要身份的 API 均应使用当前登录的 Bearer JWT 或符合 scope / KB binding 的 `X-Api-Key`。具体请求模型以 Swagger 和对应 Endpoint / Application Contract 为准。

认证与健康：

```text
POST /api/auth/register
POST /api/auth/login
POST /api/auth/refresh
POST /api/auth/logout
GET  /api/auth/me
GET  /api/health
GET  /api/health/ready
```

Tenant、成员与 API Key：

```text
GET/POST          /api/tenants
PUT/DELETE        /api/tenants/{tenantId}
GET               /api/tenants/{tenantId}/members
PUT               /api/tenants/{tenantId}/members
DELETE            /api/tenants/{tenantId}/members/{memberUserId}
GET/POST          /api/tenants/{tenantId}/api-keys
DELETE            /api/tenants/{tenantId}/api-keys/{apiKeyId}
```

Tenant AI Provider（详细步骤见 [AI 供应商配置指南](docs/AI_PROVIDER_CONFIGURATION.md)）：

```text
GET    /api/tenants/ai/providers
GET    /api/tenants/{tenantId}/ai/connections
POST   /api/tenants/{tenantId}/ai/connections
POST   /api/tenants/{tenantId}/ai/connections/{connectionId}/test
DELETE /api/tenants/{tenantId}/ai/connections/{connectionId}
GET/POST /api/tenants/{tenantId}/ai/deployments
POST   /api/tenants/{tenantId}/ai/deployments/{deploymentId}/health
DELETE /api/tenants/{tenantId}/ai/deployments/{deploymentId}
GET/PUT /api/tenants/{tenantId}/ai/defaults
```

Knowledge Base 与文档：

```text
GET/POST          /api/knowledge-bases
GET/PUT/DELETE    /api/knowledge-bases/{knowledgeBaseId}
GET               /api/knowledge-bases/{knowledgeBaseId}/members
PUT               /api/knowledge-bases/{knowledgeBaseId}/members
DELETE            /api/knowledge-bases/{knowledgeBaseId}/members/{memberUserId}
GET               /api/knowledge-bases/{knowledgeBaseId}/embedding-index
POST              /api/knowledge-bases/{knowledgeBaseId}/embedding-index/reindex

GET/POST          /api/knowledge-bases/{knowledgeBaseId}/documents
POST              /api/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/retry
POST              /api/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/reindex
POST              /api/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/cancel
GET               /api/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/file
DELETE            /api/knowledge-bases/{knowledgeBaseId}/documents/{documentId}
```

Search、Chat 与 Conversation：

```text
POST   /api/search
POST   /api/chat
POST   /api/conversations
GET    /api/conversations?knowledgeBaseId=...&archived=false
PATCH  /api/conversations/{conversationId}
POST   /api/conversations/{conversationId}/archive
DELETE /api/conversations/{conversationId}/archive
GET    /api/conversations/{conversationId}/messages
GET    /api/conversations/{conversationId}/messages/page?before=...&limit=...
POST   /api/conversations/{conversationId}/messages
POST   /api/conversations/{conversationId}/messages/stream
POST   /api/conversations/{conversationId}/messages/{messageId}/feedback
DELETE /api/conversations/{conversationId}
```

治理中心（以下为主要路由族）：

```text
GET        /api/governance/overview
GET/PUT    /api/governance/knowledge-bases/{knowledgeBaseId}/rag-profile
GET        /api/governance/runs
GET        /api/governance/audit
GET/PUT    /api/governance/model-prices
GET/POST   /api/governance/prompts
PUT        /api/governance/prompts/{promptId}
GET        /api/governance/webhooks/providers
GET/POST   /api/governance/webhooks
PUT/DELETE /api/governance/webhooks/{webhookId}
POST       /api/governance/webhooks/{webhookId}/test
GET/POST   /api/governance/evaluation/datasets
GET/POST   /api/governance/evaluation/datasets/{datasetId}/cases
GET        /api/governance/evaluation/runs
POST       /api/governance/evaluation/datasets/{datasetId}/run
```

其他管理端点：

```text
GET /api/dashboard
GET /api/ingestion/jobs
GET /api/settings/product
GET /swagger
SignalR hub: /hubs/documents
```

路由摘要不是完整 OpenAPI 规范；遇到参数、权限或响应差异时，以当前代码与 Swagger 为准。

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

- 生产必须使用强随机 `AUTH_SIGNING_KEY`，并通过 secret store 注入 `SECRET_PROTECTION_MASTER_KEY`；更换主密钥前需评估现有 Tenant AI Provider Credential 的解密兼容性。
- 不要把 `.env`、S3 secret、模型 API key 或 Webhook secret 提交到 Git。
- API Key 与 Refresh Token 明文只在签发时返回；服务端持久化的是 hash / 受保护的 Secret。
- 生产环境关闭公开注册（`AUTH_ALLOW_REGISTRATION=false`），并使用 HTTPS 与受控的网络出口。
- Tenant Provider 的公网 URL / SSRF 校验不应被反向代理或私网地址绕过；不要把本机、私网、link-local 或 metadata endpoint 作为 Provider / Webhook 目标。
- 知识库检索内容应被视为不可信数据；最终 Citation Gate、RBAC 与确定性检索策略不能被用户内容或 Prompt 覆盖。
- S3 bucket 建议保持私有，通过本 API 做权限检查后流式下载。
- 生产迁移建议使用单独 deployment identity；应用运行账号不应长期持有 DDL 权限。
- 新增或修改功能后同步校准相关文档；纯文档变更不需要伪造运行时 Changelog 条目。
