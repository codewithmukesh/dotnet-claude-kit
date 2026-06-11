# Contributing to dotnet-claude-kit

Thank you for your interest in contributing! This guide covers how to add skills, knowledge documents, templates, and MCP improvements.

## Getting Started

1. Fork and clone the repository
2. Read the spec in `docs/dotnet-claude-kit-SPEC.md`
3. Read `CLAUDE.md` for repo development conventions
4. Check open issues for areas where help is needed

## Contribution Areas

### Skills

Skills are the core of dotnet-claude-kit. Each skill lives at `skills/<skill-name>/SKILL.md`.

**Before creating a new skill:**
- Check the [existing skills](#current-skills) to avoid overlap
- Open a [Skill Proposal](../../issues/new?template=new-skill.yml) issue for discussion
- Review the skill format in `CLAUDE.md`

**Skill requirements:**
- YAML frontmatter with `name` and `description`
- Required sections: Core Principles, Patterns, Anti-patterns, Decision Guide
- Maximum 400 lines
- Every recommendation has a "why"
- Code examples use C# 14 / .NET 10 patterns
- BAD/GOOD code comparisons in Anti-patterns section

### Knowledge Documents

Knowledge files in `knowledge/` are reference material, not skills.

- `dotnet-whats-new.md` — Updated per .NET preview/release
- `common-antipatterns.md` — Patterns Claude should never generate
- `package-recommendations.md` — Vetted NuGet packages
- `breaking-changes.md` — Migration gotchas
- `decisions/*.md` — Architecture Decision Records

**To update knowledge:**
- Open a [Knowledge Update](../../issues/new?template=new-knowledge.yml) issue
- Verify information against official Microsoft documentation
- Include links to sources

### Templates

Templates in `templates/<type>/` provide drop-in `CLAUDE.md` files for user projects.

Each template needs:
- `CLAUDE.md` — The drop-in file with project context, skills references, commands
- `README.md` — When and how to use the template

### Workflow Skills (Orchestrators)

Claude Code merged slash commands into skills — there is no `commands/` directory. Workflow orchestrators (e.g., `/verify`, `/scaffold`) are skills at `skills/<name>/SKILL.md`; the skill name registers the slash invocation automatically.

**Workflow skill requirements:**
- YAML frontmatter with `name` and `description` (add `disable-model-invocation: true` for explicit-only invocation)
- Required sections: What, When, How, Example, Related
- Maximum 200 lines (knowledge skills get 400)
- Orchestrators invoke skills/agents — they don't contain the logic themselves

### Agents

Agents in `agents/` are specialist subagents.

**Agent requirements:**
- YAML frontmatter with `name` (matches file name) and `description` (include concrete trigger scenarios — Claude routes delegation on this)
- Optional `model:` uses tier aliases only (`fable`, `opus`, `sonnet`, `haiku`) — never pinned version IDs
- No `permissionMode`, `hooks`, or `mcpServers` in frontmatter (unsupported for plugin agents)
- Required sections: Role definition, Skill dependencies, MCP tool usage, Response patterns, Boundaries

### Rules

Rules in `rules/dotnet/` are always-loaded conventions.

**Rule requirements:**
- YAML frontmatter with `alwaysApply: true` and `description`
- Maximum 100 lines (rules are always in context — every line costs tokens)
- Prescriptive DO/DON'T format with brief rationale for each rule
- All rules combined should stay under ~600 lines total

### Roslyn MCP Server

The MCP server at `mcp/CWM.RoslynNavigator/` provides semantic analysis tools.

```bash
# Build
dotnet build mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx

# Run tests
dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx
```

**MCP contribution guidelines:**
- Tools are read-only — no code generation or modifications
- Responses are token-optimized — return paths, line numbers, and short snippets
- Add integration tests using the `TestSolutionFixture`
- Update the README with any new tool documentation

## Development Workflow

1. Create a feature branch from `main`
2. Make your changes following the conventions in `CLAUDE.md`
3. Ensure all validations pass:
   - Skill frontmatter is valid
   - Skill files are under 400 lines
   - MCP server builds: `dotnet build mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx`
   - MCP tests pass: `dotnet test mcp/CWM.RoslynNavigator/tests/CWM.RoslynNavigator.Tests.csproj`
   - Code formatting: `dotnet format --verify-no-changes`
4. Open a pull request with a clear description of changes

## Code of Conduct

Be kind, be constructive. We're building tools to make .NET development better for everyone.

## Architecture Decision Records

For significant decisions about defaults or patterns, create an ADR:

1. Copy `knowledge/decisions/template.md`
2. Number it sequentially (e.g., `005-your-decision.md`)
3. Fill in Context, Decision, and Consequences
4. Submit as part of your PR

## Questions?

Open an issue or start a discussion. We're happy to help contributors get started.
