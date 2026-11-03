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
| `find_symbol` | Find where a type, method, or property is defined. `match`: `exact` (default), `prefix`, `camelhump`, or `substring` — see [Fuzzy search](#fuzzy-search) |
| `find_references` | All usages of a symbol across the solution |
| `find_implementations` | Types that implement an interface or derive from a base class |
| `find_callers` | All methods that call a specific method, including calls through an interface or base member (`Via`: direct / interface / base / implementation, plus `ViaSymbol`) |
| `find_overrides` | Overrides of a virtual or abstract method |
| `find_dead_code` | Unused types, methods, and properties |
| `get_type_hierarchy` | Inheritance chain, interfaces, and derived types |
| `get_public_api` | Public members of a type without reading the full file |
| `get_symbol_detail` | Full signature, parameters, return type, and XML docs |
| `get_project_graph` | Solution project dependency tree; with several solutions loaded, also which solution(s) each project belongs to |
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
| `analyze_change_impact` | Blast radius of changing one symbol: references by project and file, implementations and overrides that must move with it, transitive callers (interface/base dispatch included, counted in `IndirectCallers`), risk rating |
| `get_token_savings` | Tokens this server has saved in this repository, with a per-tool leaderboard. Opt-in — see [Token savings](#token-savings) |
| `get_symbol_at` | The symbol at a file position — pass the identifier `text` (or a `column`) on a `line`. Kind, signature, containing type, definition, `SymbolId`, and declared/expression types with nullability |
| `preview_rename` | What a semantic rename **would** change — per-line old/new text across implementations, overrides and call sites, plus compiler errors it would introduce. Never writes to disk or the workspace — see [Rename preview](#rename-preview) |
| `get_workspace_status` | What is loaded and how completely: solutions, project/document counts, warm-up progress, snapshot version, and every project that loaded or compiled incompletely — see [Workspace health](#workspace-health) |

### Symbol names

Every tool that takes a symbol accepts three forms, from loosest to tightest:

```
CreateOrderAsync                              # bare — may be ambiguous
OrderService.CreateOrderAsync                 # type-qualified
MyApp.Orders.OrderService.CreateOrderAsync    # fully qualified
```

A fourth form names one symbol exactly, overload included — a **SymbolId**, the
documentation-comment ID that `find_symbol`, `get_symbol_at`, and `AmbiguousMatch`
candidates return:

```
M:MyApp.Orders.OrderService.CreateOrderAsync(System.String,System.Threading.CancellationToken)
T:MyApp.Orders.OrderService
```

Pass it wherever a name goes; it is resolved centrally, so every symbol-taking tool
accepts it. The `X:` prefix cannot begin a C# name, so it never collides with one. Roslyn
may append `~ReturnType` to method IDs; both forms resolve.

Parameter lists, generic arguments, and metadata arity are ignored, so
`IRepository<Order>.GetByIdAsync(Guid, CancellationToken)` resolves the same as
`IRepository.GetByIdAsync`. Qualifiers match on segment boundaries — `derService.Get`
will not match `OrderService.Get`.

When a bare name matches several distinct symbols, tools return an `AmbiguousMatch` error
listing the candidates rather than silently picking one. Overloads, partial declarations,
and per-target-framework duplicates count as one symbol, not an ambiguity. Each candidate's
`File` and `Line` can be passed straight back as `file`/`line`.

### Fuzzy search

`find_symbol` matches the exact name by default. When you only know part of it, set
`match`:

| `match` | Finds | Example for `GetByIdAsync` |
|---------|-------|----------------------------|
| `exact` | The name itself (case-sensitive) | `GetByIdAsync` |
| `prefix` | + names starting with the text | `getby` |
| `camelhump` | + names whose humps start with the query's humps, anchored on the first | `GBIA`, `gbia`, `GetByAsync` |
| `substring` | + names containing the text anywhere | `IdAsync` |

Each mode includes the tighter ones. Results are ranked exact > prefix > camelhump >
substring, then types before members, then by project and qualified name, and each
carries its `Match` tier. A qualifier still matches exactly — `OrderService.Cre` with
`prefix` finds only `OrderService.CreateOrderAsync`. The search runs as a predicate over
each project's declaration table (names only, no binding), so it costs about as much as
an exact lookup.

### Errors

Failures return a structured response with a stable `Error` code, never an empty result
set — so "this symbol does not exist" is always distinguishable from "this symbol exists
and has no references".

| Code | Meaning |
|------|---------|
| `SymbolNotFound` | No symbol by that name. `DidYouMean` lists up to five close names (typos, prefixes, camel humps) when there are any. |
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

### Workspace health

MSBuildWorkspace keeps going when a project fails to evaluate, a project reference points
nowhere, or restore never ran — the solution opens and every tool answers from whatever did
load. `get_workspace_status` makes that visible. It reports `Healthy` plus, per incomplete
project, the issues found:

- declared in a solution but not loaded (missing or unloadable project file)
- a workspace load failure naming the project (e.g. a missing `ProjectReference`)
- no source documents (only the SDK's `obj/` files)
- compilation threw, or the core library is unresolved (references missing — run `dotnet restore`)
- an analyzer or source generator file that does not exist

It also carries the raw `LoadDiagnostics`, background warm-up progress (`WarmUp`:
`Compiled` of `Total`), `SnapshotVersion`, which increments on every reload or
in-place document refresh, and `Reloading` / `ReloadReason` / `LastReloadError` for
background reloads (see [Scaling](#scaling)).

When the workspace is incomplete, any tool result that comes back **empty** — `TotalFound`
(or `Count`) of 0, or a `SymbolNotFound` / `FileNotFound` error — gains a `WorkspaceWarning`
property naming the incomplete projects, so "nothing found" is not mistaken for "nothing
exists". Non-empty results and healthy workspaces are untouched.

### Rename preview

`preview_rename` runs Roslyn's renamer (`Renamer.RenameSymbolAsync`) on the immutable
`Solution` snapshot and diffs the result. The workspace is never updated and nothing is
written — the server's tools stay read-only; apply the edits yourself.

| Field | Meaning |
|-------|---------|
| `Changes` | One entry per changed line: `File`, `Line`, `Old`, `New` (trimmed), capped by `maxResults` with the usual `Count`/`TotalFound`/`Truncated`/`Limit` |
| `Files` | Distinct files touched |
| `Conflicts` | Errors present after the rename but not before (e.g. `CS0111` when the new name duplicates a member), up to 20; `ConflictCount` is the total |

Conflicts are checked where a rename can break things without a full rebuild: semantic
errors in the changed documents, and declaration errors (duplicate members, clashing type
names) across the projects that contain them. Roslyn's own conflict-resolution details are
not public API, so they are not reported separately. `newName` must be a valid C#
identifier (`@` escapes a keyword); `renameOverloads`, `renameInStrings`, and
`renameInComments` mirror the IDE options.

## Token savings

This server exists on the premise that a structured symbol query costs far fewer tokens
than reading and grepping source. `get_token_savings` measures that premise rather than
asserting it.

**Off by default.** Set `CWM_ROSLYN_TELEMETRY=on` and restart the server to collect.

```bash
CWM_ROSLYN_TELEMETRY=on cwm-roslyn-navigator --solution MySolution.sln
```

### What is collected, and where it goes

Nothing is transmitted. There is no network call anywhere in the collection path, no
identity, and no cross-machine correlation. One line per tool call is appended locally:

```
%LOCALAPPDATA%/cwm-roslyn-navigator/repos/<hash-of-solution-path>/
  repo.json     solution path and scale
  calls.jsonl   {"ts":…,"tool":"find_symbol","ok":true,"results":3,
                 "respTok":180,"baseTok":4200,"files":3,"ms":42,"state":"ok"}
```

It lives outside your repository, so there is no `.gitignore` entry to remember and no way
to commit it by accident. Delete the directory to erase everything. The log rolls at 50 MB.

### How the number is calculated

Savings are a counterfactual — what *would* have been spent — so the method matters more
than the figure. The baseline is the **file-read floor**: the on-disk size of the source
files each answer was actually derived from. Without the tool, those files are the minimum
that would have been read to answer the same question.

Three rules keep it conservative, and all three push the number **down**:

| Rule | Why |
|------|-----|
| A file counts **once per session** | Without the tool it would have been read once and stayed in context. Charging it again on every later call is the main way this metric could inflate. |
| Per-call deduplication | Twenty references in one file is one file to read. |
| Capped at repository size | No single call can claim to have saved more than the repo contains. |

Errors, zero-result calls, and calls made while the workspace was still loading contribute
nothing. The baseline also ignores the wrong files a text search would have surfaced first
and the false positives it would have returned — real costs that are simply not counted.
The result understates savings, which is the correct direction of error for a public claim.

### Aggregating across repositories

`get_token_savings` answers for the repository in front of you. The `stats` CLI aggregates
every repository and adds what telemetry alone cannot see — real measured spend and
adoption rate, both read from Claude Code transcripts:

```bash
cd mcp/CWM.RoslynNavigator
dotnet run --project stats/CWM.RoslynNavigator.Stats.csproj              # table
dotnet run --project stats/CWM.RoslynNavigator.Stats.csproj -- --markdown
dotnet run --project stats/CWM.RoslynNavigator.Stats.csproj -- --json
dotnet run --project stats/CWM.RoslynNavigator.Stats.csproj -- --repo myapp
```

**Adoption rate** is the share of C# navigation that went through the MCP rather than raw
file reads:

```
adoption = roslyn_calls / (roslyn_calls + Read/Grep/Glob calls targeting .cs)
```

Repositories where the server was never used are excluded from the denominator — including
them would drag the aggregate down for reasons unrelated to the tools. The denominator
counts only searches that demonstrably targeted C#, so an unfiltered `Grep` is left out;
that biases adoption **upward**, and the report says so.

Low adoption in a repository where the server *is* configured is usually the more
actionable finding: it means a tool description is not attracting the work it should.

### The one number that is not measured

`bytesPerToken` is currently **3.5, an uncalibrated estimate**. C# is denser than prose, so
it sits below the ~4 commonly cited for English, but that value has not been derived from
measured token counts. Every report says so, and `get_token_savings` returns
`DivisorCalibrated: false` until it has been.

Calibrating means sampling real `.cs` files, getting exact counts from the Anthropic
`count_tokens` endpoint, and regressing bytes against tokens. The CLI does this:

```bash
dotnet run --project stats/CWM.RoslynNavigator.Stats.csproj -- calibrate \
  --api-key $ANTHROPIC_API_KEY --path /path/to/a/dotnet/repo --sample 200
```

`count_tokens` is free and returns exact counts, so anyone can reproduce the result and
check the claim. The run is manual and lives only in this CLI — the MCP server never calls
it and stays fully offline. Sampling is seeded, so a run can be repeated exactly.

Apply the output to `TokenEstimator` and record the sample size and R² beside the constant.
A low R² would itself be a finding: it would mean C# token density varies too much for any
single divisor, and the metric should report a range rather than a point.

**This has not been run yet** — it needs an API key. Until it is, treat the figure as
indicative rather than precise.

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

Working across several solutions (e.g. microservices)? Repeat `--solution`, or add
`--all-solutions` — see [Multiple Solutions](#multiple-solutions).

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

1. **Explicit `--solution` argument** — Pass a `.sln`/`.slnx` file path directly, or a directory to scan recursively. Repeatable — see [Multiple Solutions](#multiple-solutions)
2. **Working directory scan** — If no argument, scans the current working directory recursively for solution files
3. **MCP roots discovery** — On the first tool call, if no solution was found at startup, the server requests workspace roots from the MCP host (e.g., Claude Code) and scans those directories, loading a solution from **each** root that has one. If no root has a solution yet, it looks again on a later call after 30 s. When the host sends `notifications/roots/list_changed`, the next call rediscovers; a different set of solutions reloads in the background while the current one keeps answering. Roots never override a solution configured with `--solution` or found in the working directory. This enables true zero-arg global tool operation.
4. **Deterministic selection** — Shallowest solution wins (BFS); within the same depth, alphabetical (case-insensitive) ordering is used. With `--all-solutions`, every solution found is loaded instead.

### Recursive Search

Discovery searches up to **3 levels deep** using breadth-first search, so a solution at `src/MyApp.sln` or `src/backend/Api/Api.sln` is found automatically.

The following directories are skipped during scanning: `.git`, `.vs`, `.idea`, `node_modules`, `bin`, `obj`, `packages`, `artifacts`, `TestResults`, `.claude`.

## Multiple Solutions

A feature in a microservices codebase often spans several solutions. Instead of registering
one server per solution, load them all into one:

| Flag | Effect |
|------|--------|
| `--solution <path>` / `-s <path>` | Repeatable. Each value is a `.sln`/`.slnx` file, or a directory scanned for its first solution. The first one is the *primary* solution. |
| `--all-solutions` | Load **every** solution found within the depth limit — in each `--solution` directory, in the working directory when no `--solution` is given, and in each MCP root. |

```bash
# Explicit list
cwm-roslyn-navigator --solution services/orders/Orders.sln --solution services/billing/Billing.slnx

# Everything under the repo (up to 3 levels deep)
cwm-roslyn-navigator --solution . --all-solutions
```

MCP config for a microservices repo:

```json
{
  "mcpServers": {
    "cwm-roslyn-navigator": {
      "command": "cwm-roslyn-navigator",
      "args": ["--solution", "${workspaceFolder}", "--all-solutions"]
    }
  }
}
```

**How it works — one workspace, many solutions.** The primary solution opens exactly as a
single solution does. Every further solution contributes only the projects not already
loaded, so a shared library referenced by five services is loaded and compiled **once**, and
project references across solution boundaries resolve to that one project. Every tool then
works across all solutions with no extra parameter: `find_references` on a shared contract
returns callers from every service.

- **Which solutions are loaded** — `get_project_graph` adds a `Solutions` list (name, path,
  declared project count, load error if any) and, per project, the solution(s) that declare
  it. Both fields are omitted when a single solution is loaded, so single-solution output is
  unchanged.
- **Paths** — tool results are relative to the deepest directory containing every loaded
  solution (e.g. `orders/src/OrderService.cs`), so they stay unambiguous across solutions and
  can be passed straight back to file-addressed tools.
- **Failures** — a solution file that cannot be read is reported in `Solutions[].Error`; the
  others still load. A project that fails to load is logged and skipped, as within one solution.
- **Reloads** — a changed solution file, `.csproj`, or shared build file reloads **all**
  configured solutions. Shared build files are `Directory.Build.props`/`.targets`,
  `Directory.Packages.props`, `global.json`, and `NuGet.config` in any directory from each
  project or solution up to the file-system root — so `src/Directory.Build.props` or one above
  the solution folder counts, as it does for MSBuild. A new source file is added in place to the project
  that owns it, in whichever solution; it causes a reload only when that project customizes
  its compile items (see [Scaling](#scaling)).
- **Limits** — names must still be unique enough to resolve: if two services both define
  `OrderService`, qualify it (`Orders.Api.OrderService`), as you would within one solution.
  `.cwm-navigator.json` is discovered from the common root upward.

**Cost.** Memory and load time scale with the number of *distinct* projects, not solutions —
shared projects are free after the first. [Scaling](#scaling) applies to the combined
project count.

## Architecture

```
Program.cs              → MSBuildLocator → Host → MCP stdio transport
WorkspaceManager.cs     → MSBuildWorkspace lifecycle, compilation warm-up, on-demand refresh
DefaultCompileGlob.cs   → Decides when a new .cs file can be added without a reload
WorkspaceInitializer.cs → BackgroundService triggers workspace load on startup
SolutionDiscovery.cs    → Auto-detect .sln/.slnx from args or working directory
SolutionFileReader.cs   → List a solution's projects (multi-solution dedupe and membership)
SymbolResolver.cs       → Cross-project symbol resolution with disambiguation
Tools/                  → MCP tool implementations (26 read-only tools)
Responses/              → Token-optimized JSON response DTOs
Telemetry/              → Opt-in local measurement, behind one call-tool filter

stats/                  → Separate CLI: cross-repo aggregate + divisor calibration
```

The Stats CLI deliberately does not reference the server project — it reads JSONL and has
no business pulling in the Roslyn workspace stack. It mirrors the record type instead, and
a contract test round-trips a real server record through the mirror so the two cannot drift
apart silently.

## Scaling

One strategy for every solution size:

- **Ready at open.** Tools answer as soon as the solution(s) are open; until then they
  return a `Loading` status. On a 39-project solution that is ~13 s after start instead of
  ~23 s.
- **Background warm-up.** Every project then compiles in the background, one build per
  core, dependencies first. A reload cancels the warm-up of the snapshot it replaces.
- **Queries never wait in line.** A tool that needs a compilation the warm-up has not
  reached builds it on demand, concurrently with the others, and shares any build already
  in flight — Roslyn builds one compilation per project per snapshot.
- **Edits are incremental.** The Roslyn solution snapshot holds every compilation and forks
  them on a document edit, including those of projects that reference the edited one.
- **Changes are found cheaply.** At most every 5 s, a tool call lists each directory that
  holds documents (one listing instead of one `stat` per file): edited files are re-read,
  deleted files are removed, and new `.cs` files beside existing ones are picked up. New files
  in new folders are found by a background walk every 60 s, which never descends into a
  project's `bin/`/`obj/`, dot-folders, `node_modules`, another project's folder, or a symlink or
  junction (a link pointing back up the tree would otherwise loop).
- **New files skip the reload.** A new file is added in place to the project whose folder
  is its deepest ancestor, when that project compiles through the SDK default glob — no
  `<Compile Include/Remove>`, default items left on, no unfollowed imports in the project or
  its `Directory.Build.*` files. Otherwise the solution reloads so MSBuild can decide. Files
  under `bin/`, `obj/`, or dot-folders are ignored, and a file the project excluded is
  remembered so it never triggers a second reload.
- **Reloads never take the workspace offline.** A changed solution, `.csproj`, or shared build
  file (`Directory.Build.*`, `Directory.Packages.props`, `global.json`, `NuGet.config`, in any
  folder from a project up to the root) reloads every solution in the background: the next workspace is opened beside the current
  one while every tool call keeps answering from the last good snapshot, then swapped in
  atomically. Only the very first load returns `Loading`. Answers given during a reload carry
  a `WorkspaceWarning` saying they come from the previous snapshot, and
  `get_workspace_status` reports `Reloading` and the `ReloadReason`. Calls that notice the
  same change concurrently share one reload; a build file saved again while a reload runs is
  picked up by the next one. A reload that fails (e.g. an unparseable solution file) keeps the
  previous snapshot, is reported as `LastReloadError` (and `Healthy: false`), and is retried
  after 30 s. The replaced workspace is disposed as soon as the last call still using it ends.

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

### 0.12.0

- **`CWM.RoslynNavigator.Stats` CLI** — aggregates savings across every repository, with a
  tool leaderboard, miss rates, and table / `--json` / `--markdown` output.
- **Transcript mining** — real measured spend and adoption rate read from Claude Code
  transcripts, so adoption is available even where telemetry was never enabled.
- **`stats calibrate`** — derives the bytes-per-token divisor from exact `count_tokens`
  results. Manual and offline-by-default; the server never calls it.
- The repository join reads `cwd` from transcript line content rather than decoding the
  directory name, which is lossy — `dotnet-claude-kit` cannot be recovered from
  `C--Users-...-dotnet-claude-kit`. This was the design's flagged fragile point.
- Reports distinguish "no data" from "data exists and the server was never invoked".

### 0.11.0

- **New: `get_token_savings`** — measures the premise the server is built on instead of
  asserting it. Baseline is the on-disk size of the files each answer came from, with
  session-level deduplication, per-call deduplication, and a repository-size cap, all of
  which push the figure down. See [Token savings](#token-savings).
- **Opt-in local telemetry** (`CWM_ROSLYN_TELEMETRY=on`). Nothing is transmitted; records
  are appended under `%LOCALAPPDATA%` outside the repository. Off by default.
- Collection hangs off a single MCP call-tool filter rather than a line in each tool, so
  every tool — including ones added later — is measured with nothing to remember.
- `get_public_api` and `get_type_hierarchy` now report the type's `File` and `Line`. Useful
  on its own, and it lets those two tools contribute a baseline like the rest.
- The bytes-per-token divisor is an uncalibrated estimate and every report says so.

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
