# 当前 AI 供应商配置与上线实施指南

本文档描述 KnowledgeRag 当前 `main` 已实现的 **Tenant AI Provider / Model Deployment / 路由治理 / Embedding Reindex / 成本治理** 配置流程。

适用范围：

- Tenant AI Provider Connection
- Chat / Embedding / Rerank Model Deployment
- Deployment health 与显式 fallback
- Tenant 默认模型
- Knowledge Base 级 Planner / Answer / Reviewer / Reranker 路由
- Embedding 索引代次重建与原子切换
- Deployment-first 模型价格与 Usage 归属

> 当前 Tenant Provider Catalog 只暴露 `openai-compatible` 类型，但可以连接满足项目兼容要求的 OpenAI-compatible API，例如 OpenAI、DeepSeek、Qwen 兼容网关、vLLM、LiteLLM、OpenRouter 等。是否真的可用于某种模型类型，取决于目标端点是否实现本文后续列出的接口契约。

---

## 1. 当前配置模型

当前配置不是“填一个 API Key 就结束”，而是分成 6 层：

```text
Platform AI fallback (.env / appsettings)
            │
            ▼
Tenant
  └─ Provider Connection
       ├─ Base URL
       ├─ Credential
       └─ enabled / revision
            │
            ▼
     Model Deployment
       ├─ remote model name
       ├─ chat / embedding / rerank
       ├─ embedding dimensions
       ├─ health status
       └─ explicit fallback deployment
            │
            ├───────────────┐
            ▼               ▼
      Tenant Defaults     KB RAG Profile
      Chat               Planner
      Embedding target   Answer
      Reranker           Reviewer / Specialist
                         Reranker
            │               │
            └───────┬───────┘
                    ▼
          Immutable runtime snapshot
                    │
                    ├─ Chat / Planner / Reviewer
                    ├─ Reranker
                    └─ Embedding Reindex target
```

关键原则：

1. **Provider Connection** 表示一个租户级供应商账号或 Gateway。
2. **Model Deployment** 是稳定的治理身份，不只是模型字符串；health、fallback、价格、Run Usage 都围绕 Deployment 管理。
3. Chat 类角色可以在 Knowledge Base 级显式绑定 Deployment。
4. 未显式绑定时使用 Tenant Default，再没有才回退到平台级 `AI_*` 配置。
5. **修改 Tenant 默认 Embedding 不会立即改变现有知识库的向量空间。** 必须执行版本化 Reindex，成功后才原子切换。
6. Chat / Rerank 在一次 RAG Run 开始时会解析实际 Deployment / fallback，并固定成 endpoint snapshot；Run 中途不会因为配置变化自动漂移到另一个 Provider。
7. **Embedding Reindex 当前固定使用 Tenant Default 指向的那个 Embedding Deployment，不会自动沿 Deployment fallback 链切换模型。** 这是为了保证一个 index generation 始终对应唯一向量空间。

---

## 2. 上线前准备

### 2.1 配置平台级 fallback

平台级 AI 配置仍然保留，用于：

- 租户尚未配置 AI Provider 时的默认能力；
- Tenant Default 为空时的 fallback；
- 已存在的 legacy 配置兼容。

`.env` 示例：

```env
AI_BASE_URL=https://api.openai.com/v1
AI_API_KEY=your-platform-key
AI_CHAT_MODEL=your-chat-model
AI_EMBEDDING_MODEL=your-embedding-model
AI_EMBEDDING_DIMENSIONS=1536
AI_SEND_EMBEDDING_DIMENSIONS=false
```

如果生产环境计划完全依赖 Tenant Provider，也建议保留一个受控的平台 fallback，或者明确接受“租户未配置时不可调用 AI”的运维结果。

### 2.2 配置 Credential 加密主密钥

Tenant Provider Credential 会被服务端加密保存，不会通过 API 返回明文。

生产环境必须配置独立主密钥：

```env
SECRET_PROTECTION_MASTER_KEY=replace-with-a-long-random-secret
```

要求：

- 使用部署平台的 Secret Store 注入；
- 不要提交到 Git；
- 不要与普通数据库密码共享；
- 轮换该密钥前必须设计现有 Credential 的迁移策略。

开发环境在该值为空时仍可兼容性回退到 `AUTH_SIGNING_KEY`，但生产环境不建议依赖此行为。

### 2.3 确认权限

Tenant AI 配置的权限边界：

- `Owner / Admin`：可以新增、编辑、测试、删除 Connection / Deployment，保存 Tenant Defaults；
- `Member`：可以读取安全元数据，但不能修改 Credential、路由与默认值。

知识库级 RAG Profile 还需要对应知识库的治理写权限。

---

## 3. 先确认供应商兼容性

Tenant-managed Provider 当前必须满足以下基础要求。

### 3.1 网络要求

Base URL 必须：

- 使用公网 `HTTPS`；
- 不能是 `localhost`；
- 不能解析到 loopback / RFC1918 私网 / link-local / IPv6 unique-local；
- 不依赖 HTTP redirect；
- 不依赖应用进程的 ambient HTTP proxy。

这是服务端 SSRF 安全策略的一部分。

### 3.2 API 契约

当前代码会使用这些 OpenAI-compatible 路径：

| 用途 | 当前调用契约 |
| --- | --- |
| Connection 测试 | `GET /models` |
| Deployment health | `GET /models` |
| Chat / Planner / Answer / Reviewer / Specialist | OpenAI-compatible Chat API，模型名来自 Deployment |
| Embedding | `POST /embeddings` |
| LLM Reranker | `POST /chat/completions`，要求返回 JSON score 列表 |

因此：

- “Connection 测试成功”只证明认证和 `/models` 基础连通性，不等于所有模型推理都成功；
- 上线前还必须实际跑一次 Chat / Retrieval / Embedding；
- Tenant-managed Embedding 当前会带 `dimensions` 参数，并校验返回向量长度与 Deployment 配置完全一致；目标服务必须兼容该行为；
- 当前 `rerank` Deployment 是 **LLM reranker 治理类型**，实际仍通过 `chat/completions` 做 JSON 相关性评分，并不是调用 Cohere 风格 `/rerank` API。

---

## 4. 步骤一：创建 Provider Connection

管理后台进入：

```text
租户与权限
→ 选择 Tenant
→ AI Providers
→ Provider Connections
→ 添加连接
```

填写：

| 字段 | 说明 |
| --- | --- |
| 名称 | 便于运维识别，例如 `OpenAI Production` / `LiteLLM Primary` |
| Provider | 当前选择 `OpenAI Compatible` |
| Base URL | OpenAI-compatible API 根地址，例如 `https://api.openai.com/v1` |
| API Key / Credential | 供应商密钥；只写入加密存储 |
| 启用此连接 | 是否允许该 Connection 被解析使用 |

推荐命名规则：

```text
<provider-or-gateway>-<environment>-<account>
```

例如：

```text
openai-prod-main
litellm-prod-primary
openrouter-prod-fallback
```

### 编辑 Credential

编辑已有 Connection 时：

- Credential 输入框留空：保留原 Credential；
- 填入新值：执行 Credential 轮换；
- API 不会把旧 Credential 回显给前端。

### 活动 Embedding 索引保护

如果某个 Connection 正被活动或构建中的 Embedding index generation 使用：

- 不能停用该 Connection；
- 不能更改 Provider Type；
- 不能更改 Base URL；
- **允许只轮换 Credential**。

如果确实要换 Embedding Provider / Base URL，应先配置新的 Deployment，并按第 11 节完成新索引代次切换。

---

## 5. 步骤二：测试 Provider Connection

在 Connection 卡片点击：

```text
测试
```

服务端会对该 Connection 执行：

```http
GET <BaseUrl>/models
Authorization: Bearer <credential>
```

成功条件：HTTP 2xx。

常见失败：

- `401 / 403`：Credential 错误或权限不足；
- `404`：Base URL 层级不对，例如缺少 `/v1`；
- SSRF blocked：URL 指向 localhost / 私网 / 非 HTTPS；
- timeout / DNS：目标服务无法从 KnowledgeRag 后端网络访问。

> Connection Test 不会检查指定模型是否真的存在，因此下一步仍要创建并检查 Deployment health。

---

## 6. 步骤三：创建 Model Deployment

进入：

```text
AI Providers
→ Model Deployments
→ 添加模型
```

每个 Deployment 需要：

- Provider Connection；
- 显示名称；
- 远端模型名；
- 模型类型；
- 可选显式 fallback；
- enabled 状态。

### 6.1 Chat Deployment

用于：

- Planner；
- Answer；
- Reviewer；
- Specialist Agents。

示例：

```text
Display name: Primary Chat
Remote model: <供应商实际模型 ID>
Type: chat
```

不要把展示名当成远端模型 ID。`Remote model` 必须与供应商 API 接受的 `model` 值一致。

### 6.2 Embedding Deployment

Embedding 额外要求填写：

```text
Embedding dimensions
```

例如某模型实际返回 1536 维，则 Deployment 必须配置为：

```text
1536
```

当前服务会：

1. 在 `/embeddings` 请求中发送配置的 dimensions；
2. 检查返回向量实际长度；
3. 长度不一致则直接失败，不写入混合维度向量空间。

因此不要猜维度，必须以目标供应商/模型实际能力为准。

### 6.3 Rerank Deployment

当前 `rerank` Deployment 用于项目内置 **LLM reranker**。

它要求目标模型能够处理 `chat/completions`，并按 Prompt 返回 JSON 相关性分数。

如果当前部署使用：

```env
RERANKER_MODE=local
```

可以暂时不配置 Tenant Reranker Default。

---

## 7. 步骤四：执行 Deployment Health Check

新建 Deployment 后初始 health 通常为：

```text
unknown
```

在 Deployment 行点击 health 图标执行检查。

服务端会保存：

- `healthy` / `unhealthy`；
- 最后检查时间；
- 最近错误信息。

当前 health check 仍使用：

```http
GET /models
```

### Chat / Rerank Runtime 对 health 的处理

Chat / Rerank 在解析显式 Deployment 路由时，下面情况会被视为不可用：

```text
Deployment disabled
OR
Provider Connection disabled
OR
health == unhealthy
```

`unknown` 当前不会自动阻断 Chat / Rerank 路由。

### Embedding 的 health 语义不同

当前 Embedding Reindex：

- 使用 Tenant Default 指向的 **确切 Embedding Deployment**；
- 检查 Deployment / Connection 是否 enabled；
- 不会因为 `health == unhealthy` 自动改用 fallback；
- 不会在一个 generation 内切换到另一 Embedding Deployment。

因此生产上在启动 Reindex 前应先主动执行 health check，再做实际 Embedding smoke。health 只是运维状态，不是 Embedding generation 的自动路由器。

生产建议：

1. 新建后主动执行 health check；
2. health 为 `healthy` 后再绑定生产 RAG Profile或作为 Embedding target；
3. 再做一次真实 Chat / Search / Reindex smoke，避免 `/models` 正常但推理接口异常。

---

## 8. 步骤五：配置显式 Fallback

编辑 Deployment，可以选择同类型的另一个 Deployment 作为 fallback。

例如：

```text
chat-primary
  └─ fallback → chat-secondary
                    └─ fallback → chat-emergency
```

规则：

- fallback 必须属于同一 Tenant；
- fallback 必须与源 Deployment 类型相同；
- 不能 fallback 到自己；
- 不允许形成循环，例如 `A → B → A`；
- Chat / Rerank 运行时最大解析深度为 16；
- 系统不会在未配置 fallback 时随机选择其它 Provider。

### Chat / Rerank 什么时候会走 fallback

运行开始时，如果当前 Deployment：

- disabled；
- 所属 Connection disabled；
- health 为 `unhealthy`；

则沿显式 fallback 链继续解析。

解析成功后，本次 Run 固定使用解析出的 endpoint snapshot，不会在中途再次动态换 Provider。

### Embedding fallback 当前不自动生效

虽然 Deployment 数据模型允许同类型 Embedding Deployment 保存 fallback 关系，但 **当前 Embedding Reindex 不沿该链自动选另一个模型**。

原因是 Embedding generation 必须固定：

```text
Provider + Base URL + Model + Dimensions
```

如果目标 Embedding Deployment 不可用，应：

1. 修复该 Deployment / Connection；或
2. 把 Tenant `Embedding target` 改到另一个明确的 Embedding Deployment；
3. 再启动新的 Reindex。

### 推荐策略

Chat / Rerank fallback 优先使用：

```text
同用途、同模型能力级别、不同 Provider / Gateway
```

而不是把质量差异极大的模型串成 fallback。

如果 fallback 模型能力明显不同，应在 Evaluation 中专门建立回归集验证结果质量。

---

## 9. 步骤六：配置 Tenant Defaults

进入：

```text
AI Providers
→ Tenant Defaults
```

当前有三个默认槽位：

| 默认项 | 用途 |
| --- | --- |
| Chat | KB 未显式绑定 Chat 角色 Deployment 时使用 |
| Embedding target | 下一次 KB Embedding Reindex 的目标 |
| Reranker | KB 未显式绑定 Reranker 时使用 |

### `Platform fallback / none`

选择该项表示 Tenant 不强制覆盖对应模型，运行时继续使用平台级配置或其它已有兼容 fallback。

### 特别注意 Embedding Default

保存新的：

```text
Embedding target
```

**不会立即改变现有知识库搜索使用的 Embedding 模型。**

它只决定下一次 KB Reindex 的目标 Deployment。

这是为了避免：

```text
查询向量 = 新模型
文档向量 = 旧模型
```

这种错误混合向量空间。

---

## 10. 步骤七：配置 Knowledge Base 角色路由

进入：

```text
AI Governance Center
→ 选择 Tenant
→ 选择 Knowledge Base
→ RAG 配置
→ Model & Budget Governance
```

可分别配置：

- `Planner deployment`
- `Answer deployment`
- `Reviewer / specialist deployment`
- `Reranker deployment`

### 推荐做法

生产配置优先绑定 Deployment，不要再依赖 raw model string。

例如：

```text
Planner deployment             → chat-fast-primary
Answer deployment              → chat-quality-primary
Reviewer / specialist          → chat-quality-primary
Reranker deployment            → rerank-primary
```

如果希望继承 Tenant Default：

```text
Tenant / platform default
```

### Legacy model 字段

界面仍保留：

- Legacy planner model
- Legacy answer model
- Legacy reviewer model

它们只用于旧配置兼容。

新环境建议：

```text
Deployment binding = 主配置
Legacy raw model   = 不再作为主要路由方式
```

否则同名模型位于不同 Provider 时无法形成可靠的治理身份。

---

## 11. 步骤八：切换 Embedding 时执行版本化 Reindex

当 Tenant Default 的 `Embedding target` 改成新 Deployment 后，进入目标 Knowledge Base：

```text
Knowledge Base
→ Documents
→ Embedding 索引代次
→ 按当前租户默认 Embedding 重建
```

系统会：

1. 固定目标 Deployment 的 Provider / Model / Dimensions 快照；
2. 创建新的 index generation；
3. 为全部当前文档生成 staging chunks / vectors；
4. pgvector / Qdrant 使用独立 generation 物理空间；
5. BM25 / PostgreSQL FTS 同样按 generation 隔离；
6. 重建期间所有查询继续使用旧 active generation；
7. 全部文档成功且源文档 manifest 未变化后，在数据库事务中切换 active generation；
8. 切换后 best-effort 清理旧 generation。

### 失败时

如果：

- 某文档 Embedding 失败；
- 供应商不可用；
- 文档集合/内容在重建中变化；
- 向量维度不匹配；

则新 generation 标记失败，**旧 active generation 保持可查询**。

不要通过手工修改数据库把未完成 generation 设为 active。

---

## 12. 步骤九：配置 Deployment Pricing

进入：

```text
AI Governance Center
→ 成本 / 模型
→ Deployment pricing
```

建议为实际生产使用的 Deployment 分别配置：

- input cost / 1M tokens；
- output cost / 1M tokens。

例如同一个远端模型名：

```text
provider-a / model-x
provider-b / model-x
```

可以拥有不同价格，不会再因为 `model-x` 字符串相同而串价。

### 价格查找顺序

当前成本治理优先：

```text
resolved DeploymentId price
      ↓ 没有
legacy (tenant, model-name) price
      ↓ 没有
平台兼容 fallback 估算
```

Budget Reservation 和实际 Usage Settle 都优先使用解析后的 Deployment 身份。

Legacy model-name price 只建议用于旧配置过渡。

---

## 13. 步骤十：上线验证

完成配置后不要只看“保存成功”，建议按下面顺序验证。

### 13.1 Connection

```text
AI Providers → Provider Connection → 测试
```

期望：成功。

### 13.2 Deployment health

对实际生产会用到的全部 Deployment 执行 health check。

期望：

```text
healthy
```

### 13.3 Embedding active generation

如果更换了 Embedding：

```text
Knowledge Base → Documents → Embedding 索引代次
```

确认：

- `重建中` 最终变为 `活动代次`；
- 活动模型名正确；
- dimensions 正确；
- 如果失败，旧索引仍然显示为活动配置。

### 13.4 Retrieval Lab

执行一条真实检索，确认：

- Vector Search 有结果；
- BM25 / FTS 有结果；
- Hybrid / RRF 排序正常；
- 没有 dimension mismatch。

### 13.5 RAG Chat

执行至少一条真实知识问答，确认：

- Planner 正常；
- Answer 正常；
- Reviewer / Citation Gate 正常；
- Sources 可打开；
- 如果启用 Specialist，Trace 中可以看到调用。

### 13.6 Run Trace / Audit

进入：

```text
AI Governance Center
→ Run Trace
```

检查本轮 Run 是否记录正确模型/Deployment 成本身份。

再进入：

```text
Audit Log
```

检查 Provider / Deployment / health / routing / pricing 修改是否有审计记录。

### 13.7 Cost

进入：

```text
成本 / 模型
```

确认本轮 Usage 使用对应 Deployment price，而不是意外落到 legacy model-name fallback。

---

## 14. 推荐的生产配置模板

一个常见的生产拓扑：

```text
Tenant: production

Provider Connections
├─ gateway-primary
└─ gateway-secondary

Chat Deployments
├─ chat-fast-primary
│    └─ fallback → chat-fast-secondary
├─ chat-fast-secondary
├─ chat-quality-primary
│    └─ fallback → chat-quality-secondary
└─ chat-quality-secondary

Embedding Deployments
├─ embedding-primary
└─ embedding-secondary

Rerank Deployments
├─ rerank-primary
│    └─ fallback → rerank-secondary
└─ rerank-secondary

Tenant Defaults
├─ Chat             → chat-quality-primary
├─ Embedding target → embedding-primary
└─ Reranker         → rerank-primary

KB Profile
├─ Planner          → chat-fast-primary
├─ Answer           → chat-quality-primary
├─ Reviewer         → chat-quality-primary
└─ Reranker         → rerank-primary
```

Chat / Rerank 可以使用显式 fallback。

Embedding 不应在同一 active index generation 中自动切换 Provider/模型；当前实现通过 **明确修改 Embedding target → 重新构建新 generation → 原子激活** 完成切换。

---

## 15. API 对照表

UI 的主要操作对应以下 API。

### Provider Catalog

```http
GET /api/tenants/ai/providers
```

### Provider Connections

```http
GET    /api/tenants/{tenantId}/ai/connections
POST   /api/tenants/{tenantId}/ai/connections
POST   /api/tenants/{tenantId}/ai/connections/{connectionId}/test
DELETE /api/tenants/{tenantId}/ai/connections/{connectionId}
```

### Model Deployments

```http
GET    /api/tenants/{tenantId}/ai/deployments
POST   /api/tenants/{tenantId}/ai/deployments
POST   /api/tenants/{tenantId}/ai/deployments/{deploymentId}/health
DELETE /api/tenants/{tenantId}/ai/deployments/{deploymentId}
```

### Tenant Defaults

```http
GET /api/tenants/{tenantId}/ai/defaults
PUT /api/tenants/{tenantId}/ai/defaults
```

### Knowledge Base Embedding Generation

```http
GET  /api/knowledge-bases/{knowledgeBaseId}/embedding-index
POST /api/knowledge-bases/{knowledgeBaseId}/embedding-index/reindex
```

Knowledge Base RAG Profile 与 Model Price API 由 Governance Center 使用；日常运维优先通过 UI 操作，以避免绕过类型、权限、fallback cycle 和 Deployment identity 校验。

---

## 16. 删除 / 停用时的约束

### Provider Connection 不能直接删除的情况

- 仍有关联 Model Deployment；
- 被活动或构建中的 Embedding generation 使用。

### Model Deployment 不能直接删除的情况

如果 Deployment 仍被以下任一对象引用，会拒绝删除：

- Tenant Default；
- 其它 Deployment 的 fallback；
- Knowledge Base RAG Profile；
- Deployment price；
- 历史 RAG Run identity。

如果 Deployment 已产生生产历史，推荐：

```text
停用，而不是删除
```

这样可以保持历史成本与 Run Trace 的身份稳定。

---

## 17. 故障处理与回滚

### Chat / Rerank Deployment 故障

优先：

1. 执行 Deployment health check；
2. 确认 Connection credential；
3. 如果已配置 fallback，确认 fallback health；
4. 必要时把 KB Profile 显式切到健康 Deployment；
5. 做 RAG Chat smoke。

### Embedding Deployment 故障

如果新 generation 尚未激活：

```text
不要修改旧 active generation
```

修复 Provider / Credential / Deployment，或者明确把 Tenant `Embedding target` 切到另一个 Embedding Deployment，然后重新执行 Reindex。

不要依赖 Embedding Deployment 的 fallback 字段自动切换 generation。

如果想回到旧 Embedding，应把 Tenant Embedding target 配回目标 Deployment，再生成一个新的 generation；不要手工拼接两套向量。

### Credential 泄露

1. 在供应商侧撤销旧 Key；
2. 编辑 Provider Connection，填入新 Credential；
3. 保存；
4. Connection Test；
5. Deployment health check；
6. Chat / Retrieval smoke。

仅 Credential 轮换不会要求把活动 Embedding generation 的 Base URL / 模型快照改掉。

---

## 18. 上线检查清单

### 基础安全

- [ ] `SECRET_PROTECTION_MASTER_KEY` 已配置为独立生产 Secret
- [ ] Base URL 为公网 HTTPS
- [ ] 没有把 Credential 写入 Git / 日志 /文档
- [ ] Connection Test 成功

### Deployment

- [ ] Chat Deployment 已创建
- [ ] Embedding Deployment 维度与实际返回一致
- [ ] Reranker Deployment 与当前 `RERANKER_MODE` 设计一致
- [ ] 所有生产 Deployment health 为 `healthy`
- [ ] Chat / Rerank fallback 仅同类型、无循环
- [ ] 没有把 Embedding fallback 当成自动 generation failover

### Routing

- [ ] Tenant Defaults 已确认
- [ ] KB Planner / Answer / Reviewer / Reranker 路由已确认
- [ ] 新配置不再主要依赖 Legacy raw model 字段

### Embedding

- [ ] 修改 Embedding target 后已执行 KB Reindex
- [ ] 新 generation 已变为 Active
- [ ] Retrieval Lab 查询正常
- [ ] 失败场景下旧 generation 仍可查询

### Cost / Governance

- [ ] 生产 Deployment 已配置价格
- [ ] Run Trace 使用正确 Deployment identity
- [ ] Audit Log 可看到关键配置修改
- [ ] Evaluation 回归集在新路由下通过

---

## 19. 最短实施路径

如果只是把一个新的 OpenAI-compatible 供应商上线到某个知识库，最短路径是：

```text
1. 配 SECRET_PROTECTION_MASTER_KEY
2. Tenant → AI Providers → 添加 Provider Connection
3. Connection Test
4. 添加 Chat Deployment
5. 添加 Embedding Deployment + 正确 dimensions
6. 对 Deployment 做 health check
7. 配 Tenant Defaults
8. Governance Center → RAG 配置 → 绑定 Planner / Answer / Reviewer / Reranker
9. Knowledge Base → Documents → 按当前 Tenant Embedding 重建
10. 等待新 Embedding generation Active
11. Cost → 为 Deployment 配价格
12. Retrieval Lab + RAG Chat + Run Trace + Audit 做上线验证
```

如果需要高可用，再在 Chat / Rerank 上补充第二 Provider / Deployment，并配置显式 fallback；Embedding 高可用通过“明确切 target + 新 generation 重建”处理。


## 20. 实现与回归测试入口

本文后续维护应优先核对当前实现和测试：

- Tenant Provider Connection / Deployment / defaults / health / fallback：`src/KnowledgeRag.Infrastructure/Tenancy/TenantAiProviderService.cs`
- Credential 加密保护：`src/KnowledgeRag.Infrastructure/AI/AiCredentialProtector.cs`
- RAG request snapshot、模型路由与 budget reservation：`src/KnowledgeRag.Infrastructure/Governance/RagRuntimeProfileService.cs`
- Embedding generation / active pointer 切换：`src/KnowledgeRag.Infrastructure/Documents/KnowledgeBaseIndexGenerationService.cs`
- Tenant AI Provider API：`src/KnowledgeRag.Api/Endpoints/TenantEndpoints.cs`
- RAG Profile、pricing、audit、prompt、webhook、evaluation API：`src/KnowledgeRag.Api/Endpoints/GovernanceEndpoints.cs`
- Provider API 回归：`tests/KnowledgeRag.Api.IntegrationTests/TenantAiProviderApiTests.cs`
- Usage / pricing 回归：`tests/KnowledgeRag.Api.IntegrationTests/AiUsageAccountingTests.cs`
- KB generation 回归：`tests/KnowledgeRag.Api.IntegrationTests/DocumentIndexVersionTests.cs`

如路由、字段、fallback 语义、Embedding generation 生命周期或计费优先级发生变化，应同步修改本文与 [RAG Agent Workflow](RAG_AGENT_WORKFLOW.md)，并运行 [验证指南](../VALIDATION.md) 中对应的测试。
