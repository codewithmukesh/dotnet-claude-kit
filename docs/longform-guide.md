# dotnet-claude-kit Deep Dive Guide

A comprehensive guide covering setup, workflows, optimization, and troubleshooting for dotnet-claude-kit.

---

## Getting Started

### Prerequisites

- .NET 10 SDK
- Claude Code CLI (`npm install -g @anthropic-ai/claude-code` or equivalent)
- Docker (for Testcontainers in integration tests)
- Node.js (for MCP servers that use `npx`)

### Installation

1. **Install the Roslyn MCP server:**
```bash
dotnet tool install -g CWM.RoslynNavigator
```

2. **Add dotnet-claude-kit as a Claude Code plugin:**
```bash
# From your .NET project directory
claude plugins add /path/to/dotnet-claude-kit
```

3. **Configure MCP servers:**
Copy or merge `mcp-configs/mcp-servers.json` into your project's `.mcp.json`:
```bash
cp /path/to/dotnet-claude-kit/mcp-configs/mcp-servers.json .mcp.json
```
Update `${workspaceFolder}` to your project root path.

4. **Drop in a template CLAUDE.md (optional):**
Choose a template from `templates/` that matches your architecture and copy its `CLAUDE.md` into your project root.

### First Session

Start Claude Code in your project directory. The plugin loads automatically. Try these commands to explore:

```
/health-check          # Assess project health
"show instincts"       # See what patterns have been learned
/plan add user export  # Plan a new feature before building
```

The system will detect your architecture, load appropriate skills, and route queries to the right specialist agent.

---

## Token Optimization Strategies

Context window budget is finite. These strategies maximize what fits.

### MCP-First Approach

Always use Roslyn MCP tools before reading source files:

| Instead Of | Use |
|-----------|-----|
| Reading a 500-line file to find one method | `find_symbol` (returns file + line number) |
| Grepping for all usages of a type | `find_references` (structured results) |
| Reading multiple files to trace inheritance | `get_type_hierarchy` (complete chain) |
| Running `dotnet build` and parsing output | `get_diagnostics` (structured errors/warnings) |
| Manually inspecting all files for dead code | `find_dead_code` (zero-reference analysis) |

This approach typically reduces token consumption by 60-80% compared to file-reading approaches.

### Skill Loading

Skills are loaded on-demand, not all at once. Each agent has a defined skill map:

- **Small queries** (single fix or pattern): 1-2 skills loaded
- **Medium queries** (feature implementation): 3-4 skills loaded
- **Large queries** (architecture review): All relevant skills for the domain

The `workflow-mastery` skill (Context Discipline section) provides advanced strategies for managing token budget during long sessions.

### Subagent Delegation

Use subagents to keep the main context window clean:

- **Research tasks**: Spawn a subagent to explore a codebase area while you work on another
- **Parallel analysis**: Multiple subagents can investigate different aspects simultaneously
- **One task per subagent**: Focused subagents produce better results than multi-task ones
- **Model selection matters**: Use Sonnet for routine subagent work, Opus for complex analysis

---

## Autonomous Workflows

dotnet-claude-kit supports several autonomous feedback loops that run without user intervention.

### Build-Fix Loop (`/build-fix`)

1. Runs `dotnet build`
2. Parses compiler errors and categorizes them (missing reference, type mismatch, syntax, etc.)
3. Applies known fix patterns for each error category
4. Rebuilds and checks if errors decreased
5. Repeats until the build is green or the iteration limit (default: 10) is reached
6. Reports what was fixed and what remains

The `build-error-resolver` agent handles this with contextual skill loading -- it pulls in `ef-core` for DbContext errors, `dependency-injection` for DI container errors, and so on.

### Test-Fix Loop (`/tdd`)

Follows strict red-green-refactor:

1. **Red**: Write a failing test that defines the desired behavior
2. **Green**: Write the minimum code to make the test pass
3. **Refactor**: Clean up while keeping tests green
4. Uses `WebApplicationFactory` + Testcontainers for integration tests
5. Verifies each step before proceeding

### Refactor Loop (`/de-sloppify`)

1. Runs `dotnet format` for style consistency
2. Uses `find_dead_code` to identify unused symbols
3. Runs `detect_antipatterns` for code smell detection
4. Applies `get_diagnostics` for analyzer warnings
5. Makes structural improvements (extract method, simplify logic)
6. Verifies build + tests after each change

---

## Verification Pipeline

The `/verify` command runs a comprehensive 7-phase pipeline. Each phase produces PASS or FAIL with actionable output. The pipeline short-circuits on critical failures.

### Phase 1: Build
Runs `dotnet build` across all projects. Must pass before proceeding.

### Phase 2: Analyzers
Checks `get_diagnostics` for compiler warnings and analyzer violations. Reports severity counts and specific issues.

### Phase 3: Antipatterns
Uses `detect_antipatterns` to scan for .NET-specific code smells: async void, sync-over-async, `new HttpClient()`, `DateTime.Now`, broad catch, logging string interpolation, missing CancellationToken, EF queries without AsNoTracking.

### Phase 4: Tests
Runs `dotnet test` with detailed output. Reports pass/fail counts and failing test details.

### Phase 5: Security
Scans for hardcoded secrets, vulnerable NuGet packages, missing auth attributes, and CORS misconfigurations.

### Phase 6: Formatting
Runs `dotnet format --verify-no-changes` to ensure consistent code style.

### Phase 7: Diff Review
Reviews the diff against the base branch for logical issues, missing tests, and architecture violations.

---

## Health Check Interpretation

The `/health-check` command produces letter grades (A through F) across multiple dimensions:

| Dimension | What It Measures | Tools Used |
|-----------|-----------------|------------|
| Code Quality | Antipatterns, analyzer warnings, formatting | `detect_antipatterns`, `get_diagnostics` |
| Architecture | Dependency direction, circular deps, module boundaries | `get_project_graph`, `detect_circular_dependencies` |
| Test Coverage | Types with corresponding test classes | `get_test_coverage_map` |
| Dead Code | Unused types, methods, properties | `find_dead_code` |
| Security | Secrets, auth, OWASP compliance | Manual scan + antipattern detection |

### Grade Scale
- **A**: Excellent. Production-ready with confidence.
- **B**: Good. Minor issues that should be addressed but are not blocking.
- **C**: Acceptable. Notable gaps that need attention before scaling.
- **D**: Concerning. Significant issues that risk production stability.
- **F**: Critical. Fundamental problems that must be resolved immediately.

Each grade includes specific findings and recommended actions.

---

## Instinct System

The instinct system learns project-specific patterns and conventions over time.

### How It Works

1. **Observation**: As you work, the system observes patterns in your codebase (naming conventions, architecture choices, testing patterns).
2. **Correction capture**: When you correct Claude, the `instinct-system` skill captures the pattern and writes it to `MEMORY.md`.
3. **Confidence scoring**: Each instinct has a confidence score (0-100) that increases with repeated observations and decreases when contradicted.
4. **Application**: High-confidence instincts are applied automatically. Low-confidence instincts are suggested but not enforced.

### Managing Instincts

```
"show instincts"       # View all instincts with confidence scores
"export instincts"     # Export for sharing across projects
"import instincts"     # Import from another project
```

Instincts are stored in `.claude/instincts.json` and are project-specific. Exporting creates a portable format that another project can import and adapt.

---

## Session Management

### Starting a Session

When starting a new session, the system:
1. Loads `CLAUDE.md` and any `MEMORY.md` for project context
2. Checks for handoff notes in `.claude/handoff.md` from the previous session
3. Loads instincts from `.claude/instincts.json`
4. Makes rules from `rules/dotnet/` available (always-apply)

### Mid-Session Checkpointing

Use `/checkpoint` to save progress at any point:
- Creates a descriptive git commit with conventional commit prefix
- Writes a handoff note with completed work and next steps
- Useful before risky changes or when switching tasks

### Ending a Session

Use `/wrap-up` when done for the day:
- Summarizes completed work
- Lists pending tasks and open questions
- Captures learnings and new instincts
- Writes handoff to `.claude/handoff.md` for the next session

---

## Troubleshooting

### MCP Server Not Connecting

**Symptom:** MCP tools return errors or are not available.

**Fixes:**
1. Verify the tool is installed: `cwm-roslyn-navigator --version`
2. Check that a `.sln` or `.slnx` file exists (the server discovers it via BFS up to 3 levels)
3. Verify `.mcp.json` has the correct server configuration
4. Check that `${workspaceFolder}` is replaced with the actual path
5. The server needs time to load on first use -- it returns "loading" status during initialization

### Build-Fix Loop Not Converging

**Symptom:** `/build-fix` hits the iteration limit without a green build.

**Fixes:**
1. Check if errors are decreasing per iteration -- if not, the fixes may be introducing new errors
2. Look for circular dependency issues with `detect_circular_dependencies`
3. Check for missing NuGet packages (`dotnet restore` may be needed)
4. Some errors require manual intervention (architecture-level issues, missing project references)

### Hooks Not Running

**Symptom:** Format or antipattern hooks do not trigger.

**Fixes:**
1. Verify `hooks/hooks.json` is present and well-formed
2. Check that the plugin is loaded (`claude plugins list`)
3. Ensure hook scripts are executable (`chmod +x hooks/*.sh`)
4. Review hook matcher patterns -- `Edit|Write` matches those tool names

### High Token Consumption

**Symptom:** Context window fills up quickly, responses get truncated.

**Fixes:**
1. Use MCP tools instead of reading files (see Token Optimization above)
2. Load fewer skills -- only what the current task needs
3. Use subagents for research tasks to keep main context clean
4. Use the `workflow-mastery` skill (Context Discipline section) for advanced strategies
5. Consider splitting large tasks into smaller, focused sessions

### Tests Failing with Testcontainers

**Symptom:** Integration tests fail with Docker-related errors.

**Fixes:**
1. Verify Docker is running: `docker info`
2. Check Docker has enough resources allocated (memory, disk)
3. Ensure the Testcontainers NuGet package version matches your test framework
4. On CI, use Docker-in-Docker or a dedicated Docker host
5. Check for port conflicts if tests run in parallel

### Instincts Not Applying

**Symptom:** Previously learned patterns are not being followed.

**Fixes:**
1. Check instinct confidence by asking "show instincts" -- low-confidence instincts are suggestions, not enforced
2. Verify `.claude/instincts.json` exists and is not corrupted
3. Instincts are project-specific -- they do not transfer automatically between projects (use the instinct-system export and import modes)

---

## Creating Custom Skills

Skills follow the Agent Skills open standard. To create a new skill:

### 1. Create the Directory

```bash
mkdir -p skills/my-skill
```

### 2. Write SKILL.md

Every skill needs frontmatter and four required sections:

```markdown
---
name: my-skill
description: >
  What this skill does and when Claude should load it.
  Include trigger keywords and specific scenarios.
---

# My Skill

## Core Principles

1. **First principle** -- Rationale for why this matters.
2. **Second principle** -- Another opinionated default with explanation.
3. **Third principle** -- Keep to 3-5 principles.

## Patterns

### Pattern Name

[Working C# code example]

Brief explanation of why this is the recommended approach.

## Anti-patterns

### Anti-pattern Name

```csharp
// BAD
[code to avoid]

// GOOD
[correct approach]
```

Explanation of why the bad pattern is harmful.

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| When X | Do Y |
| When A | Do B |
```

### 3. Quality Checklist

- [ ] Maximum 400 lines -- every line earns its place
- [ ] Every recommendation has a "why"
- [ ] Code examples use modern C# 14 (primary constructors, collection expressions, records)
- [ ] BAD/GOOD comparisons in anti-patterns section
- [ ] Frontmatter has `name` (kebab-case, matches directory) and `description`

### 4. Register with Agents

Add the skill to relevant agent skill maps in the agent's `.md` file and update `AGENTS.md` if the skill introduces a new routing pattern.

### 5. Test the Skill

Load the skill in a Claude Code session and verify it activates on the trigger keywords defined in the description. Test that code examples compile conceptually and that anti-pattern comparisons are clear.
