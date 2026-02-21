<p align="center">
  <h1 align="center">dotnet-claude-kit</h1>
  <p align="center">
    <strong>Make Claude Code an expert .NET developer.</strong>
    <br />
    20 skills &bull; 8 specialist agents &bull; 5 project templates &bull; Roslyn MCP server
    <br />
    Built for .NET 10 / C# 14. Architecture-aware. Token-efficient.
  </p>
</p>

<p align="center">
  <a href="#installation">Installation</a> &bull;
  <a href="#quick-start">Quick Start</a> &bull;
  <a href="#what-you-get">What You Get</a> &bull;
  <a href="#skills-20">Skills</a> &bull;
  <a href="#agents-8">Agents</a> &bull;
  <a href="#templates-5">Templates</a> &bull;
  <a href="#roslyn-mcp-server">MCP Server</a> &bull;
  <a href="#contributing">Contributing</a>
</p>

---

## The Problem

Claude Code is powerful, but out of the box it doesn't know **your** .NET conventions. It generates `DateTime.Now` instead of `TimeProvider`. It wraps EF Core in repository abstractions. It picks an architecture without asking about your domain. It reads entire source files when a Roslyn query would cost 10x fewer tokens.

**dotnet-claude-kit fixes all of that.**

## What This Is

A curated knowledge layer that sits between Claude Code and your .NET project. Drop a single `CLAUDE.md` into your repo and Claude instantly knows:

- Which architecture fits your project (VSA, Clean Architecture, DDD, Modular Monolith)
- How to write modern C# 14 with primary constructors, collection expressions, and records
- How to build minimal APIs with `TypedResults`, `MapGroup`, and proper OpenAPI metadata
- How to use EF Core without repository wrappers, with compiled queries and interceptors
- How to test with `WebApplicationFactory` + `Testcontainers` instead of in-memory fakes
- How to navigate your codebase via Roslyn semantic analysis instead of expensive file reads

**No configuration. No setup wizards. Just copy one file and go.**

## Installation

### As a Claude Code Plugin (Recommended)

```
/plugin marketplace add codewithmukesh/dotnet-claude-kit
/plugin install dotnet-claude-kit@dotnet-claude-kit
```

### Manual

```bash
git clone https://github.com/codewithmukesh/dotnet-claude-kit.git
cp dotnet-claude-kit/templates/web-api/CLAUDE.md ./your-project/CLAUDE.md
```

## Quick Start

**1. Pick a template** that matches your project type:

```bash
cp templates/web-api/CLAUDE.md ./CLAUDE.md           # REST API
cp templates/modular-monolith/CLAUDE.md ./CLAUDE.md   # Multi-module system
cp templates/blazor-app/CLAUDE.md ./CLAUDE.md          # Blazor app
cp templates/worker-service/CLAUDE.md ./CLAUDE.md      # Background workers
cp templates/class-library/CLAUDE.md ./CLAUDE.md       # NuGet packages
```

**2. Customize** — Replace `[ProjectName]`, update tech stack, choose your architecture.

**3. Start Claude Code** — Skills, agents, and the Roslyn MCP server activate automatically.

That's it. Claude now writes .NET code the way a senior .NET engineer would.

## What You Get

### Before dotnet-claude-kit

```csharp
// Claude generates this
public class OrderService
{
    private readonly IOrderRepository _repo;  // unnecessary abstraction over EF Core

    public async Task<Order> CreateOrder(CreateOrderDto dto)
    {
        var order = new Order();
        order.CreatedAt = DateTime.Now;  // wrong — use TimeProvider
        order.Items = dto.Items.ToList();
        await _repo.AddAsync(order);
        return order;  // leaks domain entity to API
    }
}
```

### After dotnet-claude-kit

```csharp
// Claude generates this
public static class CreateOrder
{
    public record Command(string CustomerId, List<OrderItemDto> Items) : IRequest<Result<Response>>;
    public record Response(Guid Id, decimal Total, DateTimeOffset CreatedAt);

    internal sealed class Handler(AppDbContext db, TimeProvider clock)
        : IRequestHandler<Command, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Command request, CancellationToken ct)
        {
            var order = Order.Create(request.CustomerId, request.Items, clock.GetUtcNow());
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
            return Result.Success(new Response(order.Id, order.Total, order.CreatedAt));
        }
    }
}
```

**TimeProvider injection. DbContext directly. Result pattern. Response DTO. Sealed handler. CancellationToken propagation.** Every pattern Claude uses comes from the skills in this kit.

---

## Skills (20)

Code-heavy reference files that teach Claude .NET best practices. Each skill is under 400 lines with concrete code examples, anti-patterns (BAD/GOOD comparisons), and decision guides.

| Category | Skills | What Claude Learns |
|----------|--------|--------------------|
| **Architecture** | [architecture-advisor](skills/architecture-advisor/SKILL.md), [vertical-slice](skills/vertical-slice/SKILL.md), [clean-architecture](skills/clean-architecture/SKILL.md), [ddd](skills/ddd/SKILL.md), [project-structure](skills/project-structure/SKILL.md) | Ask before recommending. VSA for CRUD, CA for medium complexity, DDD for rich domains, Modular Monolith for bounded contexts. |
| **Core Language** | [modern-csharp](skills/modern-csharp/SKILL.md) | Primary constructors, collection expressions, `field` keyword, records, pattern matching, spans |
| **Web / API** | [minimal-api](skills/minimal-api/SKILL.md), [api-versioning](skills/api-versioning/SKILL.md), [authentication](skills/authentication/SKILL.md) | `MapGroup`, `TypedResults`, endpoint filters, JWT/OIDC, Asp.Versioning |
| **Data** | [ef-core](skills/ef-core/SKILL.md) | No repository wrappers. Compiled queries, interceptors, `ExecuteUpdateAsync`, value converters |
| **Resilience** | [error-handling](skills/error-handling/SKILL.md), [caching](skills/caching/SKILL.md), [messaging](skills/messaging/SKILL.md) | Result pattern, `HybridCache`, MassTransit, outbox, sagas |
| **Observability** | [logging](skills/logging/SKILL.md) | Serilog structured logging, OpenTelemetry, correlation IDs |
| **Testing** | [testing](skills/testing/SKILL.md) | xUnit v3, `WebApplicationFactory`, `Testcontainers`, Verify snapshots |
| **DevOps** | [docker](skills/docker/SKILL.md), [ci-cd](skills/ci-cd/SKILL.md), [aspire](skills/aspire/SKILL.md) | Multi-stage builds, GitHub Actions, .NET Aspire orchestration |
| **Cross-cutting** | [dependency-injection](skills/dependency-injection/SKILL.md), [configuration](skills/configuration/SKILL.md) | Keyed services, Options pattern, secrets management |

## Agents (8)

Specialist agents that Claude routes queries to automatically. Each agent loads the right skills, uses MCP tools for context, and knows its boundaries.

| Agent | When It Activates | What It Does |
|-------|-------------------|-------------|
| [dotnet-architect](agents/dotnet-architect.md) | "set up project", "architecture", "choose architecture" | Runs the architecture questionnaire, recommends structure, shows complete examples |
| [api-designer](agents/api-designer.md) | "create endpoint", "OpenAPI", "versioning" | Designs minimal API endpoints with proper metadata, versioning, and auth |
| [ef-core-specialist](agents/ef-core-specialist.md) | "database", "migration", "query", "DbContext" | Optimizes queries, configures entities, manages migrations |
| [test-engineer](agents/test-engineer.md) | "write tests", "test strategy", "coverage" | Integration-first testing with real databases via Testcontainers |
| [security-auditor](agents/security-auditor.md) | "security", "authentication", "JWT" | OWASP top 10, auth configuration, secrets management |
| [performance-analyst](agents/performance-analyst.md) | "performance", "benchmark", "caching" | Identifies hot paths, configures HybridCache, async optimization |
| [devops-engineer](agents/devops-engineer.md) | "Docker", "CI/CD", "Aspire", "deploy" | Multi-stage Dockerfiles, GitHub Actions pipelines, Aspire orchestration |
| [code-reviewer](agents/code-reviewer.md) | "review this code", "PR review" | Multi-dimensional review: correctness, security, performance, conventions |

## Templates (5)

Drop-in `CLAUDE.md` files that configure Claude for specific project types. Copy one file, replace the placeholders, done.

| Template | For | Includes |
|----------|-----|----------|
| [web-api](templates/web-api/) | REST APIs, microservices | Architecture options (VSA/CA/DDD), minimal APIs, EF Core, testing |
| [modular-monolith](templates/modular-monolith/) | Multi-module systems | Module boundaries, per-module DbContext, MassTransit integration events |
| [blazor-app](templates/blazor-app/) | Blazor Server / WASM / Auto | Component organization, render mode strategy, bUnit testing |
| [worker-service](templates/worker-service/) | Background processing | BackgroundService patterns, MassTransit consumers, proper cancellation |
| [class-library](templates/class-library/) | NuGet packages, shared libraries | Public API design, XML docs, semantic versioning, SourceLink |

## Roslyn MCP Server

Token-efficient codebase navigation via Roslyn semantic analysis. Instead of Claude reading entire source files (500-2000+ tokens each), it queries the MCP server for exactly what it needs (30-150 tokens).

| Tool | What It Does | Replaces |
|------|-------------|----------|
| `find_symbol` | Locate type/method definitions | Grep/Glob across all .cs files |
| `find_references` | Find all usages of a symbol | Grep for the type name |
| `find_implementations` | Find interface implementors | Searching for `: IInterface` |
| `get_type_hierarchy` | Inheritance chain + interfaces | Reading multiple files |
| `get_project_graph` | Solution dependency tree | Parsing .csproj files manually |
| `get_public_api` | Public API without full file | Reading entire source files |
| `get_diagnostics` | Compiler warnings/errors | Running `dotnet build` and parsing |

The MCP server starts automatically via `.mcp.json`. No manual setup required.

See [mcp/CWM.RoslynNavigator/README.md](mcp/CWM.RoslynNavigator/README.md) for details.

## Knowledge Base

Living reference documents updated per .NET release:

| Document | Purpose |
|----------|---------|
| [dotnet-whats-new](knowledge/dotnet-whats-new.md) | .NET 10 / C# 14 features and how to use them |
| [common-antipatterns](knowledge/common-antipatterns.md) | Patterns Claude should never generate |
| [package-recommendations](knowledge/package-recommendations.md) | Vetted NuGet packages with rationale and "when NOT to use" |
| [breaking-changes](knowledge/breaking-changes.md) | .NET migration gotchas |
| [decisions/](knowledge/decisions/) | Architecture Decision Records explaining every default |

## Defaults & Decisions

Every default is documented with an ADR explaining **why**:

| Decision | Default | Why |
|----------|---------|-----|
| Architecture | Advisor-driven | Asks questions first, then recommends VSA, CA, DDD, or Modular Monolith ([ADR-005](knowledge/decisions/005-multi-architecture.md)) |
| Error handling | Result pattern | Exceptions are for exceptional cases ([ADR-002](knowledge/decisions/002-result-over-exceptions.md)) |
| ORM | EF Core | Best developer experience for most scenarios ([ADR-003](knowledge/decisions/003-ef-core-default-orm.md)) |
| Caching | HybridCache | Built-in stampede protection, L1+L2 ([ADR-004](knowledge/decisions/004-hybrid-cache-default.md)) |
| APIs | Minimal APIs | Lighter, composable, architecture-agnostic |
| Testing | Integration-first | `WebApplicationFactory` + `Testcontainers` over in-memory fakes |
| Time | `TimeProvider` | Testable, injectable, no more `DateTime.Now` |
| HTTP clients | `IHttpClientFactory` | No more `new HttpClient()` socket exhaustion |

## Repository Structure

```
dotnet-claude-kit/
├── CLAUDE.md                    # Instructions for developing THIS repo
├── AGENTS.md                    # Agent routing & orchestration
├── agents/                      # 8 specialist agents
├── skills/                      # 20 architecture-aware skills
├── templates/                   # 5 drop-in CLAUDE.md templates
├── knowledge/                   # Living reference documents + ADRs
├── mcp/CWM.RoslynNavigator/     # Roslyn MCP server
├── hooks/                       # Claude Code hooks
├── .mcp.json                    # MCP server registration
├── .claude-plugin/              # Plugin marketplace manifests
└── .github/workflows/           # CI validation
```

## Hooks

Automated workflow integration:

- **Pre-commit** — `dotnet format --verify-no-changes` ensures consistent formatting
- **Post-scaffold** — `dotnet restore` after `.csproj` changes

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for how to add skills, knowledge, templates, and MCP tools.

## License

[MIT](LICENSE)

---

<p align="center">
  Built by <a href="https://codewithmukesh.com">Mukesh Murugan</a> &bull; Powered by Claude Code
</p>
