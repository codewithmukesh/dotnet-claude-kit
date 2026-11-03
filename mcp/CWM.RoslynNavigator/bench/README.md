# CWM.RoslynNavigator benchmark

A black-box benchmark for the navigator. It starts a server build as a real MCP stdio
process, the way Claude Code does, and measures what an agent gets from it on five public
.NET codebases of different sizes. It needs no server internals, so the same harness
measures the current source build and any published release.

It answers three questions:

1. **How fast is it?** Startup, peak memory, and the cold and warm latency of every tool.
2. **Did a change help?** The same numbers for a published release and the current build, side by side.
3. **What does it save?** Tokens and time for twelve everyday agent tasks, compared with doing the same task with Grep and Read.

The committed run is in [`results/`](results/). Each folder holds one JSON file per
(corpus × build) and a `report.md` that combines them.

## Corpora

| Tier | Corpus | Commit | Solution | Restore |
|---|---|---|---|---|
| small | [ardalis/CleanArchitecture](https://github.com/ardalis/CleanArchitecture) | `e7c79be54ba0b001a5b332c1efe935f520b9db4b` | `Clean.Architecture.slnx` | `dotnet restore Clean.Architecture.slnx -p:NuGetAudit=false` |
| small | [dotnet/eShop](https://github.com/dotnet/eShop) | `913acea66708a59595351174cee9b29f37179d29` | `eShop.slnx` | restore each project on its own (see below) |
| medium | [jellyfin/jellyfin](https://github.com/jellyfin/jellyfin) | `5b385862fb97201d954a6ac7c9c745d791a738fa` | `Jellyfin.slnx` | `dotnet restore Jellyfin.slnx` |
| large | [OrchardCMS/OrchardCore](https://github.com/OrchardCMS/OrchardCore) | `f6040cb5634c8390d1e4ed3fca586724d175b7e3` | `OrchardCore.slnx` | `dotnet restore OrchardCore.slnx` |
| xl | [abpframework/abp](https://github.com/abpframework/abp) (tag 10.7.1) | `6aeb69d8314c7649fb3277ffb726fcbb20631a68` | `framework/Volo.Abp.slnx` | `dotnet restore framework/Volo.Abp.slnx -p:NuGetAudit=false` |

Each corpus has a definition in [`corpus/`](corpus/): the commit, the solution, notes, and
the **targets**, which are the exact symbols and files every tool call and task asks about.

Known corpus quirks. These are also recorded in each definition's `Notes`:

- **CleanArchitecture.** At this commit the repo treats NuGet vulnerability warnings
  (NU1902/NU1903) as errors, so restore needs `-p:NuGetAudit=false`. The source is unchanged.
- **eShop.** Three MAUI projects (`src/ClientApp`, `src/HybridApp`, `tests/ClientApp.UnitTests`)
  need the MAUI workloads. A solution-level restore aborts on them, so restore every other
  project in `eShop.slnx` on its own. The MAUI projects then fail to load, and the run records that.
- **Jellyfin and OrchardCore.** Both reference analyzer or source-generator projects from
  their own solution (`OutputItemType="Analyzer"`). Those few projects are built after restore,
  as they would be in any developer's working copy. Without that step the server's
  implementation search throws. See [Server bugs found by the bench](#server-bugs-found-by-the-bench).
- **eShop.** The MAUI projects still load, once per target framework and without their
  packages. That is why the run shows 35 Roslyn projects loaded from 28 declared.
- **ABP.** The dev branch pins SDK 11.0 RC and targets `net11.0` in 176 of 178 framework
  projects, and SDK 10 cannot load those. The 10.7.1 release tag targets `net10.0`, so the
  xl tier still uses the same codebase. On Windows, clone with `core.longpaths=true`.

## Reproduce

Prerequisites: .NET SDK 10 (the committed run used 10.0.403) and about 6 GB of free disk for the clones and packages.

```bash
# 1. Clone the corpora at the committed SHAs (shallow fetch of one commit each)
mkdir corpus && cd corpus
clone() { git init -q "$1" && git -C "$1" config core.longpaths true && git -C "$1" remote add origin "$2" \
          && git -C "$1" fetch -q --depth 1 origin "$3" && git -C "$1" checkout -q FETCH_HEAD; }
clone CleanArchitecture https://github.com/ardalis/CleanArchitecture e7c79be54ba0b001a5b332c1efe935f520b9db4b
clone eShop             https://github.com/dotnet/eShop             913acea66708a59595351174cee9b29f37179d29
clone jellyfin          https://github.com/jellyfin/jellyfin        5b385862fb97201d954a6ac7c9c745d791a738fa
clone OrchardCore       https://github.com/OrchardCMS/OrchardCore   f6040cb5634c8390d1e4ed3fca586724d175b7e3
clone abp-10.7.1        https://github.com/abpframework/abp         6aeb69d8314c7649fb3277ffb726fcbb20631a68

# 2. Restore (MSBuildWorkspace only loads restored projects)
dotnet restore CleanArchitecture/Clean.Architecture.slnx -p:NuGetAudit=false
for p in $(grep -o 'Project Path="[^"]*"' eShop/eShop.slnx | cut -d'"' -f2); do dotnet restore "eShop/$p"; done
dotnet restore jellyfin/Jellyfin.slnx
dotnet build jellyfin/src/Jellyfin.CodeAnalysis/Jellyfin.CodeAnalysis.csproj -c Debug
dotnet restore OrchardCore/OrchardCore.slnx
for p in SourceGenerators Localization.Analyzers Localization.Analyzers.CodeFixes; do
  dotnet build "OrchardCore/src/OrchardCore/OrchardCore.$p/OrchardCore.$p.csproj" -c Debug; done
dotnet restore abp-10.7.1/framework/Volo.Abp.slnx -p:NuGetAudit=false
cd ..

# 3. Build the server and the harness (from mcp/CWM.RoslynNavigator)
dotnet build CWM.RoslynNavigator.slnx -c Release

# 4. Install the published release to compare against (latest on nuget.org at the time: 0.8.0)
dotnet tool install CWM.RoslynNavigator --version 0.8.0 --tool-path ./.bench-tools/0.8.0

# 5. Run every corpus against both builds
dotnet bench/CWM.RoslynNavigator.Bench/bin/Release/net10.0/CWM.RoslynNavigator.Bench.dll run \
  --corpus-root ../../corpus \
  --corpus bench/corpus/clean-architecture.json --corpus bench/corpus/eshop.json \
  --corpus bench/corpus/jellyfin.json --corpus bench/corpus/orchardcore.json --corpus bench/corpus/abp.json \
  --server published-0.8.0=./.bench-tools/0.8.0/cwm-roslyn-navigator \
  --server current=src/bin/Release/net10.0/CWM.RoslynNavigator.dll \
  --out bench/results
```

A server path that ends in `.dll` runs under the `dotnet` host. Any other path runs directly.
Builds run one after another and never at the same time. Each run's JSON is written as
soon as it finishes, so `--skip-existing` resumes a run that was interrupted. To render the
report again without re-running anything, use `report --in bench/results/<date>`.

Other verbs:

- `probe --corpus <def> --corpus-root <dir> --server <label=path>` checks that a corpus loads.
  It prints the server version, the time to ready, the project graph, and any load problems.
- `call --corpus <def> --corpus-root <dir> --server <label=path> --tool <name> --args <json>`
  makes one call and prints the response. `--warm` waits for warm-up first, `--repeat N`
  repeats the call N times for warm timings out of process, and `--quiet` skips the payload.
- `run ... --date <name>` writes into `results/<name>` instead of today's date, so a
  follow-up run never overwrites a committed folder.
- `select --corpus <def> --corpus-root <dir> --server <label=path>` regenerates a corpus's
  targets. Only run it when you add or bump a corpus. Committed targets keep runs comparable.

## What is measured

### Startup and memory

| Metric | Definition |
|---|---|
| Ready | Process start until `get_project_graph` stops returning a loading status. Polled every 250 ms over MCP. |
| First answer | A `find_symbol` call sent the moment the server is ready, measured until its result arrives. This is what an agent waits for on its first real question. |
| All compilations warm | Process start until the server logs `All compilations warmed`. Reported as n/a if the log line never appears. |
| Server peak WS | The OS-reported peak working set of the server process. Found through the process tree, skipping the shell that wraps it on Windows. |
| Build-host peak WS | The sum of the per-process peaks of the MSBuild build hosts the server spawns. |

Memory is measured on Windows and Linux. Other platforms report n/a.

### Per-tool latency

Each of the 22 tools is called with arguments built from the corpus targets (see
`Measurement/ToolCatalog.cs`). `get_token_savings` is excluded because it reports on
telemetry, and the bench turns telemetry off (`CWM_ROSLYN_TELEMETRY=off`).

- **Cold** is the first call to the tool in a session, made after warm-up has been awaited.
- **Warm** is the median and p95 of five more calls. p95 uses nearest rank, so with five samples it is the slowest call.
- Latency is the client-side round trip over stdio, including JSON-RPC framing.
- The per-call timeout is 300 s. A call that exceeds it is recorded as `timeout`.
- If a cold call takes longer than 60 s, its warm repeats are skipped and marked `skipped`.
- If a parameter does not exist in an older build's schema, it is dropped before the call.
- If a tool does not exist in a build, it is reported as `n/a`.

### Token savings per task

There are twelve tasks, defined in `Measurement/TaskCatalog.cs`: locate a type, find usages
of a widely used interface, list its implementations, outline the largest file, read one
method body, find a method's callers, type hierarchy, project graph, DI registrations,
endpoint map, resolve a stack trace, and diagnostics for one file. Each task is answered two ways.

- **Navigator:** the tokens in the tool response, and that call's measured warm median latency.
- **Grep + Read:** the realistic manual route, priced as two parts:
  - **Search.** The pattern a competent agent would type into Grep, run over every `.cs`
    (or `.csproj`) file in the solution's project directories. Output uses `path:line:text`
    lines like `rg -n`, cut at **250 lines** (Claude Code's Grep default), so the manual cost
    never includes lines an agent would not see.
  - **Read.** The full content of every file the answer lives in. These files are taken
    from the navigator's own answer and priced with the server's own `BaselineCalculator`,
    which is compiled into the harness and not re-implemented. Each file counts once, and
    the total is capped at the size of the repository. Outline and diagnostics tasks read
    the target file itself.
- **Saved** is the manual tokens minus the navigator tokens. **×** is the manual tokens divided by the navigator tokens.

Where these numbers lean conservative, which means against the navigator:

- When a navigator answer hits its result cap (`Trunc.`), its file list is partial. The
  read side then counts only those files, even though an agent would need more.
- The type-hierarchy answer names derived types but not their files, so the read side counts only the base type's file.
- Read output adds line-number prefixes that are not counted. Files larger than one Read window would need several calls, and those extra calls are not counted either.
- Each task is priced on its own, as if the agent had read nothing before it.

Where these numbers can lean the other way:

- For "locate a type", Grep alone often gives the location, and the read floor assumes the
  agent then opens the file. The table shows the Grep and Read columns separately so you can drop Read.
- For "diagnostics for one file", the real manual route is a build. A build is not measured,
  so the file read stands in for it.

### Time: measured vs modelled

Two kinds of time are reported. They always appear in separately labelled columns and are never added together.

- **Measured.** Navigator latency is the tool round trip. Manual time is the Grep wall time
  plus the time to read the files from disk, with the OS file cache warm. The search runs
  in-process in .NET, not with native ripgrep, so treat it as an upper bound for the search.
  Neither side includes model think time or agent tool-call overhead. The headline table
  counts agent tool calls separately: one navigator call versus one Grep plus one Read per file.
- **Modelled.** The time a model spends reading tokens it did not need, computed as
  `saved tokens ÷ prefill throughput`. The default is **10,000 input tokens/s** (`--prefill-tps`).
  This is an assumption, not a measurement. Anthropic does not publish prefill throughput.
  One public long-context benchmark
  ([General Compute, "Long-context benchmarks at 128K tokens"](https://www.generalcompute.com/blog/long-context-benchmarks-128k-tokens))
  gives an example of a 64K-token prompt taking about 7 s to first token, which is roughly
  9,000 tokens/s. That is an inference vendor's illustration, not a Claude measurement. The default rounds that up, so the model
  is assumed to be faster and the modelled saving comes out smaller. The modelled time scales
  linearly: halve the rate and it doubles.

### Tokens

Tokens are estimated as `UTF-8 bytes / 3.5`, using the server's own `TokenEstimator`. **The
divisor is not calibrated**, and every report says so. Calibrating it against a tokenizer
would change all token figures by the same factor, so savings percentages and multipliers
would not change.

## How targets are chosen

`select` runs once per corpus against the current build. It is deterministic: text nominates
candidates, the server scores them, the highest score wins, and ties are broken by name. Every
score is logged in the definition's `SelectionLog`.

| Target | Rule |
|---|---|
| Interface | Of the 12 most frequently mentioned interfaces declared once in production code, the one with the most `find_implementations` results. |
| Type | Of the 12 most mentioned non-static classes declared once, the one with the most `find_references` results. On multi-targeted solutions such as abp, counts include the per-TFM duplicates (bug 2). |
| Base class | Of the 12 most mentioned abstract classes, the one with the most derived types (`get_type_hierarchy`). If none qualify, the type above is used. |
| Method | Among the public methods of the top three types, the one with the most `find_callers` results. |
| Virtual method | Among virtual or abstract declarations whose names are most overridden in the text, the one with the most `find_overrides` results. If none qualify, the target is omitted. |
| Large file | The largest hand-written production `.cs` file the server can outline. Designer, `*.g.cs`, migrations, and wwwroot files are skipped. |
| Stack trace | A BCL throw frame, then the method, then up to three of its real callers, then a runtime frame. There are no file or line suffixes, as in a Release build without PDBs. |

The scan covers only the directories of the projects the solution declares. Repositories
that keep samples or a second solution next to the benchmarked one are not counted twice.

## Run time

The committed run took **82 minutes** on a 24-thread Windows machine. The current build
finishes every corpus in under 7 minutes, and all five corpora take about 11 minutes.
Most of the total comes from 0.8.0 on OrchardCore (43 min) and ABP (25 min). Both runs
include a 20-minute wait for a warm-up log line that 0.8.0 never wrote on those solutions.
Pass a lower `--warm-timeout-min` if you only need the current build. `select` takes
between 25 s and 130 s per corpus.

## Server bugs found by the bench

These were found during the committed run. The server source was left unchanged, because
the bench is black-box.

The list below is kept as found. Status on main since then:

- **Fixed:** 1 (unresolved analyzer references are dropped after load), 2 (results merged by
  location, and multi-targeted projects load one framework by default), 3 (`get_project_graph`
  is one paged entry per csproj with relative paths: 173k → 7.8k tokens on abp), 4 (hints match
  on directory boundaries with normalized separators), 5 (54 s → 1.2 s cold on orchardcore).
- **Item 6:** file-scope `get_diagnostics` now uses the file's own semantic model (abp 855 → 113 ms
  cold). The `find_callers` warm figure did not reproduce out of process (77–93 ms on abp).
- Runs after the fixes: [`results/2026-11-04-workspace-round2/`](results/2026-11-04-workspace-round2/report.md)
  and [`results/2026-11-04-tools-round2/`](results/2026-11-04-tools-round2/report.md).

1. **Unbuilt in-solution analyzers make `find_implementations` throw.** Corpora: jellyfin and
   orchardcore, restored but not built. Call: `find_implementations {"interfaceName":"IFileSystem","maxResults":1}`.
   Error: `InvalidOperationException: Unexpected value 'Microsoft.CodeAnalysis.Diagnostics.UnresolvedAnalyzerReference'`.
   It is thrown from `SerializerService.CreateChecksum`, through `DependentTypeFinder`, from
   `FindImplementationsTool.ExecuteAsync` (FindImplementationsTool.cs:37). The tool returns
   an MCP error, and the server keeps running. Every interface the selector tried failed this
   way on both corpora. Building the analyzer projects makes it go away. An agent working on
   a fresh clone hits this.
2. **Multi-targeted projects return duplicate results.** Roslyn loads one project per target
   framework, and the results are not deduplicated by location. On abp, `find_symbol {"name":"AbpException","kind":"class"}`
   returns 5 identical entries, one per TFM. On eshop, `find_references {"symbolName":"IBasketService","maxResults":100}`
   reports TotalFound=200 for 8 textual matches, with the same File:Line repeated up to 26 times.
   The duplicates inflate response tokens, use up the `maxResults` cap, and inflated the
   selector's reference counts on abp.
3. **`get_project_graph` is larger than the files it replaces on big solutions.** It returns
   70,873 tokens on orchardcore and 173,037 on abp. On abp that is 690 entries for 270 declared
   projects, each with an absolute path. Reading every `.csproj` costs 76,770 tokens on abp.
4. **File hints never match on Windows when given the server's own paths.** `SymbolResolver.NarrowByLocation`
   (SymbolResolver.cs:234) compares `SyntaxTree.FilePath.EndsWith(file)` against the raw hint.
   The server returns forward-slash relative paths, but Windows file paths use backslashes,
   so the hint is silently ignored. Repro on clean-architecture: `get_dependency_graph {"symbolName":"BuildRoute","file":"src/Clean.Architecture.Web/Contributors/GetById.GetContributorByIdRequest.cs","line":6}`
   returns `AmbiguousMatch`, and `analyze_change_impact` behaves the same way. The bench works
   around this by passing type-qualified names.
5. **`analyze_change_impact` is slow on orchardcore.** It takes 54.5 s cold and 48.2 s warm
   for `WorkflowExecutionContext.Dispose`, which has 72 callers, at depth 2. It is the slowest
   call in the whole run.
6. **Possible regressions to investigate (current vs 0.8.0).**
   - File-scope `get_diagnostics` cold: 815 ms vs 8 ms on jellyfin, and 855 ms vs 12 ms on abp.
     0.8.0 may compute less here, so compare the payloads before calling it a regression.
   - `find_callers` warm p50 on abp: 230 ms vs 69 ms.
7. **Fixed since 0.8.0.** 0.8.0 rejects forward-slash relative paths in `get_file_outline`
   ("not found in the solution"). On orchardcore and abp it never logged warm-up completion.
   On orchardcore it spawned 24 build hosts, with 3.2 GB peak combined memory.

## Caveats

- The results come from one machine and are not averaged across runs. Compare builds within one run, not across machines.
- A cold call depends on what earlier calls already computed, so the order in `ToolCatalog` matters, and it is fixed.
- Grep patterns are reasonable but not exhaustive. A better or worse pattern would change the Grep columns. The patterns are in `TaskCatalog.cs` so you can check them.
