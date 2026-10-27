# Migration Guide

## 新安装

无需手工建表。默认 `Database:AutoMigrate=true`，启动时会运行 EF Core migrations。

也可以手工执行：

```bash
dotnet ef database update --project src/KnowledgeRag.Infrastructure --startup-project src/KnowledgeRag.Api
```

## 从此前 EnsureCreated 版本升级

此前版本使用 `Database.EnsureCreated()`，数据库里没有 `__EFMigrationsHistory`。当前初始 migration 描述的是完整新 schema，因此不能对已有旧表直接执行，否则会遇到 `table already exists`。

开发/测试环境建议：

1. 备份需要的文件和数据。
2. 删除旧 SQLite DB / 重建 PostgreSQL database。
3. 重新启动，让 migrations 创建新 schema。
4. 重新导入文档。

生产已有数据建议：

1. 先备份数据库和对象存储。
2. 在 staging 对旧 schema 做一次显式数据迁移。
3. 为每个旧用户创建 Tenant / TenantMember。
4. 给每个旧 KnowledgeBase 写入对应 `TenantId`。
5. 创建 RefreshTokens / ApiKeys 等新表。
6. 确认 schema 与当前 ModelSnapshot 一致后，再 baseline `202610270001_InitialProduction` 到 `__EFMigrationsHistory`。
7. 之后所有 schema 变化都使用正常 EF migrations。

不要在生产数据库直接手工插入 migration history，除非已经验证 schema 与 migration 完全等价。

## 生产发布

推荐 migration bundle：

```bash
dotnet ef migrations bundle \
  --project src/KnowledgeRag.Infrastructure \
  --startup-project src/KnowledgeRag.Api \
  --output artifacts/efbundle
```

部署时给 bundle 提供数据库连接字符串，再由具有 DDL 权限的 deployment identity 执行。应用运行账号可以只保留正常读写权限。
