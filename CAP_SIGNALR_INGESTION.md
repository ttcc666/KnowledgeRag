# CAP、SignalR 与文档索引（当前实现）

本文区分 **单文档摄取** 和 **Knowledge Base 级 Embedding generation 重建**。两者共用 CAP 事件与索引 subscriber，但对旧索引的可见性和失败清理策略不同。

## 1. 单文档摄取流程

```text
POST /api/knowledge-bases/{knowledgeBaseId}/documents
  → 校验权限、文件类型与上传大小
  → 保存原始文件 + Document(Queued)
  → 发布 knowledge.document.index.requested.v1
  → DocumentIndexingSubscriber
  → 解析 / 切片 / Embedding / vector upsert
  → 在数据库事务中替换当前 active generation 的该文档 chunks
  → 清理旧 vector points
  → SignalR 发送进度
```

- 原始文件先存入当前配置的 `IFileStorage` 实现（Local 或 S3-compatible）。
- 普通文档事件不带 `IndexGenerationId`；subscriber 会解析 KB 当前的 active generation，并使用该 generation 绑定的 Embedding endpoint。
- 对已 Ready 的文档，CAP 重复投递不会无条件重复索引；显式 `retry` / `reindex` 会创建新的队列请求。
- 单文档 reindex 先写入新的 point IDs，再在数据库事务中替换 chunks；只有提交成功后才清理旧 vectors。索引中途失败时会清理本次 staging points，而不是先删除当前可用 chunks。
- 当 active generation 在处理期间发生切换，任务会清理本轮 staging points，并针对新的 active generation 重新排队。

## 2. Knowledge Base 级 Embedding Reindex

路由：

```http
GET  /api/knowledge-bases/{knowledgeBaseId}/embedding-index
POST /api/knowledge-bases/{knowledgeBaseId}/embedding-index/reindex
```

此流程用于更换 Embedding 模型 / Deployment 或重建整个 KB 的向量空间，不等同于逐个调用普通文档 reindex。

```text
Resolve Tenant Embedding target once
  → create Building generation + document manifest
  → publish one event per source document (with IndexGenerationId)
  → build staging chunks / vectors for every document
  → verify every generation document is Ready and manifest still matches
  → transactionally update KnowledgeBase.ActiveIndexGenerationId
  → mark previous generation Superseded
  → clean previous-generation chunks / vectors
```

关键约束：

- 同一个 KB 同时最多有一个 `Building` generation。
- 新 generation 记录其 Embedding Deployment、模型名、维度及 Provider / connection 快照；重建期间不会每个文档重新解析 Tenant Default。
- 新 generation 完整成功之前，搜索继续使用旧的 active generation。
- 任一文档失败、源文档被删除或源内容在重建期间变化，都会阻止本次 generation 激活并清理 staging 数据；上一代 active index 保持可用。
- 激活通过数据库事务更新 active-generation 指针，避免检索同时看到两代 chunks。
- generation 构建期间不允许删除参与该 generation 的源文档；等待重建结束后再执行删除。
- 修改 Tenant 的 Embedding target **不会自动重建向量**，必须显式调用 Reindex。当前 Reindex 使用明确的 Embedding target Deployment，不会自动沿 Deployment fallback 链切换模型。

## 3. CAP transport 与数据库

### 本地开发 / SQLite

默认使用：

- `Cap:Transport=inmemory`
- `DotNetCore.CAP.InMemoryStorage`
- `Savorboard.CAP.InMemoryMessageQueue`

此模式适合本地开发和测试，不提供跨进程 / 重启的持久消息保证。不要把它当成生产多实例 worker 的队列。

### PostgreSQL + RabbitMQ

`.env` 示例：

```env
DATABASE_PROVIDER=PostgreSql
DATABASE_CONNECTION_STRING=Host=postgres;Port=5432;Database=knowledge_rag;Username=knowledge_rag;Password=...
CAP_TRANSPORT=rabbitmq
RABBITMQ_HOST=rabbitmq
RABBITMQ_PORT=5672
RABBITMQ_USER=knowledge_rag
RABBITMQ_PASSWORD=...
RABBITMQ_VHOST=/
CAP_EXCHANGE=knowledge-rag.cap
```

启动依赖服务：

```bash
docker compose --profile postgres --profile cap up -d postgres rabbitmq
```

如果要启动应用容器，并让它使用以上连接配置：

```bash
docker compose --profile postgres --profile cap up -d --build app
```

CAP 在 PostgreSQL 中使用 `cap` schema 保存其消息状态，并通过 RabbitMQ 传输事件。确保应用使用的数据库连接串指向容器网络中可访问的 PostgreSQL 服务。

## 4. SignalR 通知

Hub 路径为 `/hubs/documents`，由认证后的当前用户接收其有权限的任务通知。UI 将这些事件用于刷新文档列表和显示进度。

当前常见阶段包括：

- `processing`
- `parsing`
- `chunking`
- `embedding`
- `storing`
- `completed`
- `failed`
- `cancelled`

通知是**尽力而为的 UI 信号**：SignalR 推送失败不会回滚已完成的索引任务。持久状态以数据库中的 `Documents.Status`、`Documents.LastError` 和 KB Embedding Index generation 状态为准，可通过对应 REST API 重新查询。

## 5. Retry、取消与幂等性

- CAP 可以重试 subscriber 失败的消息；事件处理应允许同一请求被再次投递。
- 单文档取消通过文档状态检查生效；取消后清理当前尝试生成的 staging vectors，不应误删旧的 active chunks。
- 普通单文档索引与 KB-wide generation 重建使用不同的 point ID / generation 语义，失败清理必须限制在本次 generation。
- KB-wide generation 只有所有文档成功、文档清单 hash 匹配且仍处于 Building 状态时才能激活。
- 监控和排障时先检查 API 持久状态、CAP/RabbitMQ 健康及应用日志；不要仅凭前端没有收到 SignalR 事件判断任务失败。

## 6. 相关实现入口

- `src/KnowledgeRag.Infrastructure/Documents/KnowledgeIngestionService.cs`
- `src/KnowledgeRag.Infrastructure/Documents/DocumentIndexingSubscriber.cs`
- `src/KnowledgeRag.Infrastructure/Documents/KnowledgeBaseIndexGenerationService.cs`
- `src/KnowledgeRag.Infrastructure/Documents/DocumentEventTopics.cs`
- `src/KnowledgeRag.Api/Realtime/DocumentProcessingHub.cs`
- `src/KnowledgeRag.Api/Endpoints/DocumentEndpoints.cs`
- `src/KnowledgeRag.Api/Endpoints/KnowledgeBaseEndpoints.cs`

