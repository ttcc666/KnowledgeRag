# Troubleshooting Guide

本文按现有 API、配置和 CI 提供可执行的排障路径。若错误信息与本文不一致，以当前部署配置、Swagger、服务端日志和对应实现为准。不要在日志或工单中粘贴 Token、API Key、模型凭据、Webhook Secret 或完整的敏感文档内容。

## 1. 应用启动失败 / 配置错误

**现象**

- 容器退出、API 无法监听，或应用启动时报告 Options / Secret / Provider 配置错误。
- 前端可打开但 API 请求失败。

**优先检查**

1. 查看容器状态和应用日志：
   
   ~~~bash
   docker compose ps
   docker compose logs app
   ~~~

2. 核对根目录 .env 与 docker-compose.yml 中的配置名，特别是 DATABASE_PROVIDER、DATABASE_CONNECTION_STRING、AUTH_SIGNING_KEY、SECRET_PROTECTION_MASTER_KEY、AI_API_KEY。
3. 当前 Auth SigningKey 要求至少 32 字节，但生产不能使用 appsettings.json 中公开的占位值；生产应显式注入随机密钥。
4. 确认容器使用 .NET 10 镜像与当前配置；CI 的 .NET 版本为 10.0.x。

**修复与验证**

- 修正 Secret / Options 后重新启动应用。
- 等待服务启动后执行本手册第 11 节的 health / readiness 检查。
- 如果启动失败发生在 Secret 解密阶段，不要盲目换密钥；先确认现有加密数据使用的是哪一类 key。参见 [Security Hardening](SECURITY_HARDENING.md) 与 [Production Operations](PRODUCTION_OPERATIONS.md)。

## 2. SQLite / PostgreSQL 连接或 Migration 失败

**现象**

- 应用无法启动，数据库连接异常，或迁移时报错。
- SQLite 可以运行，但 PostgreSQL 部署失败。

**检查**

1. 核对 DATABASE_PROVIDER、DATABASE_CONNECTION_STRING、DATABASE_AUTO_MIGRATE。
2. SQLite 默认连接为本地文件路径；容器部署应确保数据库目录位于持久化 volume，而不是临时容器层。
3. PostgreSQL 模式先确认服务可用：

   ~~~bash
   docker compose exec -T postgres pg_isready
   ~~~

4. 查看应用日志：

   ~~~bash
   docker compose logs app postgres
   ~~~

5. 生产推荐由独立 deployment identity 执行 migration，应用配置 DATABASE_AUTO_MIGRATE=false。迁移命令、baseline 和 rollback 约束见 [Migration Guide](../MIGRATION_GUIDE.md)。

**修复与验证**

- 修正主机名、端口、数据库名、用户名、密码、TLS 和网络策略。
- 生产先备份数据库，再按 Migration Guide 执行迁移；不要手工伪造 EF Migration History。
- 验证 /api/health/ready 返回 200，然后运行后端测试或部署 smoke test。
- SQLite / PostgreSQL 的日期时间排序、SQL 方言和类型差异需分别验证；不要只依据 SQLite 测试判断 PostgreSQL 一定正常。

## 3. pgvector 初始化 / PostgreSQL FTS 检索异常

**现象**

- 应用启动时 native index 初始化失败。
- 向量检索报 extension / vector 类型错误。
- PostgreSQL FTS 没有结果或分词行为与预期不符。

**检查**

1. 核对以下配置：
   
   ~~~env
   DATABASE_PROVIDER=PostgreSql
   SEARCH_VECTOR_BACKEND=pgvector
   SEARCH_LEXICAL_BACKEND=postgres_fts
   POSTGRES_FTS_CONFIG=simple
   ~~~

2. 当前 SearchBackend:AutoInitializeNativeIndexes 默认开启。启用 pgvector / PostgreSQL FTS 时，应用启动期间会初始化相应 native index，因此 PostgreSQL 必须先 ready。
3. 检查 PostgreSQL 镜像 / 实例是否提供 pgvector extension，以及应用账号是否有执行初始化所需的权限。
4. 查看日志与 readiness：

   ~~~bash
   docker compose logs app postgres
   curl --fail --silent http://localhost:8080/api/health/ready
   ~~~

5. 仓库 native CI 的外部后端测试命令如下；只有在 PostgreSQL、连接字符串与测试环境变量已配置时才可运行：

   ~~~bash
   RUN_EXTERNAL_BACKEND_TESTS=1 \
EXTERNAL_DATABASE_CONNECTION_STRING='Host=localhost;Port=5432;Database=knowledge_rag;Username=knowledge_rag;Password=...' \
dotnet test tests/KnowledgeRag.Api.IntegrationTests/KnowledgeRag.Api.IntegrationTests.csproj \
  -c Release --filter Category=ExternalBackend
   ~~~

**修复与验证**

- 测试进程必须设置 RUN_EXTERNAL_BACKEND_TESTS=1；否则测试会按设计直接跳过。EXTERNAL_DATABASE_CONNECTION_STRING 必须指向宿主机可访问的 PostgreSQL。
- 确认启用的 vector / lexical backend 与真实数据库一致。
- 确认数据库用户有必要的 extension / index 权限，且 schema 初始化已完成。
- 使用真实 PostgreSQL 执行 ExternalBackend 测试；SQLite 测试不能替代 pgvector 与 PostgreSQL FTS smoke。
- FTS 的结果受配置的 text search configuration 与语言分词能力影响。中文检索结果不理想时，不要直接把问题归因于向量模型；先比较 BM25、PostgreSQL FTS 与向量召回结果。

## 4. Embedding / LLM Provider 调用失败

**现象**

- Chat / Planner / Reviewer / Reranker 报 Provider 错误。
- Embedding 生成失败，文档长期处于 Processing 或 Failed。
- 修改了 Tenant 默认模型，但当前 KB 仍使用旧 Embedding。

**检查**

1. 平台级 Provider 核对 AI_API_KEY、AI_BASE_URL、AI_CHAT_MODEL、AI_EMBEDDING_MODEL 等实际部署配置。
2. Tenant-managed Provider 使用以下管理接口：
   - GET /api/tenants/ai/providers
   - GET /api/tenants/{tenantId}/ai/connections
   - POST /api/tenants/{tenantId}/ai/connections/{connectionId}/test
   - GET /api/tenants/{tenantId}/ai/deployments
   - POST /api/tenants/{tenantId}/ai/deployments/{deploymentId}/health
   - GET /api/tenants/{tenantId}/ai/defaults
3. 确认 KB RAG Profile 的 Planner / Answer / Reviewer / Reranker role binding 指向预期 Deployment。
4. 核对 Provider 的 Base URL、模型名、API 兼容性、凭据、TLS、出口网络、配额与超时。
5. 不要把 API Key 或 Provider Credential 写入日志来“确认配置是否正确”；使用连接测试和安全状态摘要。

**修复与验证**

- 修复连接 / Deployment 后，重新运行 connection test 或 deployment health check。
- 对 Embedding Deployment，修改 Tenant 默认值不会自动改写现有向量；按 KB 启动 embedding-index/reindex，并观察 generation 状态。
- 重建完成后验证 active generation 已切换，再执行 Search / Chat smoke。详见 [AI Provider Configuration](AI_PROVIDER_CONFIGURATION.md)。

## 5. Hybrid Search 结果异常

**现象**

- 查询结果为空、召回偏低、重复 chunk 较多或排序与预期不符。

**检查**

1. 使用当前 Search API 直接检查检索结果：

   ~~~http
   POST /api/search
   Content-Type: application/json
   Authorization: Bearer <access-token>

   {
     "knowledgeBaseId": "<knowledge-base-guid>",
     "query": "要排查的问题",
     "topK": 10
   }
   ~~~

2. 确认调用身份对目标 KB 至少有 Viewer 权限。
3. 检查文档是否 Ready、当前 active Embedding generation 是否正确，以及文档是否有可搜索的 chunks。
4. 分别检查向量 backend、lexical backend 和最终融合结果；相关实现入口是 KnowledgeSearchService、MultiQuerySearchService、Bm25SearchService、PostgresFtsSearchService、PgVectorStore / QdrantVectorStore 与 RerankerService。
5. 如果只在 PostgreSQL FTS 模式异常，先检查 POSTGRES_FTS_CONFIG；如果只在向量模式异常，检查 Embedding model、dimensions、generation 和 vector collection / index。
6. 检查 RAG Profile 的 Top-K、候选数、融合上限和 Evidence token budget，避免把上下文截断误判为“检索没有返回”。

**修复与验证**

- 先通过 /api/search 固定输入并确认 SearchHit，再检查 Chat Workflow；不要一次同时调整 embedding、RRF 和 reranker。
- 保持 Vector / lexical / RRF / dedupe 的确定性行为；用 Evaluation 指标比较 Recall@K、MRR、nDCG，而不是只凭个别问题主观调参。
- 关联文档：[系统架构](../ARCHITECTURE.md)、[RAG Agent Workflow](RAG_AGENT_WORKFLOW.md)、[Governance & Evaluation](GOVERNANCE_EVALUATION.md)。

## 6. Planner / Retrieval / Reviewer / Repair 失败

**现象**

- Timeline 停在某一阶段、答案显示证据不足、Reviewer 不通过或发生 Repair。
- Chat 请求失败，但数据库中可能已经存在之前的会话消息。

**检查**

1. 先查看当前会话的 SSE progress 事件，再查看 Governance 中对应 KB 的 RAG Run 与 Trace。
2. 关注 Activity / span：rag.workflow.turn、rag.workflow.plan、rag.multi_query_search、rag.search、rag.workflow.answer、rag.workflow.review。
3. 检查当前 KB 的 RAG Profile 和本次 Run 使用的模型 / Agent mode / evidence 数量 / retrieval rounds。
4. 依据当前策略判断失败位置：
   - Planner 失败：回退到原始问题，不应单独导致整轮失败。
   - 原始用户 query 检索失败：当前 retrieval round 失败。
   - 可选 Planner query 失败：记录失败并继续使用成功的检索结果。
   - Planner 标记无需知识检索：Retrieval 可跳过。
   - Evidence 为空：Answer 应 abstain，不应用模型记忆填补知识事实。
   - Reviewer 关闭：跳过 Reviewer。
   - Reviewer 拒绝：在配置的 retry budget 内执行补充检索和重新生成；预算耗尽后返回安全的证据不足答案。
   - 最终 Citation Gate 拒绝：非法引用或知识型答案缺少有效引用时，答案不会作为可信结果输出。
5. 确认 EnableAgentWorkflow、EnablePlanner、EnableReviewer、PlannerMaxQueries、ReviewerMaxRetries 和 ReviewerMinConfidence 的有效值。

**修复与验证**

- 先修复失败的 Provider / 检索 / 权限，而不是单纯增加 Reviewer retries。
- 在 [RAG Agent Workflow](RAG_AGENT_WORKFLOW.md) 中查看完整的 fallback 与 failure policy。
- 运行后端回归测试：

  ~~~bash
  dotnet test KnowledgeRag.NonAzure.sln -c Release
  ~~~

- 对流程行为变化补充 Evaluation case 或对应 integration test；不要关闭 Citation Gate 来让失败用例通过。

## 7. SSE 连接中断 / Timeline 不更新 / Citation 缺失

**现象**

- 浏览器没有收到进度事件，流在中途断开，或回答中没有可点击 Citation / Source。

**检查**

1. 确认请求路径为 POST /api/conversations/{conversationId}/messages/stream，Body 包含 message，并使用有效的认证凭据。
2. 当前 SSE 事件名是 progress、delta、complete。progress 是结构化 Timeline；delta 是通过完整 Workflow 与 Citation Gate 后发出的答案；complete 是最终 ChatResponse。
3. 当前正确性优先策略不是逐 token 流式展示答案。启用 Reviewer 时，答案正文在最终审核和 Citation Gate 完成前会被缓冲。
4. 浏览器 Network 面板检查响应 Content-Type 是否为 text/event-stream、代理是否缓冲响应、请求是否被 401 / 403 拒绝，以及连接是否被 ingress timeout 中断。
5. 检查 Conversation message API 返回的 SourcesJson 与最终 ChatResponse 的 sources；前端只应为服务端返回的可信来源创建 Citation。
6. 反向代理需要保留流式响应；Endpoint 当前设置 Cache-Control: no-cache 与 X-Accel-Buffering: no。

**修复与验证**

- 先验证 API 与浏览器的原始 SSE event，再检查前端的 EventSource / fetch reader adapter。
- 客户端断开会取消下游 request token；重连后不要假设原请求可从中间继续。当前 Endpoint 未定义 SSE event replay contract。
- 若答案存在但 Sources 为空，检查检索证据、Evidence ID、Citation Contract 和 FinalAnswerPolicy；不要在前端自行推断来源。
- 相关测试：[FinalAnswerPolicyTests.cs](../tests/KnowledgeRag.Api.IntegrationTests/FinalAnswerPolicyTests.cs) 和前端 Playwright tests。

## 8. Evaluation 结果异常 / Baseline Regression

**现象**

- Pass Rate、Groundedness、Citation Validity 或 Retrieval metrics 下降。
- Evaluation Run 失败、被取消或没有 baseline comparison。

**检查**

1. 确认 Dataset 至少包含一个 Case；当前空 Dataset 会被拒绝。
2. 检查 Case 的 category、query、requiresKnowledge、ExpectedAnswerContains，以及可选的 expected source 标注。
3. Evaluation v3 的检索指标包括 Hit Rate、Recall@K、MRR、nDCG；它们与回答和引用指标分开统计。
4. Evaluation 会使用先前已完成的 Run 作为 baseline；首次运行没有历史 baseline 时，comparison 可能为空。
5. 查询接口：
   - GET /api/governance/evaluation/datasets?tenantId=<tenant-guid>
   - GET /api/governance/evaluation/runs?tenantId=<tenant-guid>&datasetId=<dataset-guid>
   - POST /api/governance/evaluation/datasets/{datasetId}/run?tenantId=<tenant-guid>
6. 核对执行时的 RAG Profile / model routing snapshot，避免把 Provider、Prompt、Embedding generation 或 KB 内容变化误认为单一排序算法回归。

**修复与验证**

- 对 baseline regression 先比较逐 Case 结果与来源命中，再判断是 Retrieval、Answer、Reviewer 还是 Citation 问题。
- 固定同一 Dataset、KB 内容和模型路由，对比 single-query fallback、Planner + multi-query、full Reviewer workflow。
- 详情见 [Governance & Evaluation](GOVERNANCE_EVALUATION.md)。

## 9. Budget Reservation / Cost / Quota 异常

**现象**

- RAG Run 未开始、预算拒绝、成本统计与 Provider 控制台不一致，或 Reservation 状态长时间不变。

**检查**

1. 查看 KB RAG Profile 中的 Daily Run Limit、Monthly Budget 与 evidence / retry 上限。
2. 查看 GET /api/governance/runs?knowledgeBaseId=<kb-guid>，核对 Run status、estimated cost、model / deployment identity 和创建时间。
3. Reservation 的过期 TTL 当前为 1 小时；陈旧 Reservation 会标记为 reservation-expired。
4. 核对 Tenant Model Pricing 与 Deployment-specific pricing。不同 Provider 使用同名模型时，应使用 Deployment identity 区分计价。
5. 当前成本记录属于项目的估算与治理账本，不应默认等同于 Provider 最终账单。
6. 当前没有统一的应用层 429 rate limit contract；不要把预算拒绝一律当作 HTTP 429。

**修复与验证**

- 先核对当前 RAG Profile 与实际解析的 Deployment，再核对价格单位、输入 / 输出 token estimate 与运行状态。
- 不要通过无限增加 Budget 或关闭 reservation 检查来消除错误。
- 如需手工调整治理策略，使用管理接口并检查 Audit Log。

## 10. RabbitMQ / CAP 消息积压或 Webhook 投递失败

**现象**

- 文档停在 Queued / Processing，Webhook 没有到达接收方，或失败重试不断增加。

**检查**

1. 核对配置：
   
   ~~~env
   CAP_TRANSPORT=rabbitmq
   CAP_FAILED_RETRY_COUNT=20
   RABBITMQ_HOST=rabbitmq
   RABBITMQ_PORT=5672
   ~~~

2. RabbitMQ 健康检查：

   ~~~bash
   docker compose exec -T rabbitmq rabbitmq-diagnostics -q ping
   ~~~

3. PostgreSQL + RabbitMQ + OTel 的本地组合：

   ~~~bash
   docker compose --profile postgres --profile cap --profile otel up -d postgres rabbitmq otel-collector
   docker compose --profile postgres --profile cap --profile otel up -d --build app
   ~~~

4. 查看日志：

   ~~~bash
   docker compose logs app postgres rabbitmq otel-collector
   ~~~

5. 确认 PostgreSQL CAP schema 已创建，并检查 RabbitMQ 连接凭据、vhost、exchange 和网络。
6. Webhook 订阅检查 LastStatusCode、LastError、LastDeliveredAt、IsActive、EventTypes 和接收方状态。
7. Webhook 429 / 5xx / timeout 会触发 CAP 可重试路径；普通 4xx 和 SSRF policy block 不按同一规则重试。当前状态字段代表最近一次投递，不是完整 delivery history。
8. SQLite / in-memory CAP 适合开发测试；进程重启时不要假定内存队列中未处理的消息仍存在。

**修复与验证**

- 先修复网络、凭据、Provider URL、事件筛选和接收方响应，再调整 retry count。
- Webhook SSRF 拒绝时检查 DNS 是否包含私网、loopback 或 link-local 地址；不要为测试方便而删除 SSRF policy。
- 对 receiver 实现 Delivery ID 幂等，避免重试产生重复副作用。
- 详细语义见 [Webhook Automation](WEBHOOK_AUTOMATION.md) 与 [Production Operations](PRODUCTION_OPERATIONS.md)。

## 11. Health / Readiness 与 OpenTelemetry Trace 缺失

**现象**

- /api/health 返回成功，但搜索、消息或存储仍不可用。
- /api/health/ready 返回 503。
- OTLP backend 看不到预期 span。

**检查**

1. GET /api/health 返回进程状态和已配置的 backend 摘要；它不主动验证所有外部依赖。
2. GET /api/health/ready 只执行数据库 connectivity check。200 ready 不代表 Qdrant、RabbitMQ、S3、Provider 或 Webhook destination 一定可用。
3. 核对配置：
   
   ~~~env
   OTEL_ENABLED=true
   OTEL_SERVICE_NAME=KnowledgeRag.NonAzure
   OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
   OTEL_CONSOLE_EXPORTER=false
   ~~~

4. Collector 示例配置位于 observability/otel-collector.yaml；Compose 的 otel profile 会启动示例 Collector。
5. 检查 app / collector 日志、OTLP endpoint、协议 / 端口、防火墙和 exporter 配置。
6. 常见 Workflow spans 包括 rag.workflow.turn、rag.workflow.plan、rag.multi_query_search、rag.workflow.answer 与 rag.workflow.review。

**修复与验证**

- 先验证应用是否创建 span，再验证 exporter / Collector / backend 是否接收到数据。
- 在容器网络中使用服务名；不要把容器内的 localhost 当作另一个容器。
- 生产环境应保护 Telemetry backend，因为 Trace 与 Run 可能包含业务查询等敏感信息。

## 12. 前端路由、API 请求、TanStack Query 与 Playwright

**现象**

- 页面显示空数据、登录状态异常、路由跳转失败、旧消息不刷新，或 Playwright 在本地 / CI 失败。

**检查**

1. 前端路径为 src/KnowledgeRag.Admin。开发模式下先启动 API，再启动 Vite：

   ~~~bash
   cd src/KnowledgeRag.Admin
   npm ci
   npm run dev
   ~~~

2. Vite 将 /api 与 /hubs 代理到 http://localhost:8080；VITE_DEV_API_PROXY 可覆盖代理目标。
3. 浏览器 Network 面板检查请求是否返回 401 / 403 / 5xx，是否携带正确身份，以及 API Base URL 是否正确。
4. 检查 TanStack Query key、失效 / refetch 行为和当前 conversationId；服务端是消息与会话的事实来源，External Store Runtime 是 UI 适配层。
5. 检查 SSE 的 progress / delta / complete 事件，不要假设 delta 一定是 token-by-token。
6. 对 UI 回归运行仓库 CI 使用的命令：

   ~~~bash
   cd src/KnowledgeRag.Admin
   npm ci
   npm run build
   npm test
   npm run test:e2e
   ~~~

**修复与验证**

- 先定位是 API 请求失败、缓存未失效、路由状态不一致，还是 SSE adapter 解析错误。
- 不要通过关闭 RBAC、忽略 401 或绕过来源校验来修复 UI。
- 如果问题只发生于 CI，检查 Playwright Chromium 安装、浏览器控制台和 CI 上传的测试结果。

## 13. 标准验证命令

与仓库当前开发工作流一致：

~~~bash
dotnet restore KnowledgeRag.NonAzure.sln
dotnet build KnowledgeRag.NonAzure.sln -c Release --no-restore
dotnet test KnowledgeRag.NonAzure.sln -c Release --no-build
~~~

纯前端验证见第 12 节。PostgreSQL / RabbitMQ / CAP / pgvector / PostgreSQL FTS 的 native smoke 需要实际外部依赖，不可用 SQLite 测试代替。完整 CI 范围见 [DEVELOPMENT_WORKFLOW.md](../DEVELOPMENT_WORKFLOW.md) 和 [VALIDATION.md](../VALIDATION.md)。
