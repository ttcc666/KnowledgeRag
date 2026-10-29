# KnowledgeRag 文档索引

本文档集中索引当前实现相关的操作指南、架构说明和开发流程。文档应与最新 `main` 的代码、配置、测试和 CI 保持一致。

## 快速入口

| 目标 | 文档 | 说明 |
|---|---|---|
| 了解项目与启动 | [根 README](../README.md) | 当前能力、默认组合、Docker、前端、API 概览 |
| 配置 AI 供应商 | [AI Provider Configuration](AI_PROVIDER_CONFIGURATION.md) | Provider Connection、Deployment、health/fallback、Tenant Defaults、KB 路由、Embedding Reindex、Pricing |
| 理解 RAG 执行链 | [RAG Agent Workflow](RAG_AGENT_WORKFLOW.md) | Planner、Multi-query Retrieval、Answer、Reviewer/Repair、Citation Gate、Evaluation |
| 理解模块边界 | [系统架构](../ARCHITECTURE.md) | Domain / Application / Infrastructure / Api、核心模块与运行时流程 |
| 配置异步文档处理 | [CAP + SignalR ingestion](../CAP_SIGNALR_INGESTION.md) | 单文档摄取、CAP、SignalR、KB-wide Embedding generation |
| 升级数据库 | [数据库迁移指南](../MIGRATION_GUIDE.md) | 当前 migrations、旧 EnsureCreated baseline、生产发布 |
| 运行测试 | [验证指南](../VALIDATION.md) | 本地命令、Vitest / Playwright / xUnit、native CI 实际覆盖 |
| 开发与合并 | [开发流程与 CI 策略](../DEVELOPMENT_WORKFLOW.md) | 分支、Draft / Ready PR、CI 门禁、Changelog 规则 |
| 理解非 Azure 方案 | [Azure 架构替换说明](../CHANGES_FROM_AZURE.md) | 当前后端与 Azure 专有服务的映射 |
| 查看变更历史 | [CHANGELOG](../CHANGELOG.md) | 已完成功能、修复、安全与验证记录 |

## 当前代码入口

- API Endpoint 注册：`src/KnowledgeRag.Api/Endpoints/EndpointRegistration.cs`
- AI Provider / Deployment / Tenant Defaults：`src/KnowledgeRag.Infrastructure/Tenancy/TenantAiProviderService.cs`
- RAG Workflow：`src/KnowledgeRag.Infrastructure/Conversations/RagAgentService.cs`
- Runtime Profile / route snapshot / budget：`src/KnowledgeRag.Infrastructure/Governance/RagRuntimeProfileService.cs`
- 单文档 ingestion：`src/KnowledgeRag.Infrastructure/Documents/KnowledgeIngestionService.cs`
- KB-wide Embedding generation：`src/KnowledgeRag.Infrastructure/Documents/KnowledgeBaseIndexGenerationService.cs`
- Hybrid Search：`src/KnowledgeRag.Infrastructure/Search/KnowledgeSearchService.cs`
- 数据模型与 migrations：`src/KnowledgeRag.Infrastructure/Persistence`
- 前端 Chat：`src/KnowledgeRag.Admin/src/features/chat`
- 前端 Governance：`src/KnowledgeRag.Admin/src/features/governance`
- CI：`.github/workflows/ci.yml`

## 文档更新规则

- 源码、配置、测试与 CI 是实现行为的最终依据；文档不能把规划中的功能描述成已上线能力。
- API 路由、配置键、migration 名称、CLI 命令或测试范围变更时，同步修改相关文档。
- 一个事实应尽量有一个主要说明页面；其他文档通过链接引用，避免复制多份而逐渐不一致。
- 运行时行为变化应更新 `CHANGELOG.md`；纯文档修正不需要伪造运行时变更条目。
- PR 中涉及文档的变更应检查链接目标、代码路径、环境变量、API route 和命令是否存在。
