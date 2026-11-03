---
name: dotnet-init
description: >
  Interactive project initialization. Detects project type (web API, Blazor,
  worker, console, CLI tool, desktop, library), asks the right questions, and
  generates a customized CLAUDE.md — no manual template copying.
  Use when: "init project", "setup project", "initialize", "new project setup",
  "generate CLAUDE.md", "configure for dotnet-claude-kit", or starting Claude
  Code on a fresh or existing .NET codebase.
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
→ Look for .slnx / .sln files, then classify each .csproj:
  - Microsoft.NET.Sdk.Web → web-api or blazor-app
  - Microsoft.NET.Sdk.Worker → worker-service
  - UseWPF / UseWindowsForms / UseMaui = true, or Avalonia package → desktop-app
  - Microsoft.NET.Sdk + OutputType Exe → console-cli
    (PackAsTool or System.CommandLine reference → CLI tool; else console utility)
  - Microsoft.NET.Sdk without OutputType Exe → class-library
  - Multiple projects with inter-module references → modular-monolith
  - If ambiguous, ask the user

→ If NO solution/project found (greenfield), ask "What are you building?":
    - REST API / microservice → web-api       - Blazor application → blazor-app
    - Background worker → worker-service       - Multi-module system → modular-monolith
    - Console app / CLI tool → console-cli     - NuGet package / library → class-library
    - Desktop app → desktop-app (then ask: Avalonia, WPF, WinForms, or MAUI?)
  - Ask "Project name?", then scaffold: dotnet new sln -n ProjectName --format slnx, then the matching
    template (webapi / worker / console / classlib / wpf / winforms / maui;
    Avalonia: dotnet new install Avalonia.Templates → avalonia.mvvm),
    Directory.Build.props with .NET 10 defaults, src/ and tests/ folders
```

### Step 2: Questionnaire (by Project Type)

**Web API / Blazor / Worker / Modular Monolith** — load `architecture-advisor`:

1. **Domain complexity** — CRUD-heavy, moderate business rules, or rich domain?
2. **Team size** — Solo, small team, or large team?
3. **Module boundaries** — Single deployable or multiple bounded contexts?
4. **Existing patterns** — (existing projects only) Detect conventions via `convention-learner` skill

→ Recommend: VSA, Clean Architecture, DDD, or Modular Monolith with rationale.

**Console / CLI tool** — skip web architecture questions; load `console-cli`:
- Run-to-completion tool or long-running daemon? (daemon → worker-service instead)
- Distribution: dotnet tool (`PackAsTool`), Native AOT binary per OS (`native-aot`), or file-based script?
- Needs DI/config/HTTP? → Generic Host inside the command action

**Desktop** — skip web architecture questions; load `desktop-mvvm` (+ `avalonia` for Avalonia):
- Target OSes? (Windows-only → WPF is fine; macOS/Linux → Avalonia)
- Native AOT / trimming wanted? (Avalonia only — WPF/WinForms don't support trimming)

**Library** — public API surface, target frameworks, AOT compatibility (`IsAotCompatible`).

### Step 3: Tech Stack Selection

- **Database**: PostgreSQL, SQL Server, SQLite, or none yet
- **Auth / Caching / Messaging** (web + worker only): JWT/OIDC, HybridCache, Wolverine/MassTransit, or none yet
- **Testing**: Confirm xUnit defaults (+ Testcontainers when a database is involved)

### Step 4: Generate CLAUDE.md

Load the appropriate template from `templates/` as a base, then customize:

- Replace `[ProjectName]`, set the chosen architecture (or app type), configure the tech stack
- Existing projects: add conventions detected in Step 2; greenfield: recommended conventions
- Templates: `web-api`, `blazor-app`, `worker-service`, `modular-monolith`, `class-library`, `console-cli`, `desktop-app`

**Before writing, check for an existing `CLAUDE.md`. Never overwrite it silently.**

```
→ If ./CLAUDE.md does NOT exist:
  - Write the generated content to ./CLAUDE.md

→ If ./CLAUDE.md ALREADY exists:
  - Read it and identify hand-written / custom sections
    (anything not produced by a kit template)
  - Ask: "You already have a CLAUDE.md. How should I proceed?"
    - Merge (recommended) → keep the user's custom sections, add or refresh the
      kit's architecture, tech-stack, and convention sections in place
    - Overwrite → replace it entirely with the freshly generated file
    - Keep → leave it untouched; print the generated content for manual copying
  - For Merge and Overwrite: back the original up to ./CLAUDE.md.bak first.
    Never discard user content without a .bak backup.
```

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
    (console/desktop: "Add your first command / view model.")
```

**Skills invoked:** `project-setup`, `architecture-advisor`, `convention-learner`; console/desktop: `console-cli`, `desktop-mvvm`, `avalonia`, `native-aot`
**Agent:** `dotnet-architect` (web, worker, library) · `app-engineer` (console, CLI, desktop)

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

  1. What are you building? [REST API / Blazor / Worker / Console-CLI / Desktop / Library / Modular Monolith]
  > REST API

  2. Project name?
  > OrderService

  3. Domain complexity? [CRUD-heavy / Moderate / Rich domain]
  > CRUD-heavy

  Recommendation: Vertical Slice Architecture

  Creating solution...
  → dotnet new sln -n OrderService --format slnx
  → dotnet new webapi -n OrderService.Api -o src/OrderService.Api
  → dotnet new xunit3 -n OrderService.Tests -o tests/OrderService.Tests
  → Directory.Build.props with .NET 10 defaults
  → Generated: ./CLAUDE.md (OrderService + VSA + scaffold-ready)

  Run /scaffold to add your first feature.
```

## Example — Existing Project With a CLAUDE.md

```
User: /dotnet-init

Claude: Detected:
  - Solution: MyApp.slnx (3 projects) → Web API
  - Found an existing CLAUDE.md (custom "Deployment" and "Team conventions" sections)

  You already have a CLAUDE.md. How should I proceed?
  [Merge (recommended) / Overwrite / Keep]
  > Merge

  → Backed up original to ./CLAUDE.md.bak
  → Kept your Deployment + Team conventions sections
  → Refreshed architecture (VSA), tech-stack (PostgreSQL + JWT), and convention sections
  Generated: ./CLAUDE.md
  Run /health-check to see your baseline.
```

## Related

- `/plan` — Plan before building features
- `/health-check` — Assess project health after init
- `/scaffold` — Scaffold features using the chosen architecture
