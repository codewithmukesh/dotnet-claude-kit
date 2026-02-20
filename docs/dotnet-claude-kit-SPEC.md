# dotnet-claude-kit — Complete Repository Specification

> The opinionated Claude Code companion for .NET developers.
> Project-ready templates, intelligent agents, workflow automation, and a Roslyn MCP server for token-efficient codebase navigation.

---

## Summary of Decisions

| Decision | Choice |
|---|---|
| **Repo name** | `dotnet-claude-kit` |
| **Target audience** | Open source for all .NET developers |
| **Project types** | Framework-agnostic (Web API, Blazor, MAUI, modular monolith, libraries, workers) |
| **Architecture default** | Vertical Slice Architecture (opinionated default) |
| **VSA handler approach** | User's choice — provide patterns for MediatR, Wolverine, and raw handlers |
| **Modular monolith** | Optional — show how to add modules, don't require it |
| **Data access** | EF Core as the default ORM |
| **Skills count at launch** | 15–20 high-quality, opinionated |
| **Roslyn MCP** | Tightly integrated, optimized for all solution sizes |
| **Environments** | Claude Code CLI only |
| **Extras at launch** | Hooks + `.mcp.json` (no slash commands or GitHub Actions at v1) |
| **Positioning** | Best-in-class, independent — not defined relative to dotnet-artisan |

---

## Repository Structure

```
dotnet-claude-kit/
│
├── CLAUDE.md                              # Master instructions for THIS repo's development
├── AGENTS.md                              # Agent routing & orchestration rules
├── README.md                              # Repo docs, install guide, badges, philosophy
├── LICENSE                                # MIT
├── CONTRIBUTING.md                        # How to contribute skills, agents, knowledge
├── CHANGELOG.md
│
│
│  ┌─────────────────────────────────────────────────────┐
│  │  AGENTS — 8 specialist agents                       │
│  └─────────────────────────────────────────────────────┘
│
├── agents/
│   ├── dotnet-architect.md                # Central router — analyzes context, delegates
│   ├── api-designer.md                    # Minimal API, OpenAPI, versioning, rate limiting
│   ├── ef-core-specialist.md              # Migrations, queries, performance, interceptors
│   ├── test-engineer.md                   # Test strategy, generation, coverage analysis
│   ├── security-auditor.md                # OWASP, auth, secrets, data protection
│   ├── performance-analyst.md             # BenchmarkDotNet, memory, async, profiling
│   ├── devops-engineer.md                 # CI/CD, Docker, Aspire, Azure deployment
│   └── code-reviewer.md                   # Multi-dimensional PR review
│
│
│  ┌─────────────────────────────────────────────────────┐
│  │  SKILLS — 18 opinionated skills                     │
│  └─────────────────────────────────────────────────────┘
│
├── skills/
│   │
│   │  -- Core Language --
│   ├── modern-csharp/
│   │   └── SKILL.md                       # C# 13/14, records, pattern matching, spans,
│   │                                      # primary constructors, collection expressions
│   │
│   │  -- Architecture --
│   ├── vertical-slice/
│   │   └── SKILL.md                       # VSA as default. Feature folders, endpoint
│   │                                      # grouping. Patterns for MediatR, Wolverine,
│   │                                      # and raw handler classes (user picks).
│   │                                      # How to optionally introduce modules.
│   │
│   │  -- Web / API --
│   ├── minimal-api/
│   │   └── SKILL.md                       # .NET 10 minimal APIs, endpoint filters,
│   │                                      # route groups, typed results, OpenAPI
│   │
│   ├── api-versioning/
│   │   └── SKILL.md                       # Asp.Versioning, URL/header/query strategies
│   │
│   ├── authentication/
│   │   └── SKILL.md                       # ASP.NET Identity, JWT, OIDC, cookie auth,
│   │                                      # authorization policies, role/claim-based
│   │
│   │  -- Data --
│   ├── ef-core/
│   │   └── SKILL.md                       # DbContext patterns, migrations workflow,
│   │                                      # query optimization, interceptors, value
│   │                                      # converters, compiled queries, split queries
│   │
│   │  -- Resilience & Communication --
│   ├── error-handling/
│   │   └── SKILL.md                       # Result pattern (no exceptions for flow control),
│   │                                      # ProblemDetails, global exception handler,
│   │                                      # FluentValidation / manual validation
│   │
│   ├── caching/
│   │   └── SKILL.md                       # HybridCache (.NET 9+), output caching,
│   │                                      # response caching, distributed cache patterns
│   │
│   ├── messaging/
│   │   └── SKILL.md                       # MassTransit, outbox pattern, saga/choreography,
│   │                                      # RabbitMQ/Azure Service Bus configuration
│   │
│   │  -- Observability --
│   ├── logging/
│   │   └── SKILL.md                       # Serilog structured logging, OpenTelemetry
│   │                                      # traces/metrics, health checks, correlation IDs
│   │
│   │  -- Testing --
│   ├── testing/
│   │   └── SKILL.md                       # xUnit best practices, WebApplicationFactory,
│   │                                      # Testcontainers, Verify snapshot testing,
│   │                                      # test naming conventions, arrange-act-assert
│   │
│   │  -- DevOps --
│   ├── docker/
│   │   └── SKILL.md                       # Multi-stage builds, .NET container images,
│   │                                      # non-root users, health checks, .dockerignore
│   │
│   ├── ci-cd/
│   │   └── SKILL.md                       # GitHub Actions + Azure DevOps YAML pipelines,
│   │                                      # build → test → publish → deploy patterns
│   │
│   ├── aspire/
│   │   └── SKILL.md                       # .NET Aspire orchestration, AppHost, service
│   │                                      # defaults, dashboard, resource configuration
│   │
│   │  -- Cross-cutting --
│   ├── dependency-injection/
│   │   └── SKILL.md                       # Keyed services, scoped/transient/singleton
│   │                                      # guidance, decorator pattern, factory pattern
│   │
│   ├── configuration/
│   │   └── SKILL.md                       # Options pattern, IOptionsSnapshot vs IOptions,
│   │                                      # secrets management, environment-based config
│   │
│   └── project-structure/
│       └── SKILL.md                       # .slnx, Directory.Build.props, Directory.Packages.props,
│                                          # central package management, global usings,
│                                          # editorconfig, naming conventions
│
│
│  ┌─────────────────────────────────────────────────────┐
│  │  TEMPLATES — Drop-in CLAUDE.md for your project     │
│  └─────────────────────────────────────────────────────┘
│
├── templates/
│   ├── web-api/
│   │   ├── CLAUDE.md                      # Ready to copy into a REST API project
│   │   └── README.md                      # When and how to use this template
│   │
│   ├── modular-monolith/
│   │   ├── CLAUDE.md                      # Multi-module solution with VSA per module
│   │   └── README.md
│   │
│   ├── blazor-app/
│   │   ├── CLAUDE.md                      # Blazor Server / WASM / Auto
│   │   └── README.md
│   │
│   ├── worker-service/
│   │   ├── CLAUDE.md                      # Background workers, hosted services, Hangfire
│   │   └── README.md
│   │
│   └── class-library/
│       ├── CLAUDE.md                      # NuGet packages, shared libraries
│       └── README.md
│
│
│  ┌─────────────────────────────────────────────────────┐
│  │  KNOWLEDGE — Living, updatable context              │
│  └─────────────────────────────────────────────────────┘
│
├── knowledge/
│   ├── dotnet-whats-new.md                # Updated per .NET preview/release (currently .NET 10)
│   ├── common-antipatterns.md             # What Claude should NEVER generate
│   ├── package-recommendations.md         # Vetted NuGet packages with rationale
│   ├── breaking-changes.md                # .NET version migration gotchas
│   └── decisions/                         # Architecture Decision Records
│       ├── 001-vsa-default.md             # Why VSA is the default
│       ├── 002-result-over-exceptions.md  # Why Result<T> over throwing
│       ├── 003-ef-core-default-orm.md     # Why EF Core, when to escape to raw SQL
│       ├── 004-hybrid-cache-default.md    # Why HybridCache over manual patterns
│       └── template.md                    # ADR template for contributors
│
│
│  ┌─────────────────────────────────────────────────────┐
│  │  ROSLYN MCP — Token-efficient codebase navigation   │
│  └─────────────────────────────────────────────────────┘
│
├── mcp/
│   └── roslyn-navigator/
│       ├── README.md                      # Setup, prerequisites, tool reference
│       ├── roslyn-navigator.csproj
│       ├── src/
│       │   ├── Program.cs                 # MCP server entry point (stdio transport)
│       │   ├── WorkspaceManager.cs        # MSBuildWorkspace lifecycle:
│       │   │                              #   - Load once on startup, keep warm
│       │   │                              #   - File watcher for incremental updates
│       │   │                              #   - Graceful loading state (return "loading..."
│       │   │                              #     instead of errors during workspace init)
│       │   │                              #   - Solution size detection: lazy-load projects
│       │   │                              #     on demand for large solutions (50+ projects)
│       │   │
│       │   └── Tools/                     # MCP tools (read-only, query operations only)
│       │       ├── FindSymbolTool.cs       # Find where a type/method/property is defined
│       │       │                           # → Returns: file path, line number, kind
│       │       │                           # → Token cost: ~30-50 tokens vs 500+ loading files
│       │       │
│       │       ├── FindReferencesTool.cs   # All usages of a symbol across the solution
│       │       │                           # → Returns: list of {file, line, context snippet}
│       │       │                           # → Token cost: ~50-150 tokens vs 2000+ scanning
│       │       │
│       │       ├── FindImplementationsTool.cs  # What implements an interface/overrides a method
│       │       │                               # → Returns: list of {type, file, line}
│       │       │                               # → Token cost: ~30-80 tokens
│       │       │
│       │       ├── GetTypeHierarchyTool.cs     # Inheritance chain + interfaces for a type
│       │       │                               # → Returns: base types, interfaces, derived types
│       │       │
│       │       ├── GetProjectGraphTool.cs      # Solution → project dependency tree
│       │       │                               # → Returns: project names, references, target frameworks
│       │       │
│       │       ├── GetPublicApiTool.cs         # Public members of a type (without full file)
│       │       │                               # → Returns: method signatures, properties, events
│       │       │                               # → Token cost: ~100 tokens vs 500+ for full file
│       │       │
│       │       └── GetDiagnosticsTool.cs       # Compiler + analyzer warnings/errors
│       │                                       # → Returns: diagnostic ID, severity, message, location
│       │                                       # → Scope: single file, project, or solution-wide
│       │
│       └── tests/
│           └── Tools/                     # Integration tests for each tool
│               └── ...
│
│
│  ┌─────────────────────────────────────────────────────┐
│  │  HOOKS — Pre/post automation                        │
│  └─────────────────────────────────────────────────────┘
│
├── hooks/
│   ├── hooks.json                         # Claude Code hook configuration
│   │                                      # - pre-commit: run dotnet format
│   │                                      # - post-file-create: dotnet restore if .csproj changed
│   │                                      # - pre-build: warn on common misconfigs
│   ├── pre-commit-format.sh
│   └── post-scaffold-restore.sh
│
│
│  ┌─────────────────────────────────────────────────────┐
│  │  CONFIG                                             │
│  └─────────────────────────────────────────────────────┘
│
├── .mcp.json                              # MCP server registration (roslyn-navigator)
├── .editorconfig                          # C# coding style for the repo itself
│
│
│  ┌─────────────────────────────────────────────────────┐
│  │  META / CI                                          │
│  └─────────────────────────────────────────────────────┘
│
└── .github/
    ├── workflows/
    │   └── validate.yml                   # CI: lint SKILL.md frontmatter, build Roslyn MCP,
    │                                      # run MCP integration tests
    └── ISSUE_TEMPLATE/
        ├── new-skill.yml                  # Structured template for skill proposals
        └── new-knowledge.yml              # Template for knowledge updates
```

---

## Component Specifications

### 1. CLAUDE.md (Root)

Purpose: Instructions for developing THIS repo itself. Not a template for users.

Contents:
- Repo purpose and philosophy
- How skills are structured (frontmatter schema, content guidelines)
- How agents reference skills
- How the Roslyn MCP server is built and tested
- Contribution workflow
- Quality standards for PRs

### 2. AGENTS.md (Root)

Purpose: Agent routing and orchestration rules that Claude Code loads automatically.

Contents:
- Central routing logic: analyze the user's query → delegate to the right agent
- Skill loading order: which skills each agent should load
- Conflict resolution: when two agents could handle a query, which wins
- Token budget awareness: agents should prefer Roslyn MCP tools over file scanning

### 3. Agents (8 total)

Each agent is a markdown file with:
- **Role definition**: What this agent is an expert in
- **Skill dependencies**: Which skills this agent loads
- **MCP tool usage**: When to use roslyn-navigator tools vs reading files
- **Response patterns**: How to structure guidance (code examples, explanations, warnings)
- **Boundaries**: What this agent does NOT handle (delegates back to router)

| Agent | Primary Skills | Roslyn MCP Usage |
|---|---|---|
| `dotnet-architect` | vertical-slice, project-structure, all knowledge/ | `GetProjectGraph` to understand solution shape |
| `api-designer` | minimal-api, api-versioning, authentication, error-handling | `GetPublicApi` to review existing endpoints |
| `ef-core-specialist` | ef-core, configuration | `FindReferences` to trace DbContext usage |
| `test-engineer` | testing | `FindImplementations` to discover testable interfaces |
| `security-auditor` | authentication, configuration | `GetDiagnostics` for security analyzer warnings |
| `performance-analyst` | modern-csharp, caching | `FindReferences` to find hot paths |
| `devops-engineer` | docker, ci-cd, aspire | `GetProjectGraph` for build dependency order |
| `code-reviewer` | All skills contextually | All MCP tools — minimizes file reads during review |

### 4. Skills (18 total)

Each skill follows the Agent Skills open standard:

```yaml
---
name: skill-name
description: >
  What this skill does and when Claude should load it.
  Include trigger keywords and specific scenarios.
---

# Skill Title

## Core Principles
[Opinionated defaults — what we recommend and WHY]

## Patterns
[Code examples with explanation]

## Anti-patterns
[What NOT to do, with examples of the wrong way]

## Decision Guide
[When to use pattern A vs B vs C]
```

**Quality bar per skill:**
- Maximum 400 lines (respect token budgets)
- Every pattern has a code example
- Every recommendation has a "why"
- Anti-patterns section is mandatory
- Links to official Microsoft docs where relevant

### 5. Templates (5 project archetypes)

Each template is a complete, ready-to-copy CLAUDE.md that:
- Declares the project type and architecture
- References which skills apply to this project
- Configures agent behavior for this context
- Sets coding conventions (naming, file organization)
- Declares the tech stack and dependencies
- Includes project-specific commands Claude should know (build, test, run)

**Template anatomy:**

```markdown
# [Project Name]

## Project Context
[What this project is, its architecture, its constraints]

## Tech Stack
[Framework version, key NuGet packages, infrastructure]

## Architecture
[VSA structure, module layout if applicable, feature folder conventions]

## Coding Standards
[Naming, file organization, patterns this project follows]

## Skills
[Which dotnet-claude-kit skills apply — Claude loads these for context]

## MCP Tools
[How Claude should use roslyn-navigator for this project]

## Commands
[dotnet build, test, run, migrate commands specific to this project]

## Anti-patterns
[Project-specific things Claude should never do]
```

### 6. Knowledge (living documents)

These are NOT skills (not loaded by the Agent Skills system). They are reference documents that agents and the CLAUDE.md templates can point to.

| Document | Update Frequency | Purpose |
|---|---|---|
| `dotnet-whats-new.md` | Per .NET preview/release | Latest features Claude should use |
| `common-antipatterns.md` | As discovered | Patterns Claude tends to generate wrong |
| `package-recommendations.md` | Quarterly | Vetted NuGet packages with rationale |
| `breaking-changes.md` | Per major .NET version | Migration pitfalls |
| `decisions/*.md` | As architectural choices are made | ADRs explaining WHY behind defaults |

### 7. Roslyn MCP Server — `roslyn-navigator`

**Goal:** Reduce token consumption by 5-10x for codebase navigation tasks.

**Principle:** Read-only, query-only. No code generation, no refactoring, no modifications.

**Architecture:**

```
┌─────────────────────────────────────┐
│  Claude Code CLI                     │
│                                     │
│  "What implements IOrderRepository?" │
│           │                          │
│           ▼                          │
│  ┌─────────────────────┐             │
│  │ MCP Protocol (stdio)│             │
│  └──────────┬──────────┘             │
└─────────────┼───────────────────────┘
              │
              ▼
┌─────────────────────────────────────┐
│  roslyn-navigator MCP Server         │
│                                     │
│  ┌───────────────────────────────┐  │
│  │ WorkspaceManager              │  │
│  │  - MSBuildWorkspace (warm)    │  │
│  │  - File watcher (incremental) │  │
│  │  - Lazy project loading       │  │
│  └───────────────┬───────────────┘  │
│                  │                   │
│  ┌───────────────▼───────────────┐  │
│  │ Tools (7 read-only)           │  │
│  │  - FindSymbol                 │  │
│  │  - FindReferences             │  │
│  │  - FindImplementations        │  │
│  │  - GetTypeHierarchy           │  │
│  │  - GetProjectGraph            │  │
│  │  - GetPublicApi               │  │
│  │  - GetDiagnostics             │  │
│  └───────────────────────────────┘  │
└─────────────────────────────────────┘
```

**Scaling strategy for all solution sizes:**

| Solution Size | Strategy |
|---|---|
| Small (1-5 projects) | Load entire workspace on startup. Fast, simple. |
| Medium (5-15 projects) | Load entire workspace on startup. File watcher for incremental. |
| Large (15-50 projects) | Lazy-load projects on first query. Cache compilations. File watcher for changed projects only. |
| Enterprise (50+) | Same as large + project-level caching to disk. Warn Claude if a query touches an unloaded project and offer to load it. |

**Key implementation decisions:**

1. **Warm workspace:** Load MSBuildWorkspace once on server start. Keep Compilation objects in memory. This avoids the 10-30s cold start per request.

2. **Incremental updates:** Use `FileSystemWatcher` to detect `.cs` / `.csproj` changes. Recompile only affected projects using Roslyn's incremental compilation.

3. **Graceful loading state:** While the workspace is loading, tools return a structured "loading" response instead of errors. Claude knows to wait or ask the user.

4. **Token-optimized responses:** Every tool returns the minimum data needed. No full file contents — just file paths, line numbers, and short context snippets (the surrounding line or method signature).

5. **Solution discovery:** The server auto-detects `.sln` / `.slnx` files in the working directory. If multiple solutions exist, it picks the one in the root or asks via a config option.

**MCP Tool Specifications:**

#### `find_symbol`
```
Input:  { "name": "OrderRepository", "kind": "type" | "method" | "property" | "any" }
Output: { "symbols": [{ "name": "OrderRepository", "kind": "class", "file": "src/Infrastructure/Persistence/OrderRepository.cs", "line": 12, "namespace": "MyApp.Infrastructure.Persistence" }] }
```

#### `find_references`
```
Input:  { "symbolName": "IOrderRepository", "file": "src/Domain/Interfaces/IOrderRepository.cs", "line": 5 }
Output: { "references": [{ "file": "...", "line": 23, "snippet": "private readonly IOrderRepository _repo;", "kind": "field_declaration" }], "count": 7 }
```

#### `find_implementations`
```
Input:  { "interfaceName": "IOrderRepository" }
Output: { "implementations": [{ "type": "OrderRepository", "file": "...", "line": 12 }, { "type": "CachedOrderRepository", "file": "...", "line": 8 }] }
```

#### `get_type_hierarchy`
```
Input:  { "typeName": "OrderRepository" }
Output: { "baseTypes": ["BaseRepository<Order>", "object"], "interfaces": ["IOrderRepository", "IDisposable"], "derivedTypes": ["CachedOrderRepository"] }
```

#### `get_project_graph`
```
Input:  {} (no params — returns full solution)
Output: { "solution": "MyApp.slnx", "projects": [{ "name": "MyApp.Api", "path": "src/MyApp.Api/MyApp.Api.csproj", "targetFramework": "net10.0", "references": ["MyApp.Domain", "MyApp.Infrastructure"] }] }
```

#### `get_public_api`
```
Input:  { "typeName": "OrderService" }
Output: { "type": "class", "members": [{ "kind": "method", "signature": "Task<Result<Order>> CreateOrderAsync(CreateOrderRequest request)", "accessibility": "public" }] }
```

#### `get_diagnostics`
```
Input:  { "scope": "file" | "project" | "solution", "path": "src/MyApp.Api/Endpoints/OrderEndpoints.cs", "severityFilter": "error" | "warning" | "all" }
Output: { "diagnostics": [{ "id": "CS8602", "severity": "warning", "message": "Dereference of a possibly null reference.", "file": "...", "line": 45 }], "count": 3 }
```

### 8. Hooks

**hooks.json:**
```json
{
  "hooks": [
    {
      "event": "pre-commit",
      "command": "dotnet format --verify-no-changes --verbosity quiet",
      "description": "Ensure code is formatted before commit"
    },
    {
      "event": "post-file-edit",
      "pattern": "*.csproj",
      "command": "dotnet restore",
      "description": "Auto-restore after project file changes"
    }
  ]
}
```

### 9. .mcp.json

```json
{
  "mcpServers": {
    "roslyn-navigator": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "./mcp/roslyn-navigator/roslyn-navigator.csproj", "--", "--solution", "${workspaceFolder}"],
      "description": "Token-efficient .NET codebase navigation via Roslyn semantic analysis"
    }
  }
}
```

---

## Installation & Usage

### For users (dropping into their own project)

```bash
# 1. Install as a Claude Code plugin
claude plugin add codewithmukesh/dotnet-claude-kit

# 2. Copy a CLAUDE.md template into your project
#    (pick the one that matches your project type)
cp templates/web-api/CLAUDE.md ./CLAUDE.md

# 3. Edit the CLAUDE.md to set your project name, stack, and conventions

# 4. The Roslyn MCP server starts automatically via .mcp.json
#    when Claude Code opens your project
```

### For contributors

```bash
git clone https://github.com/codewithmukesh/dotnet-claude-kit.git
cd dotnet-claude-kit

# Build the Roslyn MCP server
dotnet build mcp/roslyn-navigator/

# Run validation
dotnet test mcp/roslyn-navigator/tests/

# CI validates all SKILL.md frontmatter + MCP server builds
```

---

## What Makes This Different

| Concern | dotnet-artisan | dotnet-skills | **dotnet-claude-kit** |
|---|---|---|---|
| **Philosophy** | Encyclopedia (130 skills, cover everything) | Akka.NET focused (30 skills) | **Opinionated playbook (18 skills, VSA default)** |
| **Project templates** | None | None | **5 ready-to-copy CLAUDE.md templates** |
| **Token efficiency** | No MCP, relies on file scanning | No MCP | **Roslyn MCP for 5-10x reduction** |
| **Architecture stance** | All patterns as equals | Akka.NET patterns | **VSA as opinionated default** |
| **Handler approach** | Prescriptive | N/A | **User chooses: MediatR / Wolverine / raw** |
| **Living knowledge** | Static skills only | Static skills only | **knowledge/ directory updated per .NET release** |
| **Hooks** | Has hooks | None | **Pre-commit format + post-scaffold restore** |
| **Decision records** | None | None | **ADRs explaining every opinionated default** |
| **Quality bar** | Breadth (130 skills, 2 contributors, 477 commits) | Depth in Akka.NET domain | **Depth across 18 core skills, max 400 lines each** |

---

## Launch Roadmap

### v0.1.0 — Foundation
- [ ] Repo structure, README, LICENSE, CONTRIBUTING
- [ ] CLAUDE.md (root)
- [ ] AGENTS.md (root)
- [ ] 5 core skills: modern-csharp, vertical-slice, minimal-api, ef-core, testing
- [ ] 1 template: web-api/CLAUDE.md
- [ ] knowledge/dotnet-whats-new.md
- [ ] knowledge/common-antipatterns.md
- [ ] .editorconfig

### v0.2.0 — Agents + More Skills
- [ ] 8 agents (all)
- [ ] Remaining 13 skills
- [ ] 4 remaining templates
- [ ] knowledge/package-recommendations.md
- [ ] knowledge/decisions/ (all 4 ADRs + template)

### v0.3.0 — Roslyn MCP
- [ ] roslyn-navigator MCP server (all 7 tools)
- [ ] WorkspaceManager with warm loading + file watcher
- [ ] Lazy project loading for large solutions
- [ ] Integration tests
- [ ] .mcp.json
- [ ] hooks/hooks.json + hook scripts

### v0.4.0 — Polish & Community
- [ ] CI validation workflow
- [ ] Issue templates
- [ ] CONTRIBUTING guide finalized
- [ ] Blog post / YouTube walkthrough
- [ ] Community feedback → iterate

---

## Open Questions for Future Versions

1. **Claude Code plugin marketplace** — When this stabilizes, should dotnet-claude-kit be published as a single installable plugin?
2. **Slash commands** — Should v1.1 add `.claude/commands/` for `/scaffold-feature`, `/review-pr`, etc.?
3. **GitHub Actions integration** — Should v1.1 add a workflow for Claude-powered PR reviews?
4. **Additional MCP tools** — Should roslyn-navigator expand to include `RenameSymbol`, `ExtractInterface`, or stay read-only?
5. **Blazor/MAUI-specific agents** — Are the 5 templates enough, or do Blazor/MAUI need dedicated agents?
