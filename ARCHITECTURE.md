# Backend Architecture

后端采用 **Pragmatic Clean Architecture + Feature Modules**。目标是让目录边界清晰、依赖方向稳定，同时避免为了“纯 Clean Architecture”产生大量只做转发的 Repository / Handler。

## 1. 项目依赖

```text
KnowledgeRag.Domain
        ▲
        │
KnowledgeRag.Application
        ▲
        │
KnowledgeRag.Infrastructure
        ▲
        │
KnowledgeRag.Api
```

实际引用关系：

- `Domain`：不引用本解决方案中的任何项目。
- `Application`：只引用 `Domain`。
- `Infrastructure`：引用 `Application` + `Domain`。
- `Api`：引用 `Application` + `Infrastructure` + `Domain`，只作为 composition root / HTTP adapter。

禁止：

- Domain 引用 EF Core、ASP.NET Core、OpenAI、Qdrant、S3。
- Application 引用 Infrastructure。
- Infrastructure 引用 Api。
- 在 `Program.cs` 直接写业务查询或 CRUD。

## 2. Domain

路径：`src/KnowledgeRag.Domain`

只保存业务实体和枚举：

```text
Entities/
├── IdentityEntities.cs
├── TenantEntities.cs
├── KnowledgeBaseEntities.cs
├── DocumentEntities.cs
└── ConversationEntities.cs
```

Domain 不关心：

- 数据存 PostgreSQL 还是 SQLite；
- 向量存 Qdrant 还是 pgvector；
- 文件在本地还是 MinIO；
- API 使用 JWT 还是 API Key；
- UI 是 React 还是其他框架。

## 3. Application

路径：`src/KnowledgeRag.Application`

保存跨实现稳定的输入输出契约、调用上下文和基础抽象：

```text
Contracts/
├── AuthContracts.cs
├── TenantContracts.cs
├── KnowledgeBaseContracts.cs
├── SearchContracts.cs
└── ConversationContracts.cs

Security/
└── ActorContext.cs

Abstractions/
└── SearchAbstractions.cs
```

### 为什么 DTO 不放 Api

因为同一套应用契约未来可能被：

- HTTP Minimal API；
- Worker；
- MCP/插件入口；
- CLI；
- 后台任务

共同使用。Application contracts 不应该依赖 HTTP。

## 4. Infrastructure

路径：`src/KnowledgeRag.Infrastructure`

这里放所有“如何实现”的代码。

```text
Infrastructure/
├── Identity/          # 登录、Refresh Token、JWT、API Key
├── Tenancy/           # Tenant 权限与业务
├── KnowledgeBases/    # 知识库 RBAC 与业务
├── Documents/         # 文档解析、切片、入库
├── Search/            # Hybrid Search / BM25 / Qdrant / pgvector / FTS / rerank
├── Conversations/     # Agent Framework、会话持久化
├── AI/                # OpenAI-compatible embedding/reranker client
├── Storage/           # Local / S3 / MinIO
├── Persistence/       # AppDbContext、Migration、DB health
├── Configuration/     # Options
└── Observability/     # ActivitySource
```

每个主要 Infrastructure 模块都有自己的 `ServiceCollectionExtensions.cs`。根 `Infrastructure/DependencyInjection.cs` 只组合模块，避免 DI 文件再次膨胀成巨型启动类。

### Persistence

EF Core migration 固定放这里：

```text
Persistence/Migrations/
```

新增 migration：

```bash
dotnet ef migrations add MyChange \
  --project src/KnowledgeRag.Infrastructure \
  --startup-project src/KnowledgeRag.Api \
  --output-dir Persistence/Migrations
```

### Search

搜索后端通过 Application abstraction 选择：

```text
IVectorStore
├── QdrantVectorStore
└── PgVectorStore

ILexicalSearchService
├── Bm25SearchService
└── PostgresFtsSearchService
```

因此 `KnowledgeSearchService` 不需要知道实际使用哪个后端。

## 5. Api

路径：`src/KnowledgeRag.Api`

Web 层保持薄：

```text
Api/
├── Endpoints/
│   ├── AuthEndpoints.cs
│   ├── TenantEndpoints.cs
│   ├── KnowledgeBaseEndpoints.cs
│   ├── DocumentEndpoints.cs
│   ├── SearchEndpoints.cs
│   ├── ConversationEndpoints.cs
│   └── HealthEndpoints.cs
├── Extensions/
│   ├── AuthenticationExtensions.cs
│   ├── SwaggerExtensions.cs
│   ├── TelemetryExtensions.cs
│   ├── ServiceCollectionExtensions.cs
│   └── WebApplicationExtensions.cs
├── Middleware/
│   └── ApiExceptionMiddleware.cs
├── Security/
│   └── HttpActorContext.cs
└── Program.cs
```

`Program.cs` 应持续保持简单：

```csharp
builder.Services.AddInfrastructure(...);
builder.Services.AddApiServices(...);

var app = builder.Build();
app.UseApiPipeline();
await app.Services.InitializeInfrastructureAsync(...);
app.MapApiEndpoints(...);
app.Run();
```

不要重新把 endpoint、数据库 SQL、业务规则写回 `Program.cs`。

## 6. 增加新功能时放哪里

例如新增“知识库标签”：

1. 数据字段/实体：`Domain/Entities/KnowledgeBaseEntities.cs`
2. Request/Response DTO：`Application/Contracts/KnowledgeBaseContracts.cs`
3. EF mapping + migration：`Infrastructure/Persistence`
4. 业务实现：`Infrastructure/KnowledgeBases`
5. HTTP 路由：`Api/Endpoints/KnowledgeBaseEndpoints.cs`
6. 前端：`KnowledgeRag.Admin/src/features/...`

例如新增 Elasticsearch lexical search：

1. 在 Application 继续使用 `ILexicalSearchService`，通常无需修改。
2. 在 `Infrastructure/Search/ElasticsearchSearchService.cs` 新增实现。
3. 在 `Infrastructure/Search/ServiceCollectionExtensions.cs` 增加配置选择。
4. Search/Conversation/API 不需要跟着改。

## 7. 为什么没有强制 Repository Pattern

当前业务主要由 EF Core query + 外部搜索/存储组成。为每张表再增加：

```text
IUserRepository
TenantRepository
KnowledgeBaseRepository
DocumentRepository
...
```

会增加大量机械转发代码，但没有形成真正的业务隔离价值。

因此当前约定是：

- EF Core 只允许出现在 Infrastructure；
- API 不直接访问 DbContext；
- 当某个聚合的数据规则复杂到需要独立持久化接口时，再提取 Repository。

这是“实用型 Clean Architecture”，而不是为了形式做抽象。

## 8. 命名规则

- `*Entity`：数据库/领域实体。
- `*Request` / `*Response` / `*Summary`：Application contracts。
- `*Service`：业务或基础设施能力。
- `*Store`：可替换的存储后端。
- `*Endpoints`：HTTP endpoint module。
- `*Options`：配置绑定对象。
- `*AccessService`：RBAC/权限检查。

## 9. 测试目录

当前：

```text
tests/KnowledgeRag.Api.IntegrationTests
```

集成测试通过 `WebApplicationFactory<Program>` 覆盖真实 HTTP Pipeline。

后续如果 Domain/Application 出现足够复杂的纯逻辑，建议新增：

```text
tests/KnowledgeRag.Domain.UnitTests
tests/KnowledgeRag.Application.UnitTests
```

不要把纯算法单测塞进 API 集成测试项目。
