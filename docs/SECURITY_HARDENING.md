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
| P0 | 默认 JWT Signing Key | appsettings.json 中仍有公开的占位 SigningKey；当前校验主要检查长度，未拒绝该固定占位值。生产必须覆盖它。建议单独代码 PR 增加生产启动保护与回归测试 |
| P1 | API 请求限流 | 本次审计未发现全局 ASP.NET Core Rate Limiter 或统一 429 contract。生产应在可信 API Gateway / Reverse Proxy 配置按身份、Tenant 和来源的限流，并单独评估应用内限流 |
| P1 | Audit Log 完整性 | AuditLogMiddleware 位于 UseAuthorization 之后；被 Authorization middleware 提前拒绝的请求可能不会进入该审计中间件。不要把现有 Audit Log 当作完整的认证失败日志 |
| P1 | Secret 轮换 | Webhook Secret 使用 Auth SigningKey 加密；AI Provider Credential 使用 SecretProtection.MasterKey，未配置时会回退到 Auth SigningKey。轮换前需评估已有密文 |
| P1 | 上传与解析资源边界 | 应用有上传大小限制和扩展名白名单；本次审计未证明所有 PDF / DOCX / HTML 解析场景都具备解压大小、CPU、内存与执行时间上限 |
| P2 | TLS / CORS / Proxy | 当前 API pipeline 未配置 UseHttpsRedirection、UseHsts 或 UseCors。生产应由受控 ingress / reverse proxy 终止 TLS，并针对跨域部署配置明确 allowlist |
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

当前 appsettings.json 的默认 Auth:SigningKey 是公开的占位字符串，并且其长度满足现有 32 字节检查。如果生产部署未覆盖该值，攻击者可能构造能够通过 HMAC JWT 签名校验的令牌，导致身份伪造和越权访问。

**必须措施：** 生产环境始终显式注入随机密钥；部署发布流程应验证实际配置来源。**建议代码修复：** 在 Production 环境拒绝已知占位值，补充生产配置测试。文档说明不能替代这项代码保护。

### 会话列表的权限重检

当前 ConversationService.ListAsync 在传入 knowledgeBaseId 时会检查 KB Viewer；未传入该筛选参数时，会按会话所有权查询列表，但没有逐条重检用户当前的 KB membership。因此，用户被移出 KB 后，列表可能仍返回其原有会话的标题、Conversation ID、KnowledgeBase ID 与时间等元数据。消息读取和会话操作会再次检查 KB 权限，不能据此推断消息正文也可读取。

建议独立代码 PR：在无 KB 筛选的列表查询中，只返回当前 actor 仍有权限访问的 KB 会话，并为“撤销 KB membership 后列表不再返回元数据”增加 integration test。

## 3. Secret 管理、加密与轮换

### AI Provider Credential

AiCredentialProtector 使用 AES-GCM，密文以版本前缀保存。密钥由 SecretProtection:MasterKey 派生；如果该配置为空，当前代码会回退到 Auth:SigningKey，以兼容开发环境。

生产要求：

1. 显式配置独立的 SECRET_PROTECTION_MASTER_KEY。
2. 将该密钥保存在部署 Secret Store，不提交到仓库、镜像或日志。
3. 限制读取和修改 Secret 的运维权限。
4. 轮换前先验证已有 AI Provider Credential 的解密兼容性；直接更换 MasterKey 会使旧密文无法用新 key 解密，除非先执行受控重加密或重新录入。

### Webhook Secret

WebhookSecretProtector 当前从 Auth:SigningKey 派生加密密钥。Webhook Secret 以 enc:v1: 格式保存，API 只返回 hasSecret，不返回明文。

因此更换 AUTH_SIGNING_KEY 不仅影响 JWT，也会影响已保存的 Webhook Secret 解密。轮换步骤见 [Webhook Automation](WEBHOOK_AUTOMATION.md)：盘点订阅、重新获取平台密钥、在维护窗口轮换、重新保存 Secret 并逐一调用 test API 验证。

不要混淆两类加密密钥：AI Provider Credential 的首选配置是 SECRET_PROTECTION_MASTER_KEY；Webhook Secret 当前仍依赖 Auth SigningKey。当前仓库未提供一个通用的密钥轮换命令来自动重加密所有现有 Secret。

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

- Kestrel 的最大 request body size 配置为 64 MiB。
- RagOptions 默认 MaxUploadMegabytes 为 30 MiB；上传服务还检查空文件、大小和支持的扩展名。
- 支持扩展名为 txt、md、csv、json、xml、html、pdf、docx。
- 展示文件名使用 Path.GetFileName；磁盘存储名由服务端生成的 document ID 与扩展名构成，而不是直接使用用户提供的路径。
- 上传、列表、下载、retry、reindex、cancel 和删除操作均要求认证，并按 KB Viewer / Editor 权限检查。
- 原始文件下载先通过 API 检查 KB 权限，不应把 Local / S3 bucket 作为公开静态目录。

### 生产加固

- 将上传限制与 reverse proxy / ingress 的 request size limit 一起配置，避免代理允许的请求体远大于应用限制。
- 对来自不可信用户的文档考虑病毒扫描、隔离区和审核流程。
- 限制并监控文档解析的 CPU、内存、临时磁盘与并发；特别验证 DOCX 压缩包、异常 PDF 和大型 HTML 的资源消耗。
- 生产文件存储目录与应用代码目录分离，限制 OS 权限；S3 bucket 保持私有，禁止直接公开对象 URL。
- 检查本地磁盘和临时目录空间；删除、重建和失败恢复流程要在 SQLite 与 PostgreSQL 模式分别验证。
- 文件类型不能只依赖客户端 Content-Type；当前服务使用扩展名白名单，但这不等于完整的文件内容验证或恶意文件扫描。

**待验证：** 当前代码与测试未证明对所有解析器输入都存在统一的解压后大小、执行时间和恶意文件扫描控制。不要把扩展名白名单描述成完整的上传安全方案。

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

这些控制能限制单次 RAG 工作流的资源范围，但**不是请求速率限制**。当前审计未发现应用层全局 429 Rate Limiter。生产应在 API Gateway / Reverse Proxy 层按用户、Tenant、IP 与 endpoint 设定速率和并发限制，并针对注册、登录、Refresh、文件上传、Search、Chat、Evaluation Run、Webhook test 设定合适的保护。

不要把成本估算当作 Provider 最终账单；对预算接近阈值、重试激增、异常长问题和批量 Evaluation 应设置监控与告警。

## 8. SSE、日志、Trace 与 Audit Log

- Chat 正文在完整 Workflow 与 Final Citation Gate 之后才向客户端发送；Progress Timeline 可在运行中实时更新。
- Progress payload 可能包含本轮检索 query，因此即使它不是 Chain of Thought，也可能包含用户输入或敏感业务词汇。
- Conversation 会持久化用户消息与 Assistant Answer；RAG Run 记录 Query、Answer、运行状态、成本估算和结构化 Trace。
- Audit Log 记录动作、资源、状态码、IP address、User-Agent 与部分操作详情。不要默认它是完整的安全事件日志；当前中间件顺序可能漏掉被 Authorization middleware 提前拒绝的请求。
- OpenTelemetry span tags 用于诊断阶段、检索数量、模型与耗时。配置导出端点时应限制 Collector / backend 的访问和数据保留。
- 不应记录 Bearer Token、Refresh Token、API Key、Provider Credential、Webhook Secret 或完整的敏感文件内容。

生产建议：为 Audit / Trace / Conversation 数据设定访问角色、保留期限、备份加密和安全导出流程；将外部可访问的 Telemetry backend 与应用管理面隔离。

## 9. HTTPS、CORS、反向代理与请求限制

当前 API pipeline 注册异常处理、Swagger、静态文件、Authentication、Authorization 和 AuditLogMiddleware；本次审计未发现应用内 UseHttpsRedirection、UseHsts 或 UseCors。

生产部署应：

- 由受控 Load Balancer / Ingress 终止 TLS，强制 HTTPS，并按组织策略设置 HSTS。
- API 与 React UI 尽量使用同源部署；若必须跨域，配置明确的 CORS origin allowlist，不使用任意 Origin + credentials 的组合。
- 只信任受控代理提供的 forwarded headers；不要盲目信任客户端提交的转发头。
- 在 ingress 与应用两侧设置一致的 request size、连接超时、上游超时和并发限制。
- 限制 Swagger 的生产可见性或仅允许受控网络访问。
- 对管理端点使用网络层访问控制，并持续验证认证和 RBAC，而不是依赖路径难以猜测。

## 10. 生产上线检查清单

- [ ] 生产 Auth:SigningKey 已显式覆盖默认占位值，并来自 Secret Store。
- [ ] AUTH_ALLOW_REGISTRATION=false，除非已批准公开注册并有滥用防护。
- [ ] SECRET_PROTECTION_MASTER_KEY 已独立设置；已确认 AI Credential 与 Webhook Secret 的轮换影响。
- [ ] S3 / PostgreSQL / RabbitMQ / Provider 凭据均不是示例值，权限按最小化配置。
- [ ] HTTPS、HSTS、CORS、forwarded headers、request size、timeout 与 rate limit 在 ingress 侧已核对。
- [ ] Tenant / KB role、API Key scopes、KB binding 和 API Key 撤销流程经过验证。
- [ ] Webhook HMAC、DNS SSRF 校验、redirect/proxy 禁用、timeout 与 CAP retry 语义经过验证。
- [ ] 上传大小、文件解析资源消耗、临时目录权限与 S3 私有访问边界经过验证。
- [ ] Conversation、Run Trace、Audit、OpenTelemetry 的敏感数据访问与保留策略已定义。
- [ ] Evaluation 覆盖 Prompt Injection、越权、无答案、冲突证据和引用错误。
- [ ] 备份、恢复、密钥丢失恢复、Provider Credential 重新录入和 Webhook test 流程已演练。

## 11. 本次审计中需单独跟踪的事项

1. **生产占位 Signing Key 拒绝策略：** 建议独立代码 PR，不与纯文档修改混合。
2. **应用层 Rate Limiter：** 当前未发现统一实现；评估是否需要按 Tenant / API Key 进行应用层限流。
3. **认证失败审计覆盖：** 验证 AuditLogMiddleware 的当前顺序是否满足安全审计要求。
4. **文件解析资源限制：** 以恶意 / 极端输入建立针对 PDF、DOCX、HTML 的资源限制测试。
5. **Secret 轮换工具：** 明确 Auth SigningKey、AI Credential MasterKey 与 Webhook Secret 的独立轮换流程。
