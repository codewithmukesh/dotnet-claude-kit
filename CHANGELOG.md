# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.8.0] — 2026-06-11

### Added
- **YAML frontmatter on all 10 agents** — `name` + trigger-rich `description` per the current Claude Code subagent standard, so Claude can route delegation intelligently (previously agents surfaced with a generic fallback description). `build-error-resolver` and `refactor-cleaner` declare `model: sonnet` via frontmatter
- **`hooks/README.md`** — Documents which scripts are Claude Code hooks (auto-run via `hooks.json`), which are git pre-commit hooks (manual install), and which are workflow utilities
- **Fable 5 guidance** — `model-selection` skill and rules now cover the Fable tier (above Opus) for highest-stakes architecture, debugging escalation, and critical reviews

### Changed
- **Commands migrated to skills (`commands/` removed)** — Claude Code merged slash commands into skills, so all 16 commands now live at `skills/<name>/SKILL.md` (60 skills total). 13 moved as-is with `name` frontmatter added; 3 that duplicated existing skill names (`de-sloppify`, `health-check`, `security-scan`) were merged into those skills — this also fixes their double `/name` registration. All 16 slash invocations are unchanged. CI workflow, CLAUDE.md, CONTRIBUTING.md, README, and docs updated accordingly
- **CI now enforces agent frontmatter** — validate.yml checks `name`/`description` on every agent; the obsolete validate-commands job was removed
- **`model-selection` skill modernized** — Opus 4.6 references updated to the current lineup (Fable 5, Opus 4.8, Sonnet 4.6, Haiku 4.5); prose now uses tier aliases (`fable`/`opus`/`sonnet`/`haiku`) so guidance doesn't rot; added "Hardcoding Model Versions" anti-pattern
- **CLAUDE.md / CONTRIBUTING.md** — Orchestrators are authored as skills (Claude Code merged slash commands into skills); agent frontmatter schema and workflow-skill structure documented
- **README and docs** — Hooks tables now correctly distinguish Claude Code hooks from git hooks and utility scripts
- **`.claude/rules/hooks.md`** — Post-test analysis guidance now reflects how `post-test-analyze.sh` is actually invoked (piped, not automatic)
- **`plugin.json`** — Added `displayName`; description no longer claims 7 automatic hooks

### Fixed
- **`migration-workflow` skill recommended a nonexistent `--dry-run` flag** — `dotnet ef database update -- --dry-run` passes the flag to the app host and applies the migration anyway; the skill now previews SQL with `dotnet ef migrations script --idempotent`
- **`ci-cd` skill pinned GitHub Actions `@v4`** — examples updated to `@v5` (checkout, setup-dotnet, upload-artifact), matching the repo's own CI
- **Skill routing collisions** — workflow/knowledge pairs shared trigger phrases ("verify", "wrap up", "code review", "scaffold", "migration"), so Claude couldn't reliably pick the right skill; knowledge-skill descriptions (verification-loop, wrap-up-ritual, code-review-workflow, scaffolding, migration-workflow, 80-20-review, session-management, logging, checkpoint) now state the methodology they own, point action phrases at their workflow, and keep only unique triggers
- **Skill content polish from the full-portfolio audit** — serilog (correct `Elastic.Serilog.Sinks` example, `[LoggerMessage]` pattern, `Serilog.Expressions` package note), ddd (`IDomainEvent : INotification` clarified as the MIT Mediator package), project-structure (illustrative-version caveat on `Directory.Packages.props`), README count drift (skills anchor, hooks claim)
- **MCP server logged to stdout, corrupting the JSON-RPC stream** (#10) — `CWM.RoslynNavigator` now routes all console logging to stderr (`LogToStandardErrorThreshold = Trace`), as required by the MCP stdio transport spec. Previously, log lines interleaved with protocol frames caused Claude Code to drop the connection with "JSON Parse error"
- **MCP server failed with "No .NET SDKs were found" on macOS/Linux** (#9) — When `MSBuildLocator.RegisterDefaults()` cannot resolve the SDK (wrapper-script `dotnet` on PATH, e.g. Homebrew, with `DOTNET_ROOT` unset), the server now falls back to locating the SDK via `dotnet --list-sdks`. Install docs also document setting `DOTNET_ROOT` explicitly
- **`pre-bash-guard.sh` destructive-command guard was inert** — It read the command from the legacy `CLAUDE_TOOL_INPUT` env var only; it now parses the PreToolUse JSON payload from stdin (jq with grep fallback), with the env var kept as fallback

## [0.7.0] — 2026-03-22

### Added
- **Common infrastructure knowledge doc** — `knowledge/common-infrastructure.md` with copy-paste implementations for Result, ValidationFilter, GlobalExceptionHandler (IExceptionHandler), IEndpointGroup + MapEndpoints, PaginationQuery, PagedList, and Program.cs setup checklist
- **MediatR → Mediator migration guide** — `knowledge/mediatr-to-mediator-migration.md` with side-by-side API comparison, key differences (ValueTask, MessageHandlerDelegate), code examples, and step-by-step migration checklist
- **Rate limiting section** in resilience skill — fixed window, sliding window, token bucket algorithms with custom 429 ProblemDetails response and per-endpoint `.RequireRateLimiting()` usage
- **Additional `field` keyword examples** in modern-csharp skill — lazy initialization (`field ??=`) and INotifyPropertyChanged change notification patterns
- **`maxResults` parameter on `find_references`** — Caps results at 100 (default) to prevent token-blowing responses for widely-used symbols

### Changed
- **Messaging skill rewritten Wolverine-first** — All patterns (setup, publishing, consuming, outbox, saga) now show Wolverine code. MassTransit condensed to ~30-line alternative section with commercial license note
- **Modular monolith template** updated to Wolverine types — `IPublishEndpoint` → `IMessageBus`, `IConsumer<T>` → convention-based handler
- **Error-handling skill** — Global Exception Handler section now references `common-infrastructure.md` for the modern `IExceptionHandler` approach
- **MCP server performance optimizations:**
  - `find_references` — Caches `SourceText` per document (200 async calls → ~10 for multi-reference files)
  - `find_dead_code` — Fast name-based pre-filter skips ~80-90% of expensive `FindReferencesAsync` calls
  - `get_dependency_graph` — O(1) file-to-project lookup via pre-built dictionary (was O(P*D) per recursion step)
  - `detect_circular_dependencies` — Extracted `IsUserType()` helper to reduce `ToDisplayString()` allocations
  - `SymbolResolver` — Uses `SymbolEqualityComparer.Default` for dedup instead of string allocation
  - Compilation warming now runs in parallel (`Parallel.ForEachAsync`, max 4 concurrent) for ~2-4x faster startup
  - Consolidated 4 duplicate `MakeRelativePath` methods into shared `SymbolResolver.MakeRelativePath`
- Plugin version bumped to 0.7.0

## [0.6.0] — 2026-02-28

### Added
- **16 commands** — Comprehensive command library for common .NET workflows
- **10 rules** — Always-loaded rules for coding style, architecture, error handling, security, testing, performance, git workflow, hooks, packages, and agents
- **`dotnet-init` command** — Renamed from `init` for clarity

### Changed
- Rules moved to `.claude/rules/` for plugin compatibility
- Plugin manifests updated with repository URL and enhanced validation
- CI validation updated for minimal plugin.json schema
- Plugin version bumped to 0.6.0

## [0.5.0] — 2026-02-25

### Added
- **7 meta skills** — workflow-mastery, self-correction-loop, wrap-up-ritual, context-discipline, de-sloppify, convention-learner, code-review-workflow
- **NuGet publishing** — MCP server packaged as `CWM.RoslynNavigator` global tool
- **Recursive solution discovery** — BFS search up to 3 levels for .slnx/.sln files

### Changed
- MCP server project restructured to use solution file
- IEndpointGroup auto-discovery pattern enforced across all templates
- Result pattern enforced in all scaffold and VSA examples
- Scaffolding gaps fixed: validation, CancellationToken, OpenAPI, pagination
- Packages rule added to enforce latest stable NuGet versions
- README updated with IEndpointGroup, Result pattern, and scaffold checklist
- Plugin version bumped to 0.5.0

## [0.4.0] — 2026-02-21

### Added
- **Scaffolding skill** — `scaffolding` skill with complete code generation patterns for all 4 architectures (VSA, Clean Architecture, DDD, Modular Monolith). Generates features, entities, tests, and modules.
- **Project Setup skill** — `project-setup` skill with interactive workflows for project initialization (CLAUDE.md generation), codebase health checks (graded report cards), and .NET version migration guidance.
- **Code Review Workflow skill** — `code-review-workflow` skill with structured MCP-driven PR reviews: full review, quick review, and architecture compliance check patterns.
- **Migration Workflow skill** — `migration-workflow` skill with safe workflows for EF Core migrations, NuGet dependency updates, and .NET version upgrades. Includes rollback strategies.
- **Convention Learner skill** — `convention-learner` skill that detects project-specific coding conventions (naming, structure, modifiers) and enforces them in new code and reviews.
- **4 new MCP tools:**
  - `find_dead_code` — Find unused types, methods, and properties across the solution
  - `detect_circular_dependencies` — Detect project-level and type-level circular dependencies
  - `get_dependency_graph` — Visualize method call chains with configurable depth
  - `get_test_coverage_map` — Heuristic test coverage mapping by naming convention
- **4 new hooks:**
  - `post-edit-format.sh` — Auto-format C# files after edits
  - `pre-commit-antipattern.sh` — Detect anti-patterns in staged files before commit
  - `post-test-analyze.sh` — Parse test results and output actionable summary
  - `pre-build-validate.sh` — Validate project structure before build
- **7 new test files** for MCP tools: FindCallers, FindOverrides, GetSymbolDetail, FindDeadCode, DetectCircularDependencies, GetDependencyGraph, GetTestCoverageMap
- **Test data** — UnusedHelper class and OrderServiceTests class in SampleSolution for new tool tests

### Changed
- `dotnet-architect` agent now loads `scaffolding` and `project-setup` skills
- `code-reviewer` agent now loads `code-review-workflow` and `convention-learner` skills
- `ef-core-specialist` agent now loads `migration-workflow` skill
- AGENTS.md routing table expanded with 7 new intent patterns
- AGENTS.md MCP tool preferences table expanded with 4 new tools
- Skills count: 22 → 27
- MCP tools count: 11 → 15
- Hooks count: 2 → 6
- README.md rewritten with "What Makes This 10x" section and updated tables
- Plugin version bumped to 0.4.0

## [0.3.0] — 2026-02-21

### Added
- **Multi-architecture support** — New skills: `architecture-advisor`, `clean-architecture`, `ddd`
- **Workflow mastery skill** — `workflow-mastery` skill covering parallel worktrees, plan mode strategy, verification loops, auto-format hooks, permission setup, and subagent patterns for .NET (inspired by Boris Cherny's tips)
- **Workflow Standards section** in root CLAUDE.md and all 5 templates — plan before building, verify before done, fix bugs autonomously, demand elegance, use subagents, learn from corrections
- **Architecture advisor questionnaire** — 15+ questions across 6 categories to recommend the best-fit architecture (VSA, Clean Architecture, DDD + CA, Modular Monolith)
- **ADR-005** — Multi-architecture decision record superseding ADR-001 (VSA-only default)
- **Plugin distribution** — `.claude-plugin/plugin.json` and `marketplace.json` for Claude Code plugin marketplace
- **Progressive skill loading** — All 20 skill descriptions enriched with trigger keywords for better contextual loading
- **Installation section** in README with plugin marketplace commands

### Changed
- Philosophy updated from "opinionated over encyclopedic" to "guided over prescriptive"
- Architecture default changed from VSA-only to advisor-driven (supports 4 architectures)
- `dotnet-architect` agent now loads `architecture-advisor` first, then conditionally loads architecture-specific skills
- `code-reviewer` agent contextually loads `clean-architecture` and `ddd` for project structure reviews
- All 5 templates updated to reference `architecture-advisor` skill
- `web-api` template now shows 3 architecture options (VSA, CA, DDD)
- `modular-monolith` template updated to support per-module architecture choice
- Skills count: 17 → 21
- Branding: "opinionated" → "definitive"
- ADR-001 marked as superseded by ADR-005
- MediatR description updated to mention architecture-agnostic compatibility

## [Unreleased]

### Added
- Initial repository structure
- Project spec in `docs/dotnet-claude-kit-SPEC.md`
