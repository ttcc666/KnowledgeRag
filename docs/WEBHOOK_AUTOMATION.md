# Webhook Automation 配置与安全指南

KnowledgeRag 的 Webhook Automation 用于把 RAG Run 和 Evaluation 事件异步发送到外部 HTTP endpoint 或协作平台机器人。当前实现支持自定义 HTTP、Slack、Discord、Microsoft Teams、飞书 / Lark、钉钉和企业微信群机器人。

> Webhook 是出站通知能力，不是外部系统向 KnowledgeRag 写入数据的入口。所有目标地址都会经过 SSRF 安全策略。

## 1. 当前支持的 Provider

Provider Catalog API：

```http
GET /api/governance/webhooks/providers
```

当前 Provider ID：

| Provider ID | 渠道 | Payload / 展示 |
|---|---|---|
| `custom` | 自定义 HTTP Webhook | KnowledgeRag JSON event envelope，可选 HMAC-SHA256 |
| `slack` | Slack Incoming Webhook | Block Kit |
| `discord` | Discord Webhook | Embed，自动禁用 mentions |
| `teams` | Microsoft Teams | Adaptive Card |
| `feishu` | 飞书 / Lark 自定义机器人 | Interactive Card，可选机器人签名 |
| `dingtalk` | 钉钉自定义机器人 | Markdown，可选加签 |
| `wecom` | 企业微信群机器人 | Markdown |

Provider 名称由服务端 registry 归一化。未知 Provider 会被拒绝。

## 2. API

```http
GET    /api/governance/webhooks/providers
GET    /api/governance/webhooks?tenantId={tenantId}
POST   /api/governance/webhooks?tenantId={tenantId}
PUT    /api/governance/webhooks/{webhookId}?tenantId={tenantId}
DELETE /api/governance/webhooks/{webhookId}?tenantId={tenantId}
POST   /api/governance/webhooks/{webhookId}/test?tenantId={tenantId}
```

Webhook 管理要求 Tenant Admin。

典型创建请求：

```json
{
  "name": "RAG production notifications",
  "provider": "custom",
  "url": "https://example.com/hooks/knowledge-rag",
  "eventTypes": ["rag.run.completed", "evaluation.completed"],
  "secret": "replace-with-random-secret",
  "isActive": true
}
```

`eventTypes` 支持精确事件名，也支持：

```text
*
```

表示匹配所有 Dispatch 事件。

## 3. 当前事件

### `rag.run.completed`

在 Conversation RAG turn 成功完成并 settlement 后异步派发。

当前 payload 包括：

- Run ID
- Knowledge Base ID
- Conversation ID
- status
- source count
- latency
- estimated input / output tokens
- estimated cost
- Answer model
- Agent mode

失败或取消的 Conversation Run 当前会被 settlement 为 `failed` / `cancelled`，但 `rag.run.completed` 的自动 Dispatch 发生在成功完成路径。不要把这个事件当作“所有 RAG Run 状态变化”的完整审计流；完整状态应查询 RAG Runs。

### `evaluation.completed`

Evaluation Run 正常完成后派发。

payload 包括：

- Evaluation Run / Dataset identity
- Pass Rate
- Groundedness Rate
- Citation Validity Rate
- No-answer Accuracy
- Retrieval Hit Rate
- Average Recall@K / MRR / nDCG
- Average latency
- baseline run ID
- pass / groundedness / latency regression flags

Evaluation `cancelled` 或 `failed` 当前不会发送 `evaluation.completed`。

### `webhook.test`

由 Test API 直接向指定订阅发送，用于验证网络、格式和凭据。

Test 请求不经过正常 event filter Dispatch 队列；它会直接执行 delivery，并在超时时返回 504。

## 4. 异步投递模型

正常事件 Dispatch：

```text
RAG / Evaluation request
        ↓
WebhookAutomationService.DispatchAsync
        ↓
one or more CAP delivery messages
        ↓
WebhookDeliverySubscriber
        ↓
WebhookAutomationService.DeliverRequestedAsync
        ↓
HTTP provider adapter
```

每个匹配订阅发布一条独立 CAP delivery message，因此外部 HTTP 不在 Chat / Evaluation 主请求关键路径上执行。

### 本地 / SQLite

默认 CAP 使用 in-memory storage / queue，适合开发和测试。进程重启时不要假定未投递的 Webhook 消息仍然存在。

### PostgreSQL + RabbitMQ

生产异步投递建议使用：

```env
DATABASE_PROVIDER=PostgreSql
CAP_TRANSPORT=rabbitmq
```

CAP 的持久状态位于 PostgreSQL `cap` schema，RabbitMQ 承担 transport。相关部署见 [Production Operations](PRODUCTION_OPERATIONS.md) 和 [CAP + SignalR ingestion](../CAP_SIGNALR_INGESTION.md)。

## 5. Retry 语义

HTTP response 的重试分类：

会抛出异常并交由 CAP 重试：

- timeout / request timeout
- HTTP 429
- HTTP 5xx
- 其他连接 / 发送异常（非 SSRF block）

不会按可恢复错误重试：

- 普通 4xx（429 除外）
- URL / DNS 被 SSRF policy 拒绝
- 订阅已经删除、停用或事件不再匹配

CAP 的最大失败重试次数由：

```env
CAP_FAILED_RETRY_COUNT
```

控制。不要通过把重试次数设得很大来掩盖永久性配置错误。

## 6. Delivery 状态

每次实际 delivery 都会更新订阅：

- `LastStatusCode`
- `LastError`
- `LastDeliveredAt`
- `UpdatedAt`

API 不保存完整 delivery history 表，因此这些字段代表**最近一次 delivery 状态**，不是逐次审计日志。

建议外部 receiver 使用 `Delivery ID` 做自己的幂等 / 可追踪记录。

## 7. 自定义 HTTP Event Envelope

`custom` Provider 发送：

```json
{
  "id": "<event-id>",
  "type": "rag.run.completed",
  "createdAt": "<timestamp>",
  "data": {
    "...": "event payload"
  }
}
```

Headers：

```text
X-KnowledgeRag-Event: <event type>
X-KnowledgeRag-Delivery: <event UUID>
```

如果配置了 Secret，还会发送：

```text
X-KnowledgeRag-Signature: sha256=<lowercase hex HMAC>
```

签名算法：

```text
HMAC-SHA256(secret, exact UTF-8 event envelope body)
```

接收方应对**收到的原始 request body bytes**计算 HMAC，并使用 constant-time comparison 校验。不要 parse + reserialize JSON 后再验签，否则字段顺序 / formatting 变化可能导致不一致。

## 8. 平台专属 Secret

### Slack

Incoming Webhook URL 本身包含凭据，`secret` 留空。

### Discord

Webhook URL 本身包含凭据，`secret` 留空。

### Microsoft Teams

Workflow / Incoming Webhook URL 通常包含授权信息，`secret` 留空。

### 飞书 / Lark

机器人启用签名校验时填写机器人安全设置的签名密钥。服务端自动生成 `timestamp` 与 `sign`。

### 钉钉

机器人启用加签时填写安全设置密钥。服务端会把 `timestamp` 和 `sign` 附加到 Webhook URL query。

### 企业微信

机器人 key 位于 Webhook URL，`secret` 留空。

## 9. Secret 存储与轮换

Webhook API 不返回明文 Secret，只返回：

```json
{
  "hasSecret": true
}
```

写入时 Secret 通过 AES-GCM 加密后存库。

**重要：当前 Webhook Secret encryption key 派生自 `Auth:SigningKey`。**

因此修改：

```env
AUTH_SIGNING_KEY
```

不仅会影响 JWT 签名，也会让已有 `enc:v1:` Webhook Secret 无法用新 key 解密。

生产轮换 Signing Key 前应：

1. 盘点所有 `hasSecret=true` 的 Webhook。
2. 记录 / 重新获取平台机器人 Secret。
3. 在受控维护窗口更换 Signing Key。
4. 重新保存所有需要 Secret 的 Webhook。
5. 逐个调用 Test API 验证。

AI Provider Credential 使用另一套 `SecretProtection:MasterKey` / purpose-derived key，详见 [AI Provider Configuration](AI_PROVIDER_CONFIGURATION.md)。不要混淆两类 Secret 的轮换影响。

## 10. SSRF 安全策略

Webhook URL 保存时会先做语法检查；真实连接前还会再次校验目标。

当前策略：

- 只允许 `http` / `https` URL。
- URL 不允许包含 `user:password@host` user-info。
- 阻止 `localhost`、`*.localhost`、`*.local`。
- 阻止 IPv4 loopback、0/8、10/8、172.16/12、192.168/16、169.254/16、multicast / reserved >=224 等地址。
- 阻止 IPv6 loopback / link-local / site-local / multicast / RFC4193 unique-local。
- DNS 解析结果只要包含非公网地址，就拒绝整个目标。
- HTTP handler 禁止自动 redirect。
- 禁用 ambient proxy。
- 使用 `ConnectCallback` 对经过校验的 DNS 地址直接建立 socket connection。

这些限制意味着开发机上的：

```text
http://localhost:...
http://127.0.0.1:...
http://host.docker.internal:...
http://192.168.x.x:...
```

都不能作为 Webhook destination。测试 receiver 必须使用公网可解析地址，或通过单元 / integration test 覆盖本地逻辑。

不要为了让内部地址可达而删除 SSRF policy；如果产品未来需要私网 Webhook，应设计独立的受控 egress / allowlist 模型。

## 11. Redirect 与 Proxy

当前 Webhook client：

```text
AllowAutoRedirect = false
UseProxy = false
PooledConnectionLifetime = 2 minutes
Timeout = 8 seconds
```

因此：

- 3xx 不会自动跟随到另一个 host。
- 系统代理 / `HTTP_PROXY` 不会被用于 Webhook delivery。
- 需要企业 egress proxy 的部署不能直接依赖 ambient proxy；当前实现没有“受控 proxy + destination validation”配置。

## 12. Provider Rich Message 行为

`WebhookChannelMessageFormatter` 对已知事件生成统一 presentation，再由 Provider adapter 转换为平台格式。

### RAG Run

典型字段包括：

- latency
- source count
- estimated cost
- input / output tokens
- answer model
- agent mode
- run / conversation short ID

### Evaluation

典型字段包括：

- pass rate
- groundedness
- citation validity
- no-answer accuracy
- average latency
- regression warning

未知事件使用 generic formatter，最多提取 payload 中 8 个 primitive 字段用于展示。

## 13. 接收方幂等设计

CAP / HTTP retry 意味着 receiver 应允许同一 Event ID 被重复发送。

推荐：

1. 以 `X-KnowledgeRag-Delivery` 或 envelope `id` 作为幂等键。
2. 在 receiver 侧保存已处理 ID 与过期时间。
3. 对重复 delivery 返回 2xx，而不是再次执行副作用。
4. 不要使用 message 文本内容作为幂等键。

## 14. 常见故障

### Test 返回 504

说明 outbound HTTP 超过当前 8 秒 timeout。检查目标服务响应时间、网络路由与 TLS 握手。

### `Webhook 目标被 SSRF 安全策略阻止`

检查 DNS 是否解析到私网 / loopback / link-local 地址，或目标是否使用 `.local` / localhost。DNS 同时返回公网和私网地址也会被拒绝。

### 401 / 403

通常是 Webhook URL token / key 已失效，或平台要求的机器人配置不匹配。Secret 签名只适用于对应 Provider 的安全模式。

### 429

KnowledgeRag 会抛出可重试异常并交给 CAP 重试。仍应降低事件频率或提高目标端配额，避免形成重试堆积。

### 5xx

属于可重试错误。检查 `LastError`、RabbitMQ / CAP backlog 和目标服务日志。

### 修改 EventTypes 后旧消息仍投递吗？

Subscriber 在真正发送前会重新加载订阅并检查 `IsActive` 与 event filter。已停用或已不匹配的订阅会跳过。

### 删除订阅后队列里还有旧消息

Subscriber 找不到订阅时会直接返回，不再发送。

## 15. 上线检查清单

- [ ] 使用 Tenant Admin 配置订阅。
- [ ] Provider 与目标 URL 类型匹配。
- [ ] EventTypes 只订阅需要的事件；避免无意使用 `*`。
- [ ] 自定义 HTTP receiver 已实现 HMAC 校验与 Delivery ID 幂等。
- [ ] Provider Secret 未写入仓库、日志或截图。
- [ ] PostgreSQL + RabbitMQ 场景下 CAP / RabbitMQ readiness 正常。
- [ ] Test API 成功。
- [ ] 真实 `rag.run.completed` / `evaluation.completed` 能到达目标。
- [ ] 目标系统故障时 429 / 5xx retry 行为符合预期。
- [ ] 监控最近一次 `LastStatusCode` / `LastError`。

## 16. 实现与测试入口

实现：

- `src/KnowledgeRag.Api/Endpoints/GovernanceEndpoints.cs`
- `src/KnowledgeRag.Infrastructure/Governance/WebhookAutomationService.cs`
- `src/KnowledgeRag.Infrastructure/Governance/WebhookDeliverySubscriber.cs`
- `src/KnowledgeRag.Infrastructure/Governance/WebhookChannelAdapters.cs`
- `src/KnowledgeRag.Infrastructure/Governance/WebhookUrlPolicy.cs`
- `src/KnowledgeRag.Infrastructure/Governance/WebhookSecretProtector.cs`
- `src/KnowledgeRag.Infrastructure/DependencyInjection.cs`

回归测试：

- `tests/KnowledgeRag.Api.IntegrationTests/WebhookChannelAdapterTests.cs`
- `tests/KnowledgeRag.Api.IntegrationTests/WebhookUrlPolicyTests.cs`
- `tests/KnowledgeRag.Api.IntegrationTests/WebhookDeliveryTimeoutExceptionTests.cs`
- `tests/KnowledgeRag.Api.IntegrationTests/GovernanceApiTests.cs`

相关文档：

- [Governance & Evaluation](GOVERNANCE_EVALUATION.md)
- [Production Operations](PRODUCTION_OPERATIONS.md)
- [CAP + SignalR ingestion](../CAP_SIGNALR_INGESTION.md)
