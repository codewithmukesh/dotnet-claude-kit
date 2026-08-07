# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.12.0] — 2026-08-07

Roslyn Navigator correctness release. Symbol resolution, response contracts, and workspace reload behaviour are corrected, and two tools are added: stack trace resolution and change impact analysis.

### Added
- **2 new MCP tools (20 → 22)** in CWM.RoslynNavigator 0.10.0:
  - `resolve_stack_trace` — maps a .NET exception onto the solution. Undoes compiler rewrites for async state machines, lambdas, and local functions (the frames hardest to grep for), marks which frames are yours, and points at the topmost one in your own code
  - `analyze_change_impact` — "what breaks if I change this?" in one call: references grouped by project and by file, implementations and overrides that must change alongside a signature, transitive callers, assembly-boundary exposure, and a risk rating with its rationale
- **Container distribution** for the MCP server via SDK container publishing (no Dockerfile), removing the host-SDK and `DOTNET_ROOT` setup that trips up macOS and Linux installs

### Changed
- **CWM.RoslynNavigator 0.9.0 → 0.10.0 — symbol resolution and response-contract correctness pass**:
  - **Qualified symbol names now resolve.** Lookup accepted only a bare declared name, so `find_references("OrderService.CreateOrderAsync")` returned empty — indistinguishable from a symbol with no references
  - **Structured error codes replace empty results.** Nine tools returned an empty list for an unresolvable symbol, and ambiguity was resolved by `return symbols[0]` — a bare name matching several types answered about the wrong one with full confidence. Now `SymbolNotFound`, `AmbiguousMatch` (with candidates), `WrongSymbolKind`, `FileNotFound`, `NoSource`
  - **`Truncated` and `Limit` on every list response**, so truncation is stated rather than inferred from `Count` vs `TotalFound` — a comparison that is wrong when the result set is exactly the limit
  - **`IsGenerated` on navigation results**, so an agent does not edit a `.g.cs` file the next build overwrites
  - **`find_dead_code` grades against reflection.** A type resolved via `Type.GetType("...")` has zero references and was reported at high confidence; a name in any string literal now drops to `low`, and `AssemblyScanningDetected` flags solutions that register types by scanning
  - **Reloads only on real build-file changes.** csproj comparison was timestamp-only, so a branch switch re-evaluated MSBuild for identical content. Build files now carry a content hash, and `Directory.Build.props` / `Directory.Packages.props` / `Directory.Build.targets` / `global.json` / `nuget.config` are tracked — previously invisible despite governing every project beneath them
  - **Structural file scan moved to the background**, so no tool call pays for a recursive directory walk
  - **Fixed: compilation cache survived a reload**, leaving orphaned `ProjectId` entries pinning whole Roslyn compilations in memory
- **`hooks/pre-commit-antipattern.sh` rewritten** ([#23](https://github.com/codewithmukesh/dotnet-claude-kit/issues/23)) — the naive whole-file grep is replaced by a comment- and string-aware scanner (`hooks/lib/antipattern-scan.awk`) that mirrors the Roslyn detectors' rule IDs (AP001–AP004), severities, and `SourceKind` exemptions:
  - Only the lines a commit **adds** are checked — a legacy `DateTime.Now` no longer blocks unrelated edits to the same file
  - Comments, string, verbatim, and raw-string literals are stripped, tracking state across line boundaries
  - Generated files are skipped; test and migration sources are exempted per rule, matching `SourceClassifier`
  - `.Result` is reported only when the receiver is visibly task-like, so a domain `Result<T>` no longer trips the hook; `new HttpClient(handler)` is no longer reported
  - New: `// cwm:ignore [AP00n]` line suppression and `CWM_ANTIPATTERN_WARN_ONLY=1` report-only mode
  - Fixed a stale header comment that claimed the hook used `dotnet build` diagnostics

### Added
- **ADR-006** — Three-tier anti-pattern detection: why the pre-commit hook scans text, the Roslyn MCP is the authoritative pass, and analyzer packages are the team-wide gate; includes the process for adding a new rule
- `hooks/README.md` now documents the tier model, hook options, and the grep-vs-analyzer rationale

## [0.11.0] — 2026-07-22

Full-kit deep audit release: every skill, agent, rule, template, knowledge doc, hook, and the MCP server audited against current Claude Code docs and the mid-2026 .NET ecosystem, then fixed, modernized, and extended.

### Added
- **5 new MCP tools (15 → 20)** in CWM.RoslynNavigator 0.8.0, all read-only and capped:
  - `get_symbol_source` — bounded source of ONE member (doc comment + attributes included; types return signatures-only unless `includeBodies`; hard char cap with `Truncated` flag). Kills the "read the whole file for one method" token leak
  - `get_file_outline` — type/member skeleton of a file with line numbers, no bodies
  - `get_nuget_packages` — per-project PackageReference inventory, Central Package Management aware, no network calls
  - `get_endpoint_map` — Minimal API + controller route inventory with composed MapGroup prefixes and auth posture per endpoint (`authorized`/`anonymous`/`unmarked`)
  - `get_di_registrations` — DI map with lifetimes, duplicate registrations, and captive-dependency risks (singleton → scoped)
- **`/outdated`** — dependency health workflow: package inventory via `get_nuget_packages`, staleness + CVEs via the dotnet CLI, and a commercial-license trap screen (MediatR 13+, MassTransit 9+, FluentAssertions 8+, AutoMapper 15+)
- **`/arch-check`** — architecture conformance workflow: verifies dependency direction, layer violations, module boundary leaks, and cycles against the declared architecture (VSA, Clean, DDD, Modular Monolith) using project-graph analysis
- **Agent memory** — `memory: project` on code-reviewer, dotnet-architect, security-auditor, performance-analyst, and test-engineer so specialists learn project conventions across sessions
- **Read-only code reviewer** — `disallowedTools: Write, Edit` on code-reviewer; reviews can no longer modify code
- **Worktree-isolated cleanup** — `isolation: worktree` on refactor-cleaner
- **`.NET 11 Preview Watch`** section in `knowledge/dotnet-whats-new.md` (preview 6, GA Nov 10 2026, C# 15 union types/closed hierarchies/collection-expression arguments) with an explicit "don't generate net11.0/C# 15 code unless asked" guard, plus a 10 → 11 placeholder in `knowledge/breaking-changes.md`
- **CI hardening** — windows-latest hook-behavior job (Git Bash, real JSON payloads), shellcheck, docs-count consistency check (31 claims verified against the filesystem), cross-tool parity check, plugin/marketplace/CHANGELOG version-consistency check, cross-reference checks upgraded from warn to fail, YAML-parser-based frontmatter validation

### Fixed
- **`modern-csharp` taught pre-ship C# 14 extension-member syntax** — rewritten to the shipped `extension(Order order)` block form (this skill is the always-loaded baseline, so the error propagated everywhere)
- **Windows hooks were broken** — `post-edit-format.sh` silently no-opped on backslash paths and infinite-looped at drive roots; `pre-bash-guard.sh` over-blocked when jq was absent (the default on Windows) and wrote block reasons to stdout where Claude never saw them. All fixed and covered by the new Windows CI job
- **MCP server** — upgraded ModelContextProtocol 0.2.0-preview.1 → 1.4.1 stable; multi-target TFM misreport fixed (net10.0;net8.0 flavors now report correctly); workspace no longer bricks permanently after a transient reload failure (graceful status + 30s retry); `find_implementations` returned absolute paths; `find_dead_code` name-containment pre-filter upgraded to whole-identifier matching; uniform `maxResults` + `TotalFound` caps across all list-returning tools; `get_diagnostics` capped at 100 errors-first with per-severity totals; Central Package Management introduced; internal code now follows the kit's own rules (primary constructors, sealed, TimeProvider)
- **Skill correctness** — docker HEALTHCHECK no longer relaunches the app; opentelemetry setup no longer triggers the NotSupportedException its own anti-pattern warns about; testing fixtures use real xUnit v3 signatures (ValueTask); security-scan OWASP categories corrected and remapped to OWASP Top 10:2025; project-setup gutted to a tech-stack advisor (was a triple routing collision with dotnet-init/health-check); ddd/resilience/authentication/build-fix/dotnet-init sample-code errors fixed
- **Stale paths** — phantom `rules/dotnet/` references corrected to `.claude/rules/` in CLAUDE.md, CONTRIBUTING.md, `.codex/`, `.cursor/`, and docs; `/dotnet-init` restored to shorthand-guide tables

### Changed
- **FluentAssertions removed kit-wide** — `.Should()` examples in rules and skills replaced with xUnit built-in `Assert` (FA v8+ is commercial); new Assertions stance + Object Mapping (Mapperly over commercial AutoMapper) sections in `knowledge/package-recommendations.md`
- **Knowledge docs verified against NuGet/Microsoft Learn on 2026-07-22** — Wolverine 3.x → 6.x (with migration note), Aspire rewritten (correct `Aspire.Hosting.AppHost` package, v13, polyglot rebrand, aspire CLI), MediatR 14 + Lucky Penny licensing, MassTransit v9 released (v8 EOL end 2026), xunit.v3 3.x, Scrutor 7.x, Refit 13.x, WireMock.Net 2.x, StackExchange.Redis 3.x
- **Rules trimmed to exactly 600 lines** (was 669) by removing tables that duplicated their own DO/DON'T content
- **Three oversized pipeline skills converted to workflow format** (de-sloppify 277→116, health-check 320→112, security-scan 381→121) with deep content moved to `references/`
- **`disable-model-invocation: true`** on `/checkpoint` and `/wrap-up` (session-control commands are explicit-invocation only)
- `.mcp.json` declares explicit `type: "stdio"`; docs/SPEC.md and skill-benchmark-report.md stamped as historical snapshots
- Plugin version bumped to 0.11.0 (47 skills, 16 slash commands, 20 MCP tools)

## [0.10.0] — 2026-06-12

### Added
- **`/spec` — relentless specification workflow** — Turns a vague feature idea into an agreed, persisted spec before any planning or code. Structured questioning in 3–5-question rounds across nine dimensions (problem/users, scope, domain/data, API contract, authorization, edge cases, non-functionals, integrations, acceptance criteria) with a hard never-assume rule: every gap becomes a question, "I don't know" becomes an explicit deferred decision, and contradictions are challenged on the spot. Specs live at `docs/specs/<NNN>-<slug>.md` with a Draft → In Review → Approved lifecycle — approval is an explicit act, requires zero open questions, and implementation never starts from a Draft. Acceptance criteria feed `/plan` (steps), `/tdd` (first failing tests), and commit messages
- **`/plan` consumes specs** — Step 1 now checks `docs/specs/` for an approved spec as the source of truth, and recommends `/spec` before planning any feature too big to describe in one sentence

### Changed
- Plugin version bumped to 0.10.0 (45 skills, 14 slash commands)

## [0.9.0] — 2026-06-12

### Changed
- **Skill catalog consolidated: 60 → 44 skills** — Sixteen redundant skills merged or retired to cut the always-loaded description overhead (~25%) and eliminate routing collisions between overlapping skills. Surviving descriptions absorb the absorbed skills' trigger phrases, so all existing invocation phrases still route correctly:
  - **Workflow/knowledge twins merged** — each workflow now carries its methodology inline: `code-review` ← code-review-workflow + 80-20-review (blast-radius prioritization built in), `verify` ← verification-loop, `scaffold` ← scaffolding (per-architecture templates moved to `skills/scaffold/references/architecture-patterns.md`, loaded on demand), `migrate` ← migration-workflow (now also covers .NET version upgrades and NuGet updates), `build-fix` ← autonomous-loops (loop discipline: bounded iterations, progress detection, fail-safes; test-fix loop is a first-class variant), `wrap-up` ← wrap-up-ritual + session-management (owns the full handoff lifecycle including session start), `checkpoint` ← session-management's mid-session save
  - **Learning skills unified** — `instinct-system` absorbs instinct-status/export/import (now modes: "show instincts", "export instincts", "import instincts"), self-correction-loop (corrections = confirmed instincts at full confidence → MEMORY.md), and learning-log (discoveries → .claude/learning-log.md)
  - **`workflow-mastery` absorbs context-discipline** — token budget management and MCP-first navigation are now its Context Discipline section
  - **Retired** — `model-selection` (the always-loaded `.claude/rules/agents.md` already carries model guidance) and `split-memory` (native Claude Code memory supersedes it)
- **Slash commands: 16 → 13** — `/instinct-status`, `/instinct-export`, `/instinct-import` replaced by instinct-system modes; all other commands unchanged
- **New structural rule: one skill per concern** — CLAUDE.md and CONTRIBUTING.md now prohibit workflow/knowledge twin pairs and document the `references/` progressive-disclosure pattern for deep content
- **Cross-references updated everywhere** — AGENTS.md routing tables, README catalog and counts, docs/shorthand-guide.md, docs/longform-guide.md, .codex/AGENTS.md, all 5 templates, and 4 agent skill maps point at the surviving skills
- Plugin version bumped to 0.9.0

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
