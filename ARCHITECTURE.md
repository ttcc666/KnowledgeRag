# KnowledgeRag 系统架构（当前实现）

> 本文以当前仓库的项目、目录、Endpoint、DI 注册和 EF Core Model 为准。若本文与代码不一致，先修正文档，不要依据旧目录结构添加新模块。

## 1. 总体结构

解决方案使用 **Pragmatic Clean Architecture + Feature Modules**：稳定业务模型与契约在内层，数据库、模型服务、向量库和文件存储等实现放在 Infrastructure，HTTP / SignalR 入口放在 Api。

```text
React 19 / Vite / assistant-ui
            │ HTTP / SSE / SignalR
            ▼
KnowledgeRag.Api
  Endpoints / Middleware / Auth / Hub
            │
            ▼
KnowledgeRag.Infrastructure
  Identity / Tenancy / KnowledgeBases / Documents
  AI Providers / Search / Conversations / Governance
  Persistence / Messaging / Storage / Observability
            │
            ├── PostgreSQL or SQLite
            ├── Qdrant or PostgreSQL pgvector
            ├── BM25 or PostgreSQL FTS
            ├── Local filesystem or S3-compatible storage
            ├── CAP in-memory or RabbitMQ transport
            └── OpenAI-compatible model endpoints
```

## 2. 项目依赖边界

```text
KnowledgeRag.Api ───────────────► KnowledgeRag.Infrastructure
       │                                  │
       ├──────────────► Application ◄─────┘
       │                    │
       └────────────────────┴────────────► Domain
```

当前项目引用约束：

- `KnowledgeRag.Domain`：领域实体、状态和值对象，不依赖本解决方案中的其他项目。
- `KnowledgeRag.Application`：跨层 contracts、actor/security context、可替换能力抽象；只引用 Domain。
- `KnowledgeRag.Infrastructure`：EF Core、模型客户端、搜索、存储、CAP、治理与业务服务；引用 Application + Domain。
- `KnowledgeRag.Api`：Minimal API endpoints、middleware、认证配置、SignalR hub、Swagger、Telemetry composition；引用 Application + Infrastructure + Domain。
- `src/KnowledgeRag.Admin`：独立的 React / TypeScript 管理后台，通过 HTTP、SSE 和 SignalR 使用 API。

禁止把数据库查询、检索算法或 Agent 工作流塞回 `Program.cs` / Endpoint。新功能应先定位其业务边界，再增加必要的契约、实现和路由。

## 3. Domain 与 Application

### Domain entities

实体位于 `src/KnowledgeRag.Domain/Entities`，当前按业务域拆分：

- `IdentityEntities.cs`：用户、Refresh Token 等身份数据。
- `TenantEntities.cs`：Tenant、成员与 API Key 相关数据。
- `KnowledgeBaseEntities.cs`：Knowledge Base、成员关系与 RAG Profile 关联。
- `DocumentEntities.cs`：文档与 chunks。
- `ConversationEntities.cs`：Conversation、ChatMessage / Agent session 相关数据。
- `GovernanceEntities.cs`：Evaluation、Run Trace、Audit、Prompt、Webhook、Model Pricing 等治理实体。
- `AiProviderEntities.cs`：Tenant Provider Connection、Model Deployment、Tenant AI Defaults。
- `KnowledgeBaseIndexGenerationEntities.cs`：Embedding generation、generation 文档状态与 active-generation 元数据。

新增实体时应同步检查 `AppDbContext`、Model Snapshot、migration、跨 SQLite/PostgreSQL 的类型与查询兼容性。

### Application contracts

路径：`src/KnowledgeRag.Application`

- `Contracts/AuthContracts.cs`
- `Contracts/TenantContracts.cs`
- `Contracts/KnowledgeBaseContracts.cs`
- `Contracts/DocumentProcessingContracts.cs`
- `Contracts/SearchContracts.cs`
- `Contracts/ConversationContracts.cs`
- `Contracts/AiProviderContracts.cs`
- `Contracts/ProductOperationsContracts.cs`
- `Security/ActorContext.cs`
- `Abstractions/SearchAbstractions.cs`
- `Abstractions/DocumentProcessingAbstractions.cs`

Application contracts 不应依赖 ASP.NET Core 的 `HttpContext`、EF Core 或具体 Provider。仅服务某个 Endpoint 的请求类型可以留在 Api 层；跨层使用的业务契约应放在 Application。

## 4. Infrastructure 模块

路径：`src/KnowledgeRag.Infrastructure`

| 模块 | 当前职责 |
|---|---|
| `Identity` | 注册/登录、JWT、Refresh Token rotation / replay detection、API Key authentication |
| `Tenancy` | Tenant、成员权限、Tenant AI Provider Connections / Deployments / Defaults |
| `KnowledgeBases` | KB CRUD、KB RBAC、访问校验 |
| `Documents` | 解析、切片、Embedding、CAP subscriber、单文档 retry/reindex/cancel、KB generation 重建 |
| `AI` | OpenAI-compatible client、Credential protection、Embedding / Reranker endpoint 解析 |
| `Search` | Vector Store、BM25、PostgreSQL FTS、Hybrid Search、RRF、Reranker、Multi-query |
| `Conversations` | Agent Framework Workflow、Answer policy、Citation Contract、会话历史和消息流 |
| `Governance` | RAG Profile snapshot、预算与 usage、Run Trace、Evaluation、Pricing、Audit、Prompt、Webhook |
| `Persistence` | `AppDbContext`、设计时 factory、数据库健康检查、EF Core migrations |
| `Messaging` | CAP 配置与消息集成 |
| `Storage` | Local filesystem、S3-compatible / MinIO |
| `Observability` | ActivitySource、Dashboard 汇总、业务指标 |
| `Configuration` | 强类型 Options |

模块通过各自的 `ServiceCollectionExtensions.cs` 注册，再由 `DependencyInjection.cs` 汇总。添加实现时应扩展对应模块，避免再造一个巨型 DI 文件。

## 5. 搜索与 RAG Workflow

### Deterministic Hybrid Search

```text
User query
  ├─ Embedding → Qdrant / pgvector
  └─ lexical → C# BM25 / PostgreSQL FTS
                │
                ▼
        deterministic RRF / dedupe
                │
                ▼
             reranker
                │
                ▼
             Top-K Evidence
```

可替换实现通过 Application abstractions 注入：

- `IVectorStore`：`QdrantVectorStore` / `PgVectorStore`
- `ILexicalSearchService`：`Bm25SearchService` / `PostgresFtsSearchService`

Vector、词法召回、RRF、去重、权限过滤、证据 token budget、Citation 校验与重试上限由代码确定性控制，不交由 Agent 自由决定。

### Agent Workflow

```text
RagRuntimeProfileService (scoped execution snapshot)
        ↓
Budget reservation / route resolution
        ↓
Planner → Multi-query Retrieval → Answer (+ configured specialists)
        ↓
Reviewer → bounded repair / safe abstention
        ↓
Final Citation Gate → SSE / persistence / usage settlement
```

- 原始用户 query 是必须执行的检索路径；Planner 扩展 query 是 best-effort。
- Reviewer 只负责结构化证据审核；它不能替代最终 Citation Gate。
- 模型生成的 `[E#]` 先针对本轮 Evidence 校验，再归一化为用户引用 `[n]`；非法引用不会作为可信 Source 暴露。
- Answer 正文在 workflow 和 Citation Gate 完成前不会向用户流出；SSE Timeline 可以实时报告安全的结构化阶段状态。
- RAG Profile、模型路由、Prompt、预算和限额在 request scope 内解析为一致快照，避免一次运行中途混用新旧配置。

详细执行语义见 [RAG Agent Workflow](docs/RAG_AGENT_WORKFLOW.md)。

## 6. Tenant AI Provider 与路由

Tenant 管理的模型配置采用分层模型：

```text
Provider Connection (Base URL + protected credential)
        ↓
Model Deployment (remote model + type + health + explicit fallback)
        ↓
Tenant Defaults
        ↓
Knowledge Base RAG role bindings
        ↓
RAG execution snapshot / usage pricing
```

- 当前 Provider Catalog 的类型是 `openai-compatible`；具体 Chat / Embedding / Rerank 能力取决于目标端点实现的 API 契约。
- Chat / Rerank 可以按 Deployment 的持久化 health 与显式同类型 fallback 链解析；不会任意跳到其他 Provider。
- Embedding generation 固定保存创建 generation 时解析的模型与 Deployment 快照。Embedding health 不等于自动 fallback。
- 修改 Tenant Embedding target 不会重写现有向量；需要执行 KB 的 `embedding-index/reindex`，成功后才会原子切换 active generation。
- Deployment 定价优先于旧的 `(tenant, model-name)` 价格记录，以便区分不同 Provider 上同名模型。

完整配置步骤、安全限制、回滚与 API 表见 [AI Provider Configuration](docs/AI_PROVIDER_CONFIGURATION.md)。

## 7. 文档索引与异步 ingestion

普通文档摄取流程：

```text
POST /api/knowledge-bases/{id}/documents
  → persist source file + Queued state
  → CAP event: knowledge.document.index.requested.v1
  → DocumentIndexingSubscriber
  → parse / chunk / embed / vector upsert / DB chunk persistence
  → authenticated SignalR progress at /hubs/documents
```

CAP 使用 in-memory 模式适合开发/测试；PostgreSQL + RabbitMQ 模式提供持久化消息集成。SignalR 是进度通知通道，不是任务状态的持久化真相源。

KB-wide Embedding Reindex 使用 generation ID 构建新的 staging chunks / vectors；只有 generation 中所有源文档都成功且 manifest 仍匹配时，才会通过事务更新 `ActiveIndexGenerationId`。失败时清理 staging generation，保留上一代 active index。

详见 [CAP + SignalR ingestion](CAP_SIGNALR_INGESTION.md)。

## 8. HTTP API 入口

Endpoint 以 feature module 注册，见 `src/KnowledgeRag.Api/Endpoints/EndpointRegistration.cs`：

- `HealthEndpoints`：`/api/health`、`/api/health/ready`
- `AuthEndpoints`：`/api/auth/*`
- `TenantEndpoints`：Tenant / members / API Keys / AI Provider routes
- `KnowledgeBaseEndpoints`：KB / members / embedding-index routes
- `DocumentEndpoints`：documents / retry / reindex / cancel / file
- `SearchEndpoints`：`POST /api/search`
- `ConversationEndpoints`：Conversation CRUD、bounded message pagination、feedback、SSE、`POST /api/chat`
- `DashboardEndpoints`：dashboard、ingestion jobs、product settings
- `GovernanceEndpoints`：RAG Profile、runs、audit、pricing、prompts、webhooks、evaluation
- `DocumentProcessingHub`：`/hubs/documents`

API 请求 / 响应与权限细节以当前 Endpoint、Application Contracts 和 `/swagger` 为准。

## 9. Persistence 与 migration

EF Core context、design-time factory、health check 与 migration 位于：

```text
src/KnowledgeRag.Infrastructure/Persistence/
├── AppDbContext.cs
├── AppDbContextFactory.cs
├── DatabaseHealthService.cs
└── Migrations/
```

当前 migration 包含初始生产 schema、Model 同步、治理套件、Webhook 渠道、Tenant AI Provider、Embedding generation 与 Deployment routing governance。不要在文档或部署脚本中假设“只有一个 InitialProduction migration”。

SQLite 与 PostgreSQL 的 DateTimeOffset、UUID / FK 类型、decimal 聚合、FTS 与 pgvector 行为存在差异；新增查询或 migration 时必须覆盖相应 Provider。生产升级步骤见 [Migration Guide](MIGRATION_GUIDE.md)。

## 10. Frontend 结构

前端位于 `src/KnowledgeRag.Admin/src`，当前主要 feature：

- `features/auth`：登录 / 注册
- `features/chat`：assistant-ui Chat、Timeline、Citation / Evidence、Conversation actions
- `features/knowledge-bases`：KB 列表、详情、文档和索引操作
- `features/tenants`：Tenant、成员、API Key、Provider 配置
- `features/governance`：RAG Profile、Trace、Evaluation、Cost、Audit、Prompt、Webhook
- `features/operations`：ingestion / pipeline 运维视图
- `features/dashboard`：平台概览

UI 运行时使用 assistant-ui `ExternalStoreRuntime`；TanStack Query 与服务端 API 是会话和业务数据的来源。较长对话通过服务端分页加载，不应退回一次加载全部历史。

## 11. 测试与架构约定

当前后端测试项目：

```text
tests/KnowledgeRag.Api.IntegrationTests
```

使用 `WebApplicationFactory<Program>` 测试真实 HTTP pipeline，并包含 Citation / workflow / evaluation / provider / webhook / SQLite BM25 等回归测试；`Category=ExternalBackend` 覆盖 pgvector / PostgreSQL FTS 等外部后端。

本地验证命令与 CI 分层见 [Validation](VALIDATION.md)，分支、Draft / Ready PR 与合并门禁见 [Development Workflow](DEVELOPMENT_WORKFLOW.md)。

## 12. 新增功能时的检查清单

- [ ] 代码位于正确层，不让 Api 直接访问 DbContext。
- [ ] 新跨层 DTO 放在 Application Contracts，Endpoint 专属请求类型除外。
- [ ] 更新 DI 注册与 Options / 环境变量文档。
- [ ] 数据库改动增加 migration，并确认 SQLite 与 PostgreSQL 的 schema / 查询行为。
- [ ] 外部依赖或路由改动增加集成测试；关键确定性策略增加单元回归。
- [ ] 前端改动增加 Vitest / accessibility / Playwright 覆盖。
- [ ] 同步 README、对应专项指南和文档索引；运行时行为变化同步更新 CHANGELOG。
