# KnowledgeRag.Admin

RAG 管理后台，基于 `satnaing/shadcn-admin` 的技术栈和后台 UI 模式改造。

## Stack

- React 19
- Vite 8
- TypeScript
- Tailwind CSS 4
- Shadcn/Radix UI
- TanStack Query / Router
- React Hook Form + Zod
- Zustand
- Axios + Refresh Token interceptor

## Development

先启动后端：

```bash
dotnet run --project ../KnowledgeRag.Api
```

再启动前端：

```bash
npm install
npm run dev
```

Vite 默认将 `/api` 代理到 `http://localhost:8080`。

## Build

```bash
npm run build
```

构建结果位于 `dist/`。

如果希望直接写入 ASP.NET Core 静态目录：

```bash
npm run build:api
```

Dockerfile 已自动完成前端构建和静态资源复制，不需要手动执行 `build:api`。
