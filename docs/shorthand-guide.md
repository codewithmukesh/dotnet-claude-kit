# dotnet-claude-kit Quick Reference

A compact reference for all components: commands, skills, agents, rules, hooks, and MCP tools.

---

## Commands

| Command | Description | Related Skill / Agent |
|---------|-------------|----------------------|
| `/plan` | Enter plan mode with architecture awareness | workflow-mastery / dotnet-architect |
| `/scaffold` | Architecture-aware feature scaffolding | scaffolding / dotnet-architect |
| `/build-fix` | Autonomous build-fix loop | verification-loop / build-error-resolver |
| `/verify` | 7-phase verification pipeline | verification-loop / code-reviewer |
| `/tdd` | Guided test-driven development workflow | testing / test-engineer |
| `/code-review` | MCP-powered multi-dimensional code review | code-review-workflow / code-reviewer |
| `/health-check` | Project health assessment with letter grades | health-check / code-reviewer |
| `/security-scan` | Deep security audit (OWASP Top 10, secrets, packages) | security-scan / security-auditor |
| `/migrate` | Guided EF Core migration workflow | migration-workflow / ef-core-specialist |
| `/de-sloppify` | Systematic code cleanup pass | de-sloppify / refactor-cleaner |
| `/checkpoint` | Save progress with commit and handoff note | wrap-up-ritual / -- |
| `/wrap-up` | End-of-session wrap-up ritual | wrap-up-ritual / -- |
| `/instinct-status` | Display learned instincts with confidence scores | instinct-system / -- |
| `/instinct-export` | Export instincts to shareable format | instinct-system / -- |
| `/instinct-import` | Import instincts from another project | instinct-system / -- |

---

## Skills (47 total)

### .NET Domain (29)

| Category | Skills |
|----------|--------|
| Architecture | architecture-advisor, clean-architecture, ddd, vertical-slice, project-structure |
| API | minimal-api, api-versioning, openapi, scalar, authentication, error-handling |
| Data | ef-core, configuration |
| Infrastructure | docker, container-publish, ci-cd, aspire, dependency-injection |
| Observability | logging, serilog, opentelemetry |
| Resilience & Performance | caching, resilience, httpclient-factory |
| Messaging | messaging |
| Language | modern-csharp |
| Project | project-setup, scaffolding, testing |

### Workflow (5)

| Skill | Purpose |
|-------|---------|
| code-review-workflow | Structured multi-dimensional review process |
| convention-learner | Detect and codify project conventions |
| migration-workflow | Safe EF Core migration with review and rollback |
| verification-loop | Build-test-verify feedback loop |
| workflow-mastery | Advanced Claude Code workflow patterns |

### Meta & Productivity (7)

| Skill | Purpose |
|-------|---------|
| 80-20-review | Focus review effort on highest-risk code |
| context-discipline | Manage context window and token budget |
| learning-log | Document non-obvious discoveries |
| model-selection | Choose the right model (Opus/Sonnet/Haiku) per task |
| self-correction-loop | Capture corrections and prevent recurrence |
| split-memory | Split large CLAUDE.md across organized files |
| wrap-up-ritual | Session handoff with progress and pending tasks |

### Autonomous (6 -- no SKILL.md, used internally)

autonomous-loops, de-sloppify, health-check, instinct-system, security-scan, session-management

---

## Agents (10)

| Agent | Triggers | Primary Skills |
|-------|----------|---------------|
| dotnet-architect | "architecture", "project structure", "set up project", "add module" | architecture-advisor, project-structure, scaffolding, project-setup |
| api-designer | "create endpoint", "API route", "OpenAPI", "versioning" | minimal-api, api-versioning, authentication, error-handling |
| ef-core-specialist | "database", "migration", "query", "DbContext", "EF" | ef-core, configuration, migration-workflow |
| test-engineer | "write tests", "test strategy", "WebApplicationFactory", "Testcontainers" | testing |
| security-auditor | "security", "authentication", "JWT", "OIDC", "authorize" | authentication, configuration |
| performance-analyst | "performance", "benchmark", "caching", "HybridCache" | caching |
| devops-engineer | "Docker", "CI/CD", "pipeline", "Aspire", "deploy" | docker, ci-cd, aspire |
| code-reviewer | "review this code", "PR review", "code quality", "conventions" | code-review-workflow, convention-learner |
| build-error-resolver | Build failures, compilation errors | modern-csharp, autonomous-loops |
| refactor-cleaner | "clean up", "dead code", "refactor", "remove unused" | modern-csharp |

---

## Rules (10)

| Rule File | Scope | Key Enforcement |
|-----------|-------|----------------|
| `coding-style.md` | All C# files | File-scoped namespaces, primary constructors, sealed, records, collection expressions |
| `architecture.md` | Solution structure | No repo over EF, feature folders, dependency direction, shared kernel contracts only |
| `security.md` | All code | No hardcoded secrets, parameterized queries, explicit auth, HTTPS, no PII in logs |
| `testing.md` | Test projects | Integration-first, Testcontainers, AAA, behavior over implementation, no InMemory DB |
| `performance.md` | All code | CancellationToken, TimeProvider, IHttpClientFactory, HybridCache, async all the way |
| `error-handling.md` | All code | Result pattern, ProblemDetails, no broad catch, IExceptionHandler, boundary validation |
| `git-workflow.md` | Git operations | Conventional commits, atomic commits, branch naming, never force-push main |
| `agents.md` | Agent interactions | MCP-first, subagent routing, skill loading, model selection |
| `hooks.md` | Hook responses | Auto-accept format, never skip pre-commit, review post-test analysis |
| `packages.md` | NuGet packages | Always latest stable, never hardcode from training data, `dotnet add` without --version |

All rules have `alwaysApply: true` -- they are enforced on every interaction.

---

## Hooks (7)

| Hook Script | Event | Behavior |
|-------------|-------|----------|
| `pre-bash-guard.sh` | PreToolUse (Bash) | Guards against dangerous bash commands |
| `post-edit-format.sh` | PostToolUse (Edit/Write) | Runs `dotnet format` on modified files |
| `post-scaffold-restore.sh` | PostToolUse (Edit/Write) | Runs `dotnet restore` after .csproj changes |
| `pre-commit-format.sh` | Pre-commit | Verifies formatting before commit |
| `pre-commit-antipattern.sh` | Pre-commit | Scans for antipatterns before commit |
| `pre-build-validate.sh` | Pre-build | Validates project structure before build |
| `post-test-analyze.sh` | Post-test | Analyzes test results for insights |

---

## MCP Tools (15)

| Tool | Category | Purpose |
|------|----------|---------|
| `find_symbol` | Navigation | Locate type/method/property definitions |
| `find_references` | Navigation | Find all usages of a symbol |
| `find_implementations` | Navigation | Types implementing an interface or base class |
| `find_callers` | Navigation | Methods calling a specific method |
| `find_overrides` | Navigation | Overrides of virtual/abstract methods |
| `find_dead_code` | Analysis | Unused types, methods, properties |
| `get_symbol_detail` | Inspection | Full signature, parameters, XML docs |
| `get_public_api` | Inspection | Public members without reading the file |
| `get_type_hierarchy` | Inspection | Inheritance chain and derived types |
| `get_project_graph` | Structure | Solution dependency tree |
| `get_dependency_graph` | Structure | Recursive call graph for a method |
| `get_diagnostics` | Quality | Compiler errors and analyzer warnings |
| `get_test_coverage_map` | Quality | Heuristic test coverage by naming |
| `detect_antipatterns` | Quality | .NET anti-patterns via Roslyn |
| `detect_circular_dependencies` | Quality | Cycles in project or type deps |

---

## Cross-Reference: Command to Skill to Agent

| Command | Primary Skill(s) | Primary Agent | Support Agent(s) |
|---------|-----------------|---------------|-------------------|
| `/plan` | workflow-mastery | dotnet-architect | -- |
| `/scaffold` | scaffolding, project-setup | dotnet-architect | api-designer, ef-core-specialist |
| `/build-fix` | verification-loop | build-error-resolver | -- |
| `/verify` | verification-loop | code-reviewer | dotnet-architect |
| `/tdd` | testing | test-engineer | -- |
| `/code-review` | code-review-workflow, 80-20-review | code-reviewer | -- |
| `/health-check` | health-check | code-reviewer | dotnet-architect |
| `/security-scan` | security-scan, authentication | security-auditor | -- |
| `/migrate` | migration-workflow, ef-core | ef-core-specialist | -- |
| `/de-sloppify` | de-sloppify | refactor-cleaner | code-reviewer |
| `/checkpoint` | wrap-up-ritual | -- | -- |
| `/wrap-up` | wrap-up-ritual | -- | -- |
| `/instinct-status` | instinct-system | -- | -- |
| `/instinct-export` | instinct-system | -- | -- |
| `/instinct-import` | instinct-system | -- | -- |
