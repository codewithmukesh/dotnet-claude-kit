# CWM.RoslynNavigator benchmark — 2026-11-04-workspace-round2

Builds: **—** (before) vs **current** (after). Machine: Microsoft Windows 10.0.26300, 24 logical CPUs, X64, harness on .NET 10.0.14.

Every number below is **measured** unless its column says **modelled**. Tokens are estimated as bytes / 3.5 (divisor **not calibrated** — the server's own TokenEstimator). Modelled time assumes **10,000 input tokens/s**; see bench/README.md.

## Headline — 12 agent tasks per corpus (current)

Navigator = the tool response. Grep + Read = the search an agent runs plus every file the answer lives in, each file counted once. Only tasks the navigator answered are summed. Measured times are tool round trip vs search + file I/O only — no model time on either side.

| Tier | Corpus | Tasks | Navigator tokens | Grep + Read tokens | Tokens saved | × fewer | Measured: navigator | Measured: grep + read I/O | Agent tool calls (nav / manual) | **Modelled** agent time saved |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| small | clean-architecture | 12/12 | 3,354 | 11,210 | 70.1% | 3.3× | 29 ms | 121 ms | 12 / 37 | 0.8 s |
| small | eshop | 12/12 | 10,992 | 71,699 | 84.7% | 6.5× | 87 ms | 315 ms | 12 / 81 | 6.1 s |
| medium | jellyfin | 12/12 | 28,554 | 646,136 | 95.6% | 22.6× | 759 ms | 901 ms | 12 / 136 | 61.7 s |
| large | orchardcore | 12/12 | 103,258 | 521,299 | 80.2% | 5.0× | 556 ms | 3,190 ms | 12 / 472 | 41.8 s |
| xl | abp | 12/12 | 102,517 | 318,650 | 67.8% | 3.1× | 515 ms | 2,550 ms | 12 / 444 | 21.6 s |

## Corpora

| Tier | Corpus | Commit | Solution | Projects declared | Projects loaded (current) | .cs files | C# source |
|---|---|---|---|---:|---:|---:|---:|
| small | [clean-architecture](https://github.com/ardalis/CleanArchitecture) | `e7c79be54b` | Clean.Architecture.slnx | 9 | 9 | 95 | 0.1 MB |
| small | [eshop](https://github.com/dotnet/eShop) | `913acea667` | eShop.slnx | 28 | 28 | 545 | 0.9 MB |
| medium | [jellyfin](https://github.com/jellyfin/jellyfin) | `5b385862fb` | Jellyfin.slnx | 41 | 41 | 2,248 | 13.1 MB |
| large | [orchardcore](https://github.com/OrchardCMS/OrchardCore) | `f6040cb563` | OrchardCore.slnx | 257 | 257 | 6,451 | 16.6 MB |
| xl | [abp](https://github.com/abpframework/abp) | `6aeb69d831` | framework/Volo.Abp.slnx | 270 | 271 | 4,639 | 8.4 MB |

## Startup and memory

Ready = process start until a tool stops answering "loading". First answer = a `find_symbol` issued the moment it is ready. Warm = the server's own "All compilations warmed" log line (n/a = not logged within the 20-minute wait). Server peak WS is the OS-reported peak of the server process. Server + build hosts is the largest simultaneous total seen at a 500 ms sample; build hosts are the MSBuild processes the server spawns (count in brackets).

| Tier | Corpus | Build | Ready | First answer | All compilations warm | Server peak WS | Server + build hosts (peak, sampled) | Restarts |
|---|---|---|---:|---:|---:|---:|---:|---:|
| small | clean-architecture | current | 3.5 s | 5.5 s | 5.5 s | 341 MB | 341 MB (1) | 0 |
| small | eshop | current | 5.2 s | 6.8 s | 6.8 s | 464 MB | 844 MB (4) | 0 |
| medium | jellyfin | current | 9.5 s | 12.9 s | 12.9 s | 841 MB | 837 MB (1) | 0 |
| large | orchardcore | current | 48.9 s | 56.8 s | 56.8 s | 1,487 MB | 1,487 MB (1) | 0 |
| xl | abp | current | 52.1 s | 54.9 s | 54.9 s | 1,176 MB | 4,483 MB (4) | 0 |

## Tokens per task (current)

Grep shows matching lines / lines an agent receives (head limit). Read is the file-read floor: the files the answer lives in, each once. "Trunc." marks a navigator answer that hit its result cap — its file list, and so the manual baseline, is then partial (conservative).

### small: clean-architecture

| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved |
|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|
| Locate the definition of AppDbContext | 92 |  | 42 (1 / 1) | 163 (1) | 205 | 55.1% | 2.2× | 1 | 28 | 0.0 |
| Find all usages of IEmailSender | 407 |  | 315 (8 / 8) | 1,311 (5) | 1,626 | 75.0% | 4.0× | 2 | 11 | 0.1 |
| List implementations of IEmailSender | 97 |  | 80 (2 / 2) | 481 (2) | 561 | 82.7% | 5.8× | 1 | 10 | 0.1 |
| Outline Extensions.cs (largest file) | 307 |  | — | 1,372 (1) | 1,372 | 77.6% | 4.5× | 1 | 0 | 0.1 |
| Read the body of GetContributorByIdRequest.BuildRoute | 89 |  | 265 (5 / 5) | 95 (1) | 360 | 75.3% | 4.0× | 1 | 8 | 0.0 |
| Find callers of GetContributorByIdRequest.BuildRoute | 139 |  | 265 (5 / 5) | 248 (1) | 513 | 72.9% | 3.7× | 4 | 9 | 0.0 |
| Type hierarchy of AppDbContext | 98 |  | 0 (0 / 0) | 163 (1) | 163 | 39.9% | 1.7× | 1 | 10 | 0.0 |
| Project dependency graph | 975 |  | 732 (14 / 14) | 2,839 (9) | 3,571 | 72.7% | 3.7× | 3 | 5 | 0.3 |
| DI registrations | 694 |  | 525 (13 / 13) | 1,468 (3) | 1,993 | 65.2% | 2.9× | 5 | 16 | 0.1 |
| HTTP endpoint map | 20 |  | 0 (0 / 0) | 0 (0) | 0 | — | — | 6 | 14 | -0.0 |
| Resolve a stack trace to source | 406 |  | 340 (7 / 7) | 343 (2) | 683 | 40.6% | 1.7× | 2 | 10 | 0.0 |
| Diagnostics for AppDbContext.cs | 30 |  | — | 163 (1) | 163 | 81.6% | 5.4× | 3 | 0 | 0.0 |

### small: eshop

| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved |
|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|
| Locate the definition of ApplicationUser | 82 |  | 27 (1 / 1) | 313 (1) | 340 | 75.9% | 4.1× | 1 | 36 | 0.0 |
| Find all usages of IBasketService | 440 |  | 259 (8 / 8) | 2,531 (4) | 2,790 | 84.2% | 6.3× | 2 | 32 | 0.2 |
| List implementations of IBasketService | 87 |  | 99 (3 / 3) | 1,738 (2) | 1,837 | 95.3% | 21.1× | 1 | 33 | 0.2 |
| Outline CatalogApi.cs (largest file) | 1,567 |  | — | 6,088 (1) | 6,088 | 74.3% | 3.9× | 1 | 0 | 0.5 |
| Read the body of OrderingContext.BeginTransactionAsync | 132 |  | 164 (4 / 4) | 1,243 (1) | 1,407 | 90.6% | 10.7× | 1 | 30 | 0.1 |
| Find callers of OrderingContext.BeginTransactionAsync | 69 |  | 164 (4 / 4) | 766 (1) | 930 | 92.6% | 13.5× | 2 | 34 | 0.1 |
| Type hierarchy of ViewModelBase | 121 |  | 320 (11 / 11) | 413 (1) | 733 | 83.5% | 6.1× | 1 | 40 | 0.1 |
| Project dependency graph | 2,515 |  | 1,793 (46 / 46) | 10,249 (28) | 12,042 | 79.1% | 4.8× | 4 | 5 | 1.0 |
| DI registrations | 3,653 |  | 2,259 (69 / 69) | 13,480 (18) | 15,739 | 76.8% | 4.3× | 31 | 34 | 1.2 |
| HTTP endpoint map | 1,889 |  | 1,446 (51 / 51) | 23,476 (11) | 24,922 | 92.4% | 13.2× | 39 | 36 | 2.3 |
| Resolve a stack trace to source | 332 |  | 2,549 (50 / 50) | 2,009 (2) | 4,558 | 92.7% | 13.7× | 2 | 36 | 0.4 |
| Diagnostics for ApplicationUser.cs | 105 |  | — | 313 (1) | 313 | 66.5% | 3.0× | 3 | 0 | 0.0 |

### medium: jellyfin

| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved |
|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|
| Locate the definition of BaseItem | 83 |  | 44 (1 / 1) | 30,585 (1) | 30,629 | 99.7% | 369.0× | 1 | 116 | 3.1 |
| Find all usages of IApplicationPaths | 6,179 | yes | 6,064 (146 / 146) | 156,629 (59) | 162,693 | 96.2% | 26.3× | 6 | 106 | 15.7 |
| List implementations of IApplicationPaths | 95 |  | 1,353 (22 / 22) | 2,898 (2) | 4,251 | 97.8% | 44.7× | 2 | 101 | 0.4 |
| Outline EncodingHelper.cs (largest file) | 7,878 |  | — | 102,369 (1) | 102,369 | 92.3% | 13.0× | 4 | 0 | 9.5 |
| Read the body of BaseItem.GetParent | 115 |  | 1,161 (35 / 35) | 30,585 (1) | 31,746 | 99.6% | 276.1× | 2 | 97 | 3.2 |
| Find callers of BaseItem.GetParent | 1,307 |  | 1,161 (35 / 35) | 143,589 (12) | 144,750 | 99.1% | 110.7× | 9 | 95 | 14.3 |
| Type hierarchy of BaseItem | 197 |  | 2,841 (93 / 93) | 30,585 (1) | 33,426 | 99.4% | 169.7× | 4 | 84 | 3.3 |
| Project dependency graph | 6,027 |  | 4,080 (92 / 92) | 18,051 (41) | 22,131 | 72.8% | 3.7× | 8 | 7 | 1.6 |
| DI registrations | 6,074 | yes | 7,319 (180 / 180) | 13,499 (2) | 20,818 | 70.8% | 3.4× | 396 | 96 | 1.5 |
| HTTP endpoint map | 68 |  | 6,388 (436 / 250) | 404 (1) | 6,792 | 99.0% | 99.9× | 200 | 87 | 0.7 |
| Resolve a stack trace to source | 501 |  | 1,931 (53 / 53) | 54,015 (4) | 55,946 | 99.1% | 111.7× | 6 | 112 | 5.5 |
| Diagnostics for BaseItem.cs | 30 |  | — | 30,585 (1) | 30,585 | 99.9% | 1019.5× | 121 | 0 | 3.1 |

### large: orchardcore

| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved |
|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|
| Locate the definition of StartupBase | 82 |  | 36 (1 / 1) | 245 (1) | 281 | 70.8% | 3.4× | 2 | 365 | 0.0 |
| Find all usages of IShape | 6,973 | yes | 11,291 (534 / 250) | 45,813 (49) | 57,104 | 87.8% | 8.2× | 13 | 335 | 5.0 |
| List implementations of IShape | 2,589 |  | 1,101 (21 / 21) | 15,402 (54) | 16,503 | 84.3% | 6.4× | 18 | 390 | 1.4 |
| Outline ResourceManagementOptionsConfiguration.cs (largest file) | 371 |  | — | 16,286 (1) | 16,286 | 97.7% | 43.9× | 4 | 0 | 1.6 |
| Read the body of WorkflowExecutionContext.Dispose | 80 |  | 8,131 (322 / 250) | 4,124 (1) | 12,255 | 99.3% | 153.2× | 3 | 341 | 1.2 |
| Find callers of WorkflowExecutionContext.Dispose | 6,669 | yes | 8,131 (322 / 250) | 112,281 (31) | 120,412 | 94.5% | 18.1× | 95 | 370 | 11.4 |
| Type hierarchy of StartupBase | 569 | yes | 8,133 (398 / 250) | 245 (1) | 8,378 | 93.2% | 14.7× | 14 | 349 | 0.8 |
| Project dependency graph | 70,873 |  | 14,656 (1,392 / 250) | 104,233 (257) | 118,889 | 40.4% | 1.7× | 40 | 29 | 4.8 |
| DI registrations | 9,481 | yes | 10,672 (1,661 / 250) | 36,787 (22) | 47,459 | 80.0% | 5.0× | 181 | 357 | 3.8 |
| HTTP endpoint map | 4,956 | yes | 7,841 (404 / 250) | 84,737 (41) | 92,578 | 94.6% | 18.7× | 132 | 349 | 8.8 |
| Resolve a stack trace to source | 506 |  | 8,614 (367 / 250) | 22,295 (3) | 30,909 | 98.4% | 61.1× | 15 | 304 | 3.0 |
| Diagnostics for StartupBase.cs | 109 |  | — | 245 (1) | 245 | 55.5% | 2.2× | 38 | 0 | 0.0 |

### xl: abp

| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved |
|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|
| Locate the definition of AbpException | 122 |  | 24 (1 / 1) | 133 (1) | 157 | 22.3% | 1.3× | 2 | 361 | 0.0 |
| Find all usages of ITransientDependency | 6,733 | yes | 12,387 (526 / 250) | 76,605 (100) | 88,992 | 92.4% | 13.2× | 15 | 289 | 8.2 |
| List implementations of ITransientDependency | 635 |  | 12,349 (512 / 250) | 3,393 (11) | 15,742 | 96.0% | 24.8× | 4 | 269 | 1.5 |
| Outline DistributedCache.cs (largest file) | 3,850 |  | — | 15,161 (1) | 15,161 | 74.6% | 3.9× | 5 | 0 | 1.1 |
| Read the body of AbpModule.IsAbpModule | 145 |  | 175 (5 / 5) | 1,568 (1) | 1,743 | 91.7% | 12.0× | 2 | 283 | 0.2 |
| Find callers of AbpModule.IsAbpModule | 614 |  | 175 (5 / 5) | 12,625 (5) | 12,800 | 95.2% | 20.8× | 84 | 240 | 1.2 |
| Type hierarchy of AbpModule | 117 |  | 10,282 (308 / 250) | 1,568 (1) | 11,850 | 99.0% | 101.3× | 4 | 249 | 1.2 |
| Project dependency graph | 81,928 |  | 12,619 (678 / 250) | 76,770 (270) | 89,389 | 8.3% | 1.1× | 40 | 34 | 0.8 |
| DI registrations | 7,361 | yes | 10,476 (217 / 217) | 48,461 (37) | 58,937 | 87.5% | 8.0× | 145 | 280 | 5.2 |
| HTTP endpoint map | 440 |  | 9,058 (269 / 250) | 2,414 (3) | 11,472 | 96.2% | 26.1× | 154 | 280 | 1.1 |
| Resolve a stack trace to source | 446 |  | 462 (13 / 13) | 11,812 (3) | 12,274 | 96.4% | 27.5× | 9 | 263 | 1.2 |
| Diagnostics for AbpException.cs | 126 |  | — | 133 (1) | 133 | 5.3% | 1.1× | 52 | 0 | 0.0 |

## Per-tool latency — before vs after

Client-side round trip over stdio, in ms. Cold = first call to that tool in the session, after warm-up was awaited. Warm = median / p95 of five repeats (p95 of five is the slowest). n/a = tool not in that build.

### small: clean-architecture

| Tool | Cold (—) | Cold (current) | Warm p50 (—) | Warm p50 (current) | Warm p95 (current) | Tokens (—) | Tokens (current) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `find_symbol` | — | 3 | — | 1 | 1 | — | 92 |
| `find_references` | — | 249 | — | 2 | 5 | — | 407 |
| `find_implementations` | — | 206 | — | 1 | 2 | — | 97 |
| `find_callers` | — | 173 | — | 4 | 4 | — | 139 |
| `get_type_hierarchy` | — | 7 | — | 1 | 2 | — | 98 |
| `get_public_api` | — | 18 | — | 1 | 2 | — | 143 |
| `get_symbol_detail` | — | 9 | — | 1 | 2 | — | 117 |
| `get_symbol_source` | — | 4 | — | 1 | 1 | — | 89 |
| `get_file_outline` | — | 10 | — | 1 | 1 | — | 307 |
| `get_project_graph` | — | 2 | — | 3 | 48 | — | 975 |
| `get_dependency_graph` | — | 30 | — | 2 | 3 | — | 37 |
| `detect_circular_dependencies` | — | 7 | — | 1 | 1 | — | 7 |
| `get_diagnostics` | — | 234 | — | 3 | 3 | — | 30 |
| `resolve_stack_trace` | — | 19 | — | 2 | 3 | — | 406 |
| `get_nuget_packages` | — | 14 | — | 8 | 38 | — | 1,263 |
| `analyze_change_impact` | — | 37 | — | 7 | 8 | — | 253 |
| `get_di_registrations` | — | 34 | — | 5 | 6 | — | 694 |
| `get_endpoint_map` | — | 13 | — | 6 | 6 | — | 20 |
| `get_test_coverage_map` | — | 21 | — | 7 | 7 | — | 2,152 |
| `detect_antipatterns` | — | 139 | — | 45 | 74 | — | 620 |
| `find_dead_code` | — | 243 | — | 40 | 47 | — | 35 |

### small: eshop

| Tool | Cold (—) | Cold (current) | Warm p50 (—) | Warm p50 (current) | Warm p95 (current) | Tokens (—) | Tokens (current) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `find_symbol` | — | 2 | — | 1 | 15 | — | 82 |
| `find_references` | — | 272 | — | 2 | 4 | — | 440 |
| `find_implementations` | — | 136 | — | 1 | 3 | — | 87 |
| `find_callers` | — | 146 | — | 2 | 3 | — | 69 |
| `find_overrides` | — | 7 | — | 1 | 2 | — | 466 |
| `get_type_hierarchy` | — | 8 | — | 1 | 1 | — | 121 |
| `get_public_api` | — | 5 | — | 1 | 1 | — | 353 |
| `get_symbol_detail` | — | 4 | — | 1 | 1 | — | 117 |
| `get_symbol_source` | — | 4 | — | 1 | 1 | — | 132 |
| `get_file_outline` | — | 32 | — | 1 | 2 | — | 1,567 |
| `get_project_graph` | — | 4 | — | 4 | 5 | — | 2,515 |
| `get_dependency_graph` | — | 8 | — | 2 | 3 | — | 41 |
| `detect_circular_dependencies` | — | 5 | — | 0 | 1 | — | 81 |
| `get_diagnostics` | — | 217 | — | 3 | 5 | — | 105 |
| `resolve_stack_trace` | — | 27 | — | 2 | 3 | — | 332 |
| `get_nuget_packages` | — | 19 | — | 14 | 31 | — | 2,345 |
| `analyze_change_impact` | — | 48 | — | 5 | 7 | — | 206 |
| `get_di_registrations` | — | 54 | — | 31 | 31 | — | 3,653 |
| `get_endpoint_map` | — | 47 | — | 39 | 79 | — | 1,889 |
| `get_test_coverage_map` | — | 57 | — | 17 | 19 | — | 1,969 |
| `detect_antipatterns` | — | 537 | — | 276 | 333 | — | 11,950 |
| `find_dead_code` | — | 1,102 | — | 208 | 235 | — | 885 |

### medium: jellyfin

| Tool | Cold (—) | Cold (current) | Warm p50 (—) | Warm p50 (current) | Warm p95 (current) | Tokens (—) | Tokens (current) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `find_symbol` | — | 3 | — | 1 | 2 | — | 83 |
| `find_references` | — | 799 | — | 6 | 25 | — | 6,179 |
| `find_implementations` | — | 1,172 | — | 2 | 13 | — | 95 |
| `find_callers` | — | 172 | — | 9 | 12 | — | 1,307 |
| `find_overrides` | — | 21 | — | 6 | 6 | — | 844 |
| `get_type_hierarchy` | — | 7 | — | 4 | 7 | — | 197 |
| `get_public_api` | — | 16 | — | 4 | 28 | — | 2,669 |
| `get_symbol_detail` | — | 7 | — | 1 | 2 | — | 94 |
| `get_symbol_source` | — | 4 | — | 2 | 5 | — | 115 |
| `get_file_outline` | — | 18 | — | 4 | 4 | — | 7,878 |
| `get_project_graph` | — | 6 | — | 8 | 13 | — | 6,027 |
| `get_dependency_graph` | — | 12 | — | 5 | 20 | — | 104 |
| `detect_circular_dependencies` | — | 5 | — | 0 | 1 | — | 7 |
| `get_diagnostics` | — | 1,062 | — | 121 | 145 | — | 30 |
| `resolve_stack_trace` | — | 21 | — | 6 | 6 | — | 501 |
| `get_nuget_packages` | — | 20 | — | 18 | 24 | — | 2,607 |
| `analyze_change_impact` | — | 553 | — | 301 | 307 | — | 1,525 |
| `get_di_registrations` | — | 649 | — | 396 | 436 | — | 6,074 |
| `get_endpoint_map` | — | 223 | — | 200 | 227 | — | 68 |
| `get_test_coverage_map` | — | 106 | — | 34 | 43 | — | 1,898 |
| `detect_antipatterns` | — | 2,794 | — | 1,024 | 1,713 | — | 12,127 |
| `find_dead_code` | — | 4,386 | — | 724 | 788 | — | 299 |

### large: orchardcore

| Tool | Cold (—) | Cold (current) | Warm p50 (—) | Warm p50 (current) | Warm p95 (current) | Tokens (—) | Tokens (current) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `find_symbol` | — | 7 | — | 2 | 4 | — | 82 |
| `find_references` | — | 1,763 | — | 13 | 25 | — | 6,973 |
| `find_implementations` | — | 2,502 | — | 18 | 19 | — | 2,589 |
| `find_callers` | — | 2,212 | — | 95 | 135 | — | 6,669 |
| `find_overrides` | — | 344 | — | 38 | 42 | — | 4,767 |
| `get_type_hierarchy` | — | 21 | — | 14 | 16 | — | 569 |
| `get_public_api` | — | 8 | — | 3 | 4 | — | 232 |
| `get_symbol_detail` | — | 6 | — | 3 | 4 | — | 96 |
| `get_symbol_source` | — | 6 | — | 3 | 3 | — | 80 |
| `get_file_outline` | — | 15 | — | 4 | 37 | — | 371 |
| `get_project_graph` | — | 38 | — | 40 | 46 | — | 70,873 |
| `get_dependency_graph` | — | 14 | — | 9 | 22 | — | 31 |
| `detect_circular_dependencies` | — | 9 | — | 5 | 14 | — | 85 |
| `get_diagnostics` | — | 209 | — | 38 | 42 | — | 109 |
| `resolve_stack_trace` | — | 29 | — | 15 | 17 | — | 506 |
| `get_nuget_packages` | — | 97 | — | 102 | 107 | — | 8,155 |
| `analyze_change_impact` | — | 50,679 | — | 43,590 | 44,821 | — | 4,359 |
| `get_di_registrations` | — | 429 | — | 181 | 205 | — | 9,481 |
| `get_endpoint_map` | — | 178 | — | 132 | 151 | — | 4,956 |
| `get_test_coverage_map` | — | 124 | — | 91 | 117 | — | 2,377 |
| `detect_antipatterns` | — | 970 | — | 652 | 709 | — | 13,351 |
| `find_dead_code` | — | 4,902 | — | 1,788 | 1,881 | — | 541 |

### xl: abp

| Tool | Cold (—) | Cold (current) | Warm p50 (—) | Warm p50 (current) | Warm p95 (current) | Tokens (—) | Tokens (current) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `find_symbol` | — | 6 | — | 2 | 4 | — | 122 |
| `find_references` | — | 850 | — | 15 | 47 | — | 6,733 |
| `find_implementations` | — | 206 | — | 4 | 7 | — | 635 |
| `find_callers` | — | 1,877 | — | 84 | 122 | — | 614 |
| `find_overrides` | — | 46 | — | 25 | 28 | — | 4,808 |
| `get_type_hierarchy` | — | 8 | — | 4 | 5 | — | 117 |
| `get_public_api` | — | 18 | — | 2 | 4 | — | 135 |
| `get_symbol_detail` | — | 7 | — | 2 | 2 | — | 98 |
| `get_symbol_source` | — | 5 | — | 2 | 2 | — | 145 |
| `get_file_outline` | — | 14 | — | 5 | 25 | — | 3,850 |
| `get_project_graph` | — | 39 | — | 40 | 43 | — | 81,928 |
| `get_dependency_graph` | — | 13 | — | 6 | 19 | — | 30 |
| `detect_circular_dependencies` | — | 10 | — | 5 | 5 | — | 103 |
| `get_diagnostics` | — | 498 | — | 52 | 73 | — | 126 |
| `resolve_stack_trace` | — | 29 | — | 9 | 10 | — | 446 |
| `get_nuget_packages` | — | 121 | — | 117 | 125 | — | 9,101 |
| `analyze_change_impact` | — | 1,146 | — | 220 | 752 | — | 695 |
| `get_di_registrations` | — | 474 | — | 145 | 341 | — | 7,361 |
| `get_endpoint_map` | — | 145 | — | 154 | 159 | — | 440 |
| `get_test_coverage_map` | — | 258 | — | 74 | 95 | — | 2,692 |
| `detect_antipatterns` | — | 1,459 | — | 691 | 946 | — | 13,369 |
| `find_dead_code` | — | 5,063 | — | 2,254 | 2,578 | — | 3,124 |

## Slowest tools per corpus (current, cold)

| Tier | Corpus | Slowest five (cold ms) |
|---|---|---|
| small | clean-architecture | `find_references` 249, `find_dead_code` 243, `get_diagnostics` 234, `find_implementations` 206, `find_callers` 173 |
| small | eshop | `find_dead_code` 1,102, `detect_antipatterns` 537, `find_references` 272, `get_diagnostics` 217, `find_callers` 146 |
| medium | jellyfin | `find_dead_code` 4,386, `detect_antipatterns` 2,794, `find_implementations` 1,172, `get_diagnostics` 1,062, `find_references` 799 |
| large | orchardcore | `analyze_change_impact` 50,679, `find_dead_code` 4,902, `find_implementations` 2,502, `find_callers` 2,212, `find_references` 1,763 |
| xl | abp | `find_dead_code` 5,063, `find_callers` 1,877, `detect_antipatterns` 1,459, `analyze_change_impact` 1,146, `find_references` 850 |

## Errors, timeouts, and load problems

- **clean-architecture** note: Restore needs -p:NuGetAudit=false: the repo treats NuGet vulnerability warnings (NU1902/NU1903 on SSH.NET and System.Security.Cryptography.Pkcs at this commit) as errors. Nothing in the source is changed.
- **eshop / current** workspace diagnostics logged by the server (4; non-fatal unless projects loaded < declared): Workspace failure: Msbuild failed when processing the file 'eShop\tests\Basket.FunctionalTests\Basket.FunctionalTests.csproj' with message: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-qhrr-8q5h-9q3h; Workspace failure: Msbuild failed when processing the file 'eShop\tests\Catalog.FunctionalTests\Catalog.FunctionalTests.csproj' with message: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-qhrr-8q5h-9q3h; Workspace failure: Msbuild failed when processing the file 'eShop\tests\Webhooks.FunctionalTests\Webhooks.FunctionalTests.csproj' with message: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-qhrr-8q5h-9q3h; Workspace failure: Msbuild failed when processing the file 'eShop\tests\Ordering.FunctionalTests\Ordering.FunctionalTests.csproj' with message: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-qhrr-8q5h-9q3h
- **eshop** note: 3 MAUI projects (src/ClientApp, src/HybridApp, tests/ClientApp.UnitTests) need the maui-android/maui-tizen workloads, which are not installed on the benchmark machine. They fail restore (NETSDK1147), yet MSBuildWorkspace still loads them, once per target framework, without their packages, which is why 35 Roslyn projects load from 28 declared. Every other project restores and loads normally.
- **jellyfin** note: The in-solution analyzer project src/Jellyfin.CodeAnalysis (referenced as OutputItemType=Analyzer from Directory.Build.props) is built after restore, as it would be in any developer's working copy. On a restore-only clone it is an UnresolvedAnalyzerReference, and the server's SymbolFinder-based tools throw (server bug; see the Server bugs section of bench/README.md).
- **orchardcore / current** workspace diagnostics logged by the server (80; non-fatal unless projects loaded < declared): Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore.Cms.Web\OrchardCore.Cms.Web.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8; Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore\OrchardCore.Application.Cms.Targets\OrchardCore.Application.Cms.Targets.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8; Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore\OrchardCore.Application.Cms.Core.Targets\OrchardCore.Application.Cms.Core.Targets.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8; Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore.Modules\OrchardCore.AdminDashboard\OrchardCore.AdminDashboard.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8; Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore\OrchardCore.ContentManagement\OrchardCore.ContentManagement.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8
- **orchardcore** note: The three in-solution analyzer/generator projects (OrchardCore.SourceGenerators, OrchardCore.Localization.Analyzers, OrchardCore.Localization.Analyzers.CodeFixes; referenced as OutputItemType=Analyzer from OrchardCore.Commons.props) are built after restore, as in any developer's working copy. On a restore-only clone they are UnresolvedAnalyzerReferences and the server's SymbolFinder-based tools throw (server bug; see the Server bugs section of bench/README.md).
- **abp / current** workspace diagnostics logged by the server (13; non-fatal unless projects loaded < declared): Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.Core\Volo.Abp.Core.csproj' with message: PackageReference System.Linq.Queryable will not be pruned. This package is automatically available and does not need to be referenced explicitly. Remove the PackageReference item.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.EntityFrameworkCore.MySQL.Pomelo\Volo.Abp.EntityFrameworkCore.MySQL.Pomelo.csproj' with message: Detected package version outside of dependency constraint: Pomelo.EntityFrameworkCore.MySql 9.0.0 requires Microsoft.EntityFrameworkCore.Relational (>= 9.0.0 && <= 9.0.999) but version Microsoft.EntityFrameworkCore.Relational 10.0.11 was resolved.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.EntityFrameworkCore.Oracle.Devart\Volo.Abp.EntityFrameworkCore.Oracle.Devart.csproj' with message: Detected package downgrade: Microsoft.EntityFrameworkCore.Relational from 10.0.11 to 9.0.12. Reference the package directly from the project to select a different version.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.EventBus.Rebus\Volo.Abp.EventBus.Rebus.csproj' with message: Detected package version outside of dependency constraint: Rebus 8.8.0 requires system.text.json (>= 8.0.5 && < 10.0.0) but version System.Text.Json 10.0.11 was resolved.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.EventBus.Rebus\Volo.Abp.EventBus.Rebus.csproj' with message: Detected package version outside of dependency constraint: Rebus.ServiceProvider 10.5.0 requires Microsoft.Extensions.DependencyInjection (>= 6.0.0 && < 10.0.0) but version Microsoft.Extensions.DependencyInjection 10.0.11 was resolved.
- **abp** note: Release tag 10.7.1, not the dev branch. ABP's dev head (a840be3) pins SDK 11.0.100-rc.3 and multi-targets net11.0 in 176 of 178 framework/src projects, which .NET SDK 10 cannot build or load. The 10.7.1 release targets net10.0 (plus netstandard2.x/net8/net9) and restores cleanly on SDK 10.0.403, so the xl tier keeps the same codebase.
- **abp** note: Clone with core.longpaths=true on Windows: several test paths exceed 260 characters.

