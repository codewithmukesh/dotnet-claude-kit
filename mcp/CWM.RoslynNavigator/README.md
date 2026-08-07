# CWM.RoslynNavigator — Roslyn MCP Server

> Token-efficient .NET codebase navigation via Roslyn semantic analysis.

## Overview

CWM.RoslynNavigator is a Model Context Protocol (MCP) server that provides Claude Code with semantic understanding of .NET solutions. Instead of reading entire source files (hundreds of tokens), Claude can query for specific symbols, references, and type hierarchies (tens of tokens).

## Prerequisites

- .NET 10 SDK
- A .NET solution file (`.sln` or `.slnx`)

> **macOS/Linux note**: If `dotnet` on your `PATH` is a wrapper script (common with Homebrew), set `DOTNET_ROOT` to your .NET installation root — the directory containing `sdk/` and `host/` (e.g. `/usr/local/share/dotnet` for the official installer, `/opt/homebrew/Cellar/dotnet/<version>/libexec` for Homebrew). The server falls back to resolving the SDK via `dotnet --list-sdks` when `DOTNET_ROOT` is missing, but setting it explicitly is the most reliable option. See [Troubleshooting](#troubleshooting).

## Tools

| Tool | Description |
|------|-------------|
| `find_symbol` | Find where a type, method, or property is defined |
| `find_references` | All usages of a symbol across the solution |
| `find_implementations` | Types that implement an interface or derive from a base class |
| `find_callers` | All methods that call a specific method |
| `find_overrides` | Overrides of a virtual or abstract method |
| `find_dead_code` | Unused types, methods, and properties |
| `get_type_hierarchy` | Inheritance chain, interfaces, and derived types |
| `get_public_api` | Public members of a type without reading the full file |
| `get_symbol_detail` | Full signature, parameters, return type, and XML docs |
| `get_project_graph` | Solution project dependency tree |
| `get_dependency_graph` | Call dependency graph for a method |
| `get_diagnostics` | Compiler and analyzer warnings/errors |
| `get_test_coverage_map` | Heuristic test coverage by naming convention |
| `detect_antipatterns` | .NET anti-patterns (async void, sync-over-async, etc.) |
| `detect_circular_dependencies` | Circular dependency detection at project or type level |
| `get_symbol_source` | Exact source of one symbol — members in full, types as a signatures-only skeleton (`includeBodies` opt-in), capped by `maxChars` |
| `get_file_outline` | Skeleton of one file: namespace, types, member signatures with line numbers — no bodies |
| `get_nuget_packages` | PackageReference inventory per project with versions (CPM-aware, no network calls) |
| `get_endpoint_map` | ASP.NET Core route inventory: Minimal APIs (MapGroup-composed) + controllers, with auth posture per endpoint |
| `get_di_registrations` | DI registration map with duplicate detection and captive-dependency (singleton→scoped) risk flags |
| `resolve_stack_trace` | Map an exception stack trace onto the solution — undoes async/lambda/local-function rewrites, marks which frames are yours |
| `analyze_change_impact` | Blast radius of changing one symbol: references by project and file, implementations and overrides that must move with it, transitive callers, risk rating |

### Symbol names

Every tool that takes a symbol accepts three forms, from loosest to tightest:

```
CreateOrderAsync                              # bare — may be ambiguous
OrderService.CreateOrderAsync                 # type-qualified
MyApp.Orders.OrderService.CreateOrderAsync    # fully qualified
```

Parameter lists, generic arguments, and metadata arity are ignored, so
`IRepository<Order>.GetByIdAsync(Guid, CancellationToken)` resolves the same as
`IRepository.GetByIdAsync`. Qualifiers match on segment boundaries — `derService.Get`
will not match `OrderService.Get`.

When a bare name matches several distinct symbols, tools return an `AmbiguousMatch` error
listing the candidates rather than silently picking one. Overloads, partial declarations,
and per-target-framework duplicates count as one symbol, not an ambiguity.

### Errors

Failures return a structured response with a stable `Error` code, never an empty result
set — so "this symbol does not exist" is always distinguishable from "this symbol exists
and has no references".

| Code | Meaning |
|------|---------|
| `SymbolNotFound` | No symbol by that name. Check spelling, or search with `find_symbol`. |
| `AmbiguousMatch` | Several distinct symbols match. `Candidates` lists them; re-query fully qualified, or pass `file`/`line`. |
| `WrongSymbolKind` | Resolved, but to the wrong kind (e.g. a method where a type was required). |
| `FileNotFound` | The path is not part of any project in the solution. |
| `NoSource` | A metadata symbol, or one with no member declaration. |
| `InvalidArgument` | Malformed input — an empty stack trace, for instance. |

### Result caps

Every list-returning tool accepts `maxResults` and reports four fields:

| Field | Meaning |
|-------|---------|
| `Count` | Entries in this response |
| `TotalFound` | Entries that matched in total |
| `Truncated` | Whether the cap dropped entries — **the field to branch on** |
| `Limit` | The cap that was applied; re-query above it to see the rest |

`Truncated` is stated rather than inferred from `Count` vs `TotalFound`, which gets the
boundary case wrong when the result set happens to be exactly the size of the limit.

Defaults: 50 for symbol/list tools (`find_symbol`, `find_references`,
`find_implementations`, `find_callers`, `find_overrides`, `find_dead_code`,
`get_public_api`, `get_type_hierarchy`, `get_test_coverage_map`,
`resolve_stack_trace`, `analyze_change_impact`), 100 for `detect_antipatterns`,
`get_diagnostics`, `get_endpoint_map`, `get_di_registrations`, `get_nuget_packages`, and
`get_dependency_graph` nodes, 200 for `get_file_outline` members. `get_symbol_source`
caps by characters instead (`maxChars`, default 8000, with a `Truncated` flag).
`get_diagnostics` orders errors first and always includes per-severity totals, so a
capped response never hides the important picture.

### Generated code

`find_symbol`, `find_references`, `find_callers`, `find_implementations`, and
`find_overrides` mark each result with `IsGenerated`, so an agent does not follow a hit
into a `.g.cs` file that the next build overwrites. Detection uses file conventions
(`.g.cs`, `.Designer.cs`, `obj/`), an `<auto-generated>` header, and Roslyn's
`IsImplicitlyDeclared` for compiler-synthesized members.

## Signal Quality

Analysis tools are only useful if their output can be trusted without hand-triage.
Three mechanisms keep the noise floor low.

### Source classification

Every syntax tree is classified as **production**, **test**, **generated**, or
**migration**, using the path *relative to the solution root* plus project references
and declaration attributes. Generated code is never reported by any detector. Each
detector declares which kinds it applies to — a missing `CancellationToken` on an
xUnit `[Fact]` is not a defect, so that detector runs on production only.

`detect_antipatterns` takes `scope`: `production` (default) or `all`. The response
summary always reports the file counts per kind, so nothing is silently skipped.

### Confidence levels

Findings carry `confidence`:

- **`high`** — wrong regardless of context. Safe to grade and to act on directly.
- **`medium`** — suspicious, but with legitimate uses the detector cannot rule out:
  a `catch (Exception)` that logs and rethrows, an EF query inside a command handler
  that may edit downstream. Review items, never grade inputs.

Filter with `confidence: "high"` to get only graded findings. `find_dead_code` uses
the same scale and reports `conventionFiltered` — symbols excluded because a
reference search cannot see EF entity-configuration scanning, hosted-service
registration, or extension-method dispatch.

`get_test_coverage_map` returns `applicable: false` with a reason when its
structural metric does not fit the codebase (integration- or feature-driven suites),
rather than reporting a misleading percentage.

### Suppression

A codebase can declare that a detector is wrong for it. Suppressed findings are
counted in `summary` and excluded from the violation list — visible, never hidden.

**`.cwm-navigator.json`** at the solution root (discovery walks up to the repo root):

```json
{
  "antipatterns": {
    "disable": ["AP008"],
    "suppress": [
      {
        "id": "AP005",
        "paths": ["src/Outbox/**", "src/Comms/**"],
        "reason": "bounded resilience wrappers — see CLAUDE.md §7"
      },
      { "id": "*", "paths": ["src/Legacy/**"], "reason": "frozen module" }
    ]
  }
}
```

Paths are globs supporting `**`, `*`, and literal segments, matched against the
solution-relative path. An `id` of `*` suppresses every detector under those paths.

**Inline**, for one-off exceptions — on the finding's line or the line above:

```csharp
// cwm:ignore AP004 — documented wall-clock requirement for AWS SigV4
var timestamp = DateTime.UtcNow;
```

**Attribute**, covering a whole declaration:

```csharp
[SuppressMessage("CWM", "AP005", Justification = "application boundary")]
public async Task DrainAsync() { }
```

## Installation

### As a Global Tool (Recommended)

```bash
# Install once
dotnet tool install -g CWM.RoslynNavigator

# Register with Claude Code (no --solution needed!)
claude mcp add --scope user cwm-roslyn-navigator -- cwm-roslyn-navigator
```

The server auto-discovers the solution from MCP workspace roots. No per-project configuration needed.

You can also add it manually to your Claude Code global settings (`~/.claude/settings.json`):

```json
{
  "mcpServers": {
    "cwm-roslyn-navigator": {
      "command": "cwm-roslyn-navigator"
    }
  }
}
```

**Optional override**: Pass `--solution <path>` to specify a solution file or directory explicitly:

```json
{
  "mcpServers": {
    "cwm-roslyn-navigator": {
      "command": "cwm-roslyn-navigator",
      "args": ["--solution", "${workspaceFolder}"]
    }
  }
}
```

### As a Local Tool (per-repo)

```bash
dotnet new tool-manifest   # if you don't have one
dotnet tool install CWM.RoslynNavigator
```

Then add to your project's `.mcp.json`:

```json
{
  "mcpServers": {
    "cwm-roslyn-navigator": {
      "command": "dotnet",
      "args": ["tool", "run", "cwm-roslyn-navigator", "--", "--solution", "${workspaceFolder}"]
    }
  }
}
```

### From Source (for contributors)

```bash
dotnet run --project mcp/CWM.RoslynNavigator/src/CWM.RoslynNavigator.csproj -- --solution /path/to/your/Solution.sln
```

### As a Container

Use this when you do not want a .NET 10 SDK on the host — it sidesteps the
`DOTNET_ROOT` setup that a global-tool install needs on macOS and Linux (see
[Troubleshooting](#no-net-sdks-were-found-on-startup-macoslinux)).

The image is produced by the .NET SDK's container publishing, so there is no
Dockerfile to maintain:

```bash
cd mcp/CWM.RoslynNavigator
dotnet publish src/CWM.RoslynNavigator.csproj -c Release --os linux --arch x64 /t:PublishContainer
```

That builds `cwm-roslyn-navigator:latest` locally. Register it with Claude Code by
bind-mounting the solution at the image's working directory, `/workspace`:

```bash
claude mcp add --scope user cwm-roslyn-navigator -- \
  docker run -i --rm -v "$PWD:/workspace" cwm-roslyn-navigator:latest --solution /workspace
```

`-i` is required: the server speaks MCP over stdio, so the container must keep
stdin open. The mount is read-only as far as this server is concerned — every tool
is read-only — but leave it writable if the same checkout is being built on the host.

**Base image**: `mcr.microsoft.com/dotnet/sdk:10.0`, not a runtime image. `MSBuildLocator`
needs a real SDK installation to open a solution; on `aspnet` or `runtime` the server
starts and then fails every call with "No .NET SDKs were found".

To publish somewhere other than the local daemon, set the registry on the command line:

```bash
dotnet publish src/CWM.RoslynNavigator.csproj -c Release --os linux --arch x64 \
  /t:PublishContainer -p:ContainerRegistry=ghcr.io -p:ContainerRepository=you/cwm-roslyn-navigator
```

## Solution Discovery

The server resolves the solution file in this order:

1. **Explicit `--solution` argument** — Pass a `.sln`/`.slnx` file path directly, or a directory to scan recursively
2. **Working directory scan** — If no argument, scans the current working directory recursively for solution files
3. **MCP roots discovery** — On the first tool call, if no solution was found at startup, the server requests workspace roots from the MCP host (e.g., Claude Code) and scans those directories. This is a one-shot attempt — if no solution is found, it won't retry. This enables true zero-arg global tool operation.
4. **Deterministic selection** — Shallowest solution wins (BFS); within the same depth, alphabetical (case-insensitive) ordering is used

### Recursive Search

Discovery searches up to **3 levels deep** using breadth-first search, so a solution at `src/MyApp.sln` or `src/backend/Api/Api.sln` is found automatically.

The following directories are skipped during scanning: `.git`, `.vs`, `.idea`, `node_modules`, `bin`, `obj`, `packages`, `artifacts`, `TestResults`, `.claude`.

## Architecture

```
Program.cs              → MSBuildLocator → Host → MCP stdio transport
WorkspaceManager.cs     → MSBuildWorkspace lifecycle, file watching, compilation caching
WorkspaceInitializer.cs → BackgroundService triggers workspace load on startup
SolutionDiscovery.cs    → Auto-detect .sln/.slnx from args or working directory
SymbolResolver.cs       → Cross-project symbol resolution with disambiguation
Tools/                  → MCP tool implementations (22 read-only tools)
Responses/              → Token-optimized JSON response DTOs
```

## Scaling

| Solution Size | Strategy |
|---|---|
| Small (1-15 projects) | Load entire workspace on startup, warm compilations in parallel (4 concurrent) |
| Large (15-50 projects) | Lazy-load compilations on first query per project with LRU cache (30 max) |
| Enterprise (50+) | Lazy loading + LRU eviction + warn if query touches unloaded project |

## Troubleshooting

### "No .NET SDKs were found" on startup (macOS/Linux)

`MSBuildLocator` resolves the SDK via `hostfxr_resolve_sdk2`, which locates `dotnet` on `PATH` and expects the SDK layout relative to that binary. When `dotnet` is a wrapper script (Homebrew) or `DOTNET_ROOT` is unset — typical for MCP servers launched outside an interactive shell — resolution fails with:

```
No .NET SDKs were found.
Unhandled exception. System.InvalidOperationException: Failed to find all versions of .NET Core MSBuild.
```

The server automatically falls back to `dotnet --list-sdks` to locate the SDK. If that also fails, set `DOTNET_ROOT` explicitly in the MCP registration:

```bash
claude mcp add-json --scope user cwm-roslyn-navigator \
  '{"type":"stdio","command":"cwm-roslyn-navigator","env":{"DOTNET_ROOT":"/usr/local/share/dotnet"}}'
```

Or export it in your shell profile (`~/.zshrc` / `~/.bashrc`):

```sh
export DOTNET_ROOT=/usr/local/share/dotnet
```

## Development

```bash
# Build
dotnet build mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx

# Run tests
dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx

# Run manually against a directory
dotnet run --project mcp/CWM.RoslynNavigator/src/CWM.RoslynNavigator.csproj -- --solution /path/to/your/project/

# Run manually against a solution file
dotnet run --project mcp/CWM.RoslynNavigator/src/CWM.RoslynNavigator.csproj -- --solution /path/to/your/Solution.sln
```

## Changelog

### 0.10.0

Correctness pass on symbol resolution and response contracts.

- **Qualified symbol names** — resolution accepted only a bare declared name, so
  `find_references("OrderService.CreateOrderAsync")` returned an empty result,
  indistinguishable from a symbol with no references. Type- and namespace-qualified
  names now resolve, with parameter lists, generic arguments, and metadata arity
  ignored, and suffix matching anchored on segment boundaries. See [Symbol names](#symbol-names).
- **Structured errors** — nine tools returned an empty list when a symbol could not be
  resolved, and `ResolveSymbolAsync` ended in `return symbols[0]`, so a bare name matching
  several types silently answered about the wrong one with full confidence. Tools now
  return `SymbolNotFound` / `AmbiguousMatch` (with candidates) / `WrongSymbolKind` /
  `FileNotFound` / `NoSource`. See [Errors](#errors).
- **`Truncated` and `Limit`** on every list response, so callers stop inferring
  truncation from `Count` vs `TotalFound` — a comparison that gets the exactly-at-limit
  case wrong. See [Result caps](#result-caps).
- **`IsGenerated`** on navigation results, so an agent does not follow a hit into a
  `.g.cs` file the next build overwrites. See [Generated code](#generated-code).
- **`find_dead_code` grades against reflection** — a type resolved via
  `Type.GetType("...")` has zero references and was reported at high confidence.
  A name appearing in any string literal now drops to `low` with the reason attached,
  and `AssemblyScanningDetected` reports whether the solution registers types by scanning
  at all.
- **New: `resolve_stack_trace`** — maps an exception onto the solution, undoing compiler
  rewrites for async state machines, lambdas, and local functions, and pointing at the
  topmost frame in your own code.
- **New: `analyze_change_impact`** — blast radius for one symbol in a single call:
  references by project and file, implementations and overrides that must change with a
  signature, transitive callers, assembly-boundary exposure, and a rated risk with its
  rationale.
- **Reload only on real build-file changes** — csproj comparison was timestamp-only, so a
  branch switch or formatter rewrite triggered a full MSBuild re-evaluation for identical
  content. Build files now carry a content hash, with the timestamp kept as the pre-filter.
  `Directory.Build.props`, `Directory.Packages.props`, `Directory.Build.targets`,
  `global.json`, and `nuget.config` are tracked too — previously invisible despite
  governing every project beneath them.
- **Structural scan moved off the request path** — the recursive walk for newly added
  source files ran inline, so one arbitrary tool call per cooldown paid for a full
  directory enumeration. It now runs in the background with the same staleness bound.
- **Container distribution** — `dotnet publish /t:PublishContainer` produces an image on
  the SDK base (required by MSBuildLocator), removing the host-SDK and `DOTNET_ROOT`
  setup. See [As a Container](#as-a-container).
- **Fixed: compilation cache survived a reload** — a reload mints fresh `ProjectId`s, and
  two of four `LoadSolutionAsync` call sites did not clear the cache first, leaving
  orphaned entries pinning whole Roslyn compilations in memory.

### 0.9.0

Analysis accuracy pass. On a 34-project, 106K-line codebase, `detect_antipatterns`
went from **3,512 findings (~95% false positives) to 55 — 3 high-confidence**, all
verified genuine. `find_dead_code` went from 89 to 3. See [Signal Quality](#signal-quality).

- **Source classification** — every syntax tree is classified production / test /
  generated / migration from the solution-relative path, project references, and
  declaration attributes. Generated code is never reported. Detectors declare which
  kinds they apply to. `detect_antipatterns` gains `scope` (`production` default, `all`).
- **Confidence levels** — findings carry `high` (wrong regardless of context) or
  `medium` (needs judgement). New `confidence` filter. Only `high` should be graded.
- **Suppression** — `.cwm-navigator.json` (path globs and detector disables),
  inline `// cwm:ignore APXXX — reason`, and `[SuppressMessage("CWM", "APXXX")]`.
  Suppressed findings are counted in the summary, never silently dropped.
- **Complete summaries** — `detect_antipatterns` returns per-detector counts
  (high/medium/suppressed) plus file counts by kind, accurate even when the
  violation list is truncated. Callers no longer need to sample a dump to triage.
- **Findings carry `member`** — the enclosing `Type.Method`, so a finding can be
  judged without opening the file.
- **Fixed AP010 (EF AsNoTracking)** — no longer fires on aggregates (`CountAsync`,
  `AnyAsync`, `SumAsync` — these never populate the change tracker), on
  `db.Database.SqlQuery` or `ChangeTracker.Entries` (not entity queries), or on
  load-to-mutate reads (detected via `SaveChanges`, DbSet mutation resolved
  semantically, raw-SQL row locks, or property assignment on the result). High
  confidence is now reserved for read-shaped methods.
- **Fixed AP006 (logging templates)** — only the message-template argument is
  inspected, and templates the compiler folds to a constant (adjacent string
  literals wrapping a long template, or a `const` prefix) are no longer flagged.
- **Fixed AP005/AP007 (catch blocks)** — a block that logs or rethrows is `medium`,
  not `high`; an empty block containing an explanatory comment is cleared, matching
  the tool's own suggestion; empty `catch (OperationCanceledException)` is
  recognised as the cooperative-shutdown idiom.
- **Fixed AP009 (CancellationToken)** — production only, and skips test-attributed
  methods, `HttpContext` parameters, middleware `Invoke`/`InvokeAsync`, `Main`, and
  methods sourcing an ambient token. Reported at `medium` — whether a token belongs
  on a signature depends on the caller.
- **Fixed AP002 (sync-over-async)** — now semantic: `.Result` must resolve to
  `Task<T>.Result`, so a domain `Result<T>` type is not flagged.
- **Fixed AP008 (pragma restore)** — a restore only closes a *preceding* disable.
- **`find_dead_code`** — filters convention-discovered symbols (EF
  `IEntityTypeConfiguration`, hosted services, migrations, model snapshots,
  extension-method hosts) and reports the count as `conventionFiltered`. Survivors
  whose names match a convention suffix return `medium` with an explanatory `note`.
- **`get_test_coverage_map`** — returns `applicable: false` with a reason and the
  real test-method count when the structural metric does not fit the codebase,
  instead of a misleading percentage. `/health-check` now records that dimension as
  "Not assessed" and excludes it from the GPA.

### 0.8.0

- **5 new tools (15 → 20):**
  - `get_symbol_source` — exact source of one symbol without reading the whole file: members return full source (doc comment + attributes included), types return a signatures-only skeleton unless `includeBodies` is set; hard `maxChars` cap (default 8000) with a `Truncated` flag.
  - `get_file_outline` — token-cheap skeleton of one file: namespace, types, member signatures with line numbers, no bodies; nested types up to 3 levels.
  - `get_nuget_packages` — per-project PackageReference inventory with versions, resolving through `Directory.Packages.props` when central package management is used; reports `cpm` per project; no network calls.
  - `get_endpoint_map` — ASP.NET Core route inventory: Minimal API `Map*` calls with MapGroup prefixes composed from string literals, controller actions with `Http*`/`Route` attributes, and auth posture per endpoint (`authorized`/`anonymous`/`unmarked`). Best-effort static analysis; limitations documented in the tool description.
  - `get_di_registrations` — DI registration map from `Add{Singleton,Scoped,Transient}`/`AddKeyed*`/`TryAdd*` calls with duplicate-registration flags and captive-dependency risks (singleton implementations whose constructors take scoped services).
- **ModelContextProtocol SDK upgraded to 1.4.1 stable** (from 0.2.0-preview.1) — the server now runs on the SDK's first stable line. Tool discovery, stdio transport, and MCP roots discovery are unchanged from a client's perspective.
- **Uniform result caps** — every list-returning tool now accepts `maxResults` and reports `TotalFound` (see [Result caps](#result-caps)). Previously `find_symbol`, `find_callers`, `find_overrides`, `find_implementations`, `get_public_api`, `get_type_hierarchy`, and `get_diagnostics` returned unbounded lists.
- **Fixed: multi-target TFM reporting** — `get_project_graph` reported the first `<TargetFrameworks>` entry for every flavor of a multi-targeted project (the net8.0 flavor of a `net10.0;net8.0` project claimed net10.0). Flavor detection now uses Roslyn's flavor project name and per-flavor preprocessor symbols.
- **Fixed: `find_implementations` returned absolute file paths** — now solution-relative like every other tool.
- **Fixed: workspace no longer gets stuck in Error state** — a failed reload (e.g. a `.csproj` saved mid-write) previously surfaced as a raw MCP error and left the server broken until restart. Load/refresh failures now return the graceful status response and the server retries the known solution path automatically (30s cooldown).
- **`get_diagnostics`** — describes itself honestly as compiler diagnostics (NuGet analyzers are not run), excludes hidden diagnostics from `severityFilter: "all"`, orders errors first, and reports per-severity totals.
- **`find_dead_code`** — the fast pre-filter now matches whole identifiers instead of substrings (a dead `Order` type is no longer masked by `OrderService`), and the heuristic is disclosed in the tool description.
- **`get_type_hierarchy`** — derived types for an interface now include derived interfaces and implementing types (previously empty).
- **`get_dependency_graph`** — framework-namespace filtering uses exact segment matching (`SystemX.*` is no longer skipped); adds a node cap with a `Truncated` flag.
- **Central package management** — `Directory.Packages.props` now pins all package versions; Roslyn 5.6.0, MSBuildLocator 1.11.2, Microsoft.Extensions.* 10.0.10, xunit.v3 3.2.2.

### 0.7.1

- **Fixed: logs corrupted the MCP stdio stream** ([#10](https://github.com/codewithmukesh/dotnet-claude-kit/issues/10)) — All console logging now goes to stderr. The MCP stdio transport reserves stdout for JSON-RPC; log lines on stdout caused clients to drop the connection with "JSON Parse error".
- **Fixed: "No .NET SDKs were found" on macOS/Linux** ([#9](https://github.com/codewithmukesh/dotnet-claude-kit/issues/9)) — When `MSBuildLocator.RegisterDefaults()` fails (wrapper-script `dotnet` on PATH with `DOTNET_ROOT` unset), the server falls back to resolving the SDK via `dotnet --list-sdks` and registers it with `RegisterMSBuildPath`.

### 0.7.0

- **Performance optimizations across all tools:**
  - `find_references` — Document text caching (200 async calls → ~10) + `maxResults` cap (default 50)
  - `find_dead_code` — Fast name-based pre-filter skips ~80-90% of expensive Roslyn reference searches
  - `get_dependency_graph` — O(1) file-to-project lookup via pre-built dictionary
  - `detect_circular_dependencies` — Reduced `ToDisplayString()` allocations with `IsUserType()` helper
  - `SymbolResolver` — `SymbolEqualityComparer.Default` for dedup instead of string allocation
  - Parallel compilation warming (`Parallel.ForEachAsync`, max 4 concurrent) for ~2-4x faster startup
  - Consolidated 4 duplicate `MakeRelativePath` into shared `SymbolResolver.MakeRelativePath`

### 0.6.0

- **MCP roots discovery** — When no solution is found at startup, tools now request workspace roots from the MCP host on the first call and auto-discover the solution. One-shot, thread-safe attempt via `EnsureReadyOrStatusAsync`.
- **Project restructured** — Source moved to `src/` and `tests/` layout with a new `.slnx` solution file.
- **Unified readiness check** — All 15 tools use `EnsureReadyOrStatusAsync` instead of inline state checks, reducing boilerplate and ensuring consistent lazy-init behavior.

### 0.5.2

- Recursive solution discovery (BFS up to 3 levels deep).

### 0.5.1

- Expanded README with installation, architecture, and scaling docs.

### 0.5.0

- Initial NuGet release as a `dotnet tool`. 15 read-only Roslyn MCP tools.
