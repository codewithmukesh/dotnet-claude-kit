# Codex Agent Configuration

This project uses [dotnet-claude-kit](https://github.com/codewithmukesh/dotnet-claude-kit) for .NET development intelligence.

## Available Agents

| Agent | File | When to Use |
|-------|------|-------------|
| dotnet-architect | `agents/dotnet-architect.md` | Architecture decisions, project structure, module boundaries, feature scaffolding |
| api-designer | `agents/api-designer.md` | Minimal API design, OpenAPI specs, versioning, rate limiting, CORS |
| ef-core-specialist | `agents/ef-core-specialist.md` | Database design, EF Core queries, migrations, DbContext configuration |
| test-engineer | `agents/test-engineer.md` | Test strategy, xUnit v3, WebApplicationFactory, Testcontainers |
| security-auditor | `agents/security-auditor.md` | Auth systems, OWASP compliance, secrets management, vulnerability review |
| performance-analyst | `agents/performance-analyst.md` | Profiling, benchmarks, caching strategy, async pattern optimization |
| devops-engineer | `agents/devops-engineer.md` | Docker, CI/CD pipelines, .NET Aspire orchestration, deployment |
| code-reviewer | `agents/code-reviewer.md` | Multi-dimensional code review, PR review, quality gatekeeper |
| build-error-resolver | `agents/build-error-resolver.md` | Autonomous build error fixing, iterative compilation repair |
| refactor-cleaner | `agents/refactor-cleaner.md` | Dead code removal, systematic cleanup, safe refactoring |

## Skills

Skills live in `skills/<skill-name>/SKILL.md` and follow the Agent Skills open standard.

### .NET Domain Skills
api-versioning, architecture-advisor, aspire, authentication, caching, ci-cd, clean-architecture, configuration, ddd, dependency-injection, docker, ef-core, error-handling, httpclient-factory, logging, messaging, minimal-api, modern-csharp, openapi, opentelemetry, project-setup, project-structure, resilience, scalar, serilog, testing, vertical-slice, container-publish

### Workflow Skills (each registers a slash command and carries its methodology inline)
build-fix, checkpoint, code-review, de-sloppify, dotnet-init, health-check, migrate, plan, scaffold, security-scan, tdd, verify, wrap-up

### Workflow & Learning Skills
convention-learner, workflow-mastery, instinct-system

## MCP Tools

The `cwm-roslyn-navigator` MCP server provides Roslyn-powered code intelligence:

| Tool | Purpose |
|------|---------|
| `find_symbol` | Locate where a type, method, or property is defined |
| `find_references` | Find all usages of a symbol across the solution |
| `find_implementations` | Find types implementing an interface or deriving from a base class |
| `find_callers` | Find all methods calling a specific method |
| `find_overrides` | Find overrides of virtual/abstract methods |
| `find_dead_code` | Identify unused types, methods, and properties |
| `get_symbol_detail` | Get full signature, parameters, return type, XML docs |
| `get_public_api` | Get public members of a type without reading the file |
| `get_type_hierarchy` | Get inheritance chain, interfaces, and derived types |
| `get_project_graph` | Get solution dependency tree with frameworks and references |
| `get_dependency_graph` | Get recursive call graph for a method |
| `get_diagnostics` | Get compiler and analyzer diagnostics (errors, warnings) |
| `get_test_coverage_map` | Heuristic test coverage by naming convention |
| `detect_antipatterns` | Find .NET anti-patterns via Roslyn analysis |
| `detect_circular_dependencies` | Find cycles in project or type dependencies |

Always prefer MCP tools over reading full source files to conserve context window.

## Rules

Always-applied coding conventions live in `rules/dotnet/`:

- `coding-style.md` -- C# 14 style, naming, file organization
- `architecture.md` -- Dependency direction, feature folders, data access
- `security.md` -- Secrets, input validation, auth, transport security
- `testing.md` -- Integration-first, xUnit v3, Testcontainers, AAA
- `performance.md` -- CancellationToken, TimeProvider, caching, async
- `error-handling.md` -- Result pattern, ProblemDetails, exception boundaries
- `git-workflow.md` -- Conventional commits, atomic commits, branch safety
- `agents.md` -- MCP-first tools, subagent routing, skill loading
- `hooks.md` -- Format hooks, pre-commit, post-test analysis
