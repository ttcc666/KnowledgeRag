# 数据库 Migration 与升级指南

本文覆盖 SQLite / PostgreSQL 的 EF Core migration 工作流、旧 `EnsureCreated` 数据库升级，以及生产发布建议。

## 1. 当前 Migration 状态

Migration 与 Model Snapshot 位于：

```text
src/KnowledgeRag.Infrastructure/Persistence/Migrations/
```

当前主要 migration 按顺序为：

1. `202610270001_InitialProduction`
2. `20261027050438_SyncCurrentModel`
3. `20261028100000_PlatformGovernanceSuite`
4. `20261028120000_WebhookChannelProviders`
5. `20261028164203_TenantAiProviderFoundation`
6. `20261029003620_TenantAiEmbeddingIndexGenerations`
7. `20261029021050_AiDeploymentRoutingGovernance`

这些 migration 覆盖当前的身份 / Tenant / Knowledge Base / Documents / Conversations、治理套件、Webhook、Tenant AI Provider、Embedding generation 与 Deployment routing 等数据模型。新增 migration 后应重新核对本列表并同步更新本文。

## 2. 新安装与正常升级

新数据库不需要手工创建表。当前 `Database:AutoMigrate` 默认是 `true`，应用启动时会运行 EF Core migrations。

手动更新数据库：

```bash
dotnet tool install --global dotnet-ef --version 10.*
dotnet ef database update \
  --project src/KnowledgeRag.Infrastructure \
  --startup-project src/KnowledgeRag.Api
```

SQLite（默认）：

```bash
DATABASE_PROVIDER=Sqlite \
DATABASE_CONNECTION_STRING='Data Source=App_Data/knowledge.db' \
dotnet ef database update \
  --project src/KnowledgeRag.Infrastructure \
  --startup-project src/KnowledgeRag.Api
```

PostgreSQL：

```bash
DATABASE_PROVIDER=PostgreSql \
DATABASE_CONNECTION_STRING='Host=localhost;Port=5432;Database=knowledge_rag;Username=knowledge_rag;Password=...' \
dotnet ef database update \
  --project src/KnowledgeRag.Infrastructure \
  --startup-project src/KnowledgeRag.Api
```

请确保连接串与当前环境实际使用的数据库一致。不要把生产密码写入 shell history、日志或仓库文件。

## 3. 从旧版 `EnsureCreated` 数据库升级

旧版使用 `Database.EnsureCreated()` 的数据库通常没有 `__EFMigrationsHistory`。不能直接把当前 migration 当成增量变更执行，否则可能因既有表已存在而失败；也不能只因为当前 `ModelSnapshot` 与目标代码一致，就假定旧数据库已经具备所有后来新增的表和列。

### 开发 / 可丢弃环境

1. 备份需要保留的数据库、原始文件和向量数据。
2. 停止应用。
3. 删除旧 SQLite 数据库，或在确认可丢弃后重建 PostgreSQL database。
4. 使用当前版本重新启动并运行 migrations。
5. 重新导入文档并重建索引。

### 必须保留数据的环境

1. 在 staging 对数据库、文件存储和向量后端做完整备份，并演练恢复。
2. 比较旧 schema 与 `202610270001_InitialProduction` 的 `Up` 操作所代表的 schema；**比较目标是 InitialProduction，不是当前最新 ModelSnapshot**。
3. 若旧 schema 与 InitialProduction 完全等价，才可以制定受审核的 baseline 操作，将 `202610270001_InitialProduction` 记录为已应用，然后让 EF Core 正常执行后续 migration。
4. 若 schema 不完全等价，先编写并验证显式 schema / 数据迁移脚本；不要伪造 migration history 来绕过差异。
5. 迁移旧业务数据时，核对用户到 Tenant / TenantMember 的映射、每个 Knowledge Base 的 `TenantId`、Refresh Token / API Key 相关数据，以及新版本要求的外键和唯一约束。
6. 应用后执行完整集成测试、检查关键记录数量、验证原始文件可读取、验证搜索与 Chat，并演练回滚 / 恢复。
7. 后续 schema 变化继续通过标准 EF Core migration 管理。

不要在生产数据库直接手工插入 `__EFMigrationsHistory`，除非 schema 比对和 baseline 脚本已经过审核、并在 staging 验证成功。Baseline 只表示 InitialProduction 的 schema 已存在，不代表之后的治理、Provider 或 generation migrations 已经执行。

## 4. 生成与发布 Migration

新增 migration：

```bash
dotnet ef migrations add AddSomething \
  --project src/KnowledgeRag.Infrastructure \
  --startup-project src/KnowledgeRag.Api \
  --output-dir Persistence/Migrations
```

检查生成的：

- migration 的 `Up` / `Down`；
- `AppDbContextModelSnapshot.cs`；
- PostgreSQL UUID / FK / vector / FTS 行为；
- SQLite 对 `DateTimeOffset` 排序、decimal 聚合及 SQL 翻译的限制；
- 现有数据的默认值、nullable 转换、唯一索引和删除行为。

生产推荐使用 migration bundle：

```bash
dotnet ef migrations bundle \
  --project src/KnowledgeRag.Infrastructure \
  --startup-project src/KnowledgeRag.Api \
  --output artifacts/efbundle
```

部署时先由具备 DDL 权限的 deployment identity 执行 bundle，再部署应用。可将生产的 `DATABASE_AUTO_MIGRATE=false`，避免应用运行身份长期持有 schema 修改权限。若使用此模式，必须确保发布流水线明确执行 migration。

## 5. SQLite 与 PostgreSQL 双后端检查

每次改动数据模型或查询时至少检查：

- migration 是否使用当前数据库 Provider 的正确列类型；
- 外键列与主键列类型是否匹配；
- `DateTimeOffset` 是否在 SQLite 路径中触发不支持的排序 / 比较；
- `decimal` 聚合是否需要跨 Provider 兼容处理；
- 原生 pgvector / PostgreSQL FTS 索引是否正确初始化；
- CAP 的 PostgreSQL schema 是否创建；
- migration 重复执行与空数据库初始化是否可用。

完整验证命令与 CI 行为见 [VALIDATION.md](VALIDATION.md)，架构边界见 [ARCHITECTURE.md](ARCHITECTURE.md)。
