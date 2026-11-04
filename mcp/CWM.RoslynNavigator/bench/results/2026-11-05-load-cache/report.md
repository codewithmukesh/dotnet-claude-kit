# Load cache: cold vs warm start

What the persistent load cache does to startup. **Cold** is a start with an empty cache folder
(a miss: MSBuild loads the solution and the entry is written). **Warm** is the next start of
the same solution with the entry present (a hit: the solution is restored from the cache and
MSBuild runs in the background to verify it).

## Setup

- Server: this branch, Release build (`0.12.0`), .NET SDK 10.0.403.
- Machine: Intel Core Ultra 9 275HX (24 cores), 32 GB, Windows 11. Other agents were running
  builds and test suites at the same time, so absolute numbers are noisy and cold loads are
  slower than in the [2026-11-04 run](../2026-11-04-final/report.md). The cold/warm ratio is the point.
- Corpora and commits as in the [bench README](../../README.md#corpora). ABP was loaded with
  `--load-parallelism 1` to keep memory down; the others used automatic parallelism (eShop
  picks 4 build hosts, the rest 1).
- One cold run, then two or three warm runs, one corpus at a time:

```bash
# from mcp/CWM.RoslynNavigator/bench; CWM_ROSLYN_CACHE_DIR points at an empty folder per corpus
CWM_ROSLYN_CACHE_DIR=<folder> dotnet CWM.RoslynNavigator.Bench/bin/Release/net10.0/CWM.RoslynNavigator.Bench.dll \
  probe --corpus corpus/<corpus>.json --corpus-root <corpus-root> \
  --server dev=../src/bin/Release/net10.0/CWM.RoslynNavigator.dll --wait-verify [--load-parallelism 1]
```

## Ready

Process start until `get_project_graph` stops answering `Loading` (polled every 250 ms).

| Corpus | Projects loaded | Cold | Warm (each run) | Speed-up |
|---|---:|---:|---|---:|
| CleanArchitecture | 5 | 4.8 s | 1.30 s, 1.43 s | 3.5x |
| eShop | 28 | 7.5 s | 1.43 s, 1.35 s | 5.4x |
| jellyfin | 40 | 10.1 s | 2.16 s, 1.69 s | 5.2x |
| OrchardCore | 257 | 58.5 s | 3.62 s, 2.79 s, 2.80 s | 20x |
| ABP (`framework/Volo.Abp.slnx`) | 271 | 99.8 s | 2.37 s, 2.45 s, 2.42 s | 41x |

Of a warm start, the server itself spends about 1.5 s (OrchardCore: check the entry 0.5 s,
restore 0.9 s, baseline file stamps 0.15 s; ABP: 0.4 s, 0.9 s, 0.16 s). The rest is process
start, the MCP handshake, and the probe's polling interval.

## Everything else on the same runs

| Corpus | All compilations warm, cold → warm | Verification done (warm) | Verification result |
|---|---|---|---|
| CleanArchitecture | 6.5 s → 2.8-3.2 s | 4.8-5.7 s | unchanged |
| eShop | 9.3 s → 3.5-3.6 s | 6.7-7.6 s | unchanged |
| jellyfin | 13.7 s → 5.9-6.0 s | 12.1-12.2 s | unchanged |
| OrchardCore | 69.4 s → 10.0-12.1 s | 55.6-73.9 s | unchanged |
| ABP | 103.4 s → 5.3-5.6 s | 87.5-98.2 s | unchanged |

"Verification done" is when the background MSBuild load finished and was compared. On every
run MSBuild produced exactly the cached snapshot, so the restored snapshot and its warm
compilations were kept. Writing the entry afterwards took 0.19 s (OrchardCore, 549 inputs)
and 0.26 s (ABP, 548 inputs).

## Peak memory

Peak working set over the whole run (until verification and warm-up were both done).

| Corpus | Server, cold → warm | Build hosts, cold → warm | Combined, cold → warm |
|---|---|---|---|
| CleanArchitecture | 295 → 390 MB | 131 → 139 MB | 331 → 525 MB |
| eShop | 419 → 528 MB | 632 → 632 MB (4 hosts) | 845 → 1,129 MB |
| jellyfin | 488 → 578 MB | 250 → 250 MB | 487 → 813 MB |
| OrchardCore | 863 → 1,204 MB | 1,025 → 1,020 MB | 1,410 → 2,151 MB |
| ABP | 816 → 1,107 MB | 1,618 → 1,622 MB | 2,167 → 2,651 MB |

A warm start peaks higher because, while the verification runs, the server holds the restored
snapshot (already compiling) and MSBuild's result at the same time. Cold, the MSBuild load
finishes before compilation starts. The extra is transient: the verification's workspace is
disposed as soon as the comparison is done. `--no-load-cache` restores the cold profile.

## Cache entries

| Corpus | Entry size (gzip) |
|---|---:|
| CleanArchitecture | 27 KB |
| eShop | 69 KB |
| jellyfin | 124 KB |
| OrchardCore | 664 KB |
| ABP | 485 KB |