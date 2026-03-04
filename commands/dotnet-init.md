---
description: >
  Interactive project initialization. Detects project type, asks architecture
  questions, and generates a customized CLAUDE.md — no manual template copying.
---

# /dotnet-init

## What

Interactively initializes a .NET project for use with dotnet-claude-kit. Detects the project type, asks targeted questions about architecture and tech stack, then generates a fully customized `CLAUDE.md` in the project root.

**No manual template copying required.**

## When

- Starting a new .NET project with Claude Code
- Adding dotnet-claude-kit to an existing project
- "init project", "setup project", "generate CLAUDE.md", "configure for dotnet-claude-kit"

## How

### Step 1: Detect or Ask Project Type

Analyze the current directory to determine if this is an existing or greenfield project:

```
→ Look for .slnx / .sln files
→ If found, scan .csproj files for SDK type:
  - Microsoft.NET.Sdk.Web → web-api or blazor-app
  - Microsoft.NET.Sdk.Worker → worker-service
  - Microsoft.NET.Sdk → class-library
  - Multiple projects with inter-module references → modular-monolith
  - If ambiguous, ask the user

→ If NO solution/project found (greenfield):
  - Ask: "What are you building?"
    - REST API / microservice → web-api
    - Blazor application → blazor-app
    - Background worker / queue processor → worker-service
    - NuGet package / shared library → class-library
    - Multi-module system → modular-monolith
  - Ask: "Project name?"
  - Scaffold the solution structure:
    - dotnet new sln -n ProjectName
    - dotnet new web/worker/classlib as appropriate
    - Set up Directory.Build.props with .NET 10 defaults
    - Create src/ and tests/ folder structure
```

### Step 2: Architecture Questionnaire

Load the `architecture-advisor` skill and ask targeted questions:

1. **Domain complexity** — CRUD-heavy, moderate business rules, or rich domain?
2. **Team size** — Solo, small team, or large team?
3. **Module boundaries** — Single deployable or multiple bounded contexts?
4. **Existing patterns** — (existing projects only) Detect conventions via `convention-learner` skill

→ Recommend: VSA, Clean Architecture, DDD, or Modular Monolith with rationale.

### Step 3: Tech Stack Selection

Ask about specific technology choices:

- **Database**: PostgreSQL, SQL Server, SQLite, or none yet
- **Auth**: JWT, OIDC, Cookie, or none yet
- **Caching**: HybridCache, Redis, or none yet
- **Messaging**: MassTransit, or none yet
- **Testing**: Confirm xUnit + Testcontainers defaults

### Step 4: Generate CLAUDE.md

Load the appropriate template from `templates/` as a base, then customize:

- Replace `[ProjectName]` with the actual project/solution name
- Set the chosen architecture
- Configure the tech stack section
- For existing projects: add project-specific conventions detected in Step 2
- For greenfield: add recommended conventions based on architecture choice
- Write to `./CLAUDE.md`

### Step 5: Verify Setup

```
→ Confirm .mcp.json exists or create it (for Roslyn MCP server)
→ For existing projects:
  - Verify the solution file is discoverable
  - Run a quick health check to establish baseline
  - Report: "Project initialized. Run /health-check to see your baseline."
→ For greenfield projects:
  - Verify dotnet build succeeds on the scaffolded solution
  - Report: "Project created. Run /scaffold to add your first feature."
```

**Skills invoked:** `project-setup`, `architecture-advisor`, `convention-learner`
**Agent:** `dotnet-architect`

## Example — Existing Project

```
User: /dotnet-init

Claude: Detected:
  - Solution: MyApp.slnx (3 projects)
  - SDK: Microsoft.NET.Sdk.Web → Web API
  - Database: PostgreSQL (from connection string)
  - Auth: JWT (from appsettings)

  1. Domain complexity? [CRUD-heavy / Moderate / Rich domain]
  > Moderate

  2. Team size? [Solo / Small / Large]
  > Small

  Recommendation: Vertical Slice Architecture
  Generated: ./CLAUDE.md (MyApp + VSA + PostgreSQL + JWT)
  Run /health-check to see your baseline.
```

## Example — Greenfield Project

```
User: /dotnet-init

Claude: No .sln or .csproj found. Let's set up a new project.

  1. What are you building? [REST API / Blazor / Worker / Library / Modular Monolith]
  > REST API

  2. Project name?
  > OrderService

  3. Domain complexity? [CRUD-heavy / Moderate / Rich domain]
  > CRUD-heavy

  Recommendation: Vertical Slice Architecture

  Creating solution...
  → dotnet new sln -n OrderService
  → dotnet new web -n OrderService.Api -o src/OrderService.Api
  → dotnet new xunit -n OrderService.Tests -o tests/OrderService.Tests
  → Directory.Build.props with .NET 10 defaults
  → Generated: ./CLAUDE.md (OrderService + VSA + scaffold-ready)

  Run /scaffold to add your first feature.
```

## Related

- `/plan` — Plan before building features
- `/health-check` — Assess project health after init
- `/scaffold` — Scaffold features using the chosen architecture
