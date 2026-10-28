# Changelog

本文件用于记录 KnowledgeRag 的重要功能、架构、体验、安全与运维变更。

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)；当前项目尚未建立正式 Release/Tag 流程，因此历史记录按合并 PR / 里程碑倒序整理。

## 维护规则

- 每个会影响用户、API、数据库、Agent 行为、检索质量、权限、安全、成本或运维方式的 PR，都必须更新本文件。
- 开发中的变更先写入 `Unreleased`。
- 发布版本或建立 Release/Tag 时，将 `Unreleased` 内容移动到对应版本与日期下。
- 每条记录优先说明“发生了什么”和“为什么重要”，避免只罗列文件名。
- 数据库迁移、兼容性变化、默认行为变化、Breaking Change 必须单独注明。

---

## Unreleased

### Added

- 新增 Webhook **CAP 异步投递管线**：每个订阅独立发布 delivery message，PostgreSQL 环境复用 CAP 持久化 storage / RabbitMQ 重试，避免外部 HTTP 阻塞 Chat / Evaluation 请求关键路径。
- 新增 Webhook SSRF 地址策略与回归测试，覆盖 localhost、loopback、RFC1918 私网、link-local、IPv6 unique-local 等受限目标。
- 新增 RAG **Structured Citation Contract**：Answer Agent 使用 `[E#]` 绑定当前 Evidence，服务端确定性校验并转换为稳定的用户引用编号，只持久化答案实际引用的来源。
- 新增 citation normalization 回归测试，覆盖引用重排、重复引用、非法 Evidence ID 和无引用回答。
- 新增 `DEVELOPMENT_WORKFLOW.md`，正式记录分支、Draft PR、增量 CI、Ready 全量验证、Changelog、Hotfix、数据库与 Agent/RAG 变更的开发流程。

### Changed

- Webhook delivery 使用稳定 Event ID / `X-KnowledgeRag-Delivery`，仅对 timeout、429 与 5xx 等可恢复失败交给 CAP 重试；自动重定向关闭，避免 redirect 绕过目标地址策略。
- Reviewer 增加强制引用审核规则：重要知识库事实必须具备有效 `[E#]`，缺失、越界或与 claim 不匹配的引用进入 `CitationErrors` 并使审核失败。
- Chat Source 不再等同于“本轮所有检索结果”，只暴露最终答案真正引用到的 Evidence；Run Trace 记录 citation validation 统计。
- CI 改为分层策略：Draft PR 根据改动路径选择 frontend / backend / native 测试；PR 转为 Ready、`main` push、手动触发和 nightly schedule 时强制完整 CI。
- PR 分支不再同时触发 `push` 与 `pull_request` 两套重复 CI；`push` 仅监听 `main`。
- 同一 PR 的新 commit 会通过 `concurrency.cancel-in-progress` 自动取消旧 CI，减少无效 runner 消耗。
- `build-test` 与 `native-backends-smoke` 保留稳定的检查名称；无相关改动时以轻量 no-op 成功结束，避免 required check 因工作流不触发而悬挂。
- 成本估算中的模型价格查询改为跨数据库兼容实现：Tenant / Model / Active 条件仍由数据库过滤，`UpdatedAt` 的最新记录选择改为 materialize 后在内存排序，确保 SQLite 与 PostgreSQL 行为一致。

### Fixed

- 修复 SQLite 下 `RagRuntimeProfileService.EstimateCostAsync` 对 `DateTimeOffset UpdatedAt` 执行数据库端 `ORDER BY` 时抛出 `NotSupportedException`，导致 RAG 回答完成后在持久化 Run Trace / Cost 阶段返回 HTTP 500 的问题。
- 增加 SQLite 集成回归，直接验证配置模型价格后的成本估算路径可正常执行并使用正确价格。

### Security

- Webhook 实际连接时重新解析 DNS，只允许公网地址，并阻止本机、私网、link-local、multicast、IPv6 unique-local 与 `.local` 目标；连接层检查降低 DNS rebinding / metadata endpoint SSRF 风险。
- 非法或不存在的 Evidence 引用不会作为可信 Citation 返回前端；Knowledge Base 内容仍按 untrusted data 处理，管理员 Prompt 不能覆盖强制 grounding / citation 规则。

---

## 2026-10-28 — AI Governance & Platform Suite

**PR:** [#7 feat(governance): evaluation, RAG profiles, trace, cost, audit and agents](https://github.com/ttcc666/KnowledgeRag/pull/7)  
**Main commit:** `0149dbbdd7fa854ce907e3afb914a14609ded6a3`

### Added

- 新增 **RAG Evaluation 评测中心**：支持持久化 Evaluation Dataset、Case、Run，为后续基准评测与回归比较提供正式数据模型。
- 新增 **知识库级 RAG Profile**：每个知识库可以独立配置 Planner、Reviewer、TopK、Multi-query、模型、Prompt、Agent 模式、预算和每日运行配额。
- 新增 **RAG Run 持久化 Trace**：保存每轮 Agent Workflow 的进度事件、阶段、耗时、模型、Token 估算、成本与结果信息。
- 新增 **成本与模型治理**：支持模型价格配置、成本统计、月预算和每日配额控制。
- 新增 **Audit Log**：记录 API 写操作、资源类型、Tenant、Knowledge Base、Actor、IP、User-Agent、状态码与上下文。
- 新增 **Prompt / Agent 管理中心**：支持版本化 Prompt Profile，并与实际 Agent 运行时联动。
- 新增 **Webhook / API Automation**：支持 Webhook 订阅、事件类型、HMAC 签名、Secret 保护以及 RAG Run / Evaluation 等事件通知。
- 新增 **多 Agent Specialist 路由**：通过 Agent-as-Tool 提供 Compare、Summarize、Extraction、Research 等专职 Agent。
- 新增统一 **AI 治理中心** 前端，覆盖 Evaluation、RAG Profile、Run Trace、Cost / Model、Audit、Prompt / Agent、Webhook 与 Multi-Agent 拓扑。

### Changed

- RAG 运行时现在按 Knowledge Base 动态读取 Profile，而不是仅使用全局配置。
- Planner、Reviewer、Retrieval、模型选择、Prompt 与 Specialist Agent 行为均可由 KB Profile 覆盖。
- 治理数据正式纳入 EF Core Model 与数据库 Migration，支持 SQLite 与 PostgreSQL。
- 治理查询针对 SQLite / PostgreSQL 做兼容处理，避免 `DateTimeOffset ORDER BY`、`decimal SUM` 等 provider 差异。

### Security

- Webhook Secret 不再以明文形式返回给前端，并增加持久化保护。
- Budget / Quota 在 RAG Run 执行前校验，避免超预算继续消耗模型资源。
- Audit Middleware 增强 Tenant 归属识别，支持从 route、Knowledge Base 与 query-string `tenantId` 推导租户。

### Validation

- Frontend production build、Vitest / accessibility 全通过。
- Playwright 桌面与移动端治理中心及原有产品回归全部通过。
- .NET build / integration tests 全通过。
- SQLite 治理持久化、Audit、Webhook Secret 行为测试通过。
- PostgreSQL、RabbitMQ、OpenTelemetry、CAP、pgvector、PostgreSQL FTS native smoke 全通过。

---

## 2026-10-28 — assistant-ui & Realtime Agent Workflow

**PR:** [#6 feat(chat): assistant-ui and realtime agent workflow progress](https://github.com/ttcc666/KnowledgeRag/pull/6)  
**Main commit:** `29037a4d00b479506c727b694ecc2224e40c27a5`

### Added

- Chat 主界面迁移到 **`@assistant-ui/react`**。
- 使用 assistant-ui `ExternalStoreRuntime` 对接现有 TanStack Query / 服务端消息存储。
- 新增实时 **Agent Workflow Execution Timeline**，可以展示 Planner、Retrieval、Answer、Specialist、Reviewer 与 Repair 状态。
- 新增安全的 `RagProgressEvent` 协议，统一表示 run、step、stage、actor、status、elapsed time 与结构化数据。
- SSE 增加 `progress` 事件，同时保留 `delta` 与 `complete`。
- Multi-query Retrieval 可以实时展示每条 query 的开始、完成、命中数量和耗时。
- Agent-as-Tool 调用可以在 UI 中展示 Specialist 开始 / 完成事件。

### Changed

- Agent Framework Workflow 从普通执行切换到 `RunStreamingAsync + WatchStreamAsync`。
- Reviewer 开启时仍然不会向用户流出未经审核的草稿；前端通过 progress timeline 展示当前阶段，降低等待焦虑。
- SSE Response 写入增加串行保护，避免并行检索事件同时写入导致 frame 交错。
- assistant-ui 接管 Thread、Composer、Message、自动滚动、发送与取消运行等聊天基础能力。

### Security

- Progress Event 只暴露安全的结构化执行信息，不暴露 system prompt、chain-of-thought、内部 Reviewer 推理、密钥或未过滤 Tool Result。

---

## 2026-10-28 — Agentic RAG Workflow

**PR:** [#5 feat(rag): planner, multi-query retrieval, reviewer and workflow](https://github.com/ttcc666/KnowledgeRag/pull/5)  
**Main commit:** `2bd72618f5d1cffe1ac9774fcdc494a5618dd122`

### Added

- 新增 **Query Planner Agent**，通过 Structured Output 生成 QueryPlan。
- 新增真正的 **Multi-query Retrieval**：Planner 生成查询变体并行执行 Hybrid Search。
- 新增 query-level merge / dedup / RRF，并在最终结果上再次执行 rerank。
- 新增 **Evidence Reviewer Agent**，检查 unsupported claims、missing evidence、citation error 和 confidence。
- Reviewer 不通过时支持有限次数的补充检索与回答修复。
- 新增 **EvidenceAnalysisSpecialist** Agent-as-Tool，处理多文档比较、冲突和复杂证据分析。
- 新增 Planner / Answer / Reviewer 独立模型路由配置。
- 新增 Workflow / Planner / Reviewer 开关与重试、查询数、证据数量等 rollout 配置。
- 新增 Agent / Workflow OpenTelemetry span。
- 新增 `evaluation/rag-benchmark.jsonl` 与 benchmark runner。
- 新增 Workflow Policy 与 Agent Framework Evaluation 相关测试。

### Changed

- 原单一 KnowledgeRagAgent 拆分为明确职责的 Planner → Retrieval → Answer → Reviewer Workflow。
- Retrieval、RRF、Rerank、Retry Policy 等确定性逻辑继续由代码执行，而不是交给 Agent 自由规划。
- 原始用户问题始终参与 Retrieval，不会被 Planner Query 变体挤掉。
- Reviewer 最终仍不通过时采取 fail-closed，返回证据不足而不是未经验证的草稿。

---

## 2026-10-28 — Chat-first Workspace

**PR:** [#4 feat(chat): chat-first focus layout](https://github.com/ttcc666/KnowledgeRag/pull/4)  
**Main commit:** `b92f0c1ab5ac3b0ba350fca76b004a9c5d3bd0ca`

### Added

- 新增 **Focus Mode**，一键隐藏左右面板，只保留聊天与输入框。
- Conversations 面板支持桌面端折叠并记忆用户状态。
- Sources 面板默认关闭，点击 Citation 时按需打开。
- Sources 面板支持约 `320–520px` 拖拽调整宽度并持久化。

### Changed

- 将大面积页面标题改为紧凑顶部工具栏，显著增加聊天可视区域。
- Composer 默认高度缩小，并保持多行输入时可扩展。
- AI Answer 最大阅读宽度提高到约 `960px`。
- Source Evidence 不再使用内部嵌套的小滚动框，改为整个面板统一滚动。
- 保持 Chat 页面无浏览器级全局滚动，继续使用内部独立滚动模型。

---

## 2026-10-28 — Independent Chat Pane Scrolling

**PR:** [#3 fix(chat): restore independent scrolling without clipping panes](https://github.com/ttcc666/KnowledgeRag/pull/3)  
**Main commit:** `7c97d65b37c849bfd7b7022b0685f1e1e2d7b0a7`

### Fixed

- 修复移除全局滚动后 Chat 中心内容可能被 Composer 截断、无法继续滚动的问题。
- 修复 Sources 面板底部 Final Score / Open Original Document 可能不可达的问题。
- 修复移动端隐藏的 Desktop Source DOM 导致 Playwright strict-mode 冲突的问题。

### Changed

- 左侧 Conversations、中心 Messages、右侧 Sources 改为三个独立滚动区域。
- Composer 保持固定可达。
- Chat 页面仍限制在 viewport 内，不恢复浏览器全局滚动。
- Playwright 增加长 Evidence、内部滚动、移动端 Drawer、原始文档打开等回归覆盖。

---

## 2026-10-28 — Chat Layout & Citation Compatibility

**PR:** [#2 fix(chat): layout and citation compatibility](https://github.com/ttcc666/KnowledgeRag/pull/2)  
**Main commit:** `0a14ca85aceee9c626eee20f6e7a8f850d7af97e`

### Fixed

- 修复 Chat 页面浏览器级全局滚动与多余空白。
- 修复历史 Citation Source JSON 使用 PascalCase，而前端按 camelCase 读取导致 `documentId`、`fileName` 等字段丢失的问题。
- 修复历史消息来源显示成“来源 1”且无法可靠打开原始文档的问题。

### Changed

- `ConversationService` Source JSON 改为 `JsonSerializerDefaults.Web`，统一输出 camelCase。
- `ConversationEndpoints` 对历史 PascalCase `SourcesJson` 增加兼容反序列化与重新序列化。
- Chat route 使用固定 viewport workspace，消息区承担主要滚动。
- E2E 增加 Citation Panel、Score、Original Document API 与无 document scroll 回归测试。

---

## 2026-10-28 — Chat Evidence UI & Citation Stability

**PR:** [#1 fix(chat): evidence UI and citation stability](https://github.com/ttcc666/KnowledgeRag/pull/1)  
**Main commit:** `16855abc51fbb36a6f5cf3a69b38b89e5165d1f3`

### Fixed

- 修复 Citation 点击后因缺失 score 调用 `.toFixed()` 导致页面崩溃：`Cannot read properties of undefined (reading 'toFixed')`。
- 对缺失、异常的 Citation Score 增加安全格式化。
- 强化 Source JSON 解析容错。

### Changed

- 优化 Chat 消息间距、可读性、来源 chips 和 Source Evidence Panel。
- Citation 展示更加紧凑，并为后续 Evidence / Original Document 交互奠定基础。

---

## 说明

以上为当前仓库可确认的主要迭代历史。更早的初始平台建设未建立独立 Changelog，因此不在本文件中反向推测；后续从本文件开始持续维护。
