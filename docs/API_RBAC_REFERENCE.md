# API / RBAC Reference

本文记录当前 main 中已经实现的 HTTP API、认证机制和服务层授权边界。它是开发参考，不替代运行实例上的 Swagger/OpenAPI；请求 DTO 和响应字段以对应 Endpoint / Contract 为准。

- Swagger UI：部署实例的 **/swagger**
- Endpoint 注册：[EndpointRegistration.cs](../src/KnowledgeRag.Api/Endpoints/EndpointRegistration.cs)
- Authentication：[AuthenticationExtensions.cs](../src/KnowledgeRag.Api/Extensions/AuthenticationExtensions.cs)
- 角色与 API Key scope：[ActorContext.cs](../src/KnowledgeRag.Application/Security/ActorContext.cs)、[TenantAccessService.cs](../src/KnowledgeRag.Infrastructure/Tenancy/TenantAccessService.cs)、[KnowledgeBaseAccessService.cs](../src/KnowledgeRag.Infrastructure/KnowledgeBases/KnowledgeBaseAccessService.cs)

## 1. Authentication

应用通过 SmartAuth 认证方案接受两种凭据：

| 请求凭据 | 认证方式 | 说明 |
|---|---|---|
| Authorization: Bearer JWT | JWT Bearer | 面向用户的登录会话；校验 issuer、audience、签名和有效期 |
| X-Api-Key: key | Tenant API Key | 请求包含此 Header 时，SmartAuth 选择 API Key scheme；服务端按 SHA-256 hash 查找密钥并检查 active / expiry |

如果请求同时携带 Bearer JWT 和 X-Api-Key，认证选择器会优先选择 API Key scheme。客户端不要混用两种身份。

API Key 明文只在创建成功时返回一次；列表接口只返回前缀、scope、绑定知识库、有效状态和时间等摘要信息。API Key 不能创建、列出或撤销其他 API Key。

### Auth endpoints

| Method / Route | 输入 | 成功结果 | 认证 |
|---|---|---|---|
| POST /api/auth/register | RegisterRequest：email、password、可选 displayName | AuthResponse：accessToken、refreshToken、有效期、user | 匿名；受 Auth:AllowRegistration 控制 |
| POST /api/auth/login | email、password | AuthResponse | 匿名 |
| POST /api/auth/refresh | refreshToken | 轮换后的 AuthResponse | 匿名；需要有效 Refresh Token |
| POST /api/auth/logout | refreshToken | 204 No Content | 已认证 |
| GET /api/auth/me | 无 | 当前用户摘要、API Key / tenant / scope 上下文 | 已认证 |

Refresh Token 采用轮换策略。重复使用已撤销的旧 Refresh Token 会撤销该用户的全部 Refresh Token 会话；客户端收到 refresh 失败后应重新登录，而不是无限重试旧 token。相关回归测试：[AuthTenantApiTests.cs](../tests/KnowledgeRag.Api.IntegrationTests/AuthTenantApiTests.cs)。

## 2. Tenant / Knowledge Base / API Key 权限模型

### Tenant role

Tenant role 按等级比较：

| Role | 当前边界 |
|---|---|
| Member | 读取所属 Tenant 的普通管理信息，并在有相应 KB 权限时使用知识库 |
| Admin | 管理 Tenant 成员、API Key、AI Provider 配置、Model Deployment、Tenant Defaults、Prompt、Webhook、Model Pricing、Evaluation 等管理资源 |
| Owner | Tenant 的最高角色；可删除 Tenant，但不能移除最后一个 Owner |

### Knowledge Base role

KB role 独立于 Tenant role：

| Role | 当前边界 |
|---|---|
| Viewer | 检索、聊天、查看会话与来源、查看文档列表 / 文件、读取 RAG Profile 与 Run |
| Editor | Viewer 权限，加上更新 KB、上传 / retry / reindex / cancel / 删除文档等写操作 |
| Owner | Editor 权限，加上管理 KB 成员、删除 KB、修改 KB RAG Profile 等管理操作 |

创建 KB 的用户必须有目标 Tenant 的成员权限；创建成功后会成为该 KB 的 Owner。服务端在 Endpoint 之外再次调用 Tenant / KB access service 检查权限。不能仅凭 UI 是否隐藏按钮判断访问权限。

### Tenant API Key scopes

当前允许的 scope：

| Scope | 能力 |
|---|---|
| knowledge:read | 在该 API Key 所属 Tenant 范围内读取授权的 KB、搜索和使用只读能力 |
| knowledge:write | 读取并执行被允许的 KB 写操作 |
| knowledge:admin | KB Owner 级别的 API Key 能力 |
| tenant:admin | Tenant 管理能力；要求 key 绑定 Tenant，且不能同时绑定单个 KB |

关键隔离规则：

- 可选的 KnowledgeBaseId 绑定会把 API Key 收窄到单个 KB。
- 未绑定 KB 的 key 仍受其 Tenant ID 与 scope 限制。
- knowledge:admin **不会**隐式授予 tenant:admin；两种管理权限互不继承。
- API Key 不能管理 API Key，也不能创建或删除 Tenant。
- 需要读、写、管理多个域时，应创建 scope 最小化的独立 key，不要为方便授予所有 scope。

## 3. API 路由索引

以下为主要路由和输入 / 输出语义。完整请求模型请查看 Swagger 和对应 Contract。

### Tenant 与 AI Provider

| Method / Route | 输入 / 输出 | 权限边界 |
|---|---|---|
| GET / POST **/api/tenants** | 列表 / 创建 Tenant；POST 输入 name | 已认证；创建 Tenant 不允许 API Key |
| PUT / DELETE **/api/tenants/{tenantId}** | 更新 / 删除 Tenant；成功通常为 204 | 更新要求 Tenant Admin；删除要求 Tenant Owner |
| GET **/api/tenants/{tenantId}/members** | Tenant 成员列表 | Tenant Admin |
| PUT **/api/tenants/{tenantId}/members** | 输入 email、role；添加或更新成员 | Tenant Admin |
| DELETE **/api/tenants/{tenantId}/members/{memberUserId}** | 删除成员，204 | Tenant Admin；不能删除最后一个 Tenant Owner，或让成员仍成为唯一 KB Owner |
| GET / POST **/api/tenants/{tenantId}/api-keys** | 列表 / 创建 API Key；创建输入 name、scopes、可选 knowledgeBaseId / expiresAt | Tenant Admin；创建响应中的明文 key 只显示一次 |
| DELETE **/api/tenants/{tenantId}/api-keys/{apiKeyId}** | 撤销 key，204 | Tenant Admin；API Key 本身不能管理 key |
| GET **/api/tenants/ai/providers** | Provider Catalog | 需要认证；仅返回静态目录信息 |
| GET / POST **/api/tenants/{tenantId}/ai/connections** | Provider Connection 列表 / 保存；写入可包含 Base URL 与 Credential | 列表 Tenant Member；保存 Tenant Admin |
| POST **/api/tenants/{tenantId}/ai/connections/{connectionId}/test** | 测试 Connection，返回测试结果 | Tenant Admin |
| DELETE **/api/tenants/{tenantId}/ai/connections/{connectionId}** | 删除 Connection，204 | Tenant Admin |
| GET / POST **/api/tenants/{tenantId}/ai/deployments** | Deployment 列表 / 保存 | 列表 Tenant Member；保存 Tenant Admin |
| POST **/api/tenants/{tenantId}/ai/deployments/{deploymentId}/health** | 主动执行 Deployment health check | Tenant Admin |
| DELETE **/api/tenants/{tenantId}/ai/deployments/{deploymentId}** | 删除 Deployment，204 | Tenant Admin；被引用时可能拒绝 |
| GET / PUT **/api/tenants/{tenantId}/ai/defaults** | 读取 / 更新 Tenant 默认 Chat、Embedding、Reranker Deployment | 读取 Tenant Member；更新 Tenant Admin |

### Knowledge Base、文档与检索

| Method / Route | 输入 / 输出 | 权限边界 |
|---|---|---|
| GET / POST **/api/knowledge-bases** | 列表 / 创建 KB；创建输入 name、可选 description、tenantId | 已认证；创建要求 Tenant Member，API Key 需要 KB admin scope |
| GET / PUT / DELETE **/api/knowledge-bases/{knowledgeBaseId}** | KB 详情 / 更新 / 删除 | Viewer / Editor / Owner |
| GET **/api/knowledge-bases/{knowledgeBaseId}/members** | KB 成员列表 | KB Owner |
| PUT **/api/knowledge-bases/{knowledgeBaseId}/members** | 输入 email、role，添加或更新成员 | KB Owner |
| DELETE **/api/knowledge-bases/{knowledgeBaseId}/members/{memberUserId}** | 删除 KB 成员，204 | KB Owner；不能移除最后一个 KB Owner |
| GET / POST **/api/knowledge-bases/{knowledgeBaseId}/documents** | 列表 / multipart 文件上传（字段名 file）；上传返回 202 Accepted 与 document 状态 | 列表 Viewer；上传 Editor |
| POST **/api/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/retry** | 重试失败文档，202 Accepted | Editor |
| POST **/api/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/reindex** | 重建单文档索引，202 Accepted | Editor |
| POST **/api/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/cancel** | 请求取消处理，返回文档状态 | Editor |
| GET **/api/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/file** | 流式下载源文件 | Viewer |
| DELETE **/api/knowledge-bases/{knowledgeBaseId}/documents/{documentId}** | 删除文档，成功为 204 | Editor |
| GET **/api/knowledge-bases/{knowledgeBaseId}/embedding-index** | 当前 / 最近 Embedding generation 状态 | KB Viewer |
| POST **/api/knowledge-bases/{knowledgeBaseId}/embedding-index/reindex** | 启动 KB-wide generation rebuild，202 Accepted | 已登录用户 + KB Editor；API Key 会被拒绝 |
| POST **/api/search** | JSON：knowledgeBaseId、query、可选 topK；返回 SearchHit 列表 | KB Viewer |

### Conversation、Chat 与 Streaming

| Method / Route | 输入 / 输出 | 权限边界 |
|---|---|---|
| POST **/api/conversations** | knowledgeBaseId、可选 title；返回 Conversation 摘要 | 已认证 + KB Viewer |
| GET **/api/conversations?knowledgeBaseId=...&archived=false** | 会话列表；默认只列未归档会话 | 按 actor / key 过滤会话所有权；未指定 knowledgeBaseId 时，列表未逐项重检当前 KB membership，可能仍显示已失去 KB 权限的会话元数据；消息读取与会话操作会再检查 KB Viewer |
| PATCH **/api/conversations/{conversationId}** | title；返回更新后的会话摘要 | 会话所有权 + KB Viewer |
| POST **/api/conversations/{conversationId}/archive** | 归档，204 | 会话所有权 + KB Viewer |
| DELETE **/api/conversations/{conversationId}/archive** | 取消归档，204 | 会话所有权 + KB Viewer |
| GET **/api/conversations/{conversationId}/messages** | 消息及来源 | 会话所有权 + KB Viewer |
| GET **/api/conversations/{conversationId}/messages/page?before=...&limit=...** | 有界分页；limit 默认 50 | 会话所有权 + KB Viewer |
| POST **/api/conversations/{conversationId}/messages** | message；返回 ChatResponse（answer、sources） | 会话所有权 + KB Viewer |
| POST **/api/conversations/{conversationId}/messages/stream** | message；返回 SSE | 会话所有权 + KB Viewer |
| POST **/api/conversations/{conversationId}/messages/{messageId}/feedback** | rating；204 | 会话所有权 + KB Viewer |
| DELETE **/api/conversations/{conversationId}** | 删除会话，204 | 会话所有权 + KB Viewer |
| POST **/api/chat** | knowledgeBaseId、message、可选 conversationId；缺少 conversationId 时创建会话 | 已认证 + 服务端 KB / conversation 权限校验 |

#### SSE 协议

客户端向 **POST /api/conversations/{conversationId}/messages/stream** 发送 JSON **{"message":"..."}**，响应为 **text/event-stream**。当前事件名：

| Event | Data | 语义 |
|---|---|---|
| progress | 结构化 Workflow Timeline 事件 | 运行阶段、检索状态、审核 / 修复进度 |
| delta | {"text":"..."} | 当前是通过完整 Workflow 与最终 Citation Gate 后的答案内容；启用 Reviewer 时不是逐 token 的草稿流 |
| complete | 最终 ChatResponse | 本轮完成结果及可信 Sources |

请求取消 token 会传递到下游服务；客户端断开连接时应停止等待本轮流。当前 Endpoint 没有单独的 SSE error 事件契约；连接中断或 HTTP 错误应按失败处理。Progress 可能包含本轮检索 query，不要把不可信客户端之外的连接加入会话流。

### Governance、Prompt、Webhook 与 Evaluation

这些路由都在 **/api/governance** 认证组下，但实际角色由各服务方法检查；不是所有已认证用户都可以读写所有资源。

| Method / Route | 输入 / 输出 | 权限边界 |
|---|---|---|
| GET **/api/governance/overview?tenantId=...** | Tenant 治理概览 | Tenant Member |
| GET **/api/governance/knowledge-bases/{knowledgeBaseId}/rag-profile** | 当前有效 RAG Profile | KB Viewer |
| PUT 同一路由 | 更新 Planner / Answer / Reviewer / 检索 / Budget 等 Profile | KB Owner |
| GET **/api/governance/runs?knowledgeBaseId=...&limit=...** | RAG Run 列表与追踪摘要 | KB Viewer |
| GET **/api/governance/audit?tenantId=...&limit=...** | Audit Log | Tenant Admin |
| GET / PUT **/api/governance/model-prices?tenantId=...** | Model Pricing 列表 / 更新 | 读取 Tenant Member；写入 Tenant Admin |
| GET **/api/governance/prompts?tenantId=...** | Prompt Profile 列表 | Tenant Member |
| POST / PUT **/api/governance/prompts?tenantId=...** 或 **/prompts/{promptId}** | 创建 / 更新 Prompt Profile | Tenant Admin；绑定 KB 时还要求 KB Owner |
| GET **/api/governance/webhooks/providers** | 渠道目录 | 认证用户；只读目录 |
| GET / POST **/api/governance/webhooks?tenantId=...** | 列表 / 保存订阅 | Tenant Admin |
| PUT / DELETE **/api/governance/webhooks/{webhookId}?tenantId=...** | 更新 / 删除订阅 | Tenant Admin |
| POST **/api/governance/webhooks/{webhookId}/test?tenantId=...** | 直接投递测试事件 | Tenant Admin；投递超时返回 504 |
| GET / POST **/api/governance/evaluation/datasets?tenantId=...** | Dataset 列表 / 创建 | 读取 Tenant Member；创建 Tenant Admin 且要求 KB Viewer |
| GET / POST **/api/governance/evaluation/datasets/{datasetId}/cases?tenantId=...** | Case 列表 / 添加 Case | 读取 Tenant Member；创建 Tenant Admin 且要求 KB Viewer |
| GET **/api/governance/evaluation/runs?tenantId=...&datasetId=...** | Evaluation Run 列表 | Tenant Member |
| POST **/api/governance/evaluation/datasets/{datasetId}/run?tenantId=...** | 运行 Evaluation 并返回结果 | Tenant Admin 且要求 KB Viewer |

### Health / Operations / SignalR

| Method / Route | 输入 / 输出 | 认证与语义 |
|---|---|---|
| GET **/api/health** | 进程状态与配置摘要 | 当前公开；不探测所有依赖 |
| GET **/api/health/ready** | 数据库 connectivity 结果 | 当前公开；仅数据库连通性，失败返回 503 |
| GET **/api/dashboard** | 当前 actor 范围内的概览 | 已认证 |
| GET **/api/ingestion/jobs** | 摄取任务列表 | 已认证 |
| GET **/api/settings/product** | 产品设置摘要 | 已认证 |
| SignalR **/hubs/documents** | 文档进度通知 | Hub 使用 [Authorize]，连接加入当前用户的 user group |

## 4. HTTP 错误语义

错误码由认证中间件、Endpoint 显式返回和 ApiExceptionMiddleware 共同决定；不要把下表理解成所有 Endpoint 都会返回完全相同的错误 body。

| Status | 当前含义 |
|---|---|
| 400 | 请求或业务状态不合法；InvalidOperationException 与 Webhook SSRF destination block 由全局异常中间件映射为 400 |
| 401 | 缺少 / 无效身份凭据；登录凭据错误、Refresh Token 无效 / 过期 / 重放也会返回 401 |
| 403 | 已认证但缺少 Tenant role、KB role 或 API Key scope；UnauthorizedAccessException 由异常中间件映射为 403 |
| 404 | 资源不存在；KeyNotFoundException 映射为 404。某些资源访问路径会先执行权限检查，因此不应假定所有“无权访问的资源”都返回 404 |
| 202 | 已接受异步处理，例如文件上传、文档 retry / reindex、KB-wide Embedding rebuild |
| 204 | 成功执行且无响应 body，例如归档、删除或撤销操作 |
| 500 | 未处理的服务器异常；响应使用通用错误消息，详细异常写入服务端日志 |
| 504 | Webhook test delivery 超时 |

**409 / 429 注意事项：** API 全局固定窗口限流默认按 API Key、已认证用户或匿名 IP 分区，每 60 秒最多 120 次；登录、注册、刷新令牌接口另有更严格的每来源 IP 限流，默认每 60 秒 10 次（同时受全局上限约束）。可配置 RateLimiting:PermitLimit、RateLimiting:AuthPermitLimit 与 RateLimiting:WindowSeconds。该限流是单实例内存计数，多实例需在 Gateway 层统一限流。业务预算拒绝和其他业务异常不是统一的 429 契约，部分业务异常仍可能映射为 400；409 也不是所有冲突的统一映射。反向代理、AI Provider 或 Webhook 接收方仍可能返回 429。

## 5. 授权失败排查

1. 确认请求使用了预期的凭据 Header。携带 X-Api-Key 会优先选择 API Key，即使请求同时带 Bearer JWT。
2. 对 API Key 检查是否 active、未过期、Tenant ID 正确、scope 足够、KB binding 没有把访问范围收窄。
3. 对用户 JWT 检查 token 有效期，并确认用户仍是目标 Tenant / KB 成员。
4. Tenant Admin 与 KB Owner 是不同的授权域；需要分别检查 Tenant role、KB role 和目标资源 ID。
5. 以服务端 Endpoint + Service 的权限检查为准。不要为了消除 403 而在客户端移除权限判断，也不要把 API Key 提升为全 scope。
6. 查看应用日志中的错误类别与资源路径；避免在日志、工单或截图中粘贴 Bearer Token、API Key、Refresh Token 或 Provider Credential。

## 6. 源码与回归测试入口

- Route registration：[EndpointRegistration.cs](../src/KnowledgeRag.Api/Endpoints/EndpointRegistration.cs)
- Authentication / JWT：[AuthenticationExtensions.cs](../src/KnowledgeRag.Api/Extensions/AuthenticationExtensions.cs)、[AuthService.cs](../src/KnowledgeRag.Infrastructure/Identity/AuthService.cs)、[RefreshTokenService.cs](../src/KnowledgeRag.Infrastructure/Identity/RefreshTokenService.cs)
- API Key scopes：[ApiKeyAuthenticationHandler.cs](../src/KnowledgeRag.Infrastructure/Identity/ApiKeyAuthenticationHandler.cs)、[ApiKeyService.cs](../src/KnowledgeRag.Infrastructure/Identity/ApiKeyService.cs)
- Tenant RBAC：[TenantAccessService.cs](../src/KnowledgeRag.Infrastructure/Tenancy/TenantAccessService.cs)
- KB RBAC：[KnowledgeBaseAccessService.cs](../src/KnowledgeRag.Infrastructure/KnowledgeBases/KnowledgeBaseAccessService.cs)
- Conversation ownership / KB checks：[ConversationService.cs](../src/KnowledgeRag.Infrastructure/Conversations/ConversationService.cs)
- Governance authorization：[GovernanceService.cs](../src/KnowledgeRag.Infrastructure/Governance/GovernanceService.cs)、[EvaluationService.cs](../src/KnowledgeRag.Infrastructure/Governance/EvaluationService.cs)
- API error mapping：[ApiExceptionMiddleware.cs](../src/KnowledgeRag.Api/Middleware/ApiExceptionMiddleware.cs)
- Regression tests：[AuthTenantApiTests.cs](../tests/KnowledgeRag.Api.IntegrationTests/AuthTenantApiTests.cs)、[GovernanceApiTests.cs](../tests/KnowledgeRag.Api.IntegrationTests/GovernanceApiTests.cs)、[TenantAiProviderApiTests.cs](../tests/KnowledgeRag.Api.IntegrationTests/TenantAiProviderApiTests.cs)

关联文档：[Webhook Automation](WEBHOOK_AUTOMATION.md)、[Governance & Evaluation](GOVERNANCE_EVALUATION.md)、[Production Operations](PRODUCTION_OPERATIONS.md)。
