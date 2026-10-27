FROM node:22-alpine AS frontend
WORKDIR /frontend
COPY src/KnowledgeRag.Admin/package.json src/KnowledgeRag.Admin/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/KnowledgeRag.Admin/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/KnowledgeRag.Domain/KnowledgeRag.Domain.csproj src/KnowledgeRag.Domain/
COPY src/KnowledgeRag.Application/KnowledgeRag.Application.csproj src/KnowledgeRag.Application/
COPY src/KnowledgeRag.Infrastructure/KnowledgeRag.Infrastructure.csproj src/KnowledgeRag.Infrastructure/
COPY src/KnowledgeRag.Api/KnowledgeRag.Api.csproj src/KnowledgeRag.Api/
RUN dotnet restore src/KnowledgeRag.Api/KnowledgeRag.Api.csproj
COPY . .
RUN rm -rf src/KnowledgeRag.Api/wwwroot/* && mkdir -p src/KnowledgeRag.Api/wwwroot
COPY --from=frontend /frontend/dist/ src/KnowledgeRag.Api/wwwroot/
RUN dotnet publish src/KnowledgeRag.Api/KnowledgeRag.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
# Npgsql can load GSSAPI/Kerberos support at runtime even when password auth is used.
# Install the small runtime library so container startup does not emit loader errors.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "KnowledgeRag.Api.dll"]
