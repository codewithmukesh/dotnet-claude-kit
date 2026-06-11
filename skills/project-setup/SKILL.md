---
name: project-setup
description: >
  Interactive project setup, health check, and migration workflows.
  Guides developers through project initialization with customized CLAUDE.md generation,
  codebase health analysis using MCP tools, and .NET version migration.
  Load when: "init project", "setup project", "new project", "health check",
  "analyze project", "project report", "migrate", "upgrade dotnet",
  "upgrade .NET", "generate CLAUDE.md".
---

# Project Setup & Workflows

## Core Principles

1. **Interactive over passive** — Don't dump a generic template. Ask questions, gather context, then generate a customized result tailored to the specific project.
2. **MCP-driven analysis** — Use Roslyn MCP tools for health checks and migration analysis instead of reading files manually. Token-efficient and semantically accurate.
3. **Generate, don't template** — CLAUDE.md files should be fully populated with specific choices (not `[PLACEHOLDER]` values). Every section should reflect the actual project decisions.
4. **Architecture-first** — Every workflow starts by understanding or selecting the project's architecture. Architecture drives folder structure, naming, patterns, and test organization.
5. **Verify after action** — After any workflow completes (init, migration, health check), verify the result. Run builds, tests, or health checks to confirm success.

## Patterns

### Project Init Workflow

Interactive conversational flow for new projects. Execute steps in order, waiting for user input at each decision point.

**Step 1: Project Identity**
Ask:
- Project name (used for solution, namespaces, CLAUDE.md)
- Project type: API, Blazor, Worker Service, Class Library, or Modular Monolith

**Step 2: Architecture Selection**
Delegate to the `architecture-advisor` skill:
- Run the full questionnaire (15+ questions across 6 categories)
- Recommend VSA, Clean Architecture, DDD + CA, or Modular Monolith
- Explain the rationale for the recommendation

**Step 3: Tech Stack Selection**
Ask about each dimension with a recommended default:

| Dimension | Options | Default |
|-----------|---------|---------|
| Database | PostgreSQL, SQL Server, SQLite | PostgreSQL |
| Auth | JWT Bearer, OIDC (Keycloak/Auth0), None | JWT Bearer |
| Caching | HybridCache, Redis, None | HybridCache |
| Messaging | Wolverine (RabbitMQ), MassTransit, None | None (add later) |
| Observability | Serilog + OpenTelemetry, Basic logging | Serilog + OTEL |
| Resilience | Polly v8 pipelines, Basic retry | Polly v8 |

**Step 4: Generate CLAUDE.md**
Generate a customized CLAUDE.md with all choices baked in:

```markdown
# [ProjectName] — Development Instructions

## Architecture
This project uses [Selected Architecture].
[Architecture-specific conventions and rules]

## Tech Stack
- **Runtime**: .NET 10 / C# 14
- **Database**: [Selected] with EF Core 10
- **Auth**: [Selected]
- **Caching**: [Selected]
- **Observability**: [Selected]

## Conventions
[Architecture-specific patterns, naming, folder structure]

## Skills
[List of relevant skills to load based on choices]
```

**Step 5: Next Steps**
Suggest:
- Initial project structure with `dotnet new` commands
- Directory.Build.props with common settings
- First feature scaffold to validate the architecture

### Health Check Workflow

Automated codebase analysis that produces a graded report card. Run this when asked to "check health", "analyze the project", or "how's the codebase".

**Step 1: Solution Analysis**
```
→ get_project_graph
  Analyze: project count, dependency direction, target frameworks, naming consistency
```

**Step 2: Anti-pattern Scan**
```
→ detect_antipatterns (scope: solution)
  Count and categorize: async void, sync-over-async, DateTime.Now, new HttpClient(), etc.
```

**Step 3: Compiler Diagnostics**
```
→ get_diagnostics (severity: warning, scope: solution)
  Count warnings by category: CS8600 (nullability), CS0219 (unused vars), etc.
```

**Step 4: Dead Code Detection**
```
→ find_dead_code (scope: solution)
  Identify unused types, methods, and properties that can be removed.
```

**Step 5: Test Coverage Assessment**
```
→ get_test_coverage_map
  Check: test project exists, percentage of types with corresponding tests.
```

**Step 6: Report Card**

Generate a structured report:

```
## Codebase Health Report

### Grade: B+ (82/100)

| Category | Score | Issues |
|----------|-------|--------|
| Architecture | 18/20 | Clean dependency direction, 1 questionable reference |
| Anti-patterns | 14/20 | 3 DateTime.Now usages, 1 async void |
| Diagnostics | 20/20 | 0 warnings |
| Dead Code | 16/20 | 4 unused methods found |
| Test Coverage | 14/20 | 70% of types have test coverage |

### Priority Actions
1. **Replace DateTime.Now with TimeProvider** (3 locations) — error-handling skill
2. **Fix async void** in EventService.OnMessage — critical, exceptions will be unobserved
3. **Remove dead code** — 4 unused methods in OrderService, PaymentHelper
4. **Add tests** for ShippingService, NotificationService
```

Grading scale:
- **A (90-100)**: Production-ready, well-maintained
- **B (75-89)**: Good shape, minor improvements needed
- **C (60-74)**: Needs attention, several areas to improve
- **D (40-59)**: Significant issues, prioritize cleanup
- **F (<40)**: Critical problems, stop feature work and fix

### Migration Workflow

> For complete migration workflows (EF Core, NuGet, .NET version upgrades), see the **migrate** skill.

## Anti-patterns

### Skipping Architecture Questionnaire

```
# BAD — Generating CLAUDE.md without asking about the project
"Here's your CLAUDE.md with VSA architecture..."
```

```
# GOOD — Running the full questionnaire first
"Let me understand your project first. What's the domain complexity? How many developers?
How important is independent deployability? ..."
→ Based on answers: "I recommend Clean Architecture because..."
```

### Generic CLAUDE.md with Placeholders

```markdown
<!-- BAD — User has to fill in everything manually -->
## Architecture
This project uses [ARCHITECTURE].
Database: [DATABASE]
Auth: [AUTH_METHOD]
```

```markdown
<!-- GOOD — Fully populated from the conversation -->
## Architecture
This project uses Vertical Slice Architecture.
Database: PostgreSQL with EF Core 10
Auth: JWT Bearer with ASP.NET Core Identity
```

### Health Check Without MCP Tools

```
# BAD — Reading random files and guessing at quality
"I read Program.cs and it looks fine..."
```

```
# GOOD — Systematic analysis with MCP tools
→ get_project_graph: 5 projects, clean dependency direction
→ detect_antipatterns: 3 violations (2 warning, 1 error)
→ get_diagnostics: 7 warnings (5 CS8600, 2 CS0219)
→ find_dead_code: 4 unused symbols
→ get_test_coverage_map: 65% coverage
Grade: B (78/100)
```

### Running Migration Without a Plan

```bash
# BAD — Just changing the TFM and hoping for the best
sed -i 's/net8.0/net10.0/g' **/*.csproj
dotnet build  # 47 errors
```

```bash
# GOOD — Systematic migration with verification at each step
# Phase 1: Update framework → build → fix
# Phase 2: Update packages → build → fix
# Phase 3: Adopt new features → build → test
# Phase 4: Full verification
```

## Decision Guide

| Scenario | Workflow | Key Tool |
|----------|----------|----------|
| New greenfield project | Project Init | architecture-advisor skill |
| Joining existing project | Health Check → Init (for CLAUDE.md) | get_project_graph |
| "How's our codebase?" | Health Check | detect_antipatterns, get_diagnostics |
| "Upgrade to .NET 10" | Migration | get_project_graph, breaking-changes.md |
| "Generate CLAUDE.md for this project" | Project Init (skip new project steps) | get_project_graph |
| Code quality declining | Health Check → set baseline → periodic re-check | All MCP tools |
| Onboarding new developers | Health Check + Init (generates CLAUDE.md documenting conventions) | convention-learner skill |
