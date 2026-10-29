# KnowledgeRag 开发流程与 CI 策略

本文档描述 KnowledgeRag 当前推荐的开发流程、分支与 PR 约定、测试分层、Changelog 维护规则，以及 CI 性能优化策略。

## 1. 核心原则

1. `main` 始终保持可构建、可测试、可运行。
2. 所有功能、修复和文档改动均从最新 `main` 创建独立分支。
3. 开发阶段优先运行与改动相关的快速测试，减少无意义的全量 CI。
4. **合并前仍必须执行完整 CI，并且所有必需检查为绿色。**
5. 数据库、Agent、检索、权限、成本、API 行为、UI 体验或运维方式发生变化时，需要同步更新 `CHANGELOG.md`。
6. 不通过忽略异常、关闭警告或跳过关键测试的方式换取 CI 通过。

---

## 2. 标准开发流程

### 2.1 同步主分支

```bash
git checkout main
git pull
```

开始开发前确认本地基于最新 `main`。

### 2.2 创建功能分支

推荐命名：

```text
feat/<feature-name>
fix/<bug-name>
docs/<topic>
refactor/<topic>
test/<topic>
chore/<topic>
```

例如：

```text
feat/rag-evaluation
fix/sqlite-model-price-ordering
docs/development-workflow-ci-strategy
```

### 2.3 开发与本地验证

开发阶段先执行与改动范围相匹配的测试。

#### 纯前端

```bash
cd src/KnowledgeRag.Admin
npm ci
npm run build
npm test
npm run test:e2e
```

#### 纯 .NET / SQLite 业务逻辑

```bash
dotnet restore KnowledgeRag.NonAzure.sln
dotnet build KnowledgeRag.NonAzure.sln -c Release --no-restore
dotnet test KnowledgeRag.NonAzure.sln -c Release --no-build
```

#### PostgreSQL / CAP / pgvector / PostgreSQL FTS / Docker 相关

除普通 .NET 测试外，还应执行 native backend smoke，或依赖 CI 中的 `native-backends-smoke`。

### 2.4 同步文档与 Changelog

有用户可见、API、数据库、Agent、检索、安全、成本或运维变化时，在 `CHANGELOG.md` 的 `Unreleased` 中记录：

```text
Added
Changed
Fixed
Security
```

文档也必须随实现同步维护。开始修改文档前先核对最新 `main` 的代码、配置、测试与 CI；修改时至少检查：

- API 路由和请求 / 响应契约是否仍存在；
- 环境变量、Options 名称、Docker Compose profile 和默认值是否与实际绑定一致；
- 目录 / 类 / migration 名称是否真实存在；
- 测试命令和 CI 覆盖范围是否准确区分“实际运行”与“条件跳过”；
- 跨文档的同一事实是否有唯一主说明，并通过链接引用；
- 迁移 / fallback / reindex / security 等操作步骤是否描述了当前限制与失败语义。

源码、配置、测试与 CI 是实现行为的最终依据。纯文档修正不需要在 `CHANGELOG.md` 中伪造运行时变更；如果本次实现行为发生变化，则文档和 Changelog 都要更新。

### 2.5 创建 Draft PR

开发中的 PR 默认使用 Draft。

Draft 阶段 CI 使用“按改动范围选择测试”的策略，目的是快速反馈。

典型循环：

```text
修改代码
  ↓
push
  ↓
Draft PR 增量 CI
  ↓
修复失败
  ↓
push 新 commit
  ↓
旧 CI 自动取消，新 CI 运行
```

### 2.6 转为 Ready for Review

当功能完整、局部 CI 已稳定后，将 PR 从 Draft 转为 Ready。

**Ready 事件会强制运行完整 CI，不再根据文件路径跳过测试。**

完整 CI 包括：

- Frontend production build
- Vitest / accessibility
- Playwright Chromium E2E
- .NET restore / build / test
- SQLite integration tests
- PostgreSQL native application startup
- RabbitMQ / CAP
- OpenTelemetry
- pgvector
- PostgreSQL FTS

### 2.7 合并

只有满足以下条件才合并：

- PR 不是 Draft；
- PR `mergeable = true`；
- 合并前最新 HEAD 的完整 CI 全绿；
- 没有未处理的 migration / snapshot / secret / security 问题；
- `CHANGELOG.md` 已更新（如适用）。

默认使用 Squash Merge。

### 2.8 main 合并后验证

`main` 的 push 仍会执行一次完整 CI，作为合并后的最终保护层。

---

## 3. 当前 CI 为什么慢

旧版 `.github/workflows/ci.yml` 同时监听：

```yaml
push:
pull_request:
```

这意味着一个已经打开 PR 的分支每次 push 时，通常会产生：

```text
push workflow
+
pull_request workflow
```

同一个 commit 因而可能重复执行两套完整 CI。

最近一次完整运行的实际数据大致为：

```text
build-test               ~2m24s
native-backends-smoke    ~3m02s
```

其中较大的固定成本包括：

```text
Playwright Chromium install       ~25s
Playwright E2E                    ~18s
.NET build                        ~16s
.NET tests                        ~12s
Docker app build/start            ~1m52s
External pgvector / FTS tests     ~30s
```

因此真正的问题不是单个测试特别慢，而是：

1. PR commit 会重复触发 CI；
2. 文档改动也执行浏览器、.NET 和 Docker 测试；
3. 纯前端改动仍启动 .NET/native 后端测试；
4. 普通后端改动也会重复安装 Node/Chromium；
5. 新 commit 到来时旧 CI 不一定及时停止。

---

## 4. 优化后的 CI 分层

### 4.1 触发策略

```text
Draft PR push
    ↓
按改动范围运行快速 CI

Ready for Review
    ↓
强制完整 CI

push main
    ↓
强制完整 CI

workflow_dispatch
    ↓
强制完整 CI

nightly schedule
    ↓
强制完整 CI
```

PR 分支本身不再额外触发 `push` workflow，从根源消除重复执行。

### 4.2 Draft PR 改动分类

#### 文档-only

例如：

```text
README.md
CHANGELOG.md
DEVELOPMENT_WORKFLOW.md
docs/**
```

执行：

```text
change detection
build-test no-op success
native-backends-smoke no-op success
```

不会安装 Node、Chromium、.NET SDK，也不会启动 Docker。

#### 前端改动

路径：

```text
src/KnowledgeRag.Admin/**
```

执行：

```text
npm ci
frontend build
Vitest/accessibility
Playwright E2E
```

跳过：

```text
.NET build/test
native Docker smoke
```

#### 普通后端改动

路径：

```text
src/KnowledgeRag.Api/**
src/KnowledgeRag.Application/**
src/KnowledgeRag.Infrastructure/**
src/KnowledgeRag.Domain/**
tests/**
*.sln
*.csproj
```

执行 .NET build/test。

是否执行 native smoke 由下面的 native-sensitive 分类决定。

#### Native-sensitive 改动

以下改动继续执行完整 PostgreSQL/RabbitMQ/native smoke：

```text
src/KnowledgeRag.Infrastructure/**
src/KnowledgeRag.Domain/**
src/KnowledgeRag.Api/Program.cs
src/KnowledgeRag.Api/appsettings*
tests/KnowledgeRag.Api.IntegrationTests/ExternalBackendTests.cs
docker-compose*.yml
Dockerfile
*.csproj
.github/workflows/**
```

这些路径可能影响：

- EF Core / migration
- PostgreSQL
- CAP / RabbitMQ
- pgvector
- PostgreSQL FTS
- DI / startup
- Docker/runtime configuration

因此不能在 Draft 阶段轻易跳过 native smoke。

### 4.3 CI 配置本身的改动

`.github/workflows/**` 一旦变化，强制将 frontend/backend/native 全部标记为需要测试。

这样修改 CI 的 PR 本身必须经过完整 CI，避免“优化 CI 时把 CI 配坏了却因为路径过滤而没执行”的问题。

---

## 5. 并发控制

同一个 PR 连续 push 时，新 commit 应取消旧 commit 的 CI：

```yaml
concurrency:
  group: ci-${{ github.workflow }}-${{ github.event_name }}-${{ github.event.pull_request.number || github.ref }}
  cancel-in-progress: true
```

收益：

```text
commit A CI running
       ↓
push commit B
       ↓
A 自动取消
B 开始执行
```

避免开发阶段连续修复时浪费 runner 时间。

---

## 6. 为什么仍然保留“合并前全量测试”

路径过滤只能回答“理论上哪些系统受到影响”，不能证明依赖关系永远正确。

例如一个看似普通的 Domain Entity 改动可能最终影响：

```text
EF Model
→ migration
→ PostgreSQL
→ native app startup
```

因此本项目采用两层策略：

```text
开发阶段：增量 CI，追求反馈速度
合并阶段：完整 CI，追求安全性
```

不是用选择性测试替代完整测试，而是避免在每一次中间 commit 上重复完整测试。

---

## 7. CI 目标状态

理想体验：

| 改动类型 | Draft PR 目标反馈 | Ready / main |
| --- | ---: | --- |
| 文档 | < 20 秒 | 全量 |
| 纯前端 | 约 1–2 分钟 | 全量 |
| 普通 .NET | 约 1 分钟 | 全量 |
| Native-sensitive | 约 3 分钟 | 全量 |
| CI workflow | 全量 | 全量 |

完整 CI 当前仍主要受 native Docker build/start 约束，约 3 分钟。

---

## 8. 后续还能继续做的性能优化

当前这轮优先解决“重复执行”和“无关测试”。后续可继续优化真正的固定成本。

### P1：Docker BuildKit / GitHub Actions cache

当前 native smoke 中最慢的是：

```text
Build and start application with native backends
```

约 1–2 分钟。

下一步可以把 app 镜像构建从普通 `docker compose --build` 演进到 BuildKit/buildx，并启用：

```text
cache-from: type=gha
cache-to: type=gha,mode=max
```

这样未变化的 NuGet restore、frontend build、publish 层可以复用。

### P1：Playwright browser cache

Chromium 安装目前约 20–30 秒。可以缓存 Playwright browser binary，并把系统依赖安装与 browser download 分开处理。

需要注意 GitHub-hosted runner 的 OS package 安装仍可能存在固定成本，因此需要先测量再决定是否值得维护缓存逻辑。

### P1：测试分类进一步细化

.NET tests 可以逐步增加类别：

```text
Unit
Integration
Governance
RagWorkflow
ExternalBackend
```

Draft PR 根据路径运行受影响类别，Ready/main 再运行全部。

### P2：Playwright smoke / full E2E 分层

目前 Playwright 产品流全部一起执行。

未来可以拆成：

```text
smoke
  login
  dashboard
  chat send

product-regression
  citations
  focus mode
  governance
  ingestion
  agent timeline
```

Draft 先跑 smoke；Ready/main 跑 full regression。

### P2：可复用 workflow

将以下内容拆成 reusable workflows：

```text
frontend.yml
backend.yml
native-smoke.yml
```

主 `ci.yml` 只负责分类和编排，便于本地理解和后续维护。

### P2：CI metrics

持续记录：

```text
P50 CI duration
P95 CI duration
queue time
cache hit rate
failed step distribution
```

优化应以实际数据为依据，而不是无限增加缓存复杂度。

---

## 9. Hotfix 流程

生产故障仍遵循相同质量门槛：

```text
main
 ↓
fix/<issue>
 ↓
最小修复
 ↓
增加直接复现故障的 regression test
 ↓
Draft PR 增量验证
 ↓
Ready
 ↓
完整 CI
 ↓
Squash Merge
```

Hotfix 的目标是缩小改动范围，而不是跳过完整合并前验证。

---

## 10. 数据库变更额外规则

涉及 EF Core Model / Migration 时：

1. 不手工伪造 model snapshot。
2. 使用项目对应版本的 `dotnet ef` 生成 migration/snapshot。
3. SQLite integration tests 必须通过。
4. PostgreSQL native smoke 必须通过。
5. 检查 provider-specific 限制，例如 SQLite 对部分 `DateTimeOffset` 排序、`decimal` 聚合翻译能力不同。
6. 不通过忽略 `PendingModelChangesWarning` 绕过模型不一致。

---

## 11. Agent / RAG 变更额外规则

涉及 Planner、Retrieval、Reranker、Answer、Reviewer、Specialist Agent 时至少验证：

- structured output fallback；
- retrieval mandatory / no-answer 行为；
- evidence 与最终 citation 一致；
- Reviewer fail-closed；
- streaming / progress SSE；
- session persistence；
- cost / run trace persistence；
- benchmark / evaluation regression（如适用）。

不要为了增加 Agent 数量而破坏确定性的 Retrieval / RRF / rerank Workflow。

---

## 12. 最终检查清单

合并前：

- [ ] 基于最新 `main`
- [ ] 功能或修复范围明确
- [ ] 有对应 regression test
- [ ] 本地或 Draft CI 已通过
- [ ] `CHANGELOG.md` 已更新（如适用）
- [ ] README / 专项文档已与当前实现校准（如适用）
- [ ] API、配置、migration、测试与 CI 相关描述已核对
- [ ] PR 已转 Ready
- [ ] Ready 后完整 CI 全绿
- [ ] SQLite / PostgreSQL 兼容性已考虑
- [ ] 无 secret / prompt / chain-of-thought 泄漏
- [ ] PR `mergeable = true`
- [ ] Squash Merge

该流程的目标是同时保证两件事：**开发阶段反馈足够快，合并到 main 前质量门槛不降低。**
