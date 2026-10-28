# Validation

## 当前 Unreleased — RAG Production Readiness v3

本轮新增 / 调整的验证重点：

- Final Answer Citation Gate：有效 `[E#]` 归一化、非法 Evidence ID fail-closed、知识型无引用回答 fail-closed、no-answer abstention、非知识回答。
- Reviewer 之后、SSE / DB 之前执行统一 Citation validation，客户端不再接收未经最终 Citation Gate 的模型文本。
- RAG Profile 在同一 scoped request 内解析为单一 execution snapshot，Reservation / Agent / Evaluation 使用同一模型与策略快照。
- Multi-query 原始查询保持 mandatory，Planner 扩展查询改为 best-effort；扩展查询失败不会拖垮仍有原始检索结果的整轮。
- Evidence context 增加总 token budget 与单文档 token budget，按最终排序确定性截断。
- Reviewer repair 的跨轮 Evidence merge 不直接比较 round-local reranker score，改用合并后的 fusion 归一化排序。
- Evaluation v3 增加 expected source labels、Hit Rate、Recall@K、MRR、nDCG，并与 citation / grounding / no-answer 分层统计。
- 消息历史增加 bounded server pagination；SQLite 使用 `julianday(CreatedAt)` 在数据库内完成时间比较 / 排序，PostgreSQL 使用正常 server-side `ORDER BY / LIMIT`。
- assistant-ui External Store Runtime 增加 `hasEarlier / onLoadEarlier`、Feedback Adapter、Thread List Adapter、rename / archive / restore / delete。
- Chat 继续保持 Thread、Conversations、Source Panel 独立滚动，未恢复 browser global scrolling。
- Chat 的受限富文本渲染只处理文本结构 / code fence / trusted `[n]` citation，不解析 raw HTML，不展示 Chain of Thought。
- Playwright product flow 更新为分页消息 API，并覆盖 inline citation、Source Panel、feedback、rename、archive / restore。

新增 C# deterministic regression tests：

```text
FinalAnswerPolicyTests
EvaluationRetrievalScoringTests
RagWorkflowPolicyTests (cross-round merge regression)
```

本轮没有新增 EF Core schema / migration。Conversation archive 使用过滤掉的内部 `__meta` ChatMessage 事件持久化；Feedback 复用现有 Audit Log；Evaluation v3 advanced expectation 使用向后兼容的 versioned envelope 存入现有字段。

## 当前环境限制

当前执行容器无法解析 GitHub / NuGet / NPM 外网 DNS，且没有可用于本仓库的本地 clone，因此本轮不能诚实宣称在容器内执行过完整 CLI build/test。代码通过 GitHub Repository Connector 写入独立分支，最终验证以该分支 **最新 HEAD 对应的真实 GitHub Actions** 为准。

必须由 CI 覆盖：

```bash
npm run build
npm run test
npm run test:e2e
dotnet restore KnowledgeRag.NonAzure.sln
dotnet build KnowledgeRag.NonAzure.sln -c Release --no-restore
dotnet test KnowledgeRag.NonAzure.sln -c Release --no-build
```

Ready / main 还必须覆盖 SQLite、PostgreSQL、RabbitMQ、CAP、pgvector、PostgreSQL FTS 与 production frontend build。新 commit 触发 concurrency cancel 时，只认最新 HEAD 的 `build-test` / `native-backends-smoke` 和完整 Ready CI。

---

## 项目历史验证基线

- EF Core Migration + design-time DbContext factory
- Refresh Token rotation/replay detection
- Tenant / TenantMember
- Tenant API Key + scopes + optional KnowledgeBase binding
- Qdrant / pgvector vector backend abstraction
- C# BM25 / PostgreSQL FTS lexical backend abstraction
- Local / S3/MinIO storage abstraction
- OpenTelemetry tracing + metrics + OTLP
- ASP.NET Core WebApplicationFactory integration tests
- External backend integration tests（pgvector / PostgreSQL FTS / MinIO）
- GitHub Actions CI（restore/build/test + production-backend smoke）
- 全项目升级到 .NET 10 (`net10.0`)；Docker / CI / EF Core 10 / ASP.NET Core 10 同步升级
- 前端改造为 React 19 + Vite 8 + Tailwind CSS 4 + Shadcn/Radix + TanStack Query/Router 管理后台

## 已执行的静态检查

- JSON 配置解析
- React/TypeScript 源码语法扫描（全量 `.ts/.tsx` 无 TypeScript parser syntax diagnostics）
- 前端 `package.json` / `tsconfig.json` / `components.json` JSON 解析
- C# 文件括号结构检查
- Solution 文件结构检查
- 旧 `EnsureCreated` / 旧服务签名引用搜索
- Azure SDK / Azure Identity 残留搜索
- 新后端配置与 Docker Compose 配置人工核对
- CI YAML / OpenTelemetry Collector YAML 解析
- ExternalBackend 测试源码结构检查

## 推荐本地验证

在具备 .NET 10 SDK、Node 22、Docker 与网络依赖的环境执行：

```bash
dotnet restore KnowledgeRag.NonAzure.sln
dotnet build KnowledgeRag.NonAzure.sln -c Release --no-restore
dotnet test KnowledgeRag.NonAzure.sln -c Release --no-build
cd src/KnowledgeRag.Admin
npm ci
npm run build
npm run test
npm run test:e2e
```

随后分别验证两套推荐组合：

```bash
# default
docker compose up -d --build

# native PostgreSQL search
docker compose --profile postgres up -d --build

# MinIO
docker compose --profile s3 up -d --build
```

从上一版 `EnsureCreated` 数据库升级时请先阅读 `MIGRATION_GUIDE.md`。

## shadcn-admin 前端改造验证

`src/KnowledgeRag.Admin` 已完成：

- `package.json` / `tsconfig.json` / `components.json` JSON 解析基线。
- Docker Compose / GitHub Actions / OTel Collector YAML 解析基线。
- Dockerfile 包含 Node 22 frontend stage、`npm run build`、`dist -> wwwroot` 复制。
- CI 包含 Node 22 setup 与 frontend build step。
- Chat 使用 assistant-ui External Store Runtime，同时由应用自己的 API / TanStack Query 保存会话与消息。

## 后端目录 / 架构重构验证

后端结构保持：

```text
KnowledgeRag.Domain
KnowledgeRag.Application
KnowledgeRag.Infrastructure
KnowledgeRag.Api
```

架构约束：

- Solution 包含 Domain / Application / Infrastructure / Api / IntegrationTests。
- 所有 C# 项目继续使用 `net10.0`。
- Domain 无本解决方案内部项目依赖。
- Application 只引用 Domain。
- Infrastructure 只引用 Application + Domain。
- Api 保持 composition / endpoint 边界，不把检索、Agent、Persistence 逻辑重新堆回 Endpoint。
- EF Core migrations 位于 `KnowledgeRag.Infrastructure/Persistence/Migrations`。
- 搜索、Conversation、Governance 等模块继续通过 Infrastructure DI 注册。

开发约定详见根目录 `ARCHITECTURE.md` 与 `DEVELOPMENT_WORKFLOW.md`。
