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

Agents live at `agents/<agent-name>.md`.

### Frontmatter Schema (Required)

```yaml
---
name: agent-name           # kebab-case, matches file name
description: >
  What this agent is an expert in and when Claude should delegate to it.
  Include concrete trigger scenarios — Claude routes subagent work based on this.
model: sonnet              # optional — tier alias only (fable/opus/sonnet/haiku), never a pinned version ID
---
```

Plugin agents must NOT declare `permissionMode`, `hooks`, or `mcpServers` — those fields are reserved for user/project-level agents.

### Required Sections

Below the frontmatter, each agent contains:

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

## Workflow Skill Structure (Orchestrators)

Claude Code merged slash commands into skills, so the kit no longer has a `commands/` directory. Workflow orchestrators (e.g., `/verify`, `/scaffold`, `/tdd`) are skills like any other — they live at `skills/<name>/SKILL.md` and register `/name` automatically — but follow a different body structure than knowledge skills.

### Frontmatter Schema (Required)

```yaml
---
name: workflow-name        # kebab-case, matches directory name; registers /workflow-name
description: >
  What this workflow does and its trigger phrases.
# Optional: disable-model-invocation: true — for explicit /name invocation only
---
```

### Required Sections

1. **What** — What the workflow does
2. **When** — When to use it (trigger phrases)
3. **How** — Step-by-step execution flow (invokes skills/agents)
4. **Example** — Example output or usage
5. **Related** — Related workflows

### Quality Standards

- **Maximum 200 lines** — Orchestrators are not encyclopedias (knowledge skills get 400)
- **Invoke, don't implement** — Orchestrators reference skills and agents for the actual logic
- **Clear trigger phrases** — Users should know when to reach for this workflow

## Rule Structure

Rules live at `rules/dotnet/<rule-name>.md`. Rules are always loaded into context.

### Frontmatter Schema (Required)

```yaml
---
alwaysApply: true
description: >
  What this rule enforces.
---
```

### Quality Standards

- **Maximum 100 lines** — Rules are always in context, so every line costs tokens
- **Prescriptive with rationale** — Each rule has a brief "why"
- **DO/DON'T format** — Clear, scannable rules
- **Total rules budget: ~600 lines** — All rules combined must stay lean

## Roslyn MCP Server

The MCP server lives at `mcp/CWM.RoslynNavigator/`. It's a .NET 10 application using the ModelContextProtocol SDK.

### Building

```bash
dotnet build mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx
dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx
```

### Key Rules

- Tools are **read-only** — No code generation, no modifications
- Responses are **token-optimized** — Return file paths, line numbers, and short snippets, never full file contents
- The workspace must handle **graceful loading** — Return "loading" status instead of errors during initialization

## Workflow Standards

How Claude should work on this repository (and any project using dotnet-claude-kit templates).

### Plan Before Building

- Enter plan mode for ANY non-trivial task (3+ steps or architectural decisions)
- Iterate on the plan until it's solid before writing code
- If something goes sideways mid-implementation, STOP and re-plan — don't keep pushing through a broken approach
- Write detailed specs upfront to reduce ambiguity — vague plans produce vague code

### Verify Before Done

- Never mark a task complete without proving it works
- Run `dotnet build` and `dotnet test` after changes — green builds are the minimum bar
- Use `get_diagnostics` via the Roslyn MCP to catch warnings after modifications
- Ask yourself: "Would a staff .NET engineer approve this?" — if not, iterate
- Diff behavior between main and your changes when relevant

### Fix Bugs Autonomously

- When given a bug report: investigate and fix it. Don't ask for hand-holding
- Point at logs, errors, failing tests — then resolve them
- Go fix failing CI tests without being told how
- Zero context switching required from the user

### Demand Elegance (Balanced)

- For non-trivial changes: pause and ask "is there a more elegant way?"
- If a fix feels hacky, step back: "Knowing everything I know now, implement the elegant solution"
- Skip this for simple, obvious fixes — don't over-engineer. Three lines of clear code beats a premature abstraction
- Challenge your own work before presenting it

### Use Subagents for Parallel Work

- Use subagents liberally to keep the main context window clean
- Offload research, exploration, and parallel analysis to subagents
- One task per subagent for focused execution
- For complex problems, throw more compute at it via subagents rather than doing sequential work

### Learn from Corrections

- After ANY correction from the user, capture the pattern in auto memory (`MEMORY.md`)
- Write rules that prevent the same mistake from recurring
- Review memory at session start for project-relevant lessons
- This is a compounding system — mistake rate should drop over time

## Contribution Workflow

1. Check the spec at `docs/dotnet-claude-kit-SPEC.md` for the full vision
2. Follow the skill/agent/template/command/rule structure defined above
3. Run `dotnet format --verify-no-changes` before committing
4. Ensure knowledge skills stay under 400 lines, workflow/orchestrator skills under 200, rules under 100
5. Every new pattern needs a BAD/GOOD code comparison in Anti-patterns
6. Ensure all cross-references (skills → skills, agents → skills) resolve to real files
7. All orchestrators are skills — never create a `commands/` directory; workflow skills need `name` + `description` frontmatter
8. New rules must have `alwaysApply: true` in frontmatter
9. Agents must have YAML frontmatter with `name` and `description` (trigger scenarios included); model references use tier aliases only
