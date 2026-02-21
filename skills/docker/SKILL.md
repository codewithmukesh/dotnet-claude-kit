---
name: docker
description: >
  Docker containerization for .NET 10 applications. Covers multi-stage builds,
  .NET container images, non-root user configuration, health checks, and
  .dockerignore.
  Load this skill when containerizing an application, optimizing image size,
  setting up Docker Compose for local development, or when the user mentions
  "Docker", "Dockerfile", "container", "docker-compose", "image", "multi-stage",
  "non-root", ".dockerignore", "container health check", or "dotnet publish container".
---

# Docker

## Core Principles

1. **Multi-stage builds always** — Separate build and runtime stages. Build in the SDK image, run in the ASP.NET runtime image.
2. **Non-root by default** — .NET container images support `USER app` by default since .NET 8. Never run as root in production.
3. **Layer caching matters** — Copy `.csproj` files and restore before copying source code. This caches NuGet dependencies across builds.
4. **Health checks in the container** — Use `HEALTHCHECK` in Dockerfile or configure in Docker Compose / orchestrator.

## Patterns

### Multi-Stage Dockerfile for Web API

```dockerfile
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files and restore (cached layer)
COPY ["src/MyApp.Api/MyApp.Api.csproj", "src/MyApp.Api/"]
COPY ["src/MyApp.Domain/MyApp.Domain.csproj", "src/MyApp.Domain/"]
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
RUN dotnet restore "src/MyApp.Api/MyApp.Api.csproj"

# Copy everything and build
COPY . .
RUN dotnet publish "src/MyApp.Api/MyApp.Api.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user (default in .NET 8+ images)
USER app

COPY --from=build /app/publish .

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD ["dotnet", "MyApp.Api.dll", "--urls", "http://localhost:8080/health/live"]

ENTRYPOINT ["dotnet", "MyApp.Api.dll"]
```

### .dockerignore

```
**/.git
**/.vs
**/.vscode
**/bin
**/obj
**/node_modules
**/.idea
**/Dockerfile*
**/.dockerignore
**/docker-compose*
**/*.md
**/tests
**/.editorconfig
```

### Docker Compose for Local Development

```yaml
services:
  api:
    build:
      context: .
      dockerfile: src/MyApp.Api/Dockerfile
    ports:
      - "5000:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ConnectionStrings__Default=Host=postgres;Database=myapp;Username=postgres;Password=postgres
      - ConnectionStrings__Redis=redis:6379
    depends_on:
      postgres:
        condition: service_healthy
      redis:
        condition: service_started

  postgres:
    image: postgres:17
    environment:
      POSTGRES_DB: myapp
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"
    volumes:
      - postgres-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 5s
      timeout: 5s
      retries: 5

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"

volumes:
  postgres-data:
```

### Optimized Build with .slnx

For solutions with multiple projects, restore only the necessary projects.

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and all project files
COPY *.slnx .
COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY src/**/*.csproj ./src/

# Restore project structure
RUN for file in src/**/*.csproj; do \
    mkdir -p $(dirname $file) && mv $file $(dirname $file)/; \
    done
RUN dotnet restore

COPY . .
RUN dotnet publish src/MyApp.Api -c Release -o /app/publish --no-restore
```

### Health Check Endpoint

```csharp
// In Program.cs — lightweight health endpoint for Docker
app.MapGet("/health/live", () => Results.Ok("healthy"))
    .ExcludeFromDescription();
```

## Anti-patterns

### Don't Use SDK Image for Runtime

```dockerfile
# BAD — SDK image is 900MB+, includes compilers
FROM mcr.microsoft.com/dotnet/sdk:10.0
COPY . .
RUN dotnet run

# GOOD — separate build and runtime, runtime image is ~200MB
FROM mcr.microsoft.com/dotnet/aspnet:10.0
```

### Don't Copy Everything Before Restore

```dockerfile
# BAD — any source change invalidates the NuGet cache
COPY . .
RUN dotnet restore

# GOOD — copy only project files first, then restore
COPY ["src/MyApp.Api/MyApp.Api.csproj", "src/MyApp.Api/"]
RUN dotnet restore "src/MyApp.Api/MyApp.Api.csproj"
COPY . .
```

### Don't Run as Root

```dockerfile
# BAD — running as root (security risk)
FROM mcr.microsoft.com/dotnet/aspnet:10.0
COPY --from=build /app .
ENTRYPOINT ["dotnet", "MyApp.Api.dll"]

# GOOD — use the built-in non-root user
FROM mcr.microsoft.com/dotnet/aspnet:10.0
USER app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "MyApp.Api.dll"]
```

### Don't Hardcode Connection Strings

```dockerfile
# BAD — secrets in Dockerfile
ENV ConnectionStrings__Default="Host=prod-server;Password=secret"

# GOOD — pass at runtime
# docker run -e ConnectionStrings__Default="..." myapp
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| Web API container | Multi-stage build with aspnet runtime image |
| Worker service | Multi-stage build with dotnet/runtime image |
| Local development | Docker Compose with service dependencies |
| CI builds | Multi-stage build (self-contained) |
| Image size optimization | Use Alpine variant + trimming for small images |
| Health monitoring | HEALTHCHECK instruction + /health endpoint |
| Secrets | Environment variables or mounted secrets, never in image |
