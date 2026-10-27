# Validation

本轮增加：

- EF Core Migration + design-time DbContext factory
- Refresh Token rotation/replay detection
- Tenant / TenantMember
- Tenant API Key + scopes + optional KnowledgeBase binding
- Qdrant / pgvector vector backend abstraction
- C# BM25 / PostgreSQL FTS lexical backend abstraction
- Local / S3/MinIO storage abstraction
- OpenTelemetry tracing + metrics + OTLP
- ASP.NET Core WebApplicationFactory integration tests
- External backend integration tests（pgvector / PostgreSQL FTS / MinIO）
- GitHub Actions CI（restore/build/test + production-backend smoke）
- 全项目升级到 .NET 10 (`net10.0`)；Docker / CI / EF Core 10 / ASP.NET Core 10 同步升级
- 前端改造为 React 19 + Vite 8 + Tailwind CSS 4 + Shadcn/Radix + TanStack Query/Router 管理后台

## 已执行的静态检查

- JSON 配置解析
- React/TypeScript 源码语法扫描（全量 `.ts/.tsx` 无 TypeScript parser syntax diagnostics）
- 前端 `package.json` / `tsconfig.json` / `components.json` JSON 解析
- C# 文件括号结构检查
- Solution 文件结构检查
- 旧 `EnsureCreated` / 旧服务签名引用搜索
- Azure SDK / Azure Identity 残留搜索
- 新后端配置与 Docker Compose 配置人工核对
- CI YAML / OpenTelemetry Collector YAML 解析
- ExternalBackend 测试源码结构检查

## 当前环境限制

当前执行环境没有 `dotnet` SDK 和 Docker，且容器 DNS 无法下载 NuGet/NPM 依赖，因此本环境仍无法实际执行完整 restore/build：

```bash
dotnet restore
dotnet build
dotnet test
cd src/KnowledgeRag.Admin && npm install && npm run build
docker compose up
```

项目已经加入集成测试；请在具有 .NET 10 SDK 的开发机或 CI 中首先执行：

```bash
dotnet restore KnowledgeRag.NonAzure.sln
dotnet build KnowledgeRag.NonAzure.sln -c Release --no-restore
dotnet test KnowledgeRag.NonAzure.sln -c Release --no-build
```

随后分别验证两套推荐组合：

```bash
# default
docker compose up -d --build

# native PostgreSQL search
docker compose --profile postgres up -d --build

# MinIO
docker compose --profile s3 up -d --build
```

从上一版 `EnsureCreated` 数据库升级时请先阅读 `MIGRATION_GUIDE.md`。

## shadcn-admin 前端改造验证

已新增独立 `src/KnowledgeRag.Admin`，并完成：

- `package.json` / `tsconfig.json` / `components.json` JSON 解析通过
- Docker Compose / GitHub Actions / OTel Collector YAML 解析通过
- `.ts/.tsx` 全量 TypeScript parser syntax diagnostics：0
- Dockerfile 已确认包含 Node 22 frontend stage、`npm run build`、`dist -> wwwroot` 复制
- CI 已确认包含 Node 22 setup 与 frontend build step
- 旧 `wwwroot` 单文件 demo 的主要标记已移除；源码中的 `index.html` 仅保留“未构建前端”的开发提示页

当前容器无法解析外网 NPM registry，因此无法在本环境执行真实 `npm install && npm run build`。GitHub Actions 与 Dockerfile 已配置真实前端构建，请在可联网环境首次运行 CI/Docker build 时完成依赖级类型检查和 Vite bundle 验证。

## 后端目录 / 架构重构验证

本轮将原先集中在 `KnowledgeRag.Api` 中的后端代码拆分为：

```text
KnowledgeRag.Domain
KnowledgeRag.Application
KnowledgeRag.Infrastructure
KnowledgeRag.Api
```

并完成以下检查：

- Solution 已包含 Domain / Application / Infrastructure / Api / IntegrationTests 5 个 C# 项目。
- 所有 C# 项目继续使用 `net10.0`。
- Domain 无本解决方案内部项目依赖。
- Application 只引用 Domain；源码中无 Infrastructure / Api 反向引用。
- Infrastructure 只引用 Application + Domain；源码中无 Api 引用。
- Api 中无 `AppDbContext` / `Microsoft.EntityFrameworkCore` 直接访问。
- 旧 `KnowledgeRag.Api/Data`、`Models`、`Options`、`Services`、`Migrations` 已清理。
- EF Core migrations 已迁移到 `KnowledgeRag.Infrastructure/Persistence/Migrations`。
- `Program.cs` 已收敛为 composition root；业务 Endpoint 全部拆到 `KnowledgeRag.Api/Endpoints`。
- Infrastructure DI 已按 Persistence / Identity / Tenancy / KnowledgeBases / Documents / AI / Storage / Search / Conversations 模块拆分。
- Dockerfile restore 阶段已显式复制 Domain / Application / Infrastructure / Api 四个项目文件。
- IntegrationTests 已更新到新的 Domain / Application / Infrastructure namespaces。
- 86 个 C# 文件完成字符串/注释感知的括号结构扫描：0 个结构错误。
- 5 个 `.csproj` 完成 XML 解析：通过。
- 所有 `using KnowledgeRag.*` 内部 namespace 引用均能对应当前源码 namespace：0 个缺失。
- Layer rule 扫描：0 个反向依赖违规。

开发约定详见根目录 `ARCHITECTURE.md`。
