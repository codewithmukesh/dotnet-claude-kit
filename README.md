<p align="center">
  <h1 align="center">dotnet-claude-kit</h1>
  <p align="center">
    <strong>Make Claude Code an expert .NET developer.</strong>
    <br />
    44 skills &bull; 10 specialist agents &bull; 13 slash commands &bull; 10 rules &bull; 5 project templates &bull; 15 MCP tools &bull; automation hooks
    <br />
    Built for .NET 10 / C# 14. Architecture-aware. Token-efficient.
  </p>
</p>

<p align="center">
  <a href="#installation">Installation</a> &bull;
  <a href="#quick-start">Quick Start</a> &bull;
  <a href="#what-makes-this-10x">10x Features</a> &bull;
  <a href="#slash-commands-13">Commands</a> &bull;
  <a href="#knowledge-skills-31">Skills</a> &bull;
  <a href="#agents-10">Agents</a> &bull;
  <a href="#rules-10">Rules</a> &bull;
  <a href="#templates-5">Templates</a> &bull;
  <a href="#roslyn-mcp-server">MCP Server</a> &bull;
  <a href="#contributing">Contributing</a>
</p>

---

## The Problem

Claude Code is powerful, but out of the box it doesn't know **your** .NET conventions. It generates `DateTime.Now` instead of `TimeProvider`. It wraps EF Core in repository abstractions. It picks an architecture without asking about your domain. It reads entire source files when a Roslyn query would cost 10x fewer tokens.

**dotnet-claude-kit fixes all of that.**

## What This Is

A curated knowledge and action layer that sits between Claude Code and your .NET project. Drop a single `CLAUDE.md` into your repo and Claude instantly knows:

- Which architecture fits your project (VSA, Clean Architecture, DDD, Modular Monolith)
- How to write modern C# 14 with primary constructors, collection expressions, and records
- How to build minimal APIs with `IEndpointGroup` auto-discovery, `TypedResults`, and proper OpenAPI metadata
- How to use EF Core without repository wrappers, with compiled queries and interceptors
- How to test with `WebApplicationFactory` + `Testcontainers` instead of in-memory fakes
- How to navigate your codebase via Roslyn semantic analysis instead of expensive file reads
- **How to scaffold complete features, run health checks, review PRs, and enforce conventions**

**No configuration. No setup wizards. Just copy one file and go.**

## What Makes This 10x

v0.4.0 adds an **action layer** on top of the knowledge layer — Claude doesn't just know the right patterns, it actively applies them:

| Capability | What It Does |
|-----------|-------------|
| **Scaffolding** | One command → complete feature with Result pattern, validation (FluentValidation + filter wiring), OpenAPI metadata, pagination, CancellationToken, and tests. 9-point checklist enforced. All 4 architectures. |
| **Interactive Setup** | Guided project initialization: architecture questionnaire → tech stack selection → customized CLAUDE.md generation. |
| **Health Check** | Automated codebase analysis using MCP tools: anti-pattern scan, diagnostics, dead code detection, test coverage → graded report card. |
| **PR Review** | Multi-dimensional code review: anti-patterns, diagnostics, API surface changes, blast radius, architecture compliance, test coverage. |
| **Convention Learning** | Detects project-specific patterns (naming, structure, modifiers) and enforces them in new code. Adapts to your codebase. |
| **Smart Tools** | 15 Roslyn-powered MCP tools including dependency graphs, circular dependency detection, dead code finder, and test coverage mapping. |
| **Active Hooks** | Automated quality scripts: format on edit, destructive-command guard, restore on .csproj change, plus git pre-commit checks and test analysis utilities. |

## Why dotnet-claude-kit?

| Metric | Without Kit | With Kit | Impact |
|--------|-------------|----------|--------|
| **Architecture decisions** | Claude picks randomly | Asks questions, recommends with rationale | Correct architecture from day one |
| **Code quality** | Generic C#, legacy patterns | Modern C# 14 with idiomatic .NET 10 | Zero "fix this pattern" revision cycles |
| **Codebase navigation** | Reads entire files (500-2000+ tokens each) | Roslyn MCP queries (30-150 tokens each) | **~10x token savings** on exploration |
| **Anti-patterns generated** | `DateTime.Now`, repository-over-EF, `new HttpClient()` | `TimeProvider`, direct DbContext, `IHttpClientFactory` | Production-ready on first generation |
| **Testing approach** | In-memory fakes, mocked everything | `WebApplicationFactory` + `Testcontainers` | Tests that catch real bugs |
| **Production resilience** | No retry, no circuit breakers | Polly v8 pipelines with telemetry | Handles transient failures automatically |

**The result**: Less time reviewing and correcting Claude's output. More time shipping features.

## Installation

### Plugin Install (Recommended)

Install as a Claude Code plugin — all 44 skills (including 13 slash-command workflows), 10 agents, 10 rules, hooks, and MCP config activate globally:

```bash
# In your terminal — install the Roslyn MCP server
dotnet tool install -g CWM.RoslynNavigator
```

> **macOS/Linux**: If the server fails with "No .NET SDKs were found", set `DOTNET_ROOT` to your .NET installation root (e.g. `/usr/local/share/dotnet`). See the [MCP server troubleshooting guide](mcp/CWM.RoslynNavigator/README.md#troubleshooting).

Then inside a Claude Code session:

```
# Add the marketplace and install the plugin
/plugin marketplace add codewithmukesh/dotnet-claude-kit
/plugin install dotnet-claude-kit
```

**For local development/testing** (loads directly from disk, no install needed):

```bash
claude --plugin-dir /path/to/dotnet-claude-kit
```

### Per-Project Setup

Navigate to your project directory (existing or empty) and run:

```bash
/dotnet-init
```

**Existing project?** It detects your solution, scans .csproj SDKs, reads your tech stack from config, asks architecture questions, and generates a customized `CLAUDE.md`.

**Greenfield project?** It asks what you're building, scaffolds the full solution structure (`dotnet new sln`, projects, `Directory.Build.props`, `src/` and `tests/` folders), then generates `CLAUDE.md`. Follow up with `/scaffold` to add your first feature.

No manual template copying needed.

<details>
<summary><strong>Manual Template Copy (Alternative)</strong></summary>

If you prefer manual setup, copy the template matching your project type:

```bash
cp templates/web-api/CLAUDE.md ./CLAUDE.md           # REST API
cp templates/modular-monolith/CLAUDE.md ./CLAUDE.md   # Multi-module system
cp templates/blazor-app/CLAUDE.md ./CLAUDE.md          # Blazor app
cp templates/worker-service/CLAUDE.md ./CLAUDE.md      # Background workers
cp templates/class-library/CLAUDE.md ./CLAUDE.md       # NuGet packages
```

Replace `[ProjectName]`, update tech stack, choose your architecture.

</details>

Start Claude Code — 44 skills, 10 agents, 10 rules, and 15 MCP tools activate automatically.

That's it. Claude now writes .NET code the way a senior .NET engineer would.

<details>
<summary><strong>Manual Install (Alternative)</strong></summary>

If you prefer to clone the repo and wire things up manually:

```bash
# 1. Install the MCP server globally
dotnet tool install -g CWM.RoslynNavigator

# 2. Register it in Claude Code at user scope (available in ALL projects)
claude mcp add --scope user cwm-roslyn-navigator -- cwm-roslyn-navigator --solution ${workspaceFolder}

# 3. Clone the kit
git clone https://github.com/codewithmukesh/dotnet-claude-kit.git

# 4. Load as a local plugin (or copy a template manually)
claude --plugin-dir ./dotnet-claude-kit
```

</details>

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

```csharp
// Each endpoint group auto-discovered — Program.cs never changes
public sealed class OrderEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders").WithTags("Orders");
        group.MapPost("/", CreateOrderHandler)
            .WithName("CreateOrder").Produces<CreateOrder.Response>(201)
            .ProducesValidationProblem()
            .AddEndpointFilter<ValidationFilter<CreateOrder.Command>>();
    }
}
```

**Result pattern. FluentValidation with endpoint filters. IEndpointGroup auto-discovery. TypedResults with OpenAPI metadata. CancellationToken everywhere. Sealed handlers. TimeProvider injection. DbContext directly.** Every pattern comes from the skills in this kit.

---

## Slash Commands (13)

Shortcut workflows that orchestrate skills and agents. Type the command and Claude handles the rest. These are workflow skills — each lives at `skills/<name>/SKILL.md`, registers its `/name` automatically, and carries its methodology inline (no separate knowledge twin to load).

| Command | Purpose | Works With |
|---------|---------|------------|
| `/dotnet-init` | Project setup (existing or greenfield) — detects or scaffolds, then generates CLAUDE.md | project-setup skill, dotnet-architect agent |
| `/plan` | Architecture-aware planning for non-trivial tasks | architecture-advisor skill, dotnet-architect agent |
| `/verify` | 7-phase verification: build → analyzers → antipatterns → tests → security → format → diff | — |
| `/tdd` | Red-green-refactor with xUnit + Testcontainers | testing skill, test-engineer agent |
| `/scaffold` | Architecture-aware feature scaffolding (all 4 architectures, per-architecture templates included) | dotnet-architect agent |
| `/code-review` | MCP-powered, blast-radius-prioritized code review | code-reviewer agent |
| `/build-fix` | Bounded build-fix and test-fix loops with progress detection | build-error-resolver agent |
| `/checkpoint` | Mid-session save: commit + brief handoff note | — |
| `/security-scan` | OWASP + secrets + vulnerable dependency audit | security-auditor agent |
| `/migrate` | EF Core schema, .NET version, and NuGet migrations with rollback | ef-core-specialist agent |
| `/health-check` | Project health assessment with letter grades (A-F) | code-reviewer agent |
| `/de-sloppify` | Systematic cleanup: format → dead code → analyzers → sealed | refactor-cleaner agent |
| `/wrap-up` | Session handoff lifecycle: end-of-session ritual + session-start loading | instinct-system skill |

Instinct operations (status, export, import) are modes of the [instinct-system](skills/instinct-system/SKILL.md) skill — say "show instincts", "export instincts", or "import instincts".

## Rules (10)

Always-loaded conventions that apply to every interaction. Zero configuration — they're active as soon as the plugin is installed.

| Rule | Enforces |
|------|----------|
| [coding-style](.claude/rules/coding-style.md) | C# 14 conventions, file-scoped namespaces, primary constructors, sealed, records |
| [architecture](.claude/rules/architecture.md) | Ask before recommending, no repo over EF, feature folders, dependency direction |
| [security](.claude/rules/security.md) | No hardcoded secrets, parameterized queries, explicit auth, HTTPS |
| [testing](.claude/rules/testing.md) | Integration-first, WebApplicationFactory + Testcontainers, AAA pattern |
| [performance](.claude/rules/performance.md) | CancellationToken propagation, TimeProvider, IHttpClientFactory, HybridCache |
| [error-handling](.claude/rules/error-handling.md) | Result pattern, ProblemDetails, no broad catch, boundary validation |
| [git-workflow](.claude/rules/git-workflow.md) | Conventional commits, atomic commits, never force-push main |
| [agents](.claude/rules/agents.md) | MCP-first, subagent routing, skill loading order |
| [hooks](.claude/rules/hooks.md) | Auto-accept formatting, never skip pre-commit hooks |
| [packages](.claude/rules/packages.md) | Always use latest stable NuGet versions, never rely on training data versions |

## Knowledge Skills (31)

Code-heavy reference files that teach Claude .NET best practices. Each skill is under 400 lines with concrete code examples, anti-patterns (BAD/GOOD comparisons), and decision guides. (The other 13 of the 44 skills are the workflow orchestrators documented under [Slash Commands](#slash-commands-13).)

| Category | Skills | What Claude Learns |
|----------|--------|--------------------|
| **Architecture** | [architecture-advisor](skills/architecture-advisor/SKILL.md), [vertical-slice](skills/vertical-slice/SKILL.md), [clean-architecture](skills/clean-architecture/SKILL.md), [ddd](skills/ddd/SKILL.md), [project-structure](skills/project-structure/SKILL.md) | Ask before recommending. VSA for CRUD, CA for medium complexity, DDD for rich domains, Modular Monolith for bounded contexts. |
| **Core Language** | [modern-csharp](skills/modern-csharp/SKILL.md) | Primary constructors, collection expressions, `field` keyword, records, pattern matching, spans |
| **Web / API** | [minimal-api](skills/minimal-api/SKILL.md), [api-versioning](skills/api-versioning/SKILL.md), [authentication](skills/authentication/SKILL.md), [openapi](skills/openapi/SKILL.md), [scalar](skills/scalar/SKILL.md), [httpclient-factory](skills/httpclient-factory/SKILL.md) | `MapGroup`, `TypedResults`, endpoint filters, JWT/OIDC, Asp.Versioning, built-in OpenAPI, typed HTTP clients |
| **Data** | [ef-core](skills/ef-core/SKILL.md) | No repository wrappers. Compiled queries, interceptors, `ExecuteUpdateAsync`, value converters |
| **Resilience** | [error-handling](skills/error-handling/SKILL.md), [resilience](skills/resilience/SKILL.md), [caching](skills/caching/SKILL.md), [messaging](skills/messaging/SKILL.md) | Result pattern, Polly v8 pipelines, `HybridCache`, Wolverine/MassTransit, outbox, sagas |
| **Observability** | [logging](skills/logging/SKILL.md), [serilog](skills/serilog/SKILL.md), [opentelemetry](skills/opentelemetry/SKILL.md) | Health checks and correlation IDs, Serilog structured logging, OpenTelemetry traces and metrics |
| **Testing** | [testing](skills/testing/SKILL.md) | xUnit v3, `WebApplicationFactory`, `Testcontainers`, Verify snapshots |
| **DevOps** | [docker](skills/docker/SKILL.md), [container-publish](skills/container-publish/SKILL.md), [ci-cd](skills/ci-cd/SKILL.md), [aspire](skills/aspire/SKILL.md) | Multi-stage builds, Dockerfile-less SDK publishing, GitHub Actions, .NET Aspire orchestration |
| **Cross-cutting** | [dependency-injection](skills/dependency-injection/SKILL.md), [configuration](skills/configuration/SKILL.md) | Keyed services, Options pattern, secrets management |
| **Project Setup** | [project-setup](skills/project-setup/SKILL.md), [convention-learner](skills/convention-learner/SKILL.md) | Solution scaffolding, convention detection and enforcement |
| **Workflow & Learning** | [workflow-mastery](skills/workflow-mastery/SKILL.md), [instinct-system](skills/instinct-system/SKILL.md) | Parallel worktrees, plan mode strategy, subagent patterns, context discipline; confidence-scored instincts, correction capture, discovery logging |

## Agents (10)

Specialist agents that Claude routes queries to automatically. Each agent loads the right skills, uses MCP tools for context, and knows its boundaries.

| Agent | When It Activates | What It Does |
|-------|-------------------|-------------|
| [dotnet-architect](agents/dotnet-architect.md) | "set up project", "architecture", "scaffold feature", "init project" | Runs the architecture questionnaire, scaffolds features, initializes projects |
| [api-designer](agents/api-designer.md) | "create endpoint", "OpenAPI", "versioning" | Designs minimal API endpoints with proper metadata, versioning, and auth |
| [ef-core-specialist](agents/ef-core-specialist.md) | "database", "migration", "query", "DbContext" | Optimizes queries, configures entities, manages migrations safely |
| [test-engineer](agents/test-engineer.md) | "write tests", "test strategy", "coverage" | Integration-first testing with real databases via Testcontainers |
| [security-auditor](agents/security-auditor.md) | "security", "authentication", "JWT" | OWASP top 10, auth configuration, secrets management |
| [performance-analyst](agents/performance-analyst.md) | "performance", "benchmark", "caching" | Identifies hot paths, configures HybridCache, async optimization |
| [devops-engineer](agents/devops-engineer.md) | "Docker", "CI/CD", "Aspire", "deploy" | Multi-stage Dockerfiles, GitHub Actions pipelines, Aspire orchestration |
| [code-reviewer](agents/code-reviewer.md) | "review this code", "PR review", "health check", "conventions" | MCP-driven multi-dimensional review, convention detection and enforcement |
| [build-error-resolver](agents/build-error-resolver.md) | "fix build", "build errors", "won't compile" | Autonomous build-fix loop: parse errors → categorize → fix → rebuild |
| [refactor-cleaner](agents/refactor-cleaner.md) | "clean up", "dead code", "de-sloppify" | Systematic cleanup: dead code removal, formatting, sealing, CancellationToken |

## Templates (5)

Drop-in `CLAUDE.md` files that configure Claude for specific project types. Copy one file, replace the placeholders, done.

| Template | For | Includes |
|----------|-----|----------|
| [web-api](templates/web-api/) | REST APIs, microservices | Architecture options (VSA/CA/DDD), minimal APIs, EF Core, testing |
| [modular-monolith](templates/modular-monolith/) | Multi-module systems | Module boundaries, per-module DbContext, Wolverine/MassTransit integration events |
| [blazor-app](templates/blazor-app/) | Blazor Server / WASM / Auto | Component organization, render mode strategy, bUnit testing |
| [worker-service](templates/worker-service/) | Background processing | BackgroundService patterns, Wolverine/MassTransit consumers, proper cancellation |
| [class-library](templates/class-library/) | NuGet packages, shared libraries | Public API design, XML docs, semantic versioning, SourceLink |

## Roslyn MCP Server

Token-efficient codebase navigation via Roslyn semantic analysis. Instead of Claude reading entire source files (500-2000+ tokens each), it queries the MCP server for exactly what it needs (30-150 tokens).

| Tool | What It Does | Replaces |
|------|-------------|----------|
| `find_symbol` | Locate type/method definitions | Grep/Glob across all .cs files |
| `find_references` | Find all usages of a symbol | Grep for the type name |
| `find_implementations` | Find interface implementors | Searching for `: IInterface` |
| `find_callers` | Find all methods calling a method | Manual grep for method name |
| `find_overrides` | Find overrides of virtual/abstract methods | Searching for `override` keyword |
| `get_type_hierarchy` | Inheritance chain + interfaces | Reading multiple files |
| `get_project_graph` | Solution dependency tree | Parsing .csproj files manually |
| `get_public_api` | Public API without full file | Reading entire source files |
| `get_symbol_detail` | Full signature, params, XML docs | Reading entire source files |
| `get_diagnostics` | Compiler warnings/errors | Running `dotnet build` and parsing |
| `detect_antipatterns` | 10 .NET anti-pattern rules | Manual code review |
| `find_dead_code` | Unused types, methods, properties | Manual inspection of all files |
| `detect_circular_dependencies` | Project and type-level cycles | Manually tracing references |
| `get_dependency_graph` | Method call chain visualization | Reading multiple files and tracing |
| `get_test_coverage_map` | Heuristic test coverage mapping | Searching for test files manually |

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

## Hooks & Automation Scripts (7)

Three Claude Code hooks run automatically (declared in `hooks/hooks.json`); the rest are git pre-commit hooks and workflow utilities — see [hooks/README.md](hooks/README.md) for setup:

| Script | Kind | What It Does |
|------|-------|-------------|
| `pre-bash-guard.sh` | Claude Code hook — PreToolUse (Bash) | Blocks destructive git ops (force push, reset --hard), warns on risky commands |
| `post-edit-format.sh` | Claude Code hook — PostToolUse (*.cs) | Auto-formats C# files after edits |
| `post-scaffold-restore.sh` | Claude Code hook — PostToolUse (*.csproj) | `dotnet restore` after project file changes |
| `pre-commit-format.sh` | Git pre-commit (manual install) | `dotnet format --verify-no-changes` ensures consistent formatting |
| `pre-commit-antipattern.sh` | Git pre-commit (manual install) | Detects DateTime.Now, async void, new HttpClient() in staged files |
| `post-test-analyze.sh` | Utility (pipe test output) | Parses test results and outputs actionable summary |
| `pre-build-validate.sh` | Utility (run before builds) | Validates project structure (solution file, Directory.Build.props, test projects) |

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
├── agents/                      # 10 specialist agents
├── skills/                      # 44 skills (incl. 13 slash-command workflows)
├── .claude/rules/               # 10 always-loaded rules
├── templates/                   # 5 drop-in CLAUDE.md templates
├── knowledge/                   # Living reference documents + ADRs
├── mcp/CWM.RoslynNavigator/     # Roslyn MCP server (15 tools)
├── mcp-configs/                 # MCP server config templates
├── hooks/                       # Claude Code hooks + git hooks + utilities
├── docs/                        # Shorthand + longform guides
├── .mcp.json                    # MCP server registration
├── .claude-plugin/              # Plugin marketplace manifests
├── .cursor/rules/               # Cursor IDE compatibility
├── .codex/                      # Codex CLI compatibility
└── .github/workflows/           # CI validation
```

## Multi-Platform Support

dotnet-claude-kit works with multiple AI coding tools:

| Platform | Config File | What It Provides |
|----------|------------|-----------------|
| **Claude Code** | `.claude-plugin/plugin.json` | Full integration: skills, agents, commands, rules, hooks, MCP |
| **Cursor** | `.cursor/rules/dotnet-rules.md` | Consolidated .NET rules for Cursor IDE |
| **Codex CLI** | `.codex/AGENTS.md` | Agent configuration pointing to skills and agents |

## Documentation

| Guide | For | Content |
|-------|-----|---------|
| [Shorthand Guide](docs/shorthand-guide.md) | Quick reference | All commands, skills, agents, hooks, MCP tools with cross-reference matrix |
| [Longform Guide](docs/longform-guide.md) | Deep dive | Workflows, token optimization, autonomous patterns, troubleshooting |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for how to add skills, agents, commands, rules, knowledge, templates, and MCP tools.

## License

[MIT](LICENSE)

---

<p align="center">
  Built by <a href="https://codewithmukesh.com">Mukesh Murugan</a> &bull; Powered by Claude Code
</p>
