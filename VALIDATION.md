# KnowledgeRag 验证指南

本文记录当前仓库可执行的验证命令、GitHub Actions 的真实覆盖范围，以及本地如何启动 native backend 测试环境。CI 结果必须对应待合并分支的最新 HEAD；旧 commit 的绿色结果不能替代新 commit 的验证。

## 1. 本地环境要求

- .NET 10 SDK（以根目录 `global.json` 为准）。
- Node.js 22 与 npm。
- Docker Engine / Docker Compose（用于 PostgreSQL、RabbitMQ、Qdrant、MinIO 或 OpenTelemetry Collector）。
- 可访问的 NuGet / npm package registry；需要真实模型调用的手工 smoke test 还需有效的 OpenAI-compatible Provider 配置。

## 2. 前端 Build / Unit / Accessibility / E2E

从仓库根目录执行：

```bash
cd src/KnowledgeRag.Admin
npm ci
npm run build
npm test
npm run test:e2e
```

对应内容：

- `npm run build`：TypeScript project build + Vite production build。
- `npm test`：Vitest 单元 / 组件与 accessibility 回归。
- `npm run test:e2e`：Playwright 浏览器流程。

Chat / assistant-ui 相关回归应覆盖 message-scoped Citation、Evidence Panel、SSE Workflow Timeline、history pagination、feedback、rename、archive / restore，以及桌面 / 移动端布局。

## 3. .NET Restore / Build / Integration Tests

从仓库根目录执行：

```bash
dotnet restore KnowledgeRag.NonAzure.sln
dotnet build KnowledgeRag.NonAzure.sln -c Release --no-restore
dotnet test KnowledgeRag.NonAzure.sln -c Release --no-build
```

当前测试项目为：

```text
tests/KnowledgeRag.Api.IntegrationTests
```

通过 `WebApplicationFactory<Program>` 覆盖实际 ASP.NET Core pipeline。当前回归覆盖包括：

- 注册 / 登录、Refresh Token rotation 与 replay detection、Tenant / API Key / KB 权限。
- SQLite BM25 查询兼容性。
- RAG Workflow policy、Citation Contract / Final Answer Gate、no-answer 与 citation fail-closed。
- Evaluation scoring / expected sources。
- Tenant AI Provider Connection / Deployment / defaults / routing。
- Governance API、usage accounting、Webhook URL policy、channel adapters、delivery timeout。
- Embedding index generation、vector point ID 与跨 generation 的隔离语义。

## 4. 当前 GitHub Actions CI

工作流：`.github/workflows/ci.yml`。

### `build-test`

当前执行：

1. 前端 `npm ci`。
2. `npm run build`。
3. `npm test`。
4. 安装 Playwright Chromium。
5. `npm run test:e2e`。
6. `dotnet restore KnowledgeRag.NonAzure.sln`。
7. Release build。
8. `dotnet test KnowledgeRag.NonAzure.sln -c Release --no-build`，并上传 TRX 测试结果。

具体步骤由变更分类控制：main push、手动 / nightly workflow 和 Ready for Review PR 执行完整验证；Draft PR 可根据改动范围跳过不相关的重型步骤。

### `native-backends-smoke`

当前 native job 实际执行：

1. 启动 PostgreSQL（pgvector 镜像）、RabbitMQ 与 OpenTelemetry Collector。
2. 使用 PostgreSQL、pgvector、PostgreSQL FTS、CAP/RabbitMQ 配置构建并启动应用。
3. 检查 `/api/health/ready` 和 `/api/health`。
4. 验证 PostgreSQL 中 CAP schema 存在。
5. 运行 `Category=ExternalBackend` 集成测试，通过项目代码验证 pgvector 与 PostgreSQL FTS。
6. 失败时收集容器日志，最后关闭测试服务。

当前 CI 的 native job 使用 Local storage，**不会启动 MinIO**。MinIO / S3 的 round-trip 测试由 `RUN_EXTERNAL_S3_TESTS=1` 控制，需要单独准备 MinIO 端点和凭据。

## 5. 本地 native backend smoke

先准备环境变量并启动依赖：

```bash
docker compose --profile postgres --profile cap --profile otel up -d postgres rabbitmq otel-collector
```

确认 PostgreSQL 和 RabbitMQ ready 后，再使用当前 Compose 配置启动应用：

```bash
DATABASE_PROVIDER=PostgreSql \
SEARCH_VECTOR_BACKEND=pgvector \
SEARCH_LEXICAL_BACKEND=postgres_fts \
CAP_TRANSPORT=rabbitmq \
docker compose --profile postgres --profile cap --profile otel up -d --build app
```

然后运行外部后端测试。测试进程需要能从宿主机访问 PostgreSQL，并设置测试专用连接串；以下以 Compose 默认数据库名 / 用户为例，请按自己的密码调整：

```bash
RUN_EXTERNAL_BACKEND_TESTS=1 \
EXTERNAL_DATABASE_CONNECTION_STRING='Host=localhost;Port=5432;Database=knowledge_rag;Username=knowledge_rag;Password=...' \
dotnet test tests/KnowledgeRag.Api.IntegrationTests/KnowledgeRag.Api.IntegrationTests.csproj \
  -c Release --filter Category=ExternalBackend
```

测试环境的 `EXTERNAL_DATABASE_CONNECTION_STRING` 必须指向可访问的 PostgreSQL；应用容器内的连接串则应使用 Docker network 服务名（例如 `Host=postgres`）。不要将真实密码提交到仓库。

测试结束后清理：

```bash
docker compose --profile postgres --profile cap --profile otel down -v
```

注意：`down -v` 会删除 Compose 管理的测试卷，不要对包含需保留数据的开发 / 生产环境执行。

## 6. MinIO / S3 round-trip（可选）

先启动 MinIO：

```bash
docker compose --profile s3 up -d minio
```

然后向测试进程提供 `S3_ENDPOINT`、`S3_ACCESS_KEY`、`S3_SECRET_KEY`、`S3_BUCKET` 等测试配置，并设置：

```bash
RUN_EXTERNAL_S3_TESTS=1
```

测试会验证 S3-compatible storage 的写入、读取和删除。仅设置 `RUN_EXTERNAL_BACKEND_TESTS=1` 不代表 MinIO 测试已经运行。

## 7. 推荐验证顺序

对于普通代码改动：

1. 运行改动相关的 Vitest / xUnit 测试。
2. 运行对应完整前端或后端测试。
3. 确认 Draft PR 最新 HEAD 的 required checks 通过。
4. 转 Ready 后等待完整 CI。
5. 合并后确认 main push CI 最新 HEAD 的 `build-test` 和 `native-backends-smoke` 均成功。

对于文档-only 改动，至少检查 Markdown 链接、命令、路由、配置键与源码一致；Draft 阶段可走 docs-only 快速路径，但 Ready PR 仍必须通过完整 CI。

## 8. 变更类型与必需验证

| 改动范围 | 最低验证要求 |
|---|---|
| 纯 Markdown / 文档 | 校对链接、命令、配置键与实际实现；Ready CI 全量通过 |
| React / TypeScript / Chat UI | production build、Vitest / accessibility、Playwright |
| .NET 业务逻辑 / API | Release build、完整 integration tests |
| EF Core Model / migration | SQLite tests + PostgreSQL native smoke；核对 migration history 与 Model Snapshot |
| Search / Embedding / vector IDs | BM25 / RRF / reranker 相关测试、generation 回归、native pgvector / FTS |
| CAP / RabbitMQ / SignalR | native smoke、readiness、CAP schema 与任务状态 / 通知行为 |
| Provider / cost / governance | Tenant AI Provider API、route / fallback、usage / pricing、Audit / Webhook 回归 |
| Docker / CI / runtime configuration | 检查 Compose、环境变量映射与工作流；执行完整 CI |

## 9. 验证时的安全边界

- 不在测试输出、截图或 artifact 中保存真实 API Key、Credential、Webhook Secret。
- 不将模型生成的 chain-of-thought 或系统 Prompt 当作测试产物。
- 不因 CI 失败而关闭测试、吞掉异常或扩大跳过条件。
- 如果外部服务 / 模型不可用，明确记录未执行的测试；不能将“测试跳过”描述为“真实后端验证通过”。
- 生产 migration 应在 staging 演练；旧 `EnsureCreated` 数据库升级见 [MIGRATION_GUIDE.md](MIGRATION_GUIDE.md)。

## 10. 相关文档

- [开发流程与 CI 策略](DEVELOPMENT_WORKFLOW.md)
- [系统架构](ARCHITECTURE.md)
- [CAP / SignalR / Embedding Index](CAP_SIGNALR_INGESTION.md)
- [AI Provider Configuration](docs/AI_PROVIDER_CONFIGURATION.md)
- [RAG Agent Workflow](docs/RAG_AGENT_WORKFLOW.md)
