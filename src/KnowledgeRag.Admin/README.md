# KnowledgeRag.Admin

KnowledgeRag 管理后台，采用 `satnaing/shadcn-admin` 的技术栈与后台 UI 模式，并通过本项目的 ASP.NET Core API 提供业务能力。

## 技术栈

- React 19 / Vite 8 / TypeScript
- Tailwind CSS 4 / Shadcn / Radix UI
- TanStack Query / Router
- React Hook Form + Zod、Zustand
- Axios + Refresh Token interceptor
- `@assistant-ui/react` External Store Runtime
- SignalR client（文档处理进度）

## 当前功能区

- **Auth**：登录、注册与 Refresh Token 自动轮换。
- **Dashboard / Operations**：平台概览、文档 ingestion jobs、pipeline 状态。
- **Knowledge Bases**：KB CRUD、成员权限、文档上传 / 下载 / 删除 / retry / reindex / cancel、Embedding Index generation 查看与重建。
- **Tenants**：Tenant / Tenant Members、API Key / scope / KB binding、AI Provider Connection、Model Deployment、health / fallback、Tenant Defaults。
- **Chat**：assistant-ui 对话、SSE Agent Workflow Timeline、可信 Citation / Evidence Panel、feedback、rename、archive / restore、删除与历史分页。
- **Governance**：KB RAG Profile、Run Trace、Evaluation、Model Pricing / Cost、Audit Log、Prompt Profile、Webhook Automation。

会话与业务数据的来源是服务端 API；TanStack Query 管理服务端状态，assistant-ui 负责 Chat 交互层。前端不应自行推断 Citation 的可信范围或跳过后端 RBAC。

## 本地开发

在仓库根目录先配置 `.env`（至少设置强随机 `AUTH_SIGNING_KEY` 与可用的 `AI_API_KEY`），然后启动后端：

```bash
dotnet run --project src/KnowledgeRag.Api
```

在第二个终端：

```bash
cd src/KnowledgeRag.Admin
npm ci
npm run dev
```

Vite 默认将 `/api` 请求代理到 `http://localhost:8080`。如果后端端口或代理地址有变化，请同步核对 `vite.config.ts`。

打开 `http://localhost:5173`。Swagger 位于后端的 `/swagger`。

## Build 与测试

```bash
npm ci
npm run build
npm test
npm run test:e2e
```

- `npm run build`：TypeScript project build + Vite production bundle。
- `npm test`：Vitest 单元 / 组件与 accessibility 测试。
- `npm run test:e2e`：Playwright 浏览器测试；CI 使用 Chromium。

构建结果默认位于 `dist/`。如需手动将构建结果写入 ASP.NET Core 静态目录：

```bash
npm run build:api
```

通常无需手动执行 `build:api`：`src/KnowledgeRag.Api/Dockerfile` 已在 Node 22 构建阶段生成前端资源，并在 .NET 10 publish 阶段将它们复制到 `wwwroot`。

## 相关文档

- [项目根 README](../../README.md)
- [当前 AI 供应商配置](../../docs/AI_PROVIDER_CONFIGURATION.md)
- [RAG Agent Workflow](../../docs/RAG_AGENT_WORKFLOW.md)
- [系统架构](../../ARCHITECTURE.md)
- [验证与 CI](../../VALIDATION.md)
