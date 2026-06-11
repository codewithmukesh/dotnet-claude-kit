# [Project Name] — Web API

> Copy this file into your project root and customize the sections below.

## Project Context

This is a .NET 10 REST API. Choose an architecture that fits your domain complexity (run the `architecture-advisor` skill for guidance). The architecture section below shows folder structures for VSA, Clean Architecture, and DDD — pick one and remove the others.

## Tech Stack

- **.NET 10** / C# 14
- **ASP.NET Core Minimal APIs** — `IEndpointGroup` per feature with `app.MapEndpoints()` auto-discovery
- **Entity Framework Core** — default ORM with PostgreSQL/SQL Server
- **Mediator** (source-generated, MIT) or Wolverine or raw handlers — command/query dispatch
- **FluentValidation** — request validation
- **Serilog** — structured logging
- **xUnit v3** + **Testcontainers** — testing

## Architecture

Choose one of the following structures and delete the others:

### Option A: Vertical Slice Architecture (best for CRUD-heavy, small-medium teams)

```
src/
  [ProjectName].Api/
    Features/
      [Feature]/
        [Operation].cs          # Command/Query + Handler + Response
    Common/
      Behaviors/                # Mediator pipeline behaviors
      Persistence/              # DbContext, configurations
      Extensions/               # Service registration helpers
    Program.cs
```

### Option B: Clean Architecture (best for medium complexity, long-lived systems)

```
src/
  [ProjectName].Domain/         # Entities, interfaces, domain logic (no dependencies)
  [ProjectName].Application/    # Use cases, DTOs, validation (references Domain)
  [ProjectName].Infrastructure/ # EF Core, external services (references Application + Domain)
  [ProjectName].Api/            # Endpoints, middleware (references all)
```

### Option C: DDD + Clean Architecture (best for complex domains)

```
src/
  [ProjectName].Domain/         # Aggregates, value objects, domain events, domain services
  [ProjectName].Application/    # Use cases orchestrating aggregates
  [ProjectName].Infrastructure/ # Persistence, external service adapters
  [ProjectName].Api/            # Thin endpoints
```

### Tests

```
tests/
  [ProjectName].Api.Tests/      # (or [ProjectName].Tests for CA/DDD)
    Features/
      [Feature]/
        [Operation]Tests.cs
    Fixtures/
      ApiFixture.cs             # WebApplicationFactory + Testcontainers
```

## Coding Standards

- **C# 14 features** — Use primary constructors, collection expressions, `field` keyword, records, pattern matching
- **File-scoped namespaces** — Always
- **`var` for obvious types** — Use explicit types when the type isn't clear from context
- **Naming** — PascalCase for public members, `_camelCase` for private fields, suffix async methods with `Async`
- **No regions** — Ever
- **No comments for obvious code** — Only comment "why", never "what"

## Skills

Load these dotnet-claude-kit skills for context:

- `modern-csharp` — C# 14 language features and idioms
- `architecture-advisor` — Run for new projects to choose the best architecture
- `vertical-slice` — Feature folder structure and handler patterns (if using VSA)
- `clean-architecture` — Layered project structure with dependency inversion (if using CA)
- `ddd` — Aggregates, value objects, domain events (if using DDD)
- `minimal-api` — Endpoint routing, TypedResults, OpenAPI metadata
- `ef-core` — DbContext patterns, query optimization, migrations
- `testing` — xUnit v3, WebApplicationFactory, Testcontainers
- `error-handling` — Result pattern, ProblemDetails
- `authentication` — JWT/OIDC if auth is needed
- `logging` — Serilog, OpenTelemetry
- `configuration` — Options pattern, secrets management
- `dependency-injection` — Service registration patterns
- `workflow-mastery` — Parallel worktrees, verification loops, subagent patterns, context discipline
- `instinct-system` — Capture corrections, instincts, and discoveries as persistent learning
- `wrap-up` — Structured session handoff to `.claude/handoff.md`

## MCP Tools

> **Setup:** Install once globally with `dotnet tool install -g CWM.RoslynNavigator` and register with `claude mcp add --scope user cwm-roslyn-navigator -- cwm-roslyn-navigator --solution ${workspaceFolder}`. After that, these tools are available in every .NET project.

Use `cwm-roslyn-navigator` tools to minimize token consumption:

- **Before modifying a type** — Use `find_symbol` to locate it, `get_public_api` to understand its surface
- **Before adding a reference** — Use `find_references` to understand existing usage
- **To understand architecture** — Use `get_project_graph` to see project dependencies
- **To find implementations** — Use `find_implementations` instead of grep for interface/abstract class implementations
- **To check for errors** — Use `get_diagnostics` after changes

## Commands

```bash
# Build
dotnet build

# Run (development)
dotnet run --project src/[ProjectName].Api

# Run tests
dotnet test

# Add EF migration
dotnet ef migrations add [Name] --project src/[ProjectName].Api

# Apply migrations
dotnet ef database update --project src/[ProjectName].Api

# Format check
dotnet format --verify-no-changes
```

## Workflow

- **Plan first** — Enter plan mode for any non-trivial task (3+ steps or architecture decisions). Iterate until the plan is solid before writing code.
- **Verify before done** — Run `dotnet build` and `dotnet test` after changes. Use `get_diagnostics` via MCP to catch warnings. Ask: "Would a staff engineer approve this?"
- **Fix bugs autonomously** — When given a bug report, investigate and fix it without hand-holding. Check logs, errors, failing tests — then resolve them.
- **Stop and re-plan** — If implementation goes sideways, STOP and re-plan. Don't push through a broken approach.
- **Use subagents** — Offload research, exploration, and parallel analysis to subagents. One task per subagent for focused execution.
- **Learn from corrections** — After any correction, capture the pattern in memory so the same mistake never recurs.

## Anti-patterns

Do NOT generate code that:

- Defines endpoints in Program.cs — use `IEndpointGroup` per feature with `app.MapEndpoints()` auto-discovery
- Manually wires MapGroup calls in Program.cs — Program.cs should never change when adding endpoints
- Uses `DateTime.Now` — use `TimeProvider` injection instead
- Creates `new HttpClient()` — use `IHttpClientFactory`
- Uses `async void` — always return `Task`
- Blocks with `.Result` or `.Wait()` — await instead
- Uses `Results.Ok()` — use `TypedResults.Ok()` for OpenAPI
- Returns domain entities from endpoints — always map to response DTOs
- Creates repository abstractions over EF Core — use DbContext directly
- Uses in-memory database for tests — use Testcontainers
- Catches bare `Exception` — catch specific types, let the global handler catch the rest
- Uses string interpolation in log messages — use structured logging templates
