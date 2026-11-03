# CWM.RoslynNavigator benchmark — 2026-11-04-tools-round2

Builds: **main-2026-11-04** (before) vs **current** (after). Machine: Microsoft Windows 10.0.26300, 24 logical CPUs, X64, harness on .NET 10.0.14.

Every number below is **measured** unless its column says **modelled**. Tokens are estimated as bytes / 3.5 (divisor **not calibrated** — the server's own TokenEstimator). Modelled time assumes **10,000 input tokens/s**; see bench/README.md.

## Headline — 12 agent tasks per corpus (current)

Navigator = the tool response. Grep + Read = the search an agent runs plus every file the answer lives in, each file counted once. Only tasks the navigator answered are summed. Measured times are tool round trip vs search + file I/O only — no model time on either side.

| Tier | Corpus | Tasks | Navigator tokens | Grep + Read tokens | Tokens saved | × fewer | Measured: navigator | Measured: grep + read I/O | Agent tool calls (nav / manual) | **Modelled** agent time saved |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| small | clean-architecture | 12/12 | 2,800 | 11,210 | 75.0% | 4.0× | 19 ms | 77 ms | 12 / 37 | 0.8 s |
| small | eshop | 12/12 | 9,496 | 71,699 | 86.8% | 7.6× | 79 ms | 315 ms | 12 / 81 | 6.2 s |
| medium | jellyfin | 12/12 | 23,796 | 645,838 | 96.3% | 27.1× | 681 ms | 831 ms | 12 / 136 | 62.2 s |
| large | orchardcore | 12/12 | 43,666 | 477,635 | 90.9% | 10.9× | 476 ms | 3,391 ms | 12 / 307 | 43.4 s |
| xl | abp | 12/12 | 30,877 | 355,400 | 91.3% | 11.5× | 563 ms | 2,226 ms | 12 / 368 | 32.4 s |

## Corpora

| Tier | Corpus | Commit | Solution | Projects declared | Projects loaded (main-2026-11-04 / current) | .cs files | C# source |
|---|---|---|---|---:|---:|---:|---:|
| small | [clean-architecture](https://github.com/ardalis/CleanArchitecture) | `e7c79be54b` | Clean.Architecture.slnx | 9 | 9 / 9 | 95 | 0.1 MB |
| small | [eshop](https://github.com/dotnet/eShop) | `913acea667` | eShop.slnx | 28 | 35 / 35 | 545 | 0.9 MB |
| medium | [jellyfin](https://github.com/jellyfin/jellyfin) | `5b385862fb` | Jellyfin.slnx | 41 | 41 / 41 | 2,248 | 13.1 MB |
| large | [orchardcore](https://github.com/OrchardCMS/OrchardCore) | `f6040cb563` | OrchardCore.slnx | 257 | 257 / 257 | 6,451 | 16.6 MB |
| xl | [abp](https://github.com/abpframework/abp) | `6aeb69d831` | framework/Volo.Abp.slnx | 270 | 690 / 690 | 4,639 | 8.4 MB |

## Startup and memory

Ready = process start until a tool stops answering "loading". First answer = a `find_symbol` issued the moment it is ready. Warm = the server's own "All compilations warmed" log line (n/a = not logged within the 20-minute wait). Server peak WS is the OS-reported peak of the server process. Server + build hosts is the largest simultaneous total seen at a 500 ms sample; build hosts are the MSBuild processes the server spawns (count in brackets).

| Tier | Corpus | Build | Ready | First answer | All compilations warm | Server peak WS | Server + build hosts (peak, sampled) | Restarts |
|---|---|---|---:|---:|---:|---:|---:|---:|
| small | clean-architecture | main-2026-11-04 | 3.2 s | 4.6 s | 4.6 s | 335 MB | 335 MB (1) | 0 |
| small | clean-architecture | current | 3.3 s | 4.4 s | 4.4 s | 329 MB | 335 MB (1) | 0 |
| small | eshop | main-2026-11-04 | 9.4 s | 11.1 s | 11.1 s | 511 MB | 511 MB (1) | 0 |
| small | eshop | current | 7.1 s | 8.6 s | 8.6 s | 489 MB | 489 MB (1) | 0 |
| medium | jellyfin | main-2026-11-04 | 8.2 s | 11.9 s | 11.9 s | 902 MB | 902 MB (1) | 0 |
| medium | jellyfin | current | 8.8 s | 12.0 s | 12.0 s | 908 MB | 908 MB (1) | 0 |
| large | orchardcore | main-2026-11-04 | 52.4 s | 59.9 s | 59.9 s | 1,478 MB | 1,467 MB (1) | 0 |
| large | orchardcore | current | 41.3 s | 47.9 s | 47.9 s | 1,376 MB | 1,412 MB (1) | 0 |
| xl | abp | main-2026-11-04 | 63.5 s | 67.9 s | 67.8 s | 1,613 MB | 2,118 MB (1) | 0 |
| xl | abp | current | 64.4 s | 68.2 s | 68.2 s | 1,564 MB | 2,144 MB (1) | 0 |

## Tokens per task (current)

Grep shows matching lines / lines an agent receives (head limit). Read is the file-read floor: the files the answer lives in, each once. "Trunc." marks a navigator answer that hit its result cap — its file list, and so the manual baseline, is then partial (conservative).

### small: clean-architecture

| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved | Nav tok (main-2026-11-04) |
|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Locate the definition of AppDbContext | 87 |  | 42 (1 / 1) | 163 (1) | 205 | 57.6% | 2.4× | 1 | 19 | 0.0 | 73 |
| Find all usages of IEmailSender | 355 |  | 315 (8 / 8) | 1,311 (5) | 1,626 | 78.2% | 4.6× | 1 | 8 | 0.1 | 407 |
| List implementations of IEmailSender | 85 |  | 80 (2 / 2) | 481 (2) | 561 | 84.8% | 6.6× | 1 | 6 | 0.1 | 97 |
| Outline Extensions.cs (largest file) | 296 |  | — | 1,372 (1) | 1,372 | 78.4% | 4.6× | 0 | 0 | 0.1 | 307 |
| Read the body of GetContributorByIdRequest.BuildRoute | 86 |  | 265 (5 / 5) | 95 (1) | 360 | 76.1% | 4.2× | 0 | 4 | 0.0 | 89 |
| Find callers of GetContributorByIdRequest.BuildRoute | 128 |  | 265 (5 / 5) | 248 (1) | 513 | 75.0% | 4.0× | 2 | 4 | 0.0 | 131 |
| Type hierarchy of AppDbContext | 95 |  | 0 (0 / 0) | 163 (1) | 163 | 41.7% | 1.7× | 0 | 5 | 0.0 | 98 |
| Project dependency graph | 572 |  | 732 (14 / 14) | 2,839 (9) | 3,571 | 84.0% | 6.2× | 3 | 3 | 0.3 | 975 |
| DI registrations | 679 |  | 525 (13 / 13) | 1,468 (3) | 1,993 | 65.9% | 2.9× | 4 | 13 | 0.1 | 694 |
| HTTP endpoint map | 20 |  | 0 (0 / 0) | 0 (0) | 0 | — | — | 5 | 8 | -0.0 | 20 |
| Resolve a stack trace to source | 367 |  | 340 (7 / 7) | 343 (2) | 683 | 46.3% | 1.9× | 1 | 6 | 0.0 | 406 |
| Diagnostics for AppDbContext.cs | 30 |  | — | 163 (1) | 163 | 81.6% | 5.4× | 0 | 0 | 0.0 | 30 |

### small: eshop

| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved | Nav tok (main-2026-11-04) |
|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Locate the definition of ApplicationUser | 76 |  | 27 (1 / 1) | 313 (1) | 340 | 77.6% | 4.5× | 1 | 34 | 0.0 | 66 |
| Find all usages of IBasketService | 394 |  | 259 (8 / 8) | 2,531 (4) | 2,790 | 85.9% | 7.1× | 5 | 34 | 0.2 | 5,259 |
| List implementations of IBasketService | 76 |  | 99 (3 / 3) | 1,738 (2) | 1,837 | 95.9% | 24.2× | 1 | 34 | 0.2 | 87 |
| Outline CatalogApi.cs (largest file) | 1,399 |  | — | 6,088 (1) | 6,088 | 77.0% | 4.4× | 1 | 0 | 0.5 | 1,567 |
| Read the body of OrderingContext.BeginTransactionAsync | 129 |  | 164 (4 / 4) | 1,243 (1) | 1,407 | 90.8% | 10.9× | 0 | 35 | 0.1 | 132 |
| Find callers of OrderingContext.BeginTransactionAsync | 64 |  | 164 (4 / 4) | 766 (1) | 930 | 93.1% | 14.5× | 1 | 36 | 0.1 | 65 |
| Type hierarchy of ViewModelBase | 116 |  | 320 (11 / 11) | 413 (1) | 733 | 84.2% | 6.3× | 1 | 35 | 0.1 | 116 |
| Project dependency graph | 1,322 |  | 1,793 (46 / 46) | 10,249 (28) | 12,042 | 89.0% | 9.1× | 5 | 4 | 1.1 | 3,066 |
| DI registrations | 3,638 |  | 2,259 (69 / 69) | 13,480 (18) | 15,739 | 76.9% | 4.3× | 26 | 34 | 1.2 | 3,653 |
| HTTP endpoint map | 1,889 |  | 1,446 (51 / 51) | 23,476 (11) | 24,922 | 92.4% | 13.2× | 34 | 34 | 2.3 | 1,889 |
| Resolve a stack trace to source | 288 |  | 2,549 (50 / 50) | 2,009 (2) | 4,558 | 93.7% | 15.8× | 1 | 34 | 0.4 | 332 |
| Diagnostics for ApplicationUser.cs | 105 |  | — | 313 (1) | 313 | 66.5% | 3.0× | 2 | 0 | 0.0 | 30 |

### medium: jellyfin

| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved | Nav tok (main-2026-11-04) |
|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Locate the definition of BaseItem | 77 |  | 44 (1 / 1) | 30,585 (1) | 30,629 | 99.7% | 397.8× | 1 | 102 | 3.1 | 67 |
| Find all usages of IApplicationPaths | 5,441 | yes | 6,064 (146 / 146) | 156,331 (59) | 162,395 | 96.6% | 29.8× | 5 | 98 | 15.7 | 6,187 |
| List implementations of IApplicationPaths | 84 |  | 1,353 (22 / 22) | 2,898 (2) | 4,251 | 98.0% | 50.6× | 1 | 92 | 0.4 | 95 |
| Outline EncodingHelper.cs (largest file) | 7,712 |  | — | 102,369 (1) | 102,369 | 92.5% | 13.3× | 4 | 0 | 9.5 | 7,878 |
| Read the body of BaseItem.GetParent | 115 |  | 1,161 (35 / 35) | 30,585 (1) | 31,746 | 99.6% | 276.1× | 0 | 93 | 3.2 | 115 |
| Find callers of BaseItem.GetParent | 1,158 |  | 1,161 (35 / 35) | 143,589 (12) | 144,750 | 99.2% | 125.0× | 6 | 91 | 14.4 | 1,195 |
| Type hierarchy of BaseItem | 191 |  | 2,841 (93 / 93) | 30,585 (1) | 33,426 | 99.4% | 175.0× | 3 | 90 | 3.3 | 197 |
| Project dependency graph | 2,463 |  | 4,080 (92 / 92) | 18,051 (41) | 22,131 | 88.9% | 9.0× | 9 | 5 | 2.0 | 6,027 |
| DI registrations | 5,997 | yes | 7,319 (180 / 180) | 13,499 (2) | 20,818 | 71.2% | 3.5× | 314 | 85 | 1.5 | 6,074 |
| HTTP endpoint map | 68 |  | 6,388 (436 / 250) | 404 (1) | 6,792 | 99.0% | 99.9× | 333 | 88 | 0.7 | 68 |
| Resolve a stack trace to source | 460 |  | 1,931 (53 / 53) | 54,015 (4) | 55,946 | 99.2% | 121.6× | 3 | 86 | 5.6 | 501 |
| Diagnostics for BaseItem.cs | 30 |  | — | 30,585 (1) | 30,585 | 99.9% | 1019.5× | 2 | 0 | 3.1 | 30 |

### large: orchardcore

| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved | Nav tok (main-2026-11-04) |
|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Locate the definition of StartupBase | 77 |  | 36 (1 / 1) | 245 (1) | 281 | 72.6% | 3.6× | 2 | 509 | 0.0 | 69 |
| Find all usages of IShape | 6,352 | yes | 11,291 (534 / 250) | 47,022 (39) | 58,313 | 89.1% | 9.2× | 8 | 326 | 5.2 | 6,965 |
| List implementations of IShape | 2,263 |  | 1,101 (21 / 21) | 15,402 (54) | 16,503 | 86.3% | 7.3× | 12 | 346 | 1.4 | 2,589 |
| Outline ResourceManagementOptionsConfiguration.cs (largest file) | 368 |  | — | 16,286 (1) | 16,286 | 97.7% | 44.3× | 4 | 0 | 1.6 | 371 |
| Read the body of WorkflowExecutionContext.Dispose | 80 |  | 8,131 (322 / 250) | 4,124 (1) | 12,255 | 99.3% | 153.2× | 1 | 355 | 1.2 | 80 |
| Find callers of WorkflowExecutionContext.Dispose | 6,106 | yes | 8,131 (322 / 250) | 123,520 (33) | 131,651 | 95.4% | 21.6× | 64 | 341 | 12.6 | 4,431 |
| Type hierarchy of StartupBase | 661 | yes | 8,133 (398 / 250) | 245 (1) | 8,378 | 92.1% | 12.7× | 9 | 381 | 0.8 | 607 |
| Project dependency graph | 12,817 | yes | 14,656 (1,392 / 250) | 48,121 (100) | 62,777 | 79.6% | 4.9× | 60 | 19 | 5.0 | 70,873 |
| DI registrations | 9,415 | yes | 10,672 (1,661 / 250) | 36,787 (22) | 47,459 | 80.2% | 5.0× | 180 | 399 | 3.8 | 9,481 |
| HTTP endpoint map | 4,956 | yes | 7,841 (404 / 250) | 84,737 (41) | 92,578 | 94.6% | 18.7× | 124 | 352 | 8.8 | 4,956 |
| Resolve a stack trace to source | 462 |  | 8,614 (367 / 250) | 22,295 (3) | 30,909 | 98.5% | 66.9× | 6 | 362 | 3.0 | 506 |
| Diagnostics for StartupBase.cs | 109 |  | — | 245 (1) | 245 | 55.5% | 2.2× | 7 | 0 | 0.0 | 30 |

### xl: abp

| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved | Nav tok (main-2026-11-04) |
|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Locate the definition of AbpException | 65 |  | 24 (1 / 1) | 133 (1) | 157 | 58.6% | 2.4× | 3 | 237 | 0.0 | 223 |
| Find all usages of ITransientDependency | 6,161 | yes | 12,387 (526 / 250) | 96,799 (100) | 109,186 | 94.4% | 17.7× | 56 | 242 | 10.3 | 6,695 |
| List implementations of ITransientDependency | 3,865 | yes | 12,349 (512 / 250) | 74,242 (97) | 86,591 | 95.5% | 22.4× | 36 | 273 | 8.3 | 4,303 |
| Outline DistributedCache.cs (largest file) | 3,599 |  | — | 15,161 (1) | 15,161 | 76.3% | 4.2× | 8 | 0 | 1.2 | 3,850 |
| Read the body of AbpModule.IsAbpModule | 136 |  | 175 (5 / 5) | 1,568 (1) | 1,743 | 92.2% | 12.8× | 2 | 234 | 0.2 | 145 |
| Find callers of AbpModule.IsAbpModule | 257 |  | 175 (5 / 5) | 12,625 (5) | 12,800 | 98.0% | 49.8× | 142 | 242 | 1.3 | 1,241 |
| Type hierarchy of AbpModule | 844 | yes | 10,282 (308 / 250) | 1,568 (1) | 11,850 | 92.9% | 14.0× | 13 | 248 | 1.1 | 844 |
| Project dependency graph | 7,786 | yes | 12,619 (678 / 250) | 33,219 (100) | 45,838 | 83.0% | 5.9× | 40 | 20 | 3.8 | 173,037 |
| DI registrations | 7,193 | yes | 10,476 (217 / 217) | 37,719 (45) | 48,195 | 85.1% | 6.7× | 139 | 236 | 4.1 | 7,413 |
| HTTP endpoint map | 440 |  | 9,058 (269 / 250) | 2,414 (3) | 11,472 | 96.2% | 26.1× | 95 | 247 | 1.1 | 440 |
| Resolve a stack trace to source | 410 |  | 462 (13 / 13) | 11,812 (3) | 12,274 | 96.7% | 29.9× | 12 | 246 | 1.2 | 446 |
| Diagnostics for AbpException.cs | 121 |  | — | 133 (1) | 133 | 9.0% | 1.1× | 18 | 0 | 0.0 | 30 |

## Per-tool latency — before vs after

Client-side round trip over stdio, in ms. Cold = first call to that tool in the session, after warm-up was awaited. Warm = median / p95 of five repeats (p95 of five is the slowest). n/a = tool not in that build.

### small: clean-architecture

| Tool | Cold (main-2026-11-04) | Cold (current) | Warm p50 (main-2026-11-04) | Warm p50 (current) | Warm p95 (current) | Tokens (main-2026-11-04) | Tokens (current) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `find_symbol` | 1 | 1 | 0 | 1 | 1 | 73 | 87 |
| `find_references` | 151 | 162 | 1 | 1 | 3 | 407 | 355 |
| `find_implementations` | 130 | 131 | 1 | 1 | 2 | 97 | 85 |
| `find_callers` | 101 | 112 | 2 | 2 | 2 | 131 | 128 |
| `get_type_hierarchy` | 4 | 5 | 0 | 0 | 1 | 98 | 95 |
| `get_public_api` | 9 | 11 | 0 | 1 | 1 | 143 | 137 |
| `get_symbol_detail` | 4 | 6 | 0 | 0 | 1 | 117 | 107 |
| `get_symbol_source` | 3 | 4 | 1 | 0 | 1 | 89 | 86 |
| `get_file_outline` | 7 | 10 | 1 | 0 | 1 | 307 | 296 |
| `get_project_graph` | 3 | 33 | 2 | 3 | 14 | 975 | 572 |
| `get_dependency_graph` | 20 | 18 | 1 | 1 | 1 | 37 | 37 |
| `detect_circular_dependencies` | 4 | 4 | 0 | 0 | 1 | 7 | 7 |
| `get_diagnostics` | 149 | 93 | 2 | 0 | 1 | 30 | 30 |
| `resolve_stack_trace` | 13 | 13 | 1 | 1 | 1 | 406 | 367 |
| `get_nuget_packages` | 9 | 10 | 5 | 4 | 5 | 1,263 | 1,263 |
| `analyze_change_impact` | 23 | 22 | 3 | 2 | 3 | 248 | 253 |
| `get_di_registrations` | 21 | 38 | 4 | 4 | 5 | 694 | 679 |
| `get_endpoint_map` | 10 | 11 | 5 | 5 | 5 | 20 | 20 |
| `get_test_coverage_map` | 13 | 16 | 4 | 4 | 5 | 2,152 | 1,923 |
| `detect_antipatterns` | 96 | 117 | 23 | 11 | 12 | 620 | 524 |
| `find_dead_code` | 149 | 77 | 25 | 10 | 30 | 35 | 35 |

### small: eshop

| Tool | Cold (main-2026-11-04) | Cold (current) | Warm p50 (main-2026-11-04) | Warm p50 (current) | Warm p95 (current) | Tokens (main-2026-11-04) | Tokens (current) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `find_symbol` | 2 | 1 | 1 | 1 | 2 | 66 | 76 |
| `find_references` | 317 | 245 | 6 | 5 | 7 | 5,259 | 394 |
| `find_implementations` | 127 | 119 | 2 | 1 | 2 | 87 | 76 |
| `find_callers` | 147 | 130 | 2 | 1 | 2 | 65 | 64 |
| `find_overrides` | 7 | 8 | 1 | 1 | 1 | 466 | 426 |
| `get_type_hierarchy` | 8 | 7 | 1 | 1 | 1 | 116 | 116 |
| `get_public_api` | 5 | 5 | 1 | 1 | 1 | 353 | 353 |
| `get_symbol_detail` | 5 | 4 | 1 | 1 | 2 | 117 | 111 |
| `get_symbol_source` | 3 | 4 | 1 | 0 | 1 | 132 | 129 |
| `get_file_outline` | 11 | 10 | 2 | 1 | 2 | 1,567 | 1,399 |
| `get_project_graph` | 5 | 5 | 5 | 5 | 16 | 3,066 | 1,322 |
| `get_dependency_graph` | 7 | 6 | 2 | 1 | 3 | 41 | 39 |
| `detect_circular_dependencies` | 4 | 3 | 0 | 0 | 1 | 7 | 81 |
| `get_diagnostics` | 244 | 54 | 4 | 2 | 4 | 30 | 105 |
| `resolve_stack_trace` | 30 | 17 | 3 | 1 | 2 | 332 | 288 |
| `get_nuget_packages` | 38 | 11 | 12 | 10 | 23 | 2,347 | 2,347 |
| `analyze_change_impact` | 51 | 39 | 5 | 4 | 5 | 201 | 205 |
| `get_di_registrations` | 58 | 68 | 30 | 26 | 33 | 3,653 | 3,638 |
| `get_endpoint_map` | 60 | 39 | 38 | 34 | 59 | 1,889 | 1,889 |
| `get_test_coverage_map` | 62 | 64 | 26 | 27 | 44 | 2,035 | 1,825 |
| `detect_antipatterns` | 597 | 303 | 430 | 130 | 172 | 11,398 | 7,180 |
| `find_dead_code` | 1,777 | 165 | 182 | 84 | 140 | 189 | 827 |

### medium: jellyfin

| Tool | Cold (main-2026-11-04) | Cold (current) | Warm p50 (main-2026-11-04) | Warm p50 (current) | Warm p95 (current) | Tokens (main-2026-11-04) | Tokens (current) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `find_symbol` | 3 | 2 | 2 | 1 | 1 | 67 | 77 |
| `find_references` | 719 | 890 | 6 | 5 | 25 | 6,187 | 5,441 |
| `find_implementations` | 990 | 970 | 2 | 1 | 11 | 95 | 84 |
| `find_callers` | 137 | 105 | 8 | 6 | 8 | 1,195 | 1,158 |
| `find_overrides` | 15 | 15 | 3 | 3 | 4 | 844 | 741 |
| `get_type_hierarchy` | 6 | 8 | 3 | 3 | 27 | 197 | 191 |
| `get_public_api` | 44 | 12 | 3 | 2 | 2 | 2,669 | 2,660 |
| `get_symbol_detail` | 7 | 8 | 1 | 1 | 1 | 94 | 90 |
| `get_symbol_source` | 4 | 4 | 1 | 0 | 1 | 115 | 115 |
| `get_file_outline` | 12 | 13 | 4 | 4 | 4 | 7,878 | 7,712 |
| `get_project_graph` | 6 | 9 | 6 | 9 | 15 | 6,027 | 2,463 |
| `get_dependency_graph` | 12 | 10 | 5 | 2 | 10 | 104 | 104 |
| `detect_circular_dependencies` | 4 | 4 | 0 | 0 | 1 | 7 | 7 |
| `get_diagnostics` | 815 | 664 | 112 | 2 | 10 | 30 | 30 |
| `resolve_stack_trace` | 18 | 16 | 4 | 3 | 3 | 501 | 460 |
| `get_nuget_packages` | 21 | 19 | 14 | 15 | 18 | 2,607 | 2,607 |
| `analyze_change_impact` | 442 | 178 | 169 | 69 | 107 | 1,603 | 1,527 |
| `get_di_registrations` | 363 | 306 | 312 | 314 | 326 | 6,074 | 5,997 |
| `get_endpoint_map` | 392 | 315 | 389 | 333 | 336 | 68 | 68 |
| `get_test_coverage_map` | 110 | 152 | 40 | 71 | 74 | 1,898 | 1,729 |
| `detect_antipatterns` | 2,771 | 2,421 | 952 | 681 | 1,183 | 12,127 | 6,559 |
| `find_dead_code` | 6,226 | 978 | 1,086 | 212 | 239 | 299 | 281 |

### large: orchardcore

| Tool | Cold (main-2026-11-04) | Cold (current) | Warm p50 (main-2026-11-04) | Warm p50 (current) | Warm p95 (current) | Tokens (main-2026-11-04) | Tokens (current) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `find_symbol` | 6 | 4 | 3 | 2 | 3 | 69 | 77 |
| `find_references` | 1,674 | 1,616 | 12 | 8 | 11 | 6,965 | 6,352 |
| `find_implementations` | 2,134 | 2,337 | 13 | 12 | 13 | 2,589 | 2,263 |
| `find_callers` | 1,977 | 2,012 | 63 | 64 | 121 | 4,431 | 6,106 |
| `find_overrides` | 326 | 302 | 34 | 31 | 33 | 4,761 | 4,267 |
| `get_type_hierarchy` | 14 | 15 | 9 | 9 | 30 | 607 | 661 |
| `get_public_api` | 6 | 6 | 2 | 1 | 2 | 232 | 232 |
| `get_symbol_detail` | 6 | 5 | 2 | 1 | 2 | 96 | 92 |
| `get_symbol_source` | 5 | 5 | 2 | 1 | 1 | 80 | 80 |
| `get_file_outline` | 52 | 13 | 4 | 4 | 34 | 371 | 368 |
| `get_project_graph` | 39 | 62 | 36 | 60 | 61 | 70,873 | 12,817 |
| `get_dependency_graph` | 12 | 10 | 6 | 5 | 17 | 31 | 31 |
| `detect_circular_dependencies` | 8 | 8 | 4 | 4 | 12 | 7 | 85 |
| `get_diagnostics` | 196 | 82 | 24 | 7 | 8 | 30 | 109 |
| `resolve_stack_trace` | 32 | 22 | 12 | 6 | 7 | 506 | 462 |
| `get_nuget_packages` | 112 | 104 | 105 | 100 | 102 | 8,155 | 8,155 |
| `analyze_change_impact` | 54,461 | 1,200 | 48,157 | 794 | 820 | 4,347 | 4,058 |
| `get_di_registrations` | 376 | 238 | 160 | 180 | 191 | 9,481 | 9,415 |
| `get_endpoint_map` | 148 | 139 | 124 | 124 | 164 | 4,956 | 4,956 |
| `get_test_coverage_map` | 153 | 160 | 74 | 90 | 126 | 2,377 | 2,162 |
| `detect_antipatterns` | 908 | 279 | 670 | 206 | 287 | 13,351 | 7,422 |
| `find_dead_code` | 4,586 | 884 | 1,582 | 286 | 324 | 541 | 497 |

### xl: abp

| Tool | Cold (main-2026-11-04) | Cold (current) | Warm p50 (main-2026-11-04) | Warm p50 (current) | Warm p95 (current) | Tokens (main-2026-11-04) | Tokens (current) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `find_symbol` | 10 | 5 | 6 | 3 | 26 | 223 | 65 |
| `find_references` | 2,206 | 1,955 | 53 | 56 | 176 | 6,695 | 6,161 |
| `find_implementations` | 662 | 704 | 46 | 36 | 62 | 4,303 | 3,865 |
| `find_callers` | 4,141 | 3,650 | 230 | 142 | 220 | 1,241 | 257 |
| `find_overrides` | 28 | 32 | 15 | 9 | 10 | 4,808 | 4,397 |
| `get_type_hierarchy` | 29 | 20 | 18 | 13 | 14 | 844 | 844 |
| `get_public_api` | 11 | 7 | 5 | 2 | 3 | 135 | 135 |
| `get_symbol_detail` | 9 | 9 | 4 | 2 | 2 | 98 | 88 |
| `get_symbol_source` | 8 | 6 | 4 | 2 | 2 | 145 | 136 |
| `get_file_outline` | 17 | 25 | 6 | 8 | 13 | 3,850 | 3,599 |
| `get_project_graph` | 66 | 52 | 58 | 40 | 53 | 173,037 | 7,786 |
| `get_dependency_graph` | 34 | 29 | 11 | 10 | 13 | 30 | 30 |
| `detect_circular_dependencies` | 7 | 11 | 3 | 5 | 5 | 7 | 98 |
| `get_diagnostics` | 855 | 113 | 66 | 18 | 26 | 30 | 121 |
| `resolve_stack_trace` | 36 | 27 | 28 | 12 | 13 | 446 | 410 |
| `get_nuget_packages` | 83 | 132 | 120 | 117 | 122 | 9,559 | 9,559 |
| `analyze_change_impact` | 998 | 232 | 715 | 127 | 143 | 1,477 | 544 |
| `get_di_registrations` | 258 | 180 | 107 | 139 | 143 | 7,413 | 7,193 |
| `get_endpoint_map` | 117 | 103 | 90 | 95 | 112 | 440 | 440 |
| `get_test_coverage_map` | 332 | 398 | 114 | 188 | 246 | 2,111 | 2,445 |
| `detect_antipatterns` | 3,036 | 426 | 1,641 | 216 | 243 | 12,384 | 8,901 |
| `find_dead_code` | 4,227 | 2,412 | 2,170 | 527 | 790 | 3,118 | 2,820 |

## Slowest tools per corpus (current, cold)

| Tier | Corpus | Slowest five (cold ms) |
|---|---|---|
| small | clean-architecture | `find_references` 162, `find_implementations` 131, `detect_antipatterns` 117, `find_callers` 112, `get_diagnostics` 93 |
| small | eshop | `detect_antipatterns` 303, `find_references` 245, `find_dead_code` 165, `find_callers` 130, `find_implementations` 119 |
| medium | jellyfin | `detect_antipatterns` 2,421, `find_dead_code` 978, `find_implementations` 970, `find_references` 890, `get_diagnostics` 664 |
| large | orchardcore | `find_implementations` 2,337, `find_callers` 2,012, `find_references` 1,616, `analyze_change_impact` 1,200, `find_dead_code` 884 |
| xl | abp | `find_callers` 3,650, `find_dead_code` 2,412, `find_references` 1,955, `find_implementations` 704, `detect_antipatterns` 426 |

## Errors, timeouts, and load problems

- **clean-architecture** note: Restore needs -p:NuGetAudit=false: the repo treats NuGet vulnerability warnings (NU1902/NU1903 on SSH.NET and System.Security.Cryptography.Pkcs at this commit) as errors. Nothing in the source is changed.
- **eshop / main-2026-11-04** workspace diagnostics logged by the server (4; non-fatal unless projects loaded < declared): Workspace failure: Msbuild failed when processing the file 'eShop\tests\Basket.FunctionalTests\Basket.FunctionalTests.csproj' with message: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-qhrr-8q5h-9q3h; Workspace failure: Msbuild failed when processing the file 'eShop\tests\Catalog.FunctionalTests\Catalog.FunctionalTests.csproj' with message: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-qhrr-8q5h-9q3h; Workspace failure: Msbuild failed when processing the file 'eShop\tests\Ordering.FunctionalTests\Ordering.FunctionalTests.csproj' with message: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-qhrr-8q5h-9q3h; Workspace failure: Msbuild failed when processing the file 'eShop\tests\Webhooks.FunctionalTests\Webhooks.FunctionalTests.csproj' with message: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-qhrr-8q5h-9q3h
- **eshop / current** workspace diagnostics logged by the server (4; non-fatal unless projects loaded < declared): Workspace failure: Msbuild failed when processing the file 'eShop\tests\Basket.FunctionalTests\Basket.FunctionalTests.csproj' with message: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-qhrr-8q5h-9q3h; Workspace failure: Msbuild failed when processing the file 'eShop\tests\Catalog.FunctionalTests\Catalog.FunctionalTests.csproj' with message: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-qhrr-8q5h-9q3h; Workspace failure: Msbuild failed when processing the file 'eShop\tests\Ordering.FunctionalTests\Ordering.FunctionalTests.csproj' with message: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-qhrr-8q5h-9q3h; Workspace failure: Msbuild failed when processing the file 'eShop\tests\Webhooks.FunctionalTests\Webhooks.FunctionalTests.csproj' with message: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-qhrr-8q5h-9q3h
- **eshop** note: 3 MAUI projects (src/ClientApp, src/HybridApp, tests/ClientApp.UnitTests) need the maui-android/maui-tizen workloads, which are not installed on the benchmark machine. They fail restore (NETSDK1147), yet MSBuildWorkspace still loads them, once per target framework, without their packages, which is why 35 Roslyn projects load from 28 declared. Every other project restores and loads normally.
- **jellyfin** note: The in-solution analyzer project src/Jellyfin.CodeAnalysis (referenced as OutputItemType=Analyzer from Directory.Build.props) is built after restore, as it would be in any developer's working copy. On a restore-only clone it is an UnresolvedAnalyzerReference, and the server's SymbolFinder-based tools throw (server bug; see the Server bugs section of bench/README.md).
- **orchardcore / main-2026-11-04** workspace diagnostics logged by the server (80; non-fatal unless projects loaded < declared): Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore.Cms.Web\OrchardCore.Cms.Web.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8; Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore\OrchardCore.Application.Cms.Targets\OrchardCore.Application.Cms.Targets.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8; Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore\OrchardCore.Application.Cms.Core.Targets\OrchardCore.Application.Cms.Core.Targets.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8; Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore.Modules\OrchardCore.AdminDashboard\OrchardCore.AdminDashboard.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8; Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore\OrchardCore.ContentManagement\OrchardCore.ContentManagement.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8
- **orchardcore / current** workspace diagnostics logged by the server (80; non-fatal unless projects loaded < declared): Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore.Cms.Web\OrchardCore.Cms.Web.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8; Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore\OrchardCore.Application.Cms.Targets\OrchardCore.Application.Cms.Targets.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8; Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore\OrchardCore.Application.Cms.Core.Targets\OrchardCore.Application.Cms.Core.Targets.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8; Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore.Modules\OrchardCore.AdminDashboard\OrchardCore.AdminDashboard.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8; Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore\OrchardCore.ContentManagement\OrchardCore.ContentManagement.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8
- **orchardcore** note: The three in-solution analyzer/generator projects (OrchardCore.SourceGenerators, OrchardCore.Localization.Analyzers, OrchardCore.Localization.Analyzers.CodeFixes; referenced as OutputItemType=Analyzer from OrchardCore.Commons.props) are built after restore, as in any developer's working copy. On a restore-only clone they are UnresolvedAnalyzerReferences and the server's SymbolFinder-based tools throw (server bug; see the Server bugs section of bench/README.md).
- **abp / main-2026-11-04** workspace diagnostics logged by the server (13; non-fatal unless projects loaded < declared): Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.Core\Volo.Abp.Core.csproj' with message: PackageReference System.Linq.Queryable will not be pruned. This package is automatically available and does not need to be referenced explicitly. Remove the PackageReference item.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.Cli.Core\Volo.Abp.Cli.Core.csproj' with message: Package 'LibGit2Sharp 0.31.0' was restored using '.NETPortable,Version=v0.0,Profile=Profile259, .NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework '.NETStandard,Version=v2.0'. This package may not be fully compatible with your project.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.EntityFrameworkCore.MySQL.Pomelo\Volo.Abp.EntityFrameworkCore.MySQL.Pomelo.csproj' with message: Detected package version outside of dependency constraint: Pomelo.EntityFrameworkCore.MySql 9.0.0 requires Microsoft.EntityFrameworkCore.Relational (>= 9.0.0 && <= 9.0.999) but version Microsoft.EntityFrameworkCore.Relational 10.0.11 was resolved.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.EntityFrameworkCore.Oracle.Devart\Volo.Abp.EntityFrameworkCore.Oracle.Devart.csproj' with message: Detected package downgrade: Microsoft.EntityFrameworkCore.Relational from 10.0.11 to 9.0.12. Reference the package directly from the project to select a different version.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.EventBus.Rebus\Volo.Abp.EventBus.Rebus.csproj' with message: Detected package version outside of dependency constraint: Rebus 8.8.0 requires system.text.json (>= 8.0.5 && < 10.0.0) but version System.Text.Json 10.0.11 was resolved.
- **abp / current** workspace diagnostics logged by the server (13; non-fatal unless projects loaded < declared): Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.Core\Volo.Abp.Core.csproj' with message: PackageReference System.Linq.Queryable will not be pruned. This package is automatically available and does not need to be referenced explicitly. Remove the PackageReference item.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.Cli.Core\Volo.Abp.Cli.Core.csproj' with message: Package 'LibGit2Sharp 0.31.0' was restored using '.NETPortable,Version=v0.0,Profile=Profile259, .NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework '.NETStandard,Version=v2.0'. This package may not be fully compatible with your project.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.EntityFrameworkCore.MySQL.Pomelo\Volo.Abp.EntityFrameworkCore.MySQL.Pomelo.csproj' with message: Detected package version outside of dependency constraint: Pomelo.EntityFrameworkCore.MySql 9.0.0 requires Microsoft.EntityFrameworkCore.Relational (>= 9.0.0 && <= 9.0.999) but version Microsoft.EntityFrameworkCore.Relational 10.0.11 was resolved.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.EntityFrameworkCore.Oracle.Devart\Volo.Abp.EntityFrameworkCore.Oracle.Devart.csproj' with message: Detected package downgrade: Microsoft.EntityFrameworkCore.Relational from 10.0.11 to 9.0.12. Reference the package directly from the project to select a different version.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.EventBus.Rebus\Volo.Abp.EventBus.Rebus.csproj' with message: Detected package version outside of dependency constraint: Rebus 8.8.0 requires system.text.json (>= 8.0.5 && < 10.0.0) but version System.Text.Json 10.0.11 was resolved.
- **abp** note: Release tag 10.7.1, not the dev branch. ABP's dev head (a840be3) pins SDK 11.0.100-rc.3 and multi-targets net11.0 in 176 of 178 framework/src projects, which .NET SDK 10 cannot build or load. The 10.7.1 release targets net10.0 (plus netstandard2.x/net8/net9) and restores cleanly on SDK 10.0.403, so the xl tier keeps the same codebase.
- **abp** note: Clone with core.longpaths=true on Windows: several test paths exceed 260 characters.

