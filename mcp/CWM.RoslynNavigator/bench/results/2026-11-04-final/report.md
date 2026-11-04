# CWM.RoslynNavigator benchmark — 2026-11-04-final

Builds: **published-0.8.0** (before) vs **current** (after). Machine: Microsoft Windows 10.0.26300, 24 logical CPUs, X64, harness on .NET 10.0.14.

Every number below is **measured** unless its column says **modelled**. Tokens are estimated as bytes / 3.5 (divisor **not calibrated** — the server's own TokenEstimator). Modelled time assumes **10,000 input tokens/s**; see bench/README.md.

## Headline — 12 agent tasks per corpus (current)

Navigator = the tool response. Grep + Read = the search an agent runs plus every file the answer lives in, each file counted once. Only tasks the navigator answered are summed. Measured times are tool round trip vs search + file I/O only — no model time on either side.

| Tier | Corpus | Tasks | Navigator tokens | Grep + Read tokens | Tokens saved | × fewer | Measured: navigator | Measured: grep + read I/O | Agent tool calls (nav / manual) | **Modelled** agent time saved |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| small | clean-architecture | 12/12 | 2,800 | 11,210 | 75.0% | 4.0× | 19 ms | 66 ms | 12 / 37 | 0.8 s |
| small | eshop | 12/12 | 9,458 | 71,699 | 86.8% | 7.6× | 79 ms | 271 ms | 12 / 81 | 6.2 s |
| medium | jellyfin | 12/12 | 23,487 | 670,085 | 96.5% | 28.5× | 657 ms | 893 ms | 12 / 132 | 64.7 s |
| large | orchardcore | 12/12 | 43,417 | 474,615 | 90.9% | 10.9× | 503 ms | 3,234 ms | 12 / 319 | 43.1 s |
| xl | abp | 12/12 | 25,161 | 282,739 | 91.1% | 11.2× | 388 ms | 2,254 ms | 12 / 274 | 25.8 s |

## Corpora

| Tier | Corpus | Commit | Solution | Projects declared | Projects loaded (published-0.8.0 / current) | .cs files | C# source |
|---|---|---|---|---:|---:|---:|---:|
| small | [clean-architecture](https://github.com/ardalis/CleanArchitecture) | `e7c79be54b` | Clean.Architecture.slnx | 9 | 9 / 9 | 95 | 0.1 MB |
| small | [eshop](https://github.com/dotnet/eShop) | `913acea667` | eShop.slnx | 28 | 35 / 28 | 545 | 0.9 MB |
| medium | [jellyfin](https://github.com/jellyfin/jellyfin) | `5b385862fb` | Jellyfin.slnx | 41 | 41 / 41 | 2,248 | 13.1 MB |
| large | [orchardcore](https://github.com/OrchardCMS/OrchardCore) | `f6040cb563` | OrchardCore.slnx | 257 | 257 / 257 | 6,451 | 16.6 MB |
| xl | [abp](https://github.com/abpframework/abp) | `6aeb69d831` | framework/Volo.Abp.slnx | 270 | 690 / 271 | 4,639 | 8.4 MB |

## Startup and memory

Ready = process start until a tool stops answering "loading". First answer = a `find_symbol` issued the moment it is ready. Warm = the server's own "All compilations warmed" log line (n/a = not logged within the 20-minute wait). Server peak WS is the OS-reported peak of the server process. Server + build hosts is the largest simultaneous total seen at a 500 ms sample; build hosts are the MSBuild processes the server spawns (count in brackets).

| Tier | Corpus | Build | Ready | First answer | All compilations warm | Server peak WS | Server + build hosts (peak, sampled) | Restarts |
|---|---|---|---:|---:|---:|---:|---:|---:|
| small | clean-architecture | published-0.8.0 | 7.6 s | 7.6 s | 4.7 s | 389 MB | 523 MB (4) | 0 |
| small | clean-architecture | current | 3.2 s | 4.4 s | 4.4 s | 325 MB | 335 MB (1) | 0 |
| small | eshop | published-0.8.0 | 8.5 s | 8.6 s | 8.4 s | 529 MB | 529 MB (2) | 0 |
| small | eshop | current | 4.5 s | 6.0 s | 6.0 s | 456 MB | 838 MB (4) | 0 |
| medium | jellyfin | published-0.8.0 | 12.7 s | 12.7 s | 12.4 s | 1,137 MB | 1,134 MB (2) | 0 |
| medium | jellyfin | current | 8.5 s | 12.0 s | 12.0 s | 880 MB | 880 MB (1) | 0 |
| large | orchardcore | published-0.8.0 | 93.5 s | 114.2 s | n/a | 2,124 MB | 3,016 MB (14) | 0 |
| large | orchardcore | current | 44.4 s | 50.8 s | 50.8 s | 1,359 MB | 1,410 MB (1) | 0 |
| xl | abp | published-0.8.0 | 77.2 s | 95.0 s | n/a | 1,668 MB | 2,139 MB (2) | 0 |
| xl | abp | current | 47.2 s | 49.9 s | 49.9 s | 1,152 MB | 4,472 MB (4) | 0 |

## Tokens per task (current)

Grep shows matching lines / lines an agent receives (head limit). Read is the file-read floor: the files the answer lives in, each once. "Trunc." marks a navigator answer that hit its result cap — its file list, and so the manual baseline, is then partial (conservative).

### small: clean-architecture

| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved | Nav tok (published-0.8.0) |
|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Locate the definition of AppDbContext | 87 |  | 42 (1 / 1) | 163 (1) | 205 | 57.6% | 2.4× | 0 | 9 | 0.0 | 59 |
| Find all usages of IEmailSender | 355 |  | 315 (8 / 8) | 1,311 (5) | 1,626 | 78.2% | 4.6× | 2 | 10 | 0.1 | 364 |
| List implementations of IEmailSender | 85 |  | 80 (2 / 2) | 481 (2) | 561 | 84.8% | 6.6× | 1 | 6 | 0.1 | 77 |
| Outline Extensions.cs (largest file) | 296 |  | — | 1,372 (1) | 1,372 | 78.4% | 4.6× | 1 | 0 | 0.1 | not-ready |
| Read the body of GetContributorByIdRequest.BuildRoute | 86 |  | 265 (5 / 5) | 95 (1) | 360 | 76.1% | 4.2× | 0 | 6 | 0.0 | 89 |
| Find callers of GetContributorByIdRequest.BuildRoute | 128 |  | 265 (5 / 5) | 248 (1) | 513 | 75.0% | 4.0× | 2 | 6 | 0.0 | 111 |
| Type hierarchy of AppDbContext | 95 |  | 0 (0 / 0) | 163 (1) | 163 | 41.7% | 1.7× | 0 | 6 | 0.0 | 68 |
| Project dependency graph | 572 |  | 732 (14 / 14) | 2,839 (9) | 3,571 | 84.0% | 6.2× | 3 | 2 | 0.3 | 975 |
| DI registrations | 679 |  | 525 (13 / 13) | 1,468 (3) | 1,993 | 65.9% | 2.9× | 4 | 7 | 0.1 | 682 |
| HTTP endpoint map | 20 |  | 0 (0 / 0) | 0 (0) | 0 | — | — | 4 | 8 | -0.0 | 12 |
| Resolve a stack trace to source | 367 |  | 340 (7 / 7) | 343 (2) | 683 | 46.3% | 1.9× | 1 | 6 | 0.0 | n/a |
| Diagnostics for AppDbContext.cs | 30 |  | — | 163 (1) | 163 | 81.6% | 5.4× | 0 | 0 | 0.0 | 22 |

### small: eshop

| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved | Nav tok (published-0.8.0) |
|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Locate the definition of ApplicationUser | 76 |  | 27 (1 / 1) | 313 (1) | 340 | 77.6% | 4.5× | 1 | 29 | 0.0 | 52 |
| Find all usages of IBasketService | 394 |  | 259 (8 / 8) | 2,531 (4) | 2,790 | 85.9% | 7.1× | 2 | 28 | 0.2 | 4,678 |
| List implementations of IBasketService | 76 |  | 99 (3 / 3) | 1,738 (2) | 1,837 | 95.9% | 24.2× | 1 | 30 | 0.2 | 67 |
| Outline CatalogApi.cs (largest file) | 1,399 |  | — | 6,088 (1) | 6,088 | 77.0% | 4.4× | 2 | 0 | 0.5 | not-ready |
| Read the body of OrderingContext.BeginTransactionAsync | 129 |  | 164 (4 / 4) | 1,243 (1) | 1,407 | 90.8% | 10.9× | 1 | 27 | 0.1 | 132 |
| Find callers of OrderingContext.BeginTransactionAsync | 64 |  | 164 (4 / 4) | 766 (1) | 930 | 93.1% | 14.5× | 1 | 33 | 0.1 | 51 |
| Type hierarchy of ViewModelBase | 121 |  | 320 (11 / 11) | 413 (1) | 733 | 83.5% | 6.1× | 1 | 32 | 0.1 | 89 |
| Project dependency graph | 1,279 |  | 1,793 (46 / 46) | 10,249 (28) | 12,042 | 89.4% | 9.4× | 5 | 4 | 1.1 | 3,066 |
| DI registrations | 3,638 |  | 2,259 (69 / 69) | 13,480 (18) | 15,739 | 76.9% | 4.3× | 28 | 34 | 1.2 | 3,641 |
| HTTP endpoint map | 1,889 |  | 1,446 (51 / 51) | 23,476 (11) | 24,922 | 92.4% | 13.2× | 36 | 24 | 2.3 | 1,881 |
| Resolve a stack trace to source | 288 |  | 2,549 (50 / 50) | 2,009 (2) | 4,558 | 93.7% | 15.8× | 1 | 30 | 0.4 | n/a |
| Diagnostics for ApplicationUser.cs | 105 |  | — | 313 (1) | 313 | 66.5% | 3.0× | 1 | 0 | 0.0 | 22 |

### medium: jellyfin

| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved | Nav tok (published-0.8.0) |
|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Locate the definition of BaseItem | 77 |  | 44 (1 / 1) | 30,585 (1) | 30,629 | 99.7% | 397.8× | 1 | 103 | 3.1 | 53 |
| Find all usages of IApplicationPaths | 5,132 | yes | 6,064 (146 / 146) | 180,578 (55) | 186,642 | 97.3% | 36.4× | 9 | 100 | 18.2 | 5,370 |
| List implementations of IApplicationPaths | 84 |  | 1,353 (22 / 22) | 2,898 (2) | 4,251 | 98.0% | 50.6× | 2 | 93 | 0.4 | 75 |
| Outline EncodingHelper.cs (largest file) | 7,712 |  | — | 102,369 (1) | 102,369 | 92.5% | 13.3× | 3 | 0 | 9.5 | not-ready |
| Read the body of BaseItem.GetParent | 115 |  | 1,161 (35 / 35) | 30,585 (1) | 31,746 | 99.6% | 276.1× | 1 | 89 | 3.2 | 115 |
| Find callers of BaseItem.GetParent | 1,158 |  | 1,161 (35 / 35) | 143,589 (12) | 144,750 | 99.2% | 125.0× | 7 | 98 | 14.4 | 1,038 |
| Type hierarchy of BaseItem | 191 |  | 2,841 (93 / 93) | 30,585 (1) | 33,426 | 99.4% | 175.0× | 3 | 97 | 3.3 | 170 |
| Project dependency graph | 2,463 |  | 4,080 (92 / 92) | 18,051 (41) | 22,131 | 88.9% | 9.0× | 9 | 16 | 2.0 | 6,027 |
| DI registrations | 5,997 | yes | 7,319 (180 / 180) | 13,499 (2) | 20,818 | 71.2% | 3.5× | 271 | 96 | 1.5 | 6,063 |
| HTTP endpoint map | 68 |  | 6,388 (436 / 250) | 404 (1) | 6,792 | 99.0% | 99.9× | 345 | 100 | 0.7 | 60 |
| Resolve a stack trace to source | 460 |  | 1,931 (53 / 53) | 54,015 (4) | 55,946 | 99.2% | 121.6× | 4 | 100 | 5.6 | n/a |
| Diagnostics for BaseItem.cs | 30 |  | — | 30,585 (1) | 30,585 | 99.9% | 1019.5× | 2 | 0 | 3.1 | 22 |

### large: orchardcore

| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved | Nav tok (published-0.8.0) |
|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Locate the definition of StartupBase | 77 |  | 36 (1 / 1) | 245 (1) | 281 | 72.6% | 3.6× | 2 | 384 | 0.0 | 55 |
| Find all usages of IShape | 6,151 | yes | 11,291 (534 / 250) | 50,343 (54) | 61,634 | 90.0% | 10.0× | 8 | 341 | 5.6 | 6,377 |
| List implementations of IShape | 2,263 |  | 1,101 (21 / 21) | 15,402 (54) | 16,503 | 86.3% | 7.3× | 12 | 361 | 1.4 | 2,254 |
| Outline ResourceManagementOptionsConfiguration.cs (largest file) | 368 |  | — | 16,286 (1) | 16,286 | 97.7% | 44.3× | 4 | 0 | 1.6 | not-ready |
| Read the body of WorkflowExecutionContext.Dispose | 80 |  | 8,131 (322 / 250) | 4,124 (1) | 12,255 | 99.3% | 153.2× | 1 | 326 | 1.2 | 80 |
| Find callers of WorkflowExecutionContext.Dispose | 6,095 | yes | 8,131 (322 / 250) | 117,179 (30) | 125,310 | 95.1% | 20.6× | 78 | 383 | 11.9 | 4,011 |
| Type hierarchy of StartupBase | 624 | yes | 8,133 (398 / 250) | 245 (1) | 8,378 | 92.6% | 13.4× | 10 | 333 | 0.8 | 613 |
| Project dependency graph | 12,817 | yes | 14,656 (1,392 / 250) | 48,121 (100) | 62,777 | 79.6% | 4.9× | 61 | 18 | 5.0 | 70,873 |
| DI registrations | 9,415 | yes | 10,672 (1,661 / 250) | 36,787 (22) | 47,459 | 80.2% | 5.0× | 186 | 364 | 3.8 | 9,469 |
| HTTP endpoint map | 4,956 | yes | 7,841 (404 / 250) | 84,737 (41) | 92,578 | 94.6% | 18.7× | 129 | 356 | 8.8 | 4,947 |
| Resolve a stack trace to source | 462 |  | 8,614 (367 / 250) | 22,295 (3) | 30,909 | 98.5% | 66.9× | 5 | 369 | 3.0 | n/a |
| Diagnostics for StartupBase.cs | 109 |  | — | 245 (1) | 245 | 55.5% | 2.2× | 7 | 0 | 0.0 | 22 |

### xl: abp

| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved | Nav tok (published-0.8.0) |
|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Locate the definition of AbpException | 65 |  | 24 (1 / 1) | 133 (1) | 157 | 58.6% | 2.4× | 2 | 268 | 0.0 | 187 |
| Find all usages of ITransientDependency | 6,154 | yes | 12,387 (526 / 250) | 88,257 (100) | 100,644 | 93.9% | 16.4× | 13 | 242 | 9.5 | 6,044 |
| List implementations of ITransientDependency | 555 |  | 12,349 (512 / 250) | 3,393 (11) | 15,742 | 96.5% | 28.4× | 3 | 237 | 1.5 | 3,774 |
| Outline DistributedCache.cs (largest file) | 3,599 |  | — | 15,161 (1) | 15,161 | 76.3% | 4.2× | 4 | 0 | 1.2 | not-ready |
| Read the body of AbpModule.IsAbpModule | 136 |  | 175 (5 / 5) | 1,568 (1) | 1,743 | 92.2% | 12.8× | 1 | 267 | 0.2 | 145 |
| Find callers of AbpModule.IsAbpModule | 257 |  | 175 (5 / 5) | 12,625 (5) | 12,800 | 98.0% | 49.8× | 88 | 259 | 1.3 | 1,090 |
| Type hierarchy of AbpModule | 117 |  | 10,282 (308 / 250) | 1,568 (1) | 11,850 | 99.0% | 101.3× | 2 | 238 | 1.2 | 815 |
| Project dependency graph | 6,035 | yes | 12,619 (678 / 250) | 29,207 (100) | 41,826 | 85.6% | 6.9× | 59 | 20 | 3.6 | 173,037 |
| DI registrations | 7,267 | yes | 10,476 (217 / 217) | 48,461 (37) | 58,937 | 87.7% | 8.1× | 115 | 244 | 5.2 | 7,390 |
| HTTP endpoint map | 440 |  | 9,058 (269 / 250) | 2,414 (3) | 11,472 | 96.2% | 26.1× | 92 | 243 | 1.1 | 431 |
| Resolve a stack trace to source | 410 |  | 462 (13 / 13) | 11,812 (3) | 12,274 | 96.7% | 29.9× | 5 | 236 | 1.2 | n/a |
| Diagnostics for AbpException.cs | 126 |  | — | 133 (1) | 133 | 5.3% | 1.1× | 5 | 0 | 0.0 | 22 |

## Per-tool latency — before vs after

Client-side round trip over stdio, in ms. Cold = first call to that tool in the session, after warm-up was awaited. Warm = median / p95 of five repeats (p95 of five is the slowest). n/a = tool not in that build.

### small: clean-architecture

| Tool | Cold (published-0.8.0) | Cold (current) | Warm p50 (published-0.8.0) | Warm p50 (current) | Warm p95 (current) | Tokens (published-0.8.0) | Tokens (current) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `find_symbol` | 1 | 1 | 0 | 0 | 1 | 59 | 87 |
| `find_references` | 155 | 168 | 1 | 2 | 3 | 364 | 355 |
| `find_implementations` | 147 | 142 | 1 | 1 | 2 | 77 | 85 |
| `find_callers` | 108 | 119 | 1 | 2 | 2 | 111 | 128 |
| `get_type_hierarchy` | 3 | 5 | 0 | 0 | 1 | 68 | 95 |
| `get_public_api` | 9 | 11 | 0 | 0 | 1 | 112 | 137 |
| `get_symbol_detail` | 5 | 6 | 0 | 0 | 1 | 117 | 107 |
| `get_symbol_source` | 3 | 4 | 0 | 0 | 1 | 89 | 86 |
| `get_file_outline` | 1 (not-ready) | 9 | 0 | 1 | 32 | 37 | 296 |
| `get_project_graph` | 2 | 3 | 1 | 3 | 10 | 975 | 572 |
| `get_dependency_graph` | 2 | 18 | 0 | 1 | 1 | 30 | 37 |
| `detect_circular_dependencies` | 3 | 4 | 0 | 0 | 0 | 7 | 7 |
| `get_diagnostics` | 6 | 93 | 0 | 0 | 1 | 22 | 30 |
| `resolve_stack_trace` | n/a | 13 | n/a | 1 | 1 | n/a | 367 |
| `get_nuget_packages` | 6 | 9 | 3 | 4 | 5 | 1,255 | 1,263 |
| `analyze_change_impact` | n/a | 22 | n/a | 2 | 3 | n/a | 253 |
| `get_di_registrations` | 19 | 40 | 11 | 4 | 4 | 682 | 679 |
| `get_endpoint_map` | 26 | 10 | 21 | 4 | 5 | 12 | 20 |
| `get_test_coverage_map` | 894 | 16 | 430 | 4 | 4 | 2,482 | 1,923 |
| `detect_antipatterns` | 305 | 123 | 266 | 12 | 14 | 9,855 | 524 |
| `find_dead_code` | 491 | 80 | 248 | 7 | 28 | 503 | 35 |

### small: eshop

| Tool | Cold (published-0.8.0) | Cold (current) | Warm p50 (published-0.8.0) | Warm p50 (current) | Warm p95 (current) | Tokens (published-0.8.0) | Tokens (current) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `find_symbol` | 1 | 1 | 1 | 1 | 1 | 52 | 76 |
| `find_references` | 302 | 234 | 6 | 2 | 3 | 4,678 | 394 |
| `find_implementations` | 128 | 112 | 1 | 1 | 2 | 67 | 76 |
| `find_callers` | 155 | 118 | 3 | 1 | 3 | 51 | 64 |
| `find_overrides` | 8 | 8 | 2 | 1 | 1 | 417 | 426 |
| `get_type_hierarchy` | 7 | 8 | 2 | 1 | 1 | 89 | 121 |
| `get_public_api` | 5 | 5 | 1 | 0 | 1 | 327 | 353 |
| `get_symbol_detail` | 5 | 4 | 1 | 1 | 1 | 117 | 111 |
| `get_symbol_source` | 3 | 4 | 1 | 1 | 19 | 132 | 129 |
| `get_file_outline` | 3 (not-ready) | 10 | 0 | 2 | 3 | 32 | 1,399 |
| `get_project_graph` | 5 | 5 | 4 | 5 | 6 | 3,066 | 1,279 |
| `get_dependency_graph` | 3 | 7 | 1 | 1 | 2 | 30 | 39 |
| `detect_circular_dependencies` | 3 | 4 | 0 | 0 | 1 | 7 | 81 |
| `get_diagnostics` | 6 | 59 | 0 | 1 | 2 | 22 | 105 |
| `resolve_stack_trace` | n/a | 14 | n/a | 1 | 2 | n/a | 288 |
| `get_nuget_packages` | 14 | 13 | 10 | 9 | 39 | 2,338 | 2,345 |
| `analyze_change_impact` | n/a | 41 | n/a | 4 | 4 | n/a | 205 |
| `get_di_registrations` | 115 | 51 | 97 | 28 | 28 | 3,641 | 3,638 |
| `get_endpoint_map` | 201 | 42 | 194 | 36 | 49 | 1,881 | 1,889 |
| `get_test_coverage_map` | 4,600 | 50 | 831 | 16 | 35 | 2,181 | 1,740 |
| `detect_antipatterns` | 616 | 226 | 527 | 132 | 179 | 8,814 | 7,180 |
| `find_dead_code` | 1,261 | 155 | 739 | 51 | 57 | 1,425 | 827 |

### medium: jellyfin

| Tool | Cold (published-0.8.0) | Cold (current) | Warm p50 (published-0.8.0) | Warm p50 (current) | Warm p95 (current) | Tokens (published-0.8.0) | Tokens (current) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `find_symbol` | 2 | 2 | 1 | 1 | 1 | 53 | 77 |
| `find_references` | 733 | 728 | 6 | 9 | 27 | 5,370 | 5,132 |
| `find_implementations` | 964 | 1,036 | 2 | 2 | 10 | 75 | 84 |
| `find_callers` | 114 | 140 | 7 | 7 | 8 | 1,038 | 1,158 |
| `find_overrides` | 16 | 18 | 4 | 3 | 23 | 732 | 741 |
| `get_type_hierarchy` | 5 | 8 | 3 | 3 | 3 | 170 | 191 |
| `get_public_api` | 16 | 14 | 2 | 2 | 3 | 2,642 | 2,660 |
| `get_symbol_detail` | 5 | 7 | 1 | 1 | 1 | 94 | 90 |
| `get_symbol_source` | 3 | 4 | 1 | 1 | 1 | 115 | 115 |
| `get_file_outline` | 2 (not-ready) | 13 | 1 | 3 | 4 | 38 | 7,712 |
| `get_project_graph` | 6 | 10 | 5 | 9 | 31 | 6,027 | 2,463 |
| `get_dependency_graph` | 5 | 11 | 1 | 3 | 11 | 25 | 104 |
| `detect_circular_dependencies` | 4 | 4 | 0 | 0 | 1 | 7 | 7 |
| `get_diagnostics` | 7 | 885 | 1 | 2 | 22 | 22 | 30 |
| `resolve_stack_trace` | n/a | 17 | n/a | 4 | 4 | n/a | 460 |
| `get_nuget_packages` | 18 | 21 | 15 | 15 | 18 | 2,599 | 2,607 |
| `analyze_change_impact` | n/a | 275 | n/a | 67 | 119 | n/a | 1,524 |
| `get_di_registrations` | 1,356 | 316 | 241 | 271 | 314 | 6,063 | 5,997 |
| `get_endpoint_map` | 405 | 311 | 364 | 345 | 356 | 60 | 68 |
| `get_test_coverage_map` | 16,011 | 161 | 5,124 | 75 | 76 | 1,733 | 1,729 |
| `detect_antipatterns` | 2,833 | 2,393 | 2,612 | 938 | 1,853 | 9,422 | 6,559 |
| `find_dead_code` | 13,697 | 1,035 | 9,404 | 226 | 281 | 464 | 281 |

### large: orchardcore

| Tool | Cold (published-0.8.0) | Cold (current) | Warm p50 (published-0.8.0) | Warm p50 (current) | Warm p95 (current) | Tokens (published-0.8.0) | Tokens (current) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `find_symbol` | 5,945 | 4 | 8 | 2 | 2 | 55 | 77 |
| `find_references` | 1,025 | 1,647 | 12 | 8 | 29 | 6,377 | 6,151 |
| `find_implementations` | 10,132 | 2,278 | 9 | 12 | 14 | 2,254 | 2,263 |
| `find_callers` | 1,244 | 1,838 | 70 | 78 | 107 | 4,011 | 6,095 |
| `find_overrides` | 208 | 297 | 23 | 34 | 72 | 4,340 | 4,242 |
| `get_type_hierarchy` | 11 | 18 | 9 | 10 | 11 | 613 | 624 |
| `get_public_api` | 10 | 6 | 6 | 1 | 2 | 200 | 232 |
| `get_symbol_detail` | 15 | 5 | 12 | 2 | 2 | 96 | 92 |
| `get_symbol_source` | 15 | 4 | 12 | 1 | 1 | 80 | 80 |
| `get_file_outline` | 4 (not-ready) | 13 | 1 | 4 | 13 | 47 | 368 |
| `get_project_graph` | 38 | 77 | 29 | 61 | 66 | 70,873 | 12,817 |
| `get_dependency_graph` | 11 | 10 | 7 | 4 | 18 | 29 | 31 |
| `detect_circular_dependencies` | 6 | 8 | 2 | 4 | 12 | 7 | 85 |
| `get_diagnostics` | 7 | 85 | 1 | 7 | 12 | 22 | 109 |
| `resolve_stack_trace` | n/a | 20 | n/a | 5 | 6 | n/a | 462 |
| `get_nuget_packages` | 108 | 104 | 95 | 94 | 105 | 8,146 | 8,155 |
| `analyze_change_impact` | n/a | 1,011 | n/a | 752 | 914 | n/a | 4,058 |
| `get_di_registrations` | 401 | 244 | 412 | 186 | 227 | 9,469 | 9,415 |
| `get_endpoint_map` | 850 | 157 | 617 | 129 | 148 | 4,947 | 4,956 |
| `get_test_coverage_map` | 11,427 | 184 | 4,669 | 89 | 125 | 2,581 | 2,162 |
| `detect_antipatterns` | 50,005 | 299 | 2,695 | 202 | 256 | 10,312 | 7,422 |
| `find_dead_code` | 25,116 | 908 | 72,582 | 290 | 355 | 1,633 | 497 |

### xl: abp

| Tool | Cold (published-0.8.0) | Cold (current) | Warm p50 (published-0.8.0) | Warm p50 (current) | Warm p95 (current) | Tokens (published-0.8.0) | Tokens (current) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `find_symbol` | 12 | 2 | 8 | 2 | 2 | 187 | 65 |
| `find_references` | 1,696 | 894 | 45 | 13 | 27 | 6,044 | 6,154 |
| `find_implementations` | 571 | 172 | 34 | 3 | 24 | 3,774 | 555 |
| `find_callers` | 3,276 | 1,724 | 77 | 88 | 114 | 1,090 | 257 |
| `find_overrides` | 31 | 54 | 16 | 26 | 28 | 4,388 | 4,397 |
| `get_type_hierarchy` | 13 | 8 | 9 | 2 | 2 | 815 | 117 |
| `get_public_api` | 11 | 5 | 6 | 1 | 1 | 109 | 135 |
| `get_symbol_detail` | 15 | 6 | 6 | 1 | 1 | 98 | 88 |
| `get_symbol_source` | 8 | 4 | 7 | 1 | 1 | 145 | 136 |
| `get_file_outline` | 5 (not-ready) | 15 | 4 | 4 | 23 | 38 | 3,599 |
| `get_project_graph` | 86 | 63 | 57 | 59 | 60 | 173,037 | 6,035 |
| `get_dependency_graph` | 12 | 12 | 7 | 5 | 14 | 25 | 30 |
| `detect_circular_dependencies` | 8 | 9 | 5 | 5 | 5 | 7 | 103 |
| `get_diagnostics` | 12 | 89 | 3 | 5 | 9 | 22 | 126 |
| `resolve_stack_trace` | n/a | 20 | n/a | 5 | 6 | n/a | 410 |
| `get_nuget_packages` | 128 | 115 | 99 | 106 | 111 | 9,551 | 9,101 |
| `analyze_change_impact` | n/a | 258 | n/a | 59 | 179 | n/a | 544 |
| `get_di_registrations` | 242 | 249 | 236 | 115 | 126 | 7,390 | 7,267 |
| `get_endpoint_map` | 396 | 102 | 359 | 92 | 127 | 431 | 440 |
| `get_test_coverage_map` | 12,613 | 255 | 6,394 | 92 | 129 | 2,013 | 2,473 |
| `detect_antipatterns` | 4,910 | 388 | 4,509 | 243 | 281 | 9,367 | 8,901 |
| `find_dead_code` | 15,463 | 927 | 13,904 | 302 | 354 | 2,649 | 2,903 |

## Slowest tools per corpus (current, cold)

| Tier | Corpus | Slowest five (cold ms) |
|---|---|---|
| small | clean-architecture | `find_references` 168, `find_implementations` 142, `detect_antipatterns` 123, `find_callers` 119, `get_diagnostics` 93 |
| small | eshop | `find_references` 234, `detect_antipatterns` 226, `find_dead_code` 155, `find_callers` 118, `find_implementations` 112 |
| medium | jellyfin | `detect_antipatterns` 2,393, `find_implementations` 1,036, `find_dead_code` 1,035, `get_diagnostics` 885, `find_references` 728 |
| large | orchardcore | `find_implementations` 2,278, `find_callers` 1,838, `find_references` 1,647, `analyze_change_impact` 1,011, `find_dead_code` 908 |
| xl | abp | `find_callers` 1,724, `find_dead_code` 927, `find_references` 894, `detect_antipatterns` 388, `analyze_change_impact` 258 |

## Errors, timeouts, and load problems

- **clean-architecture / published-0.8.0** `get_file_outline` → not-ready: {"State":"NotFound","Message":"File \u0027src/Clean.Architecture.ServiceDefaults/Extensions.cs\u0027 not found in the solution."}
- **clean-architecture** note: Restore needs -p:NuGetAudit=false: the repo treats NuGet vulnerability warnings (NU1902/NU1903 on SSH.NET and System.Security.Cryptography.Pkcs at this commit) as errors. Nothing in the source is changed.
- **eshop / published-0.8.0** `get_file_outline` → not-ready: {"State":"NotFound","Message":"File \u0027src/Catalog.API/Apis/CatalogApi.cs\u0027 not found in the solution."}
- **eshop / published-0.8.0** workspace diagnostics logged by the server (4; non-fatal unless projects loaded < declared): Workspace failure: Msbuild failed when processing the file 'eShop\tests\Basket.FunctionalTests\Basket.FunctionalTests.csproj' with message: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-qhrr-8q5h-9q3h; Workspace failure: Msbuild failed when processing the file 'eShop\tests\Catalog.FunctionalTests\Catalog.FunctionalTests.csproj' with message: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-qhrr-8q5h-9q3h; Workspace failure: Msbuild failed when processing the file 'eShop\tests\Ordering.FunctionalTests\Ordering.FunctionalTests.csproj' with message: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-qhrr-8q5h-9q3h; Workspace failure: Msbuild failed when processing the file 'eShop\tests\Webhooks.FunctionalTests\Webhooks.FunctionalTests.csproj' with message: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-qhrr-8q5h-9q3h
- **eshop / current** workspace diagnostics logged by the server (4; non-fatal unless projects loaded < declared): Workspace failure: Msbuild failed when processing the file 'eShop\tests\Basket.FunctionalTests\Basket.FunctionalTests.csproj' with message: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-qhrr-8q5h-9q3h; Workspace failure: Msbuild failed when processing the file 'eShop\tests\Catalog.FunctionalTests\Catalog.FunctionalTests.csproj' with message: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-qhrr-8q5h-9q3h; Workspace failure: Msbuild failed when processing the file 'eShop\tests\Ordering.FunctionalTests\Ordering.FunctionalTests.csproj' with message: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-qhrr-8q5h-9q3h; Workspace failure: Msbuild failed when processing the file 'eShop\tests\Webhooks.FunctionalTests\Webhooks.FunctionalTests.csproj' with message: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-qhrr-8q5h-9q3h
- **eshop** note: 3 MAUI projects (src/ClientApp, src/HybridApp, tests/ClientApp.UnitTests) need the maui-android/maui-tizen workloads, which are not installed on the benchmark machine. They fail restore (NETSDK1147), yet MSBuildWorkspace still loads them, once per target framework, without their packages, which is why 35 Roslyn projects load from 28 declared. Every other project restores and loads normally.
- **jellyfin / published-0.8.0** `get_file_outline` → not-ready: {"State":"NotFound","Message":"File \u0027MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs\u0027 not found in the solution."}
- **jellyfin** note: The in-solution analyzer project src/Jellyfin.CodeAnalysis (referenced as OutputItemType=Analyzer from Directory.Build.props) is built after restore, as it would be in any developer's working copy. On a restore-only clone it is an UnresolvedAnalyzerReference, and the server's SymbolFinder-based tools throw (server bug; see the Server bugs section of bench/README.md).
- **orchardcore / published-0.8.0** `get_file_outline` → not-ready: {"State":"NotFound","Message":"File \u0027src/OrchardCore.Modules/OrchardCore.Resources/ResourceManagementOptionsConfiguration.cs\u0027 not found in the solution."}
- **orchardcore / published-0.8.0** workspace diagnostics logged by the server (80; non-fatal unless projects loaded < declared): Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore.Cms.Web\OrchardCore.Cms.Web.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8; Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore\OrchardCore.Application.Cms.Targets\OrchardCore.Application.Cms.Targets.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8; Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore\OrchardCore.Application.Cms.Core.Targets\OrchardCore.Application.Cms.Core.Targets.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8; Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore.Modules\OrchardCore.AdminDashboard\OrchardCore.AdminDashboard.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8; Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore\OrchardCore.ContentManagement\OrchardCore.ContentManagement.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8
- **orchardcore / current** workspace diagnostics logged by the server (80; non-fatal unless projects loaded < declared): Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore.Cms.Web\OrchardCore.Cms.Web.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8; Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore\OrchardCore.Application.Cms.Targets\OrchardCore.Application.Cms.Targets.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8; Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore\OrchardCore.Application.Cms.Core.Targets\OrchardCore.Application.Cms.Core.Targets.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8; Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore.Modules\OrchardCore.AdminDashboard\OrchardCore.AdminDashboard.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8; Workspace failure: Msbuild failed when processing the file 'OrchardCore\src\OrchardCore\OrchardCore.ContentManagement\OrchardCore.ContentManagement.csproj' with message: Package 'MySqlConnector' 2.5.0 has a known moderate severity vulnerability, https://github.com/advisories/GHSA-473q-m89c-ghf8
- **orchardcore** note: The three in-solution analyzer/generator projects (OrchardCore.SourceGenerators, OrchardCore.Localization.Analyzers, OrchardCore.Localization.Analyzers.CodeFixes; referenced as OutputItemType=Analyzer from OrchardCore.Commons.props) are built after restore, as in any developer's working copy. On a restore-only clone they are UnresolvedAnalyzerReferences and the server's SymbolFinder-based tools throw (server bug; see the Server bugs section of bench/README.md).
- **abp / published-0.8.0** `get_file_outline` → not-ready: {"State":"NotFound","Message":"File \u0027src/Volo.Abp.Caching/Volo/Abp/Caching/DistributedCache.cs\u0027 not found in the solution."}
- **abp / published-0.8.0** workspace diagnostics logged by the server (13; non-fatal unless projects loaded < declared): Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.Core\Volo.Abp.Core.csproj' with message: PackageReference System.Linq.Queryable will not be pruned. This package is automatically available and does not need to be referenced explicitly. Remove the PackageReference item.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.Cli.Core\Volo.Abp.Cli.Core.csproj' with message: Package 'LibGit2Sharp 0.31.0' was restored using '.NETPortable,Version=v0.0,Profile=Profile259, .NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework '.NETStandard,Version=v2.0'. This package may not be fully compatible with your project.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.EntityFrameworkCore.MySQL.Pomelo\Volo.Abp.EntityFrameworkCore.MySQL.Pomelo.csproj' with message: Detected package version outside of dependency constraint: Pomelo.EntityFrameworkCore.MySql 9.0.0 requires Microsoft.EntityFrameworkCore.Relational (>= 9.0.0 && <= 9.0.999) but version Microsoft.EntityFrameworkCore.Relational 10.0.11 was resolved.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.EntityFrameworkCore.Oracle.Devart\Volo.Abp.EntityFrameworkCore.Oracle.Devart.csproj' with message: Detected package downgrade: Microsoft.EntityFrameworkCore.Relational from 10.0.11 to 9.0.12. Reference the package directly from the project to select a different version.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.EventBus.Rebus\Volo.Abp.EventBus.Rebus.csproj' with message: Detected package version outside of dependency constraint: Rebus 8.8.0 requires system.text.json (>= 8.0.5 && < 10.0.0) but version System.Text.Json 10.0.11 was resolved.
- **abp / current** workspace diagnostics logged by the server (13; non-fatal unless projects loaded < declared): Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.Core\Volo.Abp.Core.csproj' with message: PackageReference System.Linq.Queryable will not be pruned. This package is automatically available and does not need to be referenced explicitly. Remove the PackageReference item.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.EntityFrameworkCore.MySQL.Pomelo\Volo.Abp.EntityFrameworkCore.MySQL.Pomelo.csproj' with message: Detected package version outside of dependency constraint: Pomelo.EntityFrameworkCore.MySql 9.0.0 requires Microsoft.EntityFrameworkCore.Relational (>= 9.0.0 && <= 9.0.999) but version Microsoft.EntityFrameworkCore.Relational 10.0.11 was resolved.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.EntityFrameworkCore.Oracle.Devart\Volo.Abp.EntityFrameworkCore.Oracle.Devart.csproj' with message: Detected package downgrade: Microsoft.EntityFrameworkCore.Relational from 10.0.11 to 9.0.12. Reference the package directly from the project to select a different version.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.Cli.Core\Volo.Abp.Cli.Core.csproj' with message: Package 'LibGit2Sharp 0.31.0' was restored using '.NETPortable,Version=v0.0,Profile=Profile259, .NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework '.NETStandard,Version=v2.0'. This package may not be fully compatible with your project.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.EventBus.Rebus\Volo.Abp.EventBus.Rebus.csproj' with message: Detected package version outside of dependency constraint: Rebus 8.8.0 requires system.text.json (>= 8.0.5 && < 10.0.0) but version System.Text.Json 10.0.11 was resolved.
- **abp** note: Release tag 10.7.1, not the dev branch. ABP's dev head (a840be3) pins SDK 11.0.100-rc.3 and multi-targets net11.0 in 176 of 178 framework/src projects, which .NET SDK 10 cannot build or load. The 10.7.1 release targets net10.0 (plus netstandard2.x/net8/net9) and restores cleanly on SDK 10.0.403, so the xl tier keeps the same codebase.
- **abp** note: Clone with core.longpaths=true on Windows: several test paths exceed 260 characters.

