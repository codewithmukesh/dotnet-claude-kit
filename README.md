<p align="center">
  <h1 align="center">dotnet-claude-kit</h1>
  <p align="center">
    <strong>Make Claude Code an expert .NET developer.</strong>
    <br />
    47 skills &bull; 10 specialist agents &bull; 15 slash commands &bull; 9 rules &bull; 5 project templates &bull; 15 MCP tools &bull; 7 hooks
    <br />
    Built for .NET 10 / C# 14. Architecture-aware. Token-efficient.
  </p>
</p>

<p align="center">
  <a href="#installation">Installation</a> &bull;
  <a href="#quick-start">Quick Start</a> &bull;
  <a href="#what-makes-this-10x">10x Features</a> &bull;
  <a href="#slash-commands-15">Commands</a> &bull;
  <a href="#skills-47">Skills</a> &bull;
  <a href="#agents-10">Agents</a> &bull;
  <a href="#rules-9">Rules</a> &bull;
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
- How to build minimal APIs with `TypedResults`, `MapGroup`, and proper OpenAPI metadata
- How to use EF Core without repository wrappers, with compiled queries and interceptors
- How to test with `WebApplicationFactory` + `Testcontainers` instead of in-memory fakes
- How to navigate your codebase via Roslyn semantic analysis instead of expensive file reads
- **How to scaffold complete features, run health checks, review PRs, and enforce conventions**

**No configuration. No setup wizards. Just copy one file and go.**

## What Makes This 10x

v0.4.0 adds an **action layer** on top of the knowledge layer — Claude doesn't just know the right patterns, it actively applies them:

| Capability | What It Does |
|-----------|-------------|
| **Scaffolding** | One command → complete feature with endpoint, handler, validator, DTO, EF config, and tests. All 4 architectures supported. |
| **Interactive Setup** | Guided project initialization: architecture questionnaire → tech stack selection → customized CLAUDE.md generation. |
| **Health Check** | Automated codebase analysis using MCP tools: anti-pattern scan, diagnostics, dead code detection, test coverage → graded report card. |
| **PR Review** | Multi-dimensional code review: anti-patterns, diagnostics, API surface changes, blast radius, architecture compliance, test coverage. |
| **Convention Learning** | Detects project-specific patterns (naming, structure, modifiers) and enforces them in new code. Adapts to your codebase. |
| **Smart Tools** | 15 Roslyn-powered MCP tools including dependency graphs, circular dependency detection, dead code finder, and test coverage mapping. |
| **Active Hooks** | 6 hooks for automated quality: format on edit, anti-pattern checks on commit, test result analysis, structure validation. |

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

Install as a Claude Code plugin — all 47 skills, 10 agents, 16 commands, 9 rules, hooks, and MCP config activate globally:

```bash
# In your terminal — install the Roslyn MCP server
dotnet tool install -g CWM.RoslynNavigator
```

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

Start Claude Code — 47 skills, 10 agents, 16 commands, 9 rules, and 15 MCP tools activate automatically.

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

**TimeProvider injection. DbContext directly. Result pattern. Response DTO. Sealed handler. CancellationToken propagation.** Every pattern Claude uses comes from the skills in this kit.

---

## Slash Commands (16)

Shortcut workflows that orchestrate skills and agents. Type the command and Claude handles the rest.

| Command | Purpose | Invokes |
|---------|---------|---------|
| `/dotnet-init` | Project setup (existing or greenfield) — detects or scaffolds, then generates CLAUDE.md | project-setup skill, dotnet-architect agent |
| `/plan` | Architecture-aware planning for non-trivial tasks | architecture-advisor skill, dotnet-architect agent |
| `/verify` | 7-phase verification: build → analyzers → antipatterns → tests → security → format → diff | verification-loop skill |
| `/tdd` | Red-green-refactor with xUnit + Testcontainers | testing skill, test-engineer agent |
| `/scaffold` | Architecture-aware feature scaffolding (all 4 architectures) | scaffolding skill, dotnet-architect agent |
| `/code-review` | MCP-powered multi-dimensional code review | code-review-workflow skill, code-reviewer agent |
| `/build-fix` | Autonomous build error fixing (iterative loop) | autonomous-loops skill, build-error-resolver agent |
| `/checkpoint` | Save progress: commit + handoff note | wrap-up-ritual skill |
| `/security-scan` | OWASP + secrets + vulnerable dependency audit | security-scan skill, security-auditor agent |
| `/migrate` | Safe EF Core migration workflow | migration-workflow skill, ef-core-specialist agent |
| `/health-check` | Project health assessment with letter grades (A-F) | health-check skill, code-reviewer agent |
| `/de-sloppify` | Systematic cleanup: format → dead code → analyzers → sealed | de-sloppify skill, refactor-cleaner agent |
| `/wrap-up` | Session ending ritual with handoff note | wrap-up-ritual skill |
| `/instinct-status` | Show learned instincts with confidence scores | instinct-system skill |
| `/instinct-export` | Export instincts to shareable format | instinct-system skill |
| `/instinct-import` | Import instincts from another project | instinct-system skill |

## Rules (9)

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

## Skills (47)

Code-heavy reference files that teach Claude .NET best practices. Each skill is under 400 lines with concrete code examples, anti-patterns (BAD/GOOD comparisons), and decision guides.

| Category | Skills | What Claude Learns |
|----------|--------|--------------------|
| **Architecture** | [architecture-advisor](skills/architecture-advisor/SKILL.md), [vertical-slice](skills/vertical-slice/SKILL.md), [clean-architecture](skills/clean-architecture/SKILL.md), [ddd](skills/ddd/SKILL.md), [project-structure](skills/project-structure/SKILL.md) | Ask before recommending. VSA for CRUD, CA for medium complexity, DDD for rich domains, Modular Monolith for bounded contexts. |
| **Core Language** | [modern-csharp](skills/modern-csharp/SKILL.md) | Primary constructors, collection expressions, `field` keyword, records, pattern matching, spans |
| **Web / API** | [minimal-api](skills/minimal-api/SKILL.md), [api-versioning](skills/api-versioning/SKILL.md), [authentication](skills/authentication/SKILL.md) | `MapGroup`, `TypedResults`, endpoint filters, JWT/OIDC, Asp.Versioning |
| **Data** | [ef-core](skills/ef-core/SKILL.md) | No repository wrappers. Compiled queries, interceptors, `ExecuteUpdateAsync`, value converters |
| **Resilience** | [error-handling](skills/error-handling/SKILL.md), [resilience](skills/resilience/SKILL.md), [caching](skills/caching/SKILL.md), [messaging](skills/messaging/SKILL.md) | Result pattern, Polly v8 pipelines, `HybridCache`, MassTransit, outbox, sagas |
| **Observability** | [logging](skills/logging/SKILL.md) | Serilog structured logging, OpenTelemetry, correlation IDs |
| **Testing** | [testing](skills/testing/SKILL.md) | xUnit v3, `WebApplicationFactory`, `Testcontainers`, Verify snapshots |
| **DevOps** | [docker](skills/docker/SKILL.md), [ci-cd](skills/ci-cd/SKILL.md), [aspire](skills/aspire/SKILL.md) | Multi-stage builds, GitHub Actions, .NET Aspire orchestration |
| **Cross-cutting** | [dependency-injection](skills/dependency-injection/SKILL.md), [configuration](skills/configuration/SKILL.md) | Keyed services, Options pattern, secrets management |
| **Workflow** | [workflow-mastery](skills/workflow-mastery/SKILL.md) | Parallel worktrees, plan mode strategy, verification loops, auto-format hooks, permission setup, subagent patterns |
| **Workflows & Automation** | [scaffolding](skills/scaffolding/SKILL.md), [project-setup](skills/project-setup/SKILL.md), [code-review-workflow](skills/code-review-workflow/SKILL.md), [migration-workflow](skills/migration-workflow/SKILL.md), [convention-learner](skills/convention-learner/SKILL.md) | Feature scaffolding for all architectures, interactive project init, MCP-driven PR reviews, safe migration workflows, convention detection and enforcement |
| **Verification & Quality** | [verification-loop](skills/verification-loop/SKILL.md), [de-sloppify](skills/de-sloppify/SKILL.md), [health-check](skills/health-check/SKILL.md), [security-scan](skills/security-scan/SKILL.md) | 7-phase verification pipeline, systematic cleanup, graded health assessment, deep security scanning |
| **Intelligence & Learning** | [instinct-system](skills/instinct-system/SKILL.md), [session-management](skills/session-management/SKILL.md), [autonomous-loops](skills/autonomous-loops/SKILL.md) | Confidence-scored pattern learning, session continuity, bounded iterative fix loops |
| **Meta & Productivity** | [self-correction-loop](skills/self-correction-loop/SKILL.md), [wrap-up-ritual](skills/wrap-up-ritual/SKILL.md), [context-discipline](skills/context-discipline/SKILL.md), [model-selection](skills/model-selection/SKILL.md), [80-20-review](skills/80-20-review/SKILL.md), [split-memory](skills/split-memory/SKILL.md), [learning-log](skills/learning-log/SKILL.md) | Self-improving correction capture, structured session handoffs, token budget management, strategic model selection, focused code review, modular CLAUDE.md, insight documentation |

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

## Hooks (7)

Automated workflow integration:

| Hook | Event | What It Does |
|------|-------|-------------|
| `pre-bash-guard.sh` | PreToolUse (Bash) | Blocks destructive git ops (force push, reset --hard), warns on risky commands |
| `pre-commit-format.sh` | Pre-commit | `dotnet format --verify-no-changes` ensures consistent formatting |
| `pre-commit-antipattern.sh` | Pre-commit | Detects DateTime.Now, async void, new HttpClient() in staged files |
| `post-scaffold-restore.sh` | Post-file-edit (*.csproj) | `dotnet restore` after project file changes |
| `post-edit-format.sh` | Post-file-edit (*.cs) | Auto-formats C# files after edits |
| `post-test-analyze.sh` | Post-test | Parses test results and outputs actionable summary |
| `pre-build-validate.sh` | Pre-build | Validates project structure (solution file, Directory.Build.props, test projects) |

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
├── skills/                      # 47 skills
├── commands/                    # 15 slash commands
├── .claude/rules/               # 9 always-loaded rules
├── templates/                   # 5 drop-in CLAUDE.md templates
├── knowledge/                   # Living reference documents + ADRs
├── mcp/CWM.RoslynNavigator/     # Roslyn MCP server (15 tools)
├── mcp-configs/                 # MCP server config templates
├── hooks/                       # 7 Claude Code hooks
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
