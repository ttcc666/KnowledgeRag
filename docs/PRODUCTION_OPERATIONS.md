# Production Operations / 生产运维 Runbook

本文把 KnowledgeRag 当前实现中的数据库、搜索、消息、存储、模型 Provider、健康检查、迁移、备份与故障排查串成一套生产运维基线。

> `docker-compose.yml` 是开发 / 单机验证参考，不是高可用编排模板。生产环境可以使用 Kubernetes、Nomad、VM / systemd 或托管数据库，但需要保持本文描述的依赖和安全边界。

## 1. 推荐生产拓扑

常见生产组合：

```text
Load Balancer / TLS termination
          ↓
KnowledgeRag.Api + React static UI
          │
          ├─ PostgreSQL
          │    ├─ application schema
          │    ├─ EF migration history
          │    ├─ CAP schema
          │    ├─ optional pgvector
          │    └─ optional PostgreSQL FTS
          │
          ├─ RabbitMQ (CAP transport)
          ├─ Qdrant（如果 vector backend=qdrant）
          ├─ S3-compatible object storage（推荐生产文件存储）
          ├─ OpenAI-compatible Provider endpoints
          └─ OpenTelemetry Collector
```

中文知识库常见：

```text
PostgreSQL + pgvector + C# BM25 + S3 + RabbitMQ
```

如果内容主要是英文 / 空格分词，可以考虑：

```text
PostgreSQL + pgvector + PostgreSQL FTS + S3 + RabbitMQ
```

## 2. 关键环境变量

以根目录 `.env.example` 和 `docker-compose.yml` 为准。

### Database

```env
DATABASE_PROVIDER=PostgreSql
DATABASE_CONNECTION_STRING=Host=postgres;Port=5432;Database=knowledge_rag;Username=knowledge_rag;Password=...
DATABASE_AUTO_MIGRATE=false
```

生产建议由 deployment identity 执行 migration bundle，并把应用的 `DATABASE_AUTO_MIGRATE` 设为 `false`。开发 / 小型单实例环境可以保留自动 migration。

### Search

```env
SEARCH_VECTOR_BACKEND=pgvector      # qdrant | pgvector
SEARCH_LEXICAL_BACKEND=bm25         # bm25 | postgres_fts
POSTGRES_FTS_CONFIG=simple
```

如果使用 Qdrant：

```env
QDRANT_COLLECTION=knowledge_chunks
```

Compose 当前把 Qdrant endpoint 固定映射为容器内：

```text
http://qdrant:6333
```

非 Compose 部署需要通过 ASP.NET Core 配置覆盖 `Qdrant` section。

### CAP / RabbitMQ

```env
CAP_TRANSPORT=rabbitmq
CAP_FAILED_RETRY_COUNT=20
RABBITMQ_HOST=rabbitmq
RABBITMQ_PORT=5672
RABBITMQ_USER=knowledge_rag
RABBITMQ_PASSWORD=...
RABBITMQ_VHOST=/
CAP_EXCHANGE=knowledge-rag.cap
```

`CAP_TRANSPORT=inmemory` 只适合本地 / 测试，不应作为多实例生产队列。

### Auth / Secrets

```env
AUTH_SIGNING_KEY=<strong-random-secret>
AUTH_ALLOW_REGISTRATION=false
AUTH_ACCESS_TOKEN_MINUTES=60
AUTH_REFRESH_TOKEN_DAYS=30
SECRET_PROTECTION_MASTER_KEY=<independent-strong-random-secret>
# Optional during a controlled key rotation:
SECRET_PROTECTION_PREVIOUS_MASTER_KEY_1=<previous-master-key>
SECRET_PROTECTION_LEGACY_AUTH_SIGNING_KEY_1=<old-auth-key-used-by-legacy-ciphertext>
```

Production 启动会拒绝已知 SigningKey 占位值、缺失/过短的 MasterKey，以及 JWT SigningKey 与 MasterKey 相同的配置。MasterKey 必须由 Secret Store 注入，不能依赖开发兼容回退。

### API request throttling

- 默认全局限流为每个 API Key / 已认证用户 / 匿名 IP 每 60 秒 120 次。
- 登录、注册和刷新令牌端点默认进一步限制为每个来源 IP 每 60 秒 10 次；此上限不会高于全局 `RateLimiting:PermitLimit`。
- Compose 部署可通过 `RATE_LIMITING_PERMIT_LIMIT`、`RATE_LIMITING_AUTH_PERMIT_LIMIT` 与 `RATE_LIMITING_WINDOW_SECONDS` 调整。
- 限流器使用单实例内存计数；多副本部署必须在 Ingress / API Gateway 配置共享限流，不能把应用内限流当作分布式配额。
- Swagger UI 在 Development 默认开放，其他环境默认关闭；如生产排障必须启用，设置 `SWAGGER_ENABLED=true`，并在网络层限制可访问来源，结束后恢复关闭。

### Storage

```env
STORAGE_PROVIDER=s3
S3_ENDPOINT=https://...
S3_REGION=...
S3_BUCKET=knowledge-rag
S3_ACCESS_KEY=...
S3_SECRET_KEY=...
S3_FORCE_PATH_STYLE=false
```

使用 MinIO 时通常：

```env
S3_FORCE_PATH_STYLE=true
```

bucket 需要提前创建；当前应用不会自动创建 bucket。

### OpenTelemetry

```env
OTEL_ENABLED=true
OTEL_SERVICE_NAME=KnowledgeRag.NonAzure
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
OTEL_CONSOLE_EXPORTER=false
```

## 3. 启动顺序

生产发布建议顺序：

```text
1. PostgreSQL ready
2. RabbitMQ ready（CAP=rabbitmq 时）
3. Qdrant ready（vector=qdrant 时）
4. S3 bucket ready（storage=s3 时）
5. OpenTelemetry Collector 可用（如果启用）
6. 执行 EF migration / migration bundle
7. 启动 KnowledgeRag application
8. 检查 /api/health/ready
9. 执行业务 smoke test
```

如果应用配置为：

```text
SearchBackend:AutoInitializeNativeIndexes=true
```

启动期间还会初始化当前启用的 pgvector / PostgreSQL FTS native index。因此 PostgreSQL 必须在应用启动前可用。

## 4. Docker Compose 参考

### PostgreSQL + RabbitMQ + OTel

```bash
docker compose --profile postgres --profile cap --profile otel up -d postgres rabbitmq otel-collector
```

等待 PostgreSQL / RabbitMQ ready 后：

```bash
docker compose --profile postgres --profile cap --profile otel up -d --build app
```

### MinIO

```bash
docker compose --profile s3 up -d minio
```

首次使用前手工创建与 `S3_BUCKET` 一致的 bucket。

### 清理测试环境

```bash
docker compose --profile postgres --profile cap --profile otel --profile s3 down -v
```

**`-v` 会删除 Compose 管理的数据卷。生产或需要保留数据的环境禁止直接执行。**

## 5. Health 与 Readiness 的真实语义

### `/api/health`

返回：

- status
- configured database provider
- configured vector backend
- configured lexical backend
- configured storage provider
- UTC timestamp

它表示应用进程可以响应，并报告当前配置；**不会主动验证所有外部依赖。**

### `/api/health/ready`

当前 readiness 只调用数据库 connectivity check：

```text
DatabaseHealthService.CanConnectAsync
```

因此 `200 ready` **不代表**以下依赖一定健康：

- Qdrant
- RabbitMQ
- S3 / MinIO
- OpenTelemetry Collector
- Tenant AI Provider endpoint
- Webhook destination

生产平台如果需要完整依赖 readiness / startup probe，应在部署层增加独立检查，而不是误解当前 endpoint。

## 6. 发布前 Smoke Test

完成 readiness 后，至少验证：

1. Auth login / refresh。
2. Tenant / KB 列表读取。
3. 文档上传并能从 `Queued` → `Processing` → `Ready`。
4. Search 返回当前 active generation 的 Evidence。
5. Chat 完成一次 RAG Run，Citation 可打开 Source。
6. Governance 能看到新 Run / cost / trace。
7. 如果启用 Webhook，发送 test event。
8. 如果启用 S3，验证上传文件能下载。

涉及 Provider / Embedding 变更时还要检查：

- Deployment health。
- 新 KB Reindex 能创建 building generation。
- 重建期间旧 active index 仍可检索。
- 成功后 active generation 切换。

## 7. Migration 发布

完整流程见 [Migration Guide](../MIGRATION_GUIDE.md)。生产推荐：

```bash
dotnet ef migrations bundle \
  --project src/KnowledgeRag.Infrastructure \
  --startup-project src/KnowledgeRag.Api \
  --output artifacts/efbundle
```

发布时：

```text
backup
  ↓
stop / drain writes（如 migration 需要）
  ↓
run migration bundle with DDL identity
  ↓
deploy new app
  ↓
readiness + smoke
```

不要：

- 在生产手工伪造 `__EFMigrationsHistory`。
- 修改 model snapshot 来掩盖 PendingModelChanges。
- 在没验证 SQLite / PostgreSQL provider 差异时直接合并 migration。

## 8. 数据与备份范围

KnowledgeRag 的完整数据可能分布在多个系统，不能只备份数据库。

### PostgreSQL 模式

至少备份：

- application database
- `__EFMigrationsHistory`
- CAP `cap` schema
- pgvector vectors（如果 vector backend=pgvector）
- DocumentChunks / FTS source rows

### Qdrant 模式

除主数据库外，还需要备份 Qdrant collection / storage。数据库中的 DocumentChunks 和 Qdrant vector points 必须来自兼容的索引 generation。

### 文件存储

- `STORAGE_PROVIDER=local`：备份应用文件目录 / `app-data` volume。
- `STORAGE_PROVIDER=s3`：备份 / version / replicate S3 bucket。

原始文件是重新索引的重要数据源。只有数据库和 vector snapshot、但缺失原始文件，会影响未来 retry / reindex / Embedding generation rebuild。

### RabbitMQ

CAP 的持久数据库状态位于 PostgreSQL，但 RabbitMQ 可能存在正在 transport 的消息。最稳妥的灾备快照是在受控窗口停止新的写入 / producer，并等待或记录 pending delivery 后再做一致性备份。

## 9. 恢复顺序

建议：

```text
1. Restore database
2. Restore original file storage
3. Restore vector backend（Qdrant 模式）
4. Restore / start RabbitMQ
5. Start application
6. Verify migrations / native indexes
7. Check KB active generation status
8. Run Search / Chat smoke
9. Verify pending document / webhook jobs
```

如果 vector backend 无法恢复，但原始文件和数据库仍完整，可以通过 KB-wide Embedding Reindex 重建向量；在执行前先评估 Embedding Provider、成本和处理时间。

## 10. Secret Rotation

### `AUTH_SIGNING_KEY`

影响：

- JWT signing / validation。
- Webhook Secret 的 AES-GCM encryption key 派生。

直接更换会让旧 JWT access token 失效。旧 `enc:v1:` Webhook Secret 与曾回退到 Auth key 加密的 AI Credential 还需要旧 Auth key 解密；轮换前把旧值暂时加入 `SECRET_PROTECTION_LEGACY_AUTH_SIGNING_KEY_1`（必要时第二个槽位），重新录入/验证相关 Secret 后再移除。轮换流程见 [Webhook Automation](WEBHOOK_AUTOMATION.md)。

### `SECRET_PROTECTION_MASTER_KEY`

影响 Tenant-managed AI Provider Credential。

如果该值为空，开发/测试兼容模式会回退到 Auth Signing Key；Production 启动会拒绝空值。

轮换 MasterKey 时先把新值设置为 `SECRET_PROTECTION_MASTER_KEY`，把旧值加入 `SECRET_PROTECTION_PREVIOUS_MASTER_KEY_1`（需要时使用第二个槽位），确认已有 AI Credential 与 Webhook Secret 可解密后，逐项重新保存凭据并验证。只有确认旧密文均已用新密钥重新保存，才移除 previous key。当前没有自动批量重加密命令。

### S3 / RabbitMQ / PostgreSQL Credential

通过部署 secret store 注入，不提交 `.env`。轮换时同时检查连接池、Pod / process restart 和权限最小化。

## 11. Tenant AI Provider 运维

详细见 [AI Provider Configuration](AI_PROVIDER_CONFIGURATION.md)。生产重点：

- Connection Base URL 必须满足当前公网 HTTPS / SSRF 安全策略。
- Credential 不从 API 返回明文。
- Deployment health 是持久化治理状态，不是动态负载均衡器。
- Chat / Rerank 只沿显式同类型 fallback。
- Embedding generation 固定 endpoint/model/dimension snapshot；不能在构建中途任意换模型。
- 修改 Tenant Embedding default 不会立即改变 active vector space，必须显式 Reindex。

## 12. CAP / RabbitMQ 运维

文档 ingestion 和 Webhook delivery 都依赖 CAP。

故障排查顺序：

1. PostgreSQL 是否可连接。
2. `cap` schema 是否存在。
3. RabbitMQ 是否 ready。
4. 应用配置是否真的是 `CAP_TRANSPORT=rabbitmq`。
5. subscriber 是否在应用实例中注册。
6. 查看文档 / Webhook 的持久状态和错误，而不是只看 SignalR / UI。

SignalR 是文档处理进度的 advisory channel；没有收到通知不代表后台任务没有完成。

## 13. Search / Index 故障排查

### Search 突然没有结果

检查：

- KB 是否有 active generation。
- DocumentChunks 是否属于当前 active generation。
- vector backend / lexical backend 配置是否与数据一致。
- Qdrant / pgvector 是否可连接。
- PostgreSQL FTS config 是否正确。

### Reindex 失败

KB-wide Embedding Reindex 是 fail-closed 的：新 generation 未完全成功时旧 active generation 不会被替换。

检查最新 generation：

```http
GET /api/knowledge-bases/{knowledgeBaseId}/embedding-index
```

常见原因：

- Provider / Credential 不可用。
- Embedding dimensions 不一致。
- 源文件缺失。
- 重建期间源文档变化。
- CAP / RabbitMQ delivery 失败。

不要为了“修复”失败 generation 手工修改 `ActiveIndexGenerationId`。

## 14. Webhook 故障排查

见 [Webhook Automation](WEBHOOK_AUTOMATION.md)。重点关注：

- `LastStatusCode`
- `LastError`
- `LastDeliveredAt`
- CAP / RabbitMQ 状态
- SSRF block
- 8 秒 HTTP timeout
- 429 / 5xx retry backlog

## 15. Observability

当前 OpenTelemetry instrumentation 包括 ASP.NET Core / HttpClient / Runtime，以及项目自己的 RAG activities / metrics。

Compose 示例 collector：

```text
observability/otel-collector.yaml
```

Prometheus exporter 示例：

```text
http://localhost:8889/metrics
```

生产中建议至少对以下指标 / 信号建立告警：

- API 5xx rate / P95 latency
- `/api/health/ready` failure
- PostgreSQL connections / errors
- RabbitMQ queue depth / consumer failure
- CAP failed messages
- document failed / cancelled rate
- RAG failed / cancelled rate
- RAG P95 latency
- monthly estimated AI cost / budget rejection rate
- Evaluation regression
- Webhook 429 / 5xx / timeout
- Qdrant / pgvector latency

部分告警需要由外部监控从日志、数据库或消息系统补充；当前应用并没有为上述每一项都提供专用 Prometheus metric。

## 16. 日志与敏感信息

生产日志、Trace 和 CI artifact 不应包含：

- AI API Key / Provider Credential
- Auth Signing Key
- Secret Protection Master Key
- S3 / RabbitMQ / DB password
- Webhook Secret
- Refresh Token / API Key 明文
- System Prompt / managed prompt 正文（除非在受控调试环境明确批准）
- Chain of Thought

用户上传的文档内容与对话答案也可能包含敏感业务数据，应按组织的数据保留策略处理日志和备份。

## 17. 滚动发布注意事项

当前服务有 Conversation 级进程内 semaphore，用来避免同一实例内同一会话并发执行。它不是跨实例 distributed lock。

多实例部署时应避免让同一 Conversation 同时被不同实例并发写入；当前代码没有为 Conversation turn 提供跨实例分布式锁。

此外：

- CAP subscriber 可能运行在多个实例，依赖 CAP 的消息语义与服务端幂等保护。
- Embedding generation activation 有数据库状态 guard，不应绕过。
- 配置 / Prompt / Deployment 更新对已开始的 RAG request 不生效，后续 request 才使用新 snapshot。

在扩大水平扩容前，应对同会话并发、长连接 SSE / SignalR、CAP consumer 和数据库连接池做压力测试。

## 18. 发布检查清单

### 发布前

- [ ] 最新 migration 已在 staging 实际执行。
- [ ] PostgreSQL / RabbitMQ / vector backend / storage 备份可恢复。
- [ ] 生产 Secret 通过 secret store 注入。
- [ ] `AUTH_ALLOW_REGISTRATION=false`（若不需要公开注册）。
- [ ] AI Provider health 正常。
- [ ] 关键 Evaluation Dataset 没有不可接受 regression。
- [ ] Ready PR 完整 CI 全绿。

### 发布后

- [ ] `/api/health` 正常。
- [ ] `/api/health/ready` 为 200。
- [ ] Login / Refresh 正常。
- [ ] Upload → indexing → Search 正常。
- [ ] Chat / Citation 正常。
- [ ] Governance Run / cost / trace 正常。
- [ ] RabbitMQ / CAP 无异常 backlog。
- [ ] Webhook Test 正常（如启用）。
- [ ] OTel traces / metrics 可见。

## 19. 实现与配置入口

- `.env.example`
- `docker-compose.yml`
- `observability/otel-collector.yaml`
- `src/KnowledgeRag.Api/appsettings.json`
- `src/KnowledgeRag.Api/Endpoints/HealthEndpoints.cs`
- `src/KnowledgeRag.Infrastructure/DependencyInjection.cs`
- `src/KnowledgeRag.Infrastructure/Persistence/ServiceCollectionExtensions.cs`
- `src/KnowledgeRag.Infrastructure/Messaging/ServiceCollectionExtensions.cs`
- `src/KnowledgeRag.Infrastructure/Documents/KnowledgeBaseIndexGenerationService.cs`
- `src/KnowledgeRag.Infrastructure/Governance/RagRunAccountingService.cs`

验证与发布流程：

- [Validation](../VALIDATION.md)
- [Development Workflow](../DEVELOPMENT_WORKFLOW.md)
- [Migration Guide](../MIGRATION_GUIDE.md)
- [CAP + SignalR ingestion](../CAP_SIGNALR_INGESTION.md)
- [Governance & Evaluation](GOVERNANCE_EVALUATION.md)
