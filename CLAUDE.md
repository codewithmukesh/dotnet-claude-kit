# dotnet-claude-kit — Development Instructions

> These instructions are for developing THIS repository. For user-facing project templates, see `templates/`.

## Repository Purpose

dotnet-claude-kit is an opinionated Claude Code companion for .NET developers. It provides skills, agents, templates, knowledge documents, and a Roslyn MCP server that make Claude Code dramatically more effective for .NET development.

## Philosophy

- **Guided over prescriptive** — We ask the right questions, then recommend the best approach with clear rationale
- **Modern .NET only** — Target .NET 10 and C# 14. No legacy patterns, no backwards compatibility with .NET Framework
- **Architecture-aware** — We support VSA, Clean Architecture, DDD, and Modular Monolith with an advisor skill that recommends the best fit (see ADR-005)
- **Token-conscious** — Every file respects context window limits. Skills max at 400 lines
- **Practical over theoretical** — Every recommendation includes a code example and a "why"

## Skill Structure

Skills follow the Agent Skills open standard. Each skill lives at `skills/<skill-name>/SKILL.md`.

### Frontmatter Schema (Required)

```yaml
---
name: skill-name           # kebab-case, matches directory name
description: >
  What this skill does and when Claude should load it.
  Include trigger keywords and specific scenarios.
---
```

### Required Sections

1. **Core Principles** — 3-5 numbered, opinionated defaults with rationale
2. **Patterns** — Code examples with explanation. Each pattern has:
   - A descriptive heading
   - Working C# code (must compile conceptually)
   - Brief explanation of why this is the recommended approach
3. **Anti-patterns** — What NOT to do, with BAD/GOOD code comparison
4. **Decision Guide** — Markdown table: Scenario → Recommendation

### Quality Standards

- **Maximum 400 lines** — Every line must earn its place. Respect token budgets.
- **Every recommendation has a "why"** — No bare rules without justification
- **Code examples must be modern C#** — Primary constructors, collection expressions, file-scoped namespaces, records
- **No Swashbuckle** — Use built-in .NET OpenAPI support
- **No repository pattern over EF Core** — Use DbContext directly
- **`TimeProvider` over `DateTime.Now`** — Always

## Agent Structure

Agents live at `agents/<agent-name>.md`. Each agent contains:

1. **Role definition** — What this agent is an expert in
2. **Skill dependencies** — Which skills this agent loads (by name)
3. **MCP tool usage** — When to use cwm-roslyn-navigator tools vs reading files
4. **Response patterns** — How to structure guidance
5. **Boundaries** — What this agent does NOT handle

## Template Structure

Templates live at `templates/<template-name>/`. Each contains:

- `CLAUDE.md` — Drop-in file for user projects
- `README.md` — When and how to use this template

Templates reference skills by name and should be self-contained — a user copies just the CLAUDE.md into their project.

## Knowledge Documents

Knowledge files at `knowledge/` are NOT skills. They're reference material that agents and templates point to. They don't follow the skill frontmatter format.

- `dotnet-whats-new.md` — Updated per .NET release
- `common-antipatterns.md` — Patterns Claude should never generate
- `package-recommendations.md` — Vetted NuGet packages
- `breaking-changes.md` — Migration gotchas
- `decisions/*.md` — ADRs using the template format

## Roslyn MCP Server

The MCP server lives at `mcp/CWM.RoslynNavigator/`. It's a .NET 10 application using the ModelContextProtocol SDK.

### Building

```bash
dotnet build mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.csproj
dotnet test mcp/CWM.RoslynNavigator/tests/CWM.RoslynNavigator.Tests.csproj
```

### Key Rules

- Tools are **read-only** — No code generation, no modifications
- Responses are **token-optimized** — Return file paths, line numbers, and short snippets, never full file contents
- The workspace must handle **graceful loading** — Return "loading" status instead of errors during initialization

## Contribution Workflow

1. Check the spec at `docs/dotnet-claude-kit-SPEC.md` for the full vision
2. Follow the skill/agent/template structure defined above
3. Run `dotnet format --verify-no-changes` before committing
4. Ensure skill files stay under 400 lines
5. Every new pattern needs a BAD/GOOD code comparison in Anti-patterns
