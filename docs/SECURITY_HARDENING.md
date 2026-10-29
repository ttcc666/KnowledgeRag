# Security Hardening Guide

本文面向生产部署与安全审计。内容区分“代码中已观察到的控制”“部署侧必须配置的措施”和“尚待专门验证的残余风险”。安全控制的存在不代表覆盖全部攻击场景。

权威专项说明：
- [API / RBAC Reference](API_RBAC_REFERENCE.md)：认证、角色、scope 和路由授权
- [Webhook Automation](WEBHOOK_AUTOMATION.md)：平台签名、HMAC、SSRF、重试与 Secret 轮换影响
- [AI Provider Configuration](AI_PROVIDER_CONFIGURATION.md)：Tenant Provider、Credential 与 Endpoint 路由
- [Production Operations](PRODUCTION_OPERATIONS.md)：生产拓扑、数据库、备份恢复与 Secret 配置

## 1. 生产风险优先级

| 优先级 | 项目 | 当前观察 / 建议 |
|---|---|---|
| P0 | 默认 JWT Signing Key | appsettings.json 仍保留开发占位值，但本分支 Production 启动校验会拒绝已知 SigningKey 占位值，并要求独立且至少 32 字节的 SecretProtection MasterKey。生产仍必须从 Secret Store 注入真实随机值 |
| P1 | API 请求限流 | API 已有全局固定窗口限流；登录、注册、刷新令牌接口默认额外限制为每来源 IP 每 60 秒 10 次。限流为单实例内存计数，多实例仍需在可信 API Gateway / Reverse Proxy 统一限流 |
| P1 | Audit Log 完整性 | AuditLogMiddleware 已移至 Authorization 之前，可记录已认证 actor 的授权拒绝和限流响应；匿名认证失败仍不会写入关联用户的 Audit Log，应结合反向代理与安全日志监控 |
| P1 | Secret 轮换 | 新写入的 AI Credential 与 Webhook Secret 使用独立 SecretProtection MasterKey；previous master keys 与 legacy Auth keys 可在受控轮换期间解密旧密文。应用没有自动批量重加密命令，移除兼容密钥前必须重新保存并验证旧凭据 |
| P1 | 上传与解析资源边界 | 已限制请求体 / 上传大小、提取文本字符数、PDF 页数与 DOCX part 字符数，并为 HTML 正则设置超时；恶意文件扫描、完整解析沙箱及所有 CPU / 内存边界仍未覆盖 |
| P2 | TLS / CORS / Proxy / Swagger | Production 已启用 HSTS；应用层 HTTPS 重定向可配置开启，CORS 使用精确 origin allowlist，Forwarded Headers 仅接受 loopback 或明确配置的可信代理。Swagger 仅在 Development 默认开放，其他环境必须显式设置 Swagger:Enabled=true；生产开启时应再加网络访问控制 |
| P2 | Trace / Audit 中的业务数据 | Chat / Run 会保存用户问题和答案；Timeline progress 可能包含检索 query。应按敏感业务数据管理访问、保留期和导出 |

## 2. Authentication、Authorization 与 RBAC

### 已实现的控制

- SmartAuth 在 JWT Bearer 与 X-Api-Key 之间选择认证方案；存在 X-Api-Key Header 时优先使用 API Key scheme。
- JWT 校验 issuer、audience、签名和有效期。
- Refresh Token 使用轮换策略；重放旧 Refresh Token 会撤销该用户的全部 Refresh Token 会话。
- API Key 服务端保存 SHA-256 hash，明文只在创建时返回；可设置过期时间、scope 与可选 KB binding。
- Tenant role 与 KB role 分开检查；API Key 的 knowledge:admin 不会自动获得 tenant:admin。
- Document、Search、Conversation、Governance 等服务在业务层执行 Tenant / KB access 检查，而不仅依赖前端隐藏控件。
- DocumentProcessingHub 标记为 Authorize，并把连接加入当前用户自己的 SignalR group。

详情见 [API / RBAC Reference](API_RBAC_REFERENCE.md) 和 AuthTenantApiTests.cs。

### 生产配置要求

- 使用独立生成的高熵随机 AUTH_SIGNING_KEY，并通过部署 Secret Store 注入；不能把示例值或仓库默认值用于生产。
- 设置 AUTH_ALLOW_REGISTRATION=false，除非业务明确需要公开注册并已配置额外的滥用防护。
- 审查 Token 有效期与 Refresh Token 轮换策略；用户离职、凭据泄漏或密钥泄漏时要有撤销会话的操作流程。
- API Key 采用最小 scope、最短合理有效期与独立用途；不要共享管理员 key。
- 生产边界应由服务器端 access service 强制执行；不要把 CORS 或 UI 路由当作授权机制。

### 已知风险：默认 Signing Key

appsettings.json 仍保留开发环境占位值，但运行时配置验证现已在 Production 启动时拒绝已知 SigningKey 占位值，并要求至少 32 字节的独立 SecretProtection:MasterKey；MasterKey 不能与 JWT SigningKey 相同。对应回归测试覆盖 Compose 与 appsettings 的公开占位值、缺失 MasterKey 和重复使用同一密钥。生产仍必须从 Secret Store 注入随机值，不能将启动校验当作密钥生成器。

### 会话列表的权限重检

当前 ConversationService.ListAsync 在传入 knowledgeBaseId 时会检查 KB Viewer；未传入该筛选参数时，会按会话所有权查询列表，但没有逐条重检用户当前的 KB membership。因此，用户被移出 KB 后，列表可能仍返回其原有会话的标题、Conversation ID、KnowledgeBase ID 与时间等元数据。消息读取和会话操作会再次检查 KB 权限，不能据此推断消息正文也可读取。

建议独立代码 PR：在无 KB 筛选的列表查询中，只返回当前 actor 仍有权限访问的 KB 会话，并为“撤销 KB membership 后列表不再返回元数据”增加 integration test。

## 3. Secret 管理、加密与轮换

### AI Provider Credential

AiCredentialProtector 使用 AES-GCM。新写入密文使用 SecretProtection:MasterKey 派生的独立密钥；开发环境在 MasterKey 为空时仍兼容 Auth:SigningKey。生产启动验证会拒绝缺失或与 JWT SigningKey 相同的 MasterKey。

生产要求：

1. 显式配置独立的 SECRET_PROTECTION_MASTER_KEY。
2. 将该密钥保存在部署 Secret Store，不提交到仓库、镜像或日志。
3. 限制读取和修改 Secret 的运维权限。
4. MasterKey 轮换时，先将新密钥配置为 MasterKey，并把旧密钥暂时加入 PreviousMasterKeys；应用会用当前密钥加密、用当前/previous keys 解密。随后通过 AI Connection 更新 API 重新录入凭据，确认所有凭据已用新密钥保存后，再移除旧密钥。若历史凭据曾使用 Auth:SigningKey 加密，轮换 Auth:SigningKey 前还应将旧值临时加入 LegacyAuthSigningKeys，直到相关凭据重新录入。

### Webhook Secret

WebhookSecretProtector 新写入使用独立 MasterKey 派生的 AES-GCM 密钥，并以 enc:v2: 保存；旧 enc:v1: 密文仍通过 Auth:SigningKey 或 LegacyAuthSigningKeys 解密。API 只返回 hasSecret，不返回明文。

Auth SigningKey 与 SecretProtection MasterKey 已分离。轮换 Auth SigningKey 会使旧 JWT 访问令牌失效；如仍有旧的 AI/Webhook 密文，先把旧 Auth key 放入 LegacyAuthSigningKeys，再逐项通过 AI Connection / Webhook 更新 API 重新录入凭据并验证。MasterKey 轮换使用 PreviousMasterKeys；只有确认旧密文均已重新保存后才移除旧密钥。应用没有自动批量重加密命令，因此不要在没有兼容密钥或重新录入计划时直接删除旧密钥。

### 其他凭据

- 生产 S3 / MinIO 不得使用 appsettings.json 中的 minioadmin 示例凭据。
- PostgreSQL、RabbitMQ、S3、模型 Provider 的密码 / API Key 应由 Secret Store 注入，避免出现在 Docker 镜像层、日志、Issue 或截图中。
- 生产数据库应用账号与 migration / DDL 账号应分离；应用账号不应长期持有不必要的 DDL 权限。
- API Key 和 Refresh Token 不应复制到支持工单或前端埋点。

## 4. Webhook、HMAC 与 SSRF

### 已实现的 Webhook outbound 控制

WebhookUrlPolicy 与 WebhookSafeConnection 当前实现了以下控制：

- URL 仅接受 http / https，拒绝 URL user-info。
- 阻止 localhost、受限 hostname、loopback、私网、link-local、multicast 和其他受限地址。
- 保存配置时检查 URL；实际发送时再次执行校验。
- DNS 解析结果只要包含受限地址就拒绝。
- 通过 ConnectCallback 在连接时再次解析并连接已校验的公网 IP，以降低 DNS 重绑定风险。
- HTTP handler 设置 AllowAutoRedirect=false、UseProxy=false，避免自动跳转和环境代理改变实际目的地。
- Webhook client 当前超时为 8 秒。
- 自定义 HTTP Webhook 可使用 HMAC-SHA256；Slack / Discord / Teams / 飞书 / 钉钉 / 企业微信使用各自的 URL 凭据或平台签名规则。
- URL / DNS 被 SSRF policy 拒绝时不会作为普通可恢复 HTTP 错误重试。

相关回归测试：[WebhookUrlPolicyTests.cs](../tests/KnowledgeRag.Api.IntegrationTests/WebhookUrlPolicyTests.cs)。平台协议和重试语义以 [Webhook Automation](WEBHOOK_AUTOMATION.md) 为准。

### 生产要求与限制

- Webhook 管理仅向 Tenant Admin 开放；只允许配置业务上必要的目标。
- 接收方应使用原始 request body bytes 验证 HMAC，并采用 constant-time comparison；用解析后重新序列化的 JSON 验签可能失败。
- 接收方应以 Delivery ID 做幂等，因为 CAP / HTTP retry 可能重复投递。
- 当前不跟随 3xx，也不使用 ambient proxy；需要企业代理的部署不能假定 HTTP_PROXY 会被 Webhook client 使用。
- 当前策略允许公网 http URL；如果生产要求传输加密，应在产品配置策略或网络出口层进一步强制 HTTPS。
- 当前策略不是任意私网访问 allowlist。如果业务需要内网 Webhook，应设计独立的受控 egress / allowlist，而不是关闭 SSRF 校验。
- DNS / IP 校验降低 SSRF 风险，但不能替代网络层 egress firewall。生产仍应阻断云 metadata、管理网段与不必要的内部服务访问。

### Tenant AI Provider URL

Tenant AI Provider 的 Base URL 是另一个出站请求边界。按 [AI Provider Configuration](AI_PROVIDER_CONFIGURATION.md) 的当前配置说明，生产 Provider 应使用公开 HTTPS endpoint，并保留当前 URL / DNS 校验策略。不要为了连接内部模型服务而直接放宽全局 SSRF 规则；若需要私有模型网络，应设计专门的受控网络边界并增加相应测试。

## 5. 文件上传、解析与内容访问

### 已实现的控制

- Kestrel request body 上限根据 Rag:MaxUploadMegabytes 计算，允许文件上限外额外 1 MiB multipart overhead；默认文件上限为 30 MiB，MultipartBodyLengthLimit 也限制单个 multipart section。
- 上传服务继续检查空文件、文件长度和支持的扩展名；启动时拒绝超出 1–63 MiB 范围的 MaxUploadMegabytes。
- 支持扩展名为 txt、md、csv、json、xml、html、pdf、docx。
- 展示文件名使用 Path.GetFileName；磁盘存储名由服务端生成的 document ID 与扩展名构成，而不是直接使用用户提供的路径。
- 上传、列表、下载、retry、reindex、cancel 和删除操作均要求认证，并按 KB Viewer / Editor 权限检查。
- 原始文件下载先通过 API 检查 KB 权限，不应把 Local / S3 bucket 作为公开静态目录。

### 生产加固

- 将上传限制与 reverse proxy / ingress 的 request size limit 一起配置，避免代理允许的请求体远大于应用限制。
- 对来自不可信用户的文档考虑病毒扫描、隔离区和审核流程。
- 解析器现对提取文本设置 MaxExtractedCharacters（默认 2,000,000）、对 PDF 设置 MaxPdfPages（默认 1,000），并为 HTML 正则设置 2 秒超时；DOCX 使用 Open XML MaxCharactersInPart 限制。仍应监控 CPU、内存、临时磁盘与并发，并考虑病毒扫描和隔离。
- 生产文件存储目录与应用代码目录分离，限制 OS 权限；S3 bucket 保持私有，禁止直接公开对象 URL。
- 检查本地磁盘和临时目录空间；删除、重建和失败恢复流程要在 SQLite 与 PostgreSQL 模式分别验证。
- 文件类型不能只依赖客户端 Content-Type；当前服务使用扩展名白名单，但这不等于完整的文件内容验证或恶意文件扫描。

**残余风险：** 这些限制降低了解析资源耗尽风险，但不是恶意文件扫描或完整沙箱；PDF 库打开文件、ZIP 容器解析及并发作业仍应在生产环境监控，不能把扩展名白名单描述成完整的上传安全方案。

## 6. Prompt Injection 与 Agent 工具边界

### 当前保护

- 检索文本作为不可信 Evidence 交给 Answer / Reviewer；Agent 不应把检索文本中的指令提升为系统指令。
- Vector、lexical search、RRF、去重、Evidence budget、权限过滤、Citation Contract 与最终 Citation Gate 由代码控制，不由 LLM 自由决定。
- Answer 可调用的 Specialist Agent-as-Tool 由当前 KB profile 配置；不应默认将任意网络、文件系统或管理能力暴露给 Specialist。
- FinalAnswerPolicy 在输出前检查 Evidence ID：非法引用 fail-closed；知识型非 abstention 答案必须带有效引用；只把真正引用的 Evidence 暴露为 Sources。
- Reviewer 失败或仍拒绝答案时，执行有界修复或返回安全的证据不足答案。

相关实现：[RagAgentService.cs](../src/KnowledgeRag.Infrastructure/Conversations/RagAgentService.cs)、[FinalAnswerPolicy.cs](../src/KnowledgeRag.Infrastructure/Conversations/FinalAnswerPolicy.cs)、[RagWorkflowPolicy.cs](../src/KnowledgeRag.Infrastructure/Conversations/RagWorkflowPolicy.cs)。

### 残余风险

Citation Gate 只能验证引用 ID 与 Evidence 对应关系，不能证明答案在语义上完全正确，也不能单独消除 Prompt Injection。生产中应继续遵循：

- 检索文档、用户输入、工具输出都视为不可信数据。
- 不把密钥、隐藏提示词或其他租户数据注入可被模型读取的上下文。
- Specialist 工具只暴露完成任务所需的最小输入与输出。
- Prompt、Agent、RAG Profile 修改应受管理员授权并写入 Audit Log。
- 对恶意指令、越权请求、冲突证据、无答案问题和伪造引用保留 Evaluation regression cases。

## 7. Token / Cost / Budget 与滥用防护

当前实现包含多层成本治理：

- RAG Profile 可配置 Daily Run Limit 与 Monthly Budget。
- RunAccounting 在执行前进行 Reservation，在结束时记录 settlement；超过 1 小时的陈旧 Reservation 会被标记为 reservation-expired。
- 默认 Evidence 上下文上限为总计 6000 tokens、每文档 2200 tokens。
- 默认 Planner 查询上限为 3；Reviewer 默认最多 1 次 Repair retry。
- Model Pricing / Deployment identity 用于估算运行成本并写入 Run / Usage 记录。

这些控制限制单次 RAG 工作流的资源范围，但不同于请求速率限制。API 提供全局固定窗口 429 Rate Limiter，默认每个已认证用户/API Key 或匿名 IP 每 60 秒 120 个请求；登录、注册和刷新令牌接口另有每来源 IP 每 60 秒默认 10 次的严格上限，可通过 RateLimiting:AuthPermitLimit 配置且不会超过全局 PermitLimit。可通过 RateLimiting:PermitLimit 与 RateLimiting:WindowSeconds 调整全局设置。限流为单实例内存计数，不是分布式配额；多实例生产仍应在 Gateway / Reverse Proxy 层统一按用户、Tenant、IP 和高成本 endpoint 限流。

不要把成本估算当作 Provider 最终账单；对预算接近阈值、重试激增、异常长问题和批量 Evaluation 应设置监控与告警。

## 8. SSE、日志、Trace 与 Audit Log

- Chat 正文在完整 Workflow 与 Final Citation Gate 之后才向客户端发送；Progress Timeline 可在运行中实时更新。
- Progress payload 可能包含本轮检索 query，因此即使它不是 Chain of Thought，也可能包含用户输入或敏感业务词汇。
- Conversation 会持久化用户消息与 Assistant Answer；RAG Run 记录 Query、Answer、运行状态、成本估算和结构化 Trace。
- Audit Log 记录动作、资源、状态码、IP address、User-Agent 与部分操作详情。AuditLogMiddleware 现在位于 Authorization 之前，可记录已认证 actor 被授权中间件拒绝的状态码，并对 4xx/5xx 设置 failed 标志。匿名认证失败不会写入关联用户的 Audit Log，仍应结合反向代理与安全日志监控。
- OpenTelemetry span tags 用于诊断阶段、检索数量、模型与耗时。配置导出端点时应限制 Collector / backend 的访问和数据保留。
- 不应记录 Bearer Token、Refresh Token、API Key、Provider Credential、Webhook Secret 或完整的敏感文件内容。

生产建议：为 Audit / Trace / Conversation 数据设定访问角色、保留期限、备份加密和安全导出流程；将外部可访问的 Telemetry backend 与应用管理面隔离。

## 9. HTTPS、CORS、反向代理与请求限制

API pipeline 现已在配置的可信代理之后处理 Forwarded Headers；Production 响应启用 HSTS；可通过 Security:EnforceHttpsRedirection 显式启用应用层 HTTPS redirect（要求 Kestrel HTTPS 端口或受信任代理提供正确的 X-Forwarded-Proto）。CORS 仅允许 Cors:AllowedOrigins 中的精确 origin，默认空列表即不允许跨域。Rate Limiting 默认启用。

生产部署应：

- 由受控 Load Balancer / Ingress 终止 TLS 并强制 HTTPS；若由应用重定向，先配置 ForwardedHeaders:KnownProxies 与 HTTPS 端口，再启用 Security:EnforceHttpsRedirection。
- API 与 React UI 尽量使用同源部署；若必须跨域，配置明确的 Cors:AllowedOrigins allowlist，不使用任意 Origin + credentials 的组合。
- 只信任 ForwardedHeaders:KnownProxies 中的代理 IP；默认不信任任意客户端提交的转发头。
- 在 ingress 与应用两侧设置一致的 request size、连接超时、上游超时和并发限制。
- Swagger 在 Development 默认开放；其他环境默认关闭。只有在受控网络确实需要时才设置 `Swagger:Enabled=true`（Compose: `SWAGGER_ENABLED=true`），并通过 ingress / VPN / allowlist 限制访问。
- 对管理端点使用网络层访问控制，并持续验证认证和 RBAC，而不是依赖路径难以猜测。

## 10. 生产上线检查清单

- [ ] 生产 Auth:SigningKey 已显式覆盖默认占位值，并来自 Secret Store；应用启动时会拒绝已知占位值。
- [ ] AUTH_ALLOW_REGISTRATION=false，除非已批准公开注册并有滥用防护。
- [ ] SECRET_PROTECTION_MASTER_KEY 已独立设置；MasterKey / PreviousMasterKeys / LegacyAuthSigningKeys 的轮换步骤已演练。
- [ ] S3 / PostgreSQL / RabbitMQ / Provider 凭据均不是示例值，权限按最小化配置。
- [ ] HSTS、CORS allowlist、可信 forwarded headers、request size 与应用层 / ingress rate limit 已核对。
- [ ] Tenant / KB role、API Key scopes、KB binding 和 API Key 撤销流程经过验证。
- [ ] Webhook HMAC、DNS SSRF 校验、redirect/proxy 禁用、timeout 与 CAP retry 语义经过验证。
- [ ] 上传大小、文件解析资源消耗、临时目录权限与 S3 私有访问边界经过验证。
- [ ] Conversation、Run Trace、Audit、OpenTelemetry 的敏感数据访问与保留策略已定义。
- [ ] Evaluation 覆盖 Prompt Injection、越权、无答案、冲突证据和引用错误。
- [ ] 备份、恢复、密钥丢失恢复、Provider Credential 重新录入和 Webhook test 流程已演练。

## 11. 本次审计中需单独跟踪的事项

1. **会话列表权限重检：** 仍需单独修复 ConversationService.ListAsync 在用户失去 KB membership 后返回旧会话元数据的问题。
2. **多实例限流：** 应用层限流是单实例内存计数；多实例部署必须在 Gateway / Reverse Proxy 统一限流。
3. **解析器沙箱与恶意文件扫描：** 当前大小 / 页数 / 字符限制不是病毒扫描或完整隔离执行。
