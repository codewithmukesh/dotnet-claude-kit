---
description: >
  Interactive project initialization. Detects project type, asks architecture
  questions, and generates a customized CLAUDE.md — no manual template copying.
---

# /init

## What

Interactively initializes a .NET project for use with dotnet-claude-kit. Detects the project type, asks targeted questions about architecture and tech stack, then generates a fully customized `CLAUDE.md` in the project root.

**No manual template copying required.**

## When

- Starting a new .NET project with Claude Code
- Adding dotnet-claude-kit to an existing project
- "init project", "setup project", "generate CLAUDE.md", "configure for dotnet-claude-kit"

## How

### Step 1: Detect Project Type

Analyze the current directory to determine the project type:

```
→ Look for .slnx / .sln files
→ Scan .csproj files for SDK type:
  - Microsoft.NET.Sdk.Web → web-api or blazor-app
  - Microsoft.NET.Sdk.Worker → worker-service
  - Microsoft.NET.Sdk → class-library
→ Check for multiple projects with inter-module references → modular-monolith
→ If ambiguous, ask the user
```

### Step 2: Architecture Questionnaire

Load the `architecture-advisor` skill and ask targeted questions:

1. **Domain complexity** — CRUD-heavy, moderate business rules, or rich domain?
2. **Team size** — Solo, small team, or large team?
3. **Module boundaries** — Single deployable or multiple bounded contexts?
4. **Existing patterns** — Detect conventions via `convention-learner` skill

→ Recommend: VSA, Clean Architecture, DDD, or Modular Monolith with rationale.

### Step 3: Tech Stack Selection

Ask about specific technology choices:

- **Database**: PostgreSQL, SQL Server, SQLite, or other
- **Auth**: JWT, OIDC, Cookie, or none yet
- **Caching**: HybridCache, Redis, or none yet
- **Messaging**: MassTransit, or none yet
- **Testing**: Confirm xUnit + Testcontainers defaults

### Step 4: Generate CLAUDE.md

Load the appropriate template from `templates/` as a base, then customize:

- Replace `[ProjectName]` with the actual project/solution name
- Set the chosen architecture
- Configure the tech stack section
- Add project-specific conventions detected in Step 2
- Write to `./CLAUDE.md`

### Step 5: Verify Setup

```
→ Confirm .mcp.json exists or create it (for Roslyn MCP server)
→ Verify the solution file is discoverable
→ Run a quick health check to establish baseline
→ Report: "Project initialized. Run /health-check to see your baseline."
```

**Skills invoked:** `project-setup`, `architecture-advisor`, `convention-learner`
**Agent:** `dotnet-architect`

## Example

```
User: /init

Claude: Let me analyze your project...

Detected:
  - Solution: MyApp.slnx (3 projects)
  - SDK: Microsoft.NET.Sdk.Web
  - Type: Web API

Architecture questionnaire:
  1. Domain complexity? [CRUD-heavy / Moderate / Rich domain]
  2. Team size? [Solo / Small / Large]
  3. Module boundaries? [Single / Multiple bounded contexts]

> User selects: Moderate, Small team, Single

Recommendation: Vertical Slice Architecture
  - Moderate complexity → feature isolation without layer overhead
  - Small team → simpler navigation, less ceremony

Tech stack:
  - Database: PostgreSQL (detected from connection string)
  - Auth: JWT (detected from appsettings)
  - Caching: None configured → recommending HybridCache

Generated: ./CLAUDE.md (customized for MyApp with VSA + PostgreSQL + JWT)

Run /health-check to see your project baseline.
```

## Related

- `/plan` — Plan before building features
- `/health-check` — Assess project health after init
- `/scaffold` — Scaffold features using the chosen architecture
