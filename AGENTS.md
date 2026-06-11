# Agent Routing & Orchestration

> This file defines how Claude Code routes queries to specialist agents and how agents coordinate.

## Agent Roster

| Agent | File | Primary Domain |
|-------|------|---------------|
| dotnet-architect | `agents/dotnet-architect.md` | Architecture, project structure, module boundaries |
| api-designer | `agents/api-designer.md` | Minimal APIs, OpenAPI, versioning, rate limiting |
| ef-core-specialist | `agents/ef-core-specialist.md` | Database, queries, migrations, EF Core patterns |
| test-engineer | `agents/test-engineer.md` | Test strategy, xUnit, WebApplicationFactory, Testcontainers |
| security-auditor | `agents/security-auditor.md` | Authentication, authorization, OWASP, secrets |
| performance-analyst | `agents/performance-analyst.md` | Benchmarks, memory, async patterns, caching |
| devops-engineer | `agents/devops-engineer.md` | Docker, CI/CD, Aspire, deployment |
| code-reviewer | `agents/code-reviewer.md` | Multi-dimensional code review |
| build-error-resolver | `agents/build-error-resolver.md` | Autonomous build error fixing |
| refactor-cleaner | `agents/refactor-cleaner.md` | Systematic dead code removal and cleanup |

## Routing Table

Match user intent to agent. When multiple agents could handle a query, the first match wins.

| User Intent Pattern | Primary Agent | Support Agent |
|---|---|---|
| "set up project", "folder structure", "architecture" | dotnet-architect | — |
| "add module", "split into modules", "bounded context" | dotnet-architect | — |
| "create endpoint", "API route", "OpenAPI", "swagger" | api-designer | — |
| "versioning", "rate limiting", "CORS" | api-designer | — |
| "database", "migration", "query", "DbContext", "EF" | ef-core-specialist | — |
| "write tests", "test strategy", "coverage" | test-engineer | — |
| "WebApplicationFactory", "Testcontainers", "xUnit" | test-engineer | — |
| "security", "authentication", "JWT", "OIDC", "authorize" | security-auditor | — |
| "performance", "benchmark", "memory", "profiling" | performance-analyst | — |
| "caching", "HybridCache", "output cache" | performance-analyst | — |
| "Docker", "container", "CI/CD", "pipeline", "deploy" | devops-engineer | — |
| "Aspire", "orchestration", "service discovery" | devops-engineer | — |
| "review this code", "PR review", "code quality" | code-reviewer | — |
| "choose architecture", "which architecture", "architecture decision" | dotnet-architect | — |
| "scaffold feature", "create feature", "add endpoint", "generate feature" | dotnet-architect | api-designer, ef-core-specialist |
| "init project", "setup project", "new project", "generate CLAUDE.md" | dotnet-architect | — |
| "health check", "analyze project", "project report" | code-reviewer | dotnet-architect |
| "review PR", "review changes", "code review", "PR review" | code-reviewer | — |
| "add migration", "ef migration", "update packages", "upgrade nuget" | ef-core-specialist | — |
| "conventions", "coding style", "detect patterns", "code consistency" | code-reviewer | — |
| "add feature" (architecture-appropriate) | dotnet-architect | api-designer, ef-core-specialist |
| "refactor" | code-reviewer | dotnet-architect |
| "build errors", "fix build", "won't compile" | build-error-resolver | — |
| "clean up", "dead code", "unused code", "de-sloppify" | refactor-cleaner | — |

## Skill Loading Order

Agents load skills in dependency order. Core skills load first.

### Default Load Order (All Agents)
1. `modern-csharp` — Always loaded, baseline C# knowledge
2. Agent-specific skills (see agent files)

### Per-Agent Skill Maps

| Agent | Skills |
|-------|--------|
| dotnet-architect | modern-csharp, architecture-advisor, project-structure, scaffold, project-setup + conditional: vertical-slice, clean-architecture, ddd |
| api-designer | modern-csharp, minimal-api, api-versioning, authentication, error-handling |
| ef-core-specialist | modern-csharp, ef-core, configuration, migrate |
| test-engineer | modern-csharp, testing |
| security-auditor | modern-csharp, authentication, configuration |
| performance-analyst | modern-csharp, caching |
| devops-engineer | modern-csharp, docker, ci-cd, aspire |
| code-reviewer | modern-csharp, code-review, convention-learner + contextual (loads relevant skills incl. clean-architecture, ddd based on files under review) |
| build-error-resolver | modern-csharp, build-fix + contextual: ef-core, dependency-injection |
| refactor-cleaner | modern-csharp, de-sloppify + contextual: testing, ef-core |

## MCP Tool Preferences

Agents should **prefer Roslyn MCP tools over file scanning** to reduce token consumption.

| Task | Use MCP Tool | Instead Of |
|------|-------------|-----------|
| Find where a type is defined | `find_symbol` | Grep/Glob across all .cs files |
| Find all usages of a type | `find_references` | Grep for the type name |
| Find implementations of an interface | `find_implementations` | Searching for `: IInterface` |
| Understand inheritance | `get_type_hierarchy` | Reading multiple files |
| Understand project dependencies | `get_project_graph` | Parsing .csproj files manually |
| Review a type's API surface | `get_public_api` | Reading the full source file |
| Check for compilation errors | `get_diagnostics` | Running `dotnet build` and parsing output |
| Find unused code for cleanup | `find_dead_code` | Manual inspection of all files |
| Check for circular dependencies | `detect_circular_dependencies` | Manually tracing project references |
| Understand method call chains | `get_dependency_graph` | Reading multiple files and tracing calls |
| Check which types have tests | `get_test_coverage_map` | Manually searching for test files |

## Cross-Agent Meta Skills

These meta and productivity skills are not tied to a specific agent — any agent can load them when the context calls for it:

| Skill | When to Load |
|-------|-------------|
| `instinct-system` | After ANY user correction, pattern detection across sessions, logging non-obvious discoveries; includes status/export/import modes |
| `wrap-up` | Session start (load handoff) and session end (write handoff to `.claude/handoff.md`, capture learnings) |
| `checkpoint` | Mid-session save before risky changes or task switches — commit + brief handoff |
| `workflow-mastery` | Context running low, large codebase navigation, parallel workflows, subagent strategy |
| `convention-learner` | Detect and enforce project-specific conventions in new code |

Model selection guidance lives in the always-loaded `.claude/rules/agents.md` — no skill load needed.

### Meta Skill Routing

| User Intent Pattern | Skill |
|---|---|
| "learn from mistakes", "remember this", "log this", "gotcha", "show instincts", "what have you learned" | instinct-system |
| "wrap up", "done for today", "handoff", "start session", "load handoff" | wrap-up |
| "save progress", "checkpoint", "pause here" | checkpoint |
| "context", "running out of tokens", "too many files" | workflow-mastery |
| "review this", "what should I review", "blast radius" | code-review |
| "fix build loop", "keep fixing", "auto-fix" | build-fix |

## Slash Commands

Commands map to skills and agents. Use these as shortcuts for common workflows.

Each workflow skill registers its own slash command and carries its methodology inline (the kit no longer splits workflows from their knowledge twins).

| Command | Supporting Skills | Primary Agent | Purpose |
|---------|------------------|---------------|---------|
| `/dotnet-init` | project-setup | dotnet-architect | Interactive project initialization |
| `/spec` | — | — | Relentless questioning → agreed spec in `docs/specs/` |
| `/plan` | architecture-advisor | dotnet-architect | Architecture-aware planning (consumes approved specs) |
| `/verify` | — | — | 7-phase verification pipeline |
| `/tdd` | testing | test-engineer | Red-green-refactor workflow |
| `/scaffold` | — | dotnet-architect | Architecture-aware feature scaffolding |
| `/code-review` | convention-learner | code-reviewer | MCP-powered, blast-radius-prioritized code review |
| `/build-fix` | — | build-error-resolver | Bounded build-fix and test-fix loops |
| `/checkpoint` | — | — | Mid-session save (commit + handoff) |
| `/security-scan` | — | security-auditor | OWASP + secrets + dependency audit |
| `/migrate` | ef-core | ef-core-specialist | EF Core schema, .NET version, and NuGet migrations |
| `/health-check` | — | code-reviewer | Graded project health report |
| `/de-sloppify` | — | refactor-cleaner | Systematic code cleanup |
| `/wrap-up` | instinct-system | — | Session handoff lifecycle (end + start) |

Instinct operations (status, export, import) are modes of the `instinct-system` skill — say "show instincts", "export instincts", or "import instincts".

## Conflict Resolution

When two agents could handle a query:

1. **Architecture questions win over implementation** — "How should I structure the payment module?" → dotnet-architect, even though api-designer could handle the endpoint part
2. **Specific beats general** — "How do I optimize this EF query?" → ef-core-specialist, not performance-analyst
3. **Security concerns are always surfaced** — Even when another agent is primary, flag security issues for the security-auditor
4. **Code review is holistic** — The code-reviewer loads skills contextually based on what's in the PR

## Token Budget Guidance

For detailed context management strategies, see the **`workflow-mastery`** skill (Context Discipline section).

- **Small queries** (single pattern/fix): Load 1-2 skills, use MCP tools for context
- **Medium queries** (feature implementation): Load 3-4 skills, use MCP tools to understand existing code
- **Large queries** (architecture review): Load all relevant skills, use `get_project_graph` first to understand the solution shape

## Response Patterns

All agents should:
1. **Start with the recommended approach** — Don't enumerate all options equally
2. **Show code first, explain after** — Developers prefer seeing the solution, then understanding why
3. **Flag anti-patterns proactively** — If the user's existing code has issues, mention them
4. **Reference skills** — Point to relevant skills for deeper reading
5. **Use MCP tools before reading files** — Reduce token consumption
