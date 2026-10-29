# Governance、Evaluation 与 AI Cost 运维指南

本文描述 KnowledgeRag 当前实现中的治理中心：Knowledge Base 级 RAG Profile、模型路由、Prompt Profile、RAG Run Trace、Token / Cost / Budget、Model Pricing、Audit Log 与 Evaluation v3。

> 本文只描述当前代码已实现的行为。若 UI、API 或数据结构发生变化，应优先核对 `GovernanceEndpoints`、`GovernanceService`、`RagRuntimeProfileService`、`RagRunAccountingService` 与 `EvaluationService`。

## 1. 治理对象与关系

```text
Tenant
  ├─ AI Provider Connections / Model Deployments
  ├─ Model Prices
  ├─ Prompt Profiles
  ├─ Audit Logs
  ├─ Webhook Subscriptions
  └─ Knowledge Bases
       ├─ RAG Profile
       ├─ RAG Runs / Trace / Usage Ledger
       └─ Evaluation Datasets
            ├─ Evaluation Cases
            └─ Evaluation Runs
```

治理中心的目标不是把检索、引用或预算策略交给模型，而是把这些策略固定成可审计的服务端配置与执行记录。

## 2. 权限模型

当前主要 Governance API 的最低权限如下：

| 能力 | 最低权限 |
|---|---|
| 查看 Tenant Governance Overview | Tenant Member |
| 查看 KB RAG Profile | KB Viewer |
| 修改 KB RAG Profile | KB Owner |
| 查看 KB RAG Runs | KB Viewer |
| 查看 Tenant Audit Log | Tenant Admin |
| 查看 Model Prices | Tenant Member |
| 修改 Model Prices | Tenant Admin |
| 查看 Prompt Profiles | Tenant Member |
| 创建 / 修改 Prompt Profile | Tenant Admin；若绑定 KB，还需要该 KB Owner |
| 查看 / 修改 / 删除 Webhook | Tenant Admin |
| 创建 Evaluation Dataset | Tenant Admin + 目标 KB Viewer |
| 查看 Dataset / Cases / Runs | Tenant Member |
| 新增 Evaluation Case | Tenant Admin + Dataset 对应 KB Viewer |
| 执行 Evaluation Run | Tenant Admin + Dataset 对应 KB Viewer |

权限检查在服务端执行。前端隐藏按钮不是权限边界。

## 3. RAG Profile

API：

```http
GET /api/governance/knowledge-bases/{knowledgeBaseId}/rag-profile
PUT /api/governance/knowledge-bases/{knowledgeBaseId}/rag-profile
```

RAG Profile 控制一轮 RAG 的关键运行参数：

- Planner：是否启用、最大 query 数。
- Multi-query：每个 query 的 top-K。
- Evidence：合并后的最大 Evidence 数。
- Reviewer：是否启用、最大 repair 次数、最低置信度。
- Planner / Answer / Reviewer / Reranker Deployment 绑定。
- 兼容用的 raw model-name override。
- Prompt Profile。
- Agent Mode。
- 每月预算与每日 Run 上限。

### 3.1 服务端范围限制

保存时服务端会确定性归一化：

| 字段 | 当前范围 |
|---|---:|
| `PlannerMaxQueries` | 1–6 |
| `MultiQueryTopK` | 1–30 |
| `MaxMergedEvidence` | 1–40 |
| `ReviewerMaxRetries` | 0–3 |
| `ReviewerMinConfidence` | 0–1 |
| `MonthlyBudgetUsd` | 最低 0 |
| `DailyRunLimit` | 最低 0 |

空的 `AgentMode` 会归一化为 `specialists`。

### 3.2 Deployment 路由

Planner / Answer / Reviewer 使用 Chat Deployment；Reranker 使用 Rerank Deployment。保存 Profile 时会验证 Deployment 属于当前 Tenant、类型正确，并解析显式 fallback 链。

一次 RAG 请求开始时，`RagRuntimeProfileService` 只解析一次 execution snapshot。Reservation、Agent Workflow 与后续 usage settlement 都使用同一份模型 / Prompt / 策略快照，避免管理员中途修改配置导致同一轮混用新旧 Provider。

Deployment、health、fallback 和 Tenant Defaults 的详细语义见 [AI Provider Configuration](AI_PROVIDER_CONFIGURATION.md)。

## 4. Budget Reservation → Execute → Settle

KnowledgeRag 的预算控制不是“运行结束后记账”，而是：

```text
Resolve immutable RAG Profile
        ↓
Estimate worst-case AI usage
        ↓
Reserve budget / daily run slot
        ↓
Execute workflow
        ↓
Observe actual workflow events
        ↓
Settle estimated usage ledger
```

### 4.1 Daily Run Limit

当 `DailyRunLimit > 0`：

- 按 UTC 自然日统计。
- `completed` 与仍处于 `reserved` 状态的 Run 都占用今日配额。
- 这样并发请求不能同时绕过配额检查。

### 4.2 Monthly Budget

当 `MonthlyBudgetUsd > 0`：

- 按 UTC 月份统计。
- 预算检查会把历史 Run 的已记录成本与本次 reservation 预估成本一起计算。
- 如果当前已达到预算，或剩余额度不足以覆盖本次 reservation，运行会在调用模型前被拒绝。

### 4.3 并发安全

有预算 / 配额限制时，Reservation 使用 Serializable transaction。SQLite busy/locked 与 PostgreSQL serialization/deadlock 冲突会做有限重试。

### 4.4 过期 Reservation

`reserved` 状态超过 1 小时会在后续 Reservation 检查时被标记为：

```text
reservation-expired
```

并把该 reservation 的成本归零，同时写入 `usage.reservation.expired` trace 事件。

这不是后台定时清理任务：如果之后没有新的 Reservation，旧记录不会主动改变状态。

## 5. AI Usage 与 Cost

RAG Run 在 reservation 和 settlement 阶段都会生成 AI usage line items。当前 operation 可能包括：

- `planner`
- `embedding`
- `reranker`
- `answer`
- `reviewer`
- `specialist`

每个 line item 记录：

- operation
- model
- calls
- input / output token estimate
- estimated cost
- `DeploymentId`
- `ProviderType`
- `estimated=true`

这些 token 与费用是 **服务端估算值**，不是 Provider 官方账单。当前通用 token estimate 采用近似字符数换算，并结合 Evidence / candidate / workflow event 数量估计。

### 5.1 价格优先级

运行时定价优先级：

```text
Deployment-specific Model Price
        ↓
legacy (tenant, model-name) Model Price
        ↓
platform fallback pricing（适用于相应 chat 路径）
```

Embedding 是例外：没有显式 Deployment / model price 时，不会错误套用 Chat token 单价，当前会按 0 记录成本。因此如果你需要准确的 Embedding 成本治理，应显式配置对应价格。

### 5.2 Model Price API

```http
GET /api/governance/model-prices?tenantId={tenantId}
PUT /api/governance/model-prices?tenantId={tenantId}
```

推荐优先按 `DeploymentId` 配价，而不是只按 model name。这样两个 Provider 即使暴露同一个远端模型名，也能独立计价。

保存价格会写入 Audit Log：

```text
ai.price.saved
```

## 6. RAG Run 与 Trace

API：

```http
GET /api/governance/runs?knowledgeBaseId={knowledgeBaseId}&limit=100
```

服务端最多返回 500 条，按创建时间倒序。

Run 记录当前包括：

- Tenant / Knowledge Base / Conversation / User / API Key identity
- query / final answer
- status
- Answer model / Answer Deployment
- Agent mode
- cited source count
- estimated input / output tokens
- estimated cost
- latency
- structured trace JSON

### 6.1 Usage Ledger

settlement 会向 Trace 追加：

```text
usage.ledger
```

其中包含本轮已观察到的 operation line items、估算 token、费用和 Answer Deployment identity。

### 6.2 失败与取消

如果 Workflow 在 reservation 后失败或取消，服务端仍会对已观察到的调用执行 settlement，并把 Run 标记为 `failed` 或 `cancelled`。因此成本治理不能只统计 `completed` Run。

### 6.3 Trace 安全边界

Trace 应只保存安全的结构化执行信息。当前设计不依赖保存 Chain of Thought、系统 Prompt 或 Provider Credential 来完成审计。

## 7. Prompt Profile

API：

```http
GET  /api/governance/prompts?tenantId={tenantId}
POST /api/governance/prompts?tenantId={tenantId}
PUT  /api/governance/prompts/{promptId}?tenantId={tenantId}
```

Prompt Profile 当前包含：

- Name
- 可选 KnowledgeBaseId
- Planner Prompt
- Answer Prompt
- Reviewer Prompt
- Version
- Active state

行为：

- 创建同名新 Profile 时会产生递增 version。
- 更新已有 Profile 时保留其 identity / version。
- 当一个绑定 KB 的 Profile 被设为 active 时，服务端会把该 KB 的其他 active Prompt Profile 关闭。
- 当前 API 没有 Prompt Profile delete endpoint。

Prompt 内容属于高敏感运行配置；不要把 secret、凭据或不必要的用户隐私写入 Prompt。

## 8. Audit Log

API：

```http
GET /api/governance/audit?tenantId={tenantId}&limit=200
```

服务端最多返回 1000 条。

当前 Audit 用于保存治理 / 安全相关操作，例如：

- RAG routing 配置变更
- Model Price 变更
- Chat feedback
- 其他调用 `WriteAuditAsync` 的操作

Audit 记录可能包含 User / API Key identity、resource、status code、IP、User-Agent 与安全的 details JSON。调用方不应把 secret 或 Chain of Thought 填入 details。

## 9. Evaluation v3

Evaluation 运行器版本：

```text
schemaVersion = 3
evaluatorVersion = retrieval-citation-v3
```

### 9.1 Dataset

```http
GET  /api/governance/evaluation/datasets?tenantId={tenantId}
POST /api/governance/evaluation/datasets?tenantId={tenantId}
```

Dataset 绑定一个 Knowledge Base。

### 9.2 Case

```http
GET  /api/governance/evaluation/datasets/{datasetId}/cases?tenantId={tenantId}
POST /api/governance/evaluation/datasets/{datasetId}/cases?tenantId={tenantId}
```

Case 输入字段：

- `category`
- `query`
- `expectedAnswerContains`
- `requiresKnowledge`
- `expectedSources[]`

`ExpectedSources` 由：

- `fileName`
- 可选 `chunkIndex`

组成。

如果没有 expected source，retrieval ranking 指标对该 Case 标记为 not applicable，不会让 Case 因缺少 retrieval label 失败。

### 9.3 no-answer Case

`category = "no-answer"` 有特殊语义：正确行为是安全 abstention，而不是强行产生知识答案。

当前 deterministic scoring 会识别一组中英文“证据不足 / 无法可靠回答”信号；no-answer Case 必须表现为 insufficient-evidence abstention，且不能包含非法 Citation。

### 9.4 Citation / Grounding 规则

知识型可回答 Case 仅仅“检索到了 Evidence”不算 grounded。

通过要求包括：

- answer 非空；
- `ExpectedAnswerContains`（如配置）匹配；
- Citation marker 可以针对本轮 Evidence 合法解析；
- 需要知识的普通 Case 至少有一个合法 Citation / Source；
- no-answer Case 正确 abstain；
- retrieval expectation（如配置）通过。

Evaluation 和 Chat 共享相同的 Structured Citation Contract，因此伪造 `[E#]` / 无效 Evidence ID 不会被当作有效 grounded answer。

## 10. Retrieval Metrics

配置 `ExpectedSources` 后，Evaluation v3 确定性计算：

- Hit Rate
- Recall@K
- MRR
- nDCG

当前 per-case retrieval pass 条件是 **至少命中一个 expected source**。Recall / MRR / nDCG 会作为更细的质量指标记录，但不会要求 Recall=1 才让该 Case 通过。

这意味着：

- `Passed=true` 不代表所有 expected source 都召回。
- 比较多文档 / multi-hop 检索时，应同时观察 Recall@K 与 nDCG，而不是只看总 Pass Rate。

## 11. Evaluation Run 与 Baseline

执行：

```http
POST /api/governance/evaluation/datasets/{datasetId}/run?tenantId={tenantId}
```

运行前会：

1. 校验 Dataset 至少有一个 Case。
2. 解析一次 KB 的 RAG execution snapshot。
3. 查找该 Dataset 最近一次 `completed` Run 作为 baseline。

每个 Case 会调用当前 RAG Agent，并记录 latency 与 deterministic assessment。

汇总指标包括：

- Pass Rate
- Groundedness Rate
- Citation Validity Rate
- No-answer Accuracy
- Retrieval Hit Rate
- Average Recall@K
- Average MRR
- Average nDCG
- Average latency
- Average retrieved / cited source count
- category summaries

### 11.1 Regression 判定

与最近一次 completed baseline 比较时：

- Pass Rate 下降 → regression。
- Groundedness Rate 下降 → regression。
- 平均延迟超过 baseline 的 120% → latency regression。

这只是当前内置 regression signal，不等于完整的发布 SLO。生产发布可以在外部流水线增加更严格阈值。

### 11.2 cancelled / failed

Evaluation 被取消时保存 `cancelled`；发生异常时保存 `failed`。已经执行完的 Case 会作为 partial summary 写入 ResultsJson。

只有 `completed` Run 才会成为之后的 baseline。

## 12. Evaluation Webhook

Evaluation 完成后发送：

```text
evaluation.completed
```

payload 包括 pass / groundedness / citation / no-answer / retrieval metrics、average latency 与 baseline regression flags。

Webhook 配置、渠道格式、重试和 SSRF 规则见 [Webhook Automation](WEBHOOK_AUTOMATION.md)。

## 13. 推荐评估流程

对生产 KB 进行模型 / Prompt / Retrieval 变更前，建议：

1. 建立包含 fact、comparison、multi-hop、summary、no-answer、conflict、prompt-injection、follow-up、extraction、citation 的 Dataset。
2. 为关键检索 Case 标注 ExpectedSources。
3. 运行当前生产配置，形成 completed baseline。
4. 修改 Deployment / Prompt / RAG Profile。
5. 再次运行同一 Dataset。
6. 同时比较 Pass / Groundedness / Citation / Retrieval / Latency / Cost，而不是只看一个指标。
7. 发现 regression 时先定位是 Retrieval、Answer/Reviewer、Citation Gate 还是 Provider routing 变化。

仓库还包含：

```text
evaluation/rag-benchmark.jsonl
evaluation/run_benchmark.py
```

用于独立 benchmark 流程。持久化 Evaluation API 与 benchmark script 是两种入口，不应把其中一方的结果自动等价为另一方。

## 14. 常见问题

### RAG 请求在调用模型前就被拒绝

检查：

- `DailyRunLimit`
- `MonthlyBudgetUsd`
- 是否存在仍处于 `reserved` 且未满 1 小时的并发 / 异常 Run
- Model Price 是否导致 reservation 预估高于预期

### 成本一直是 0

检查：

- 对应 Deployment 是否配置 active Model Price。
- 如果是 Embedding，没有显式 price 时当前设计会记录 0，而不会套用 Chat 单价。
- usage 是估算，不是 Provider billing export。

### Evaluation Pass Rate 正常但检索质量变差

查看 Retrieval Recall@K / MRR / nDCG。当前 Case retrieval pass 只要求至少命中一个 expected source。

### 新配置没有影响正在运行的请求

这是预期行为。RAG execution snapshot 在请求开始时解析一次；配置只影响后续请求。

## 15. 实现与测试入口

核心实现：

- `src/KnowledgeRag.Api/Endpoints/GovernanceEndpoints.cs`
- `src/KnowledgeRag.Infrastructure/Governance/GovernanceService.cs`
- `src/KnowledgeRag.Infrastructure/Governance/RagRuntimeProfileService.cs`
- `src/KnowledgeRag.Infrastructure/Governance/RagRunAccountingService.cs`
- `src/KnowledgeRag.Infrastructure/Governance/AiUsageAccounting.cs`
- `src/KnowledgeRag.Infrastructure/Governance/EvaluationService.cs`
- `src/KnowledgeRag.Infrastructure/Governance/EvaluationScoring.cs`
- `src/KnowledgeRag.Infrastructure/Governance/EvaluationExpectationCodec.cs`

主要回归测试：

- `tests/KnowledgeRag.Api.IntegrationTests/GovernanceApiTests.cs`
- `tests/KnowledgeRag.Api.IntegrationTests/AiUsageAccountingTests.cs`
- `tests/KnowledgeRag.Api.IntegrationTests/EvaluationScoringTests.cs`
- `tests/KnowledgeRag.Api.IntegrationTests/EvaluationRetrievalScoringTests.cs`
- `tests/KnowledgeRag.Api.IntegrationTests/RagWorkflowPolicyTests.cs`

相关文档：

- [RAG Agent Workflow](RAG_AGENT_WORKFLOW.md)
- [AI Provider Configuration](AI_PROVIDER_CONFIGURATION.md)
- [Webhook Automation](WEBHOOK_AUTOMATION.md)
- [Production Operations](PRODUCTION_OPERATIONS.md)
