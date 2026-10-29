# 从 Azure-oriented 架构到当前 KnowledgeRag 实现

KnowledgeRag 保留 Microsoft Agent Framework 作为 Agent / Workflow 执行框架，但不要求 Azure 专有模型、搜索、存储或身份服务。当前系统通过标准 API 与可替换后端组合部署。

## 1. 组件映射

| Azure-oriented component | 当前实现 |
|---|---|
| Azure OpenAI / Foundry | Tenant-managed OpenAI-compatible Provider Connection + Model Deployment；平台级 `AI` 配置作为 fallback |
| Azure AI Search | Qdrant / PostgreSQL pgvector + C# BM25 / PostgreSQL FTS + RRF + reranker |
| Azure Blob Storage | Local filesystem 或 S3-compatible storage（MinIO / AWS S3 等） |
| Azure Identity | 本地用户、JWT、旋转式 Refresh Token、Tenant API Key 与 RBAC |
| Azure-hosted telemetry | OpenTelemetry traces / metrics / OTLP |
| Azure Service Bus / event worker | CAP in-memory transport（本地）或 PostgreSQL + RabbitMQ（持久化部署） |
| ad-hoc schema creation | EF Core migrations，支持 SQLite / PostgreSQL |
| hosted agent orchestration | Microsoft Agent Framework Workflow + 确定性 Retrieval / RRF / Citation Gate |

当前项目不依赖 Azure SDK、`DefaultAzureCredential`、`AIProjectClient`、Azure AI Search SDK 或 Azure Blob SDK。模型服务仍可通过符合接口契约的兼容网关访问，但这不等于项目内置了相应厂商的专用 SDK。

## 2. 当前平台能力

- Tenant / TenantMember、Knowledge Base RBAC、带 scopes 与可选 KB binding 的 API Key。
- JWT Access Token、Refresh Token rotation / replay detection。
- CAP 异步文档摄取、SignalR 进度通知、文档 retry / reindex / cancel。
- KB-wide Embedding Index Generation：staging、manifest 校验、事务切换 active generation，失败时保留上一代索引。
- Hybrid Search：Vector + lexical + deterministic RRF + reranker。
- Planner → Multi-query Retrieval → Answer → Reviewer / bounded Repair → Final Citation Gate。
- Tenant AI Provider Connection、Model Deployment、health / explicit fallback、Tenant Defaults、KB role routing。
- RAG Evaluation、KB RAG Profile、Run Trace、Token / Cost / Budget / Daily quota、Model Pricing、Audit Log。
- Prompt / Agent 管理、Specialist Agent-as-Tool、Webhook Automation 与渠道适配。
- React / Vite / assistant-ui Chat、SSE Workflow Timeline、message-scoped Citation / Evidence Panel、bounded conversation history pagination。
- OpenTelemetry traces / metrics、Swagger、xUnit / WebApplicationFactory、Vitest / Playwright 与 native backend smoke。

## 3. 前端与 API

旧的单文件演示 UI 已被独立管理后台替代：

```text
src/KnowledgeRag.Admin
  React 19 / Vite 8 / TypeScript
  Tailwind CSS 4 / Shadcn-Radix
  TanStack Query / Router
  assistant-ui ExternalStoreRuntime
```

前端通过本项目的 API 访问 Auth、Tenant / RBAC、Knowledge Base、Documents、Search、Conversation、Tenant AI Provider 和 Governance 功能。Dockerfile 使用 Node 22 多阶段构建前端，并把构建结果复制到 ASP.NET Core 静态目录。

## 4. 后端分层

当前 solution 使用：

- `KnowledgeRag.Domain`：领域实体与枚举。
- `KnowledgeRag.Application`：跨层 contracts、安全上下文与可替换抽象。
- `KnowledgeRag.Infrastructure`：EF Core、AI Provider、Search、Document ingestion、Governance、Storage、Messaging 与业务实现。
- `KnowledgeRag.Api`：Minimal API endpoints、middleware、认证、SignalR、Swagger 与 telemetry composition。

原先的 `Api/Data`、`Api/Models`、`Api/Options`、`Api/Services`、`Api/Migrations` 不再是当前的模块位置。具体结构见 [ARCHITECTURE.md](ARCHITECTURE.md)。

## 5. 部署注意事项

- 本地默认组合为 SQLite + Qdrant + BM25 + Local storage + CAP in-memory。
- PostgreSQL + pgvector / PostgreSQL FTS 需要启用 `postgres` profile，并设置对应的数据库和 SearchBackend 配置。
- 多实例 / 生产异步消息场景应使用 PostgreSQL + RabbitMQ CAP 配置；仅启用 PostgreSQL profile 不会自动启动 RabbitMQ。
- Tenant Provider Credential 与 Webhook Secret 需要独立保护密钥和安全的 secret management。
- 修改 Embedding Deployment 不会自动把已有向量转换到新空间；需执行 KB 的 Embedding Index Reindex。
- 生产数据库升级应遵循 migration history 与实际 schema；不要将旧 `EnsureCreated` 数据库直接当作最新 migration schema。

## 6. 进一步阅读

- [系统架构](ARCHITECTURE.md)
- [当前 AI 供应商配置与上线实施指南](docs/AI_PROVIDER_CONFIGURATION.md)
- [RAG Agent Workflow](docs/RAG_AGENT_WORKFLOW.md)
- [CAP / SignalR / Embedding Index](CAP_SIGNALR_INGESTION.md)
- [数据库迁移指南](MIGRATION_GUIDE.md)
- [验证指南](VALIDATION.md)
- [文档索引](docs/README.md)
