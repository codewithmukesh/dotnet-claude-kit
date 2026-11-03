# CWM.RoslynNavigator benchmark — 2026-11-04

Builds: **published-0.8.0** (before) vs **current** (after). Machine: Microsoft Windows 10.0.26300, 24 logical CPUs, X64, harness on .NET 10.0.14.

Every number below is **measured** unless its column says **modelled**. Tokens are estimated as bytes / 3.5 (divisor **not calibrated** — the server's own TokenEstimator). Modelled time assumes **10,000 input tokens/s**; see bench/README.md.

## Headline — 12 agent tasks per corpus (current)

Navigator = the tool response. Grep + Read = the search an agent runs plus every file the answer lives in, each file counted once. Only tasks the navigator answered are summed. Measured times are tool round trip vs search + file I/O only — no model time on either side.

| Tier | Corpus | Tasks | Navigator tokens | Grep + Read tokens | Tokens saved | × fewer | Measured: navigator | Measured: grep + read I/O | Agent tool calls (nav / manual) | **Modelled** agent time saved |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| small | clean-architecture | 12/12 | 3,327 | 11,210 | 70.3% | 3.4× | 20 ms | 74 ms | 12 / 37 | 0.8 s |
| small | eshop | 12/12 | 16,262 | 71,699 | 77.3% | 4.4× | 93 ms | 285 ms | 12 / 81 | 5.5 s |
| medium | jellyfin | 12/12 | 28,434 | 646,145 | 95.6% | 22.7× | 846 ms | 884 ms | 12 / 136 | 61.8 s |
| large | orchardcore | 12/12 | 100,958 | 492,283 | 79.5% | 4.9× | 462 ms | 3,244 ms | 12 / 460 | 39.1 s |
| xl | abp | 12/12 | 198,667 | 379,987 | 47.7% | 1.9× | 711 ms | 2,186 ms | 12 / 535 | 18.1 s |

## Corpora

| Tier | Corpus | Commit | Solution | Projects declared | Projects loaded (published-0.8.0 / current) | .cs files | C# source |
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
| small | clean-architecture | published-0.8.0 | 7.9 s | 8.0 s | 4.9 s | 387 MB | 495 MB (3) | 0 |
| small | clean-architecture | current | 3.2 s | 4.6 s | 4.6 s | 335 MB | 335 MB (1) | 0 |
| small | eshop | published-0.8.0 | 12.7 s | 12.7 s | 12.4 s | 526 MB | 526 MB (2) | 0 |
| small | eshop | current | 9.4 s | 11.1 s | 11.1 s | 511 MB | 511 MB (1) | 0 |
| medium | jellyfin | published-0.8.0 | 16.6 s | 16.6 s | 16.3 s | 1,164 MB | 1,162 MB (2) | 0 |
| medium | jellyfin | current | 8.2 s | 11.9 s | 11.9 s | 902 MB | 902 MB (1) | 0 |
| large | orchardcore | published-0.8.0 | 105.0 s | 126.3 s | n/a | 2,264 MB | 3,253 MB (24) | 0 |
| large | orchardcore | current | 52.4 s | 59.9 s | 59.9 s | 1,478 MB | 1,467 MB (1) | 0 |
| xl | abp | published-0.8.0 | 77.4 s | 95.1 s | n/a | 1,677 MB | 2,133 MB (1) | 0 |
| xl | abp | current | 63.5 s | 67.9 s | 67.8 s | 1,613 MB | 2,118 MB (1) | 0 |

## Tokens per task (current)

Grep shows matching lines / lines an agent receives (head limit). Read is the file-read floor: the files the answer lives in, each once. "Trunc." marks a navigator answer that hit its result cap — its file list, and so the manual baseline, is then partial (conservative).

### small: clean-architecture

| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved | Nav tok (published-0.8.0) |
|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Locate the definition of AppDbContext | 73 |  | 42 (1 / 1) | 163 (1) | 205 | 64.4% | 2.8× | 0 | 12 | 0.0 | 59 |
| Find all usages of IEmailSender | 407 |  | 315 (8 / 8) | 1,311 (5) | 1,626 | 75.0% | 4.0× | 1 | 7 | 0.1 | 364 |
| List implementations of IEmailSender | 97 |  | 80 (2 / 2) | 481 (2) | 561 | 82.7% | 5.8× | 1 | 8 | 0.1 | 77 |
| Outline Extensions.cs (largest file) | 307 |  | — | 1,372 (1) | 1,372 | 77.6% | 4.5× | 1 | 0 | 0.1 | not-ready |
| Read the body of GetContributorByIdRequest.BuildRoute | 89 |  | 265 (5 / 5) | 95 (1) | 360 | 75.3% | 4.0× | 1 | 8 | 0.0 | 89 |
| Find callers of GetContributorByIdRequest.BuildRoute | 131 |  | 265 (5 / 5) | 248 (1) | 513 | 74.5% | 3.9× | 2 | 6 | 0.0 | 111 |
| Type hierarchy of AppDbContext | 98 |  | 0 (0 / 0) | 163 (1) | 163 | 39.9% | 1.7× | 0 | 7 | 0.0 | 68 |
| Project dependency graph | 975 |  | 732 (14 / 14) | 2,839 (9) | 3,571 | 72.7% | 3.7× | 2 | 2 | 0.3 | 975 |
| DI registrations | 694 |  | 525 (13 / 13) | 1,468 (3) | 1,993 | 65.2% | 2.9× | 4 | 8 | 0.1 | 682 |
| HTTP endpoint map | 20 |  | 0 (0 / 0) | 0 (0) | 0 | — | — | 5 | 9 | -0.0 | 12 |
| Resolve a stack trace to source | 406 |  | 340 (7 / 7) | 343 (2) | 683 | 40.6% | 1.7× | 1 | 7 | 0.0 | n/a |
| Diagnostics for AppDbContext.cs | 30 |  | — | 163 (1) | 163 | 81.6% | 5.4× | 2 | 0 | 0.0 | 22 |

### small: eshop

| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved | Nav tok (published-0.8.0) |
|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Locate the definition of ApplicationUser | 66 |  | 27 (1 / 1) | 313 (1) | 340 | 80.6% | 5.2× | 1 | 32 | 0.0 | 52 |
| Find all usages of IBasketService | 5,259 | yes | 259 (8 / 8) | 2,531 (4) | 2,790 | -88.5% | 0.5× | 6 | 30 | -0.3 | 4,673 |
| List implementations of IBasketService | 87 |  | 99 (3 / 3) | 1,738 (2) | 1,837 | 95.3% | 21.1× | 2 | 29 | 0.2 | 67 |
| Outline CatalogApi.cs (largest file) | 1,567 |  | — | 6,088 (1) | 6,088 | 74.3% | 3.9× | 2 | 0 | 0.5 | not-ready |
| Read the body of OrderingContext.BeginTransactionAsync | 132 |  | 164 (4 / 4) | 1,243 (1) | 1,407 | 90.6% | 10.7× | 1 | 29 | 0.1 | 132 |
| Find callers of OrderingContext.BeginTransactionAsync | 65 |  | 164 (4 / 4) | 766 (1) | 930 | 93.0% | 14.3× | 2 | 29 | 0.1 | 51 |
| Type hierarchy of ViewModelBase | 116 |  | 320 (11 / 11) | 413 (1) | 733 | 84.2% | 6.3× | 1 | 34 | 0.1 | 89 |
| Project dependency graph | 3,066 |  | 1,793 (46 / 46) | 10,249 (28) | 12,042 | 74.5% | 3.9× | 5 | 4 | 0.9 | 3,066 |
| DI registrations | 3,653 |  | 2,259 (69 / 69) | 13,480 (18) | 15,739 | 76.8% | 4.3× | 30 | 30 | 1.2 | 3,641 |
| HTTP endpoint map | 1,889 |  | 1,446 (51 / 51) | 23,476 (11) | 24,922 | 92.4% | 13.2× | 38 | 35 | 2.3 | 1,881 |
| Resolve a stack trace to source | 332 |  | 2,549 (50 / 50) | 2,009 (2) | 4,558 | 92.7% | 13.7× | 3 | 33 | 0.4 | n/a |
| Diagnostics for ApplicationUser.cs | 30 |  | — | 313 (1) | 313 | 90.4% | 10.4× | 4 | 0 | 0.0 | 22 |

### medium: jellyfin

| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved | Nav tok (published-0.8.0) |
|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Locate the definition of BaseItem | 67 |  | 44 (1 / 1) | 30,585 (1) | 30,629 | 99.8% | 457.1× | 2 | 102 | 3.1 | 53 |
| Find all usages of IApplicationPaths | 6,187 | yes | 6,064 (146 / 146) | 156,638 (59) | 162,702 | 96.2% | 26.3× | 6 | 101 | 15.7 | 5,363 |
| List implementations of IApplicationPaths | 95 |  | 1,353 (22 / 22) | 2,898 (2) | 4,251 | 97.8% | 44.7× | 2 | 107 | 0.4 | 75 |
| Outline EncodingHelper.cs (largest file) | 7,878 |  | — | 102,369 (1) | 102,369 | 92.3% | 13.0× | 4 | 0 | 9.5 | not-ready |
| Read the body of BaseItem.GetParent | 115 |  | 1,161 (35 / 35) | 30,585 (1) | 31,746 | 99.6% | 276.1× | 1 | 99 | 3.2 | 115 |
| Find callers of BaseItem.GetParent | 1,195 |  | 1,161 (35 / 35) | 143,589 (12) | 144,750 | 99.2% | 121.1× | 8 | 92 | 14.4 | 1,038 |
| Type hierarchy of BaseItem | 197 |  | 2,841 (93 / 93) | 30,585 (1) | 33,426 | 99.4% | 169.7× | 3 | 92 | 3.3 | 170 |
| Project dependency graph | 6,027 |  | 4,080 (92 / 92) | 18,051 (41) | 22,131 | 72.8% | 3.7× | 6 | 4 | 1.6 | 6,027 |
| DI registrations | 6,074 | yes | 7,319 (180 / 180) | 13,499 (2) | 20,818 | 70.8% | 3.4× | 312 | 92 | 1.5 | 6,063 |
| HTTP endpoint map | 68 |  | 6,388 (436 / 250) | 404 (1) | 6,792 | 99.0% | 99.9× | 389 | 99 | 0.7 | 60 |
| Resolve a stack trace to source | 501 |  | 1,931 (53 / 53) | 54,015 (4) | 55,946 | 99.1% | 111.7× | 4 | 96 | 5.5 | n/a |
| Diagnostics for BaseItem.cs | 30 |  | — | 30,585 (1) | 30,585 | 99.9% | 1019.5× | 112 | 0 | 3.1 | 22 |

### large: orchardcore

| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved | Nav tok (published-0.8.0) |
|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Locate the definition of StartupBase | 69 |  | 36 (1 / 1) | 245 (1) | 281 | 75.4% | 4.1× | 3 | 312 | 0.0 | 55 |
| Find all usages of IShape | 6,965 | yes | 11,291 (534 / 250) | 51,005 (56) | 62,296 | 88.8% | 8.9× | 12 | 375 | 5.5 | 6,251 |
| List implementations of IShape | 2,589 |  | 1,101 (21 / 21) | 15,402 (54) | 16,503 | 84.3% | 6.4× | 13 | 420 | 1.4 | 2,254 |
| Outline ResourceManagementOptionsConfiguration.cs (largest file) | 371 |  | — | 16,286 (1) | 16,286 | 97.7% | 43.9× | 4 | 0 | 1.6 | not-ready |
| Read the body of WorkflowExecutionContext.Dispose | 80 |  | 8,131 (322 / 250) | 4,124 (1) | 12,255 | 99.3% | 153.2× | 2 | 337 | 1.2 | 80 |
| Find callers of WorkflowExecutionContext.Dispose | 4,431 |  | 8,131 (322 / 250) | 78,073 (12) | 86,204 | 94.9% | 19.5× | 63 | 340 | 8.2 | 4,011 |
| Type hierarchy of StartupBase | 607 | yes | 8,133 (398 / 250) | 245 (1) | 8,378 | 92.8% | 13.8× | 9 | 333 | 0.8 | 551 |
| Project dependency graph | 70,873 |  | 14,656 (1,392 / 250) | 104,233 (257) | 118,889 | 40.4% | 1.7× | 36 | 30 | 4.8 | 70,873 |
| DI registrations | 9,481 | yes | 10,672 (1,661 / 250) | 36,787 (22) | 47,459 | 80.0% | 5.0× | 160 | 393 | 3.8 | 9,469 |
| HTTP endpoint map | 4,956 | yes | 7,841 (404 / 250) | 84,737 (41) | 92,578 | 94.6% | 18.7× | 124 | 358 | 8.8 | 4,947 |
| Resolve a stack trace to source | 506 |  | 8,614 (367 / 250) | 22,295 (3) | 30,909 | 98.4% | 61.1× | 12 | 346 | 3.0 | n/a |
| Diagnostics for StartupBase.cs | 30 |  | — | 245 (1) | 245 | 87.8% | 8.2× | 24 | 0 | 0.0 | 22 |

### xl: abp

| Task | Navigator tok | Trunc. | Grep tok (matches / shown) | Read tok (files) | Grep + Read tok | Saved | × | Measured: nav ms | Measured: grep + read ms | **Modelled** s saved | Nav tok (published-0.8.0) |
|---|---:|:-:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Locate the definition of AbpException | 223 |  | 24 (1 / 1) | 133 (1) | 157 | -42.0% | 0.7× | 6 | 208 | -0.0 | 187 |
| Find all usages of ITransientDependency | 6,695 | yes | 12,387 (526 / 250) | 73,001 (99) | 85,388 | 92.2% | 12.8× | 53 | 256 | 7.9 | 5,911 |
| List implementations of ITransientDependency | 4,303 | yes | 12,349 (512 / 250) | 79,076 (95) | 91,425 | 95.3% | 21.2× | 46 | 249 | 8.7 | 3,785 |
| Outline DistributedCache.cs (largest file) | 3,850 |  | — | 15,161 (1) | 15,161 | 74.6% | 3.9× | 6 | 0 | 1.1 | not-ready |
| Read the body of AbpModule.IsAbpModule | 145 |  | 175 (5 / 5) | 1,568 (1) | 1,743 | 91.7% | 12.0× | 4 | 282 | 0.2 | 145 |
| Find callers of AbpModule.IsAbpModule | 1,241 |  | 175 (5 / 5) | 12,625 (5) | 12,800 | 90.3% | 10.3× | 230 | 248 | 1.2 | 1,090 |
| Type hierarchy of AbpModule | 844 | yes | 10,282 (308 / 250) | 1,568 (1) | 11,850 | 92.9% | 14.0× | 18 | 246 | 1.1 | 811 |
| Project dependency graph | 173,037 |  | 12,619 (678 / 250) | 76,770 (270) | 89,389 | -93.6% | 0.5× | 58 | 30 | -8.4 | 173,037 |
| DI registrations | 7,413 | yes | 10,476 (217 / 217) | 37,719 (45) | 48,195 | 84.6% | 6.5× | 107 | 246 | 4.1 | 7,390 |
| HTTP endpoint map | 440 |  | 9,058 (269 / 250) | 2,414 (3) | 11,472 | 96.2% | 26.1× | 90 | 210 | 1.1 | 431 |
| Resolve a stack trace to source | 446 |  | 462 (13 / 13) | 11,812 (3) | 12,274 | 96.4% | 27.5× | 28 | 211 | 1.2 | n/a |
| Diagnostics for AbpException.cs | 30 |  | — | 133 (1) | 133 | 77.4% | 4.4× | 66 | 0 | 0.0 | 22 |

## Per-tool latency — before vs after

Client-side round trip over stdio, in ms. Cold = first call to that tool in the session, after warm-up was awaited. Warm = median / p95 of five repeats (p95 of five is the slowest). n/a = tool not in that build.

### small: clean-architecture

| Tool | Cold (published-0.8.0) | Cold (current) | Warm p50 (published-0.8.0) | Warm p50 (current) | Warm p95 (current) | Tokens (published-0.8.0) | Tokens (current) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `find_symbol` | 1 | 1 | 0 | 0 | 0 | 59 | 73 |
| `find_references` | 204 | 151 | 1 | 1 | 3 | 364 | 407 |
| `find_implementations` | 157 | 130 | 1 | 1 | 3 | 77 | 97 |
| `find_callers` | 110 | 101 | 2 | 2 | 2 | 111 | 131 |
| `get_type_hierarchy` | 3 | 4 | 0 | 0 | 1 | 68 | 98 |
| `get_public_api` | 10 | 9 | 0 | 0 | 1 | 112 | 143 |
| `get_symbol_detail` | 5 | 4 | 0 | 0 | 1 | 117 | 117 |
| `get_symbol_source` | 3 | 3 | 0 | 1 | 1 | 89 | 89 |
| `get_file_outline` | 1 (not-ready) | 7 | 0 | 1 | 33 | 37 | 307 |
| `get_project_graph` | 1 | 3 | 2 | 2 | 2 | 975 | 975 |
| `get_dependency_graph` | 3 | 20 | 0 | 1 | 2 | 30 | 37 |
| `detect_circular_dependencies` | 3 | 4 | 0 | 0 | 1 | 7 | 7 |
| `get_diagnostics` | 6 | 149 | 0 | 2 | 2 | 22 | 30 |
| `resolve_stack_trace` | n/a | 13 | n/a | 1 | 2 | n/a | 406 |
| `get_nuget_packages` | 8 | 9 | 4 | 5 | 27 | 1,255 | 1,263 |
| `analyze_change_impact` | n/a | 23 | n/a | 3 | 4 | n/a | 248 |
| `get_di_registrations` | 18 | 21 | 10 | 4 | 4 | 682 | 694 |
| `get_endpoint_map` | 23 | 10 | 19 | 5 | 5 | 12 | 20 |
| `get_test_coverage_map` | 1,063 | 13 | 409 | 4 | 4 | 2,482 | 2,152 |
| `detect_antipatterns` | 301 | 96 | 260 | 23 | 27 | 9,855 | 620 |
| `find_dead_code` | 587 | 149 | 257 | 25 | 29 | 503 | 35 |

### small: eshop

| Tool | Cold (published-0.8.0) | Cold (current) | Warm p50 (published-0.8.0) | Warm p50 (current) | Warm p95 (current) | Tokens (published-0.8.0) | Tokens (current) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `find_symbol` | 1 | 2 | 1 | 1 | 1 | 52 | 66 |
| `find_references` | 393 | 317 | 6 | 6 | 7 | 4,673 | 5,259 |
| `find_implementations` | 133 | 127 | 1 | 2 | 15 | 67 | 87 |
| `find_callers` | 157 | 147 | 2 | 2 | 3 | 51 | 65 |
| `find_overrides` | 34 | 7 | 2 | 1 | 2 | 417 | 466 |
| `get_type_hierarchy` | 7 | 8 | 1 | 1 | 2 | 89 | 116 |
| `get_public_api` | 5 | 5 | 1 | 1 | 1 | 327 | 353 |
| `get_symbol_detail` | 4 | 5 | 1 | 1 | 1 | 117 | 117 |
| `get_symbol_source` | 3 | 3 | 1 | 1 | 1 | 132 | 132 |
| `get_file_outline` | 2 (not-ready) | 11 | 0 | 2 | 2 | 32 | 1,567 |
| `get_project_graph` | 4 | 5 | 4 | 5 | 6 | 3,066 | 3,066 |
| `get_dependency_graph` | 3 | 7 | 0 | 2 | 3 | 30 | 41 |
| `detect_circular_dependencies` | 3 | 4 | 0 | 0 | 1 | 7 | 7 |
| `get_diagnostics` | 6 | 244 | 0 | 4 | 8 | 22 | 30 |
| `resolve_stack_trace` | n/a | 30 | n/a | 3 | 3 | n/a | 332 |
| `get_nuget_packages` | 13 | 38 | 10 | 12 | 14 | 2,338 | 2,347 |
| `analyze_change_impact` | n/a | 51 | n/a | 5 | 6 | n/a | 201 |
| `get_di_registrations` | 132 | 58 | 98 | 30 | 42 | 3,641 | 3,653 |
| `get_endpoint_map` | 186 | 60 | 190 | 38 | 63 | 1,881 | 1,889 |
| `get_test_coverage_map` | 4,267 | 62 | 1,058 | 26 | 35 | 2,181 | 2,035 |
| `detect_antipatterns` | 664 | 597 | 537 | 430 | 491 | 8,814 | 11,398 |
| `find_dead_code` | 1,733 | 1,777 | 931 | 182 | 436 | 1,425 | 189 |

### medium: jellyfin

| Tool | Cold (published-0.8.0) | Cold (current) | Warm p50 (published-0.8.0) | Warm p50 (current) | Warm p95 (current) | Tokens (published-0.8.0) | Tokens (current) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `find_symbol` | 2 | 3 | 1 | 2 | 2 | 53 | 67 |
| `find_references` | 840 | 719 | 4 | 6 | 25 | 5,363 | 6,187 |
| `find_implementations` | 1,014 | 990 | 1 | 2 | 10 | 75 | 95 |
| `find_callers` | 135 | 137 | 7 | 8 | 9 | 1,038 | 1,195 |
| `find_overrides` | 16 | 15 | 4 | 3 | 4 | 732 | 844 |
| `get_type_hierarchy` | 5 | 6 | 3 | 3 | 3 | 170 | 197 |
| `get_public_api` | 18 | 44 | 2 | 3 | 4 | 2,642 | 2,669 |
| `get_symbol_detail` | 6 | 7 | 1 | 1 | 2 | 94 | 94 |
| `get_symbol_source` | 4 | 4 | 1 | 1 | 1 | 115 | 115 |
| `get_file_outline` | 2 (not-ready) | 12 | 1 | 4 | 4 | 38 | 7,878 |
| `get_project_graph` | 6 | 6 | 5 | 6 | 6 | 6,027 | 6,027 |
| `get_dependency_graph` | 4 | 12 | 1 | 5 | 16 | 25 | 104 |
| `detect_circular_dependencies` | 4 | 4 | 0 | 0 | 1 | 7 | 7 |
| `get_diagnostics` | 8 | 815 | 1 | 112 | 118 | 22 | 30 |
| `resolve_stack_trace` | n/a | 18 | n/a | 4 | 5 | n/a | 501 |
| `get_nuget_packages` | 34 | 21 | 15 | 14 | 14 | 2,599 | 2,607 |
| `analyze_change_impact` | n/a | 442 | n/a | 169 | 214 | n/a | 1,603 |
| `get_di_registrations` | 1,440 | 363 | 222 | 312 | 349 | 6,063 | 6,074 |
| `get_endpoint_map` | 389 | 392 | 348 | 389 | 398 | 60 | 68 |
| `get_test_coverage_map` | 16,858 | 110 | 5,417 | 40 | 71 | 1,733 | 1,898 |
| `detect_antipatterns` | 3,063 | 2,771 | 2,649 | 952 | 1,643 | 9,422 | 12,127 |
| `find_dead_code` | 14,948 | 6,226 | 11,225 | 1,086 | 1,465 | 464 | 299 |

### large: orchardcore

| Tool | Cold (published-0.8.0) | Cold (current) | Warm p50 (published-0.8.0) | Warm p50 (current) | Warm p95 (current) | Tokens (published-0.8.0) | Tokens (current) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `find_symbol` | 66,709 | 6 | skipped | 3 | 5 | 55 | 69 |
| `find_references` | 58,996 | 1,674 | 29 | 12 | 16 | 6,251 | 6,965 |
| `find_implementations` | 10,950 | 2,134 | 29 | 13 | 26 | 2,254 | 2,589 |
| `find_callers` | 2,953 | 1,977 | 85 | 63 | 103 | 4,011 | 4,431 |
| `find_overrides` | 237 | 326 | 60 | 34 | 35 | 4,209 | 4,761 |
| `get_type_hierarchy` | 28 | 14 | 24 | 9 | 12 | 551 | 607 |
| `get_public_api` | 27 | 6 | 20 | 2 | 4 | 200 | 232 |
| `get_symbol_detail` | 44 | 6 | 41 | 2 | 3 | 96 | 96 |
| `get_symbol_source` | 48 | 5 | 41 | 2 | 3 | 80 | 80 |
| `get_file_outline` | 208 (not-ready) | 52 | 1 | 4 | 12 | 47 | 371 |
| `get_project_graph` | 38 | 39 | 31 | 36 | 44 | 70,873 | 70,873 |
| `get_dependency_graph` | 29 | 12 | 21 | 6 | 17 | 29 | 31 |
| `detect_circular_dependencies` | 5 | 8 | 2 | 4 | 10 | 7 | 7 |
| `get_diagnostics` | 10 | 196 | 1 | 24 | 38 | 22 | 30 |
| `resolve_stack_trace` | n/a | 32 | n/a | 12 | 18 | n/a | 506 |
| `get_nuget_packages` | 107 | 112 | 98 | 105 | 107 | 8,146 | 8,155 |
| `analyze_change_impact` | n/a | 54,461 | n/a | 48,157 | 55,251 | n/a | 4,347 |
| `get_di_registrations` | 428 | 376 | 407 | 160 | 246 | 9,469 | 9,481 |
| `get_endpoint_map` | 588 | 148 | 696 | 124 | 144 | 4,947 | 4,956 |
| `get_test_coverage_map` | 15,979 | 153 | 62,491 | 74 | 96 | 2,581 | 2,377 |
| `detect_antipatterns` | 3,226 | 908 | 2,908 | 670 | 682 | 10,312 | 13,351 |
| `find_dead_code` | 26,461 | 4,586 | 83,945 | 1,582 | 1,754 | 1,633 | 541 |

### xl: abp

| Tool | Cold (published-0.8.0) | Cold (current) | Warm p50 (published-0.8.0) | Warm p50 (current) | Warm p95 (current) | Tokens (published-0.8.0) | Tokens (current) |
|---|---:|---:|---:|---:|---:|---:|---:|
| `find_symbol` | 13 | 10 | 8 | 6 | 9 | 187 | 223 |
| `find_references` | 1,661 | 2,206 | 59 | 53 | 104 | 5,911 | 6,695 |
| `find_implementations` | 605 | 662 | 36 | 46 | 48 | 3,785 | 4,303 |
| `find_callers` | 3,166 | 4,141 | 69 | 230 | 318 | 1,090 | 1,241 |
| `find_overrides` | 38 | 28 | 16 | 15 | 24 | 4,388 | 4,808 |
| `get_type_hierarchy` | 13 | 29 | 10 | 18 | 20 | 811 | 844 |
| `get_public_api` | 12 | 11 | 6 | 5 | 5 | 109 | 135 |
| `get_symbol_detail` | 11 | 9 | 6 | 4 | 5 | 98 | 98 |
| `get_symbol_source` | 8 | 8 | 6 | 4 | 4 | 145 | 145 |
| `get_file_outline` | 9 (not-ready) | 17 | 4 | 6 | 15 | 38 | 3,850 |
| `get_project_graph` | 86 | 66 | 77 | 58 | 59 | 173,037 | 173,037 |
| `get_dependency_graph` | 13 | 34 | 6 | 11 | 12 | 25 | 30 |
| `detect_circular_dependencies` | 8 | 7 | 5 | 3 | 8 | 7 | 7 |
| `get_diagnostics` | 12 | 855 | 3 | 66 | 119 | 22 | 30 |
| `resolve_stack_trace` | n/a | 36 | n/a | 28 | 34 | n/a | 446 |
| `get_nuget_packages` | 110 | 83 | 98 | 120 | 122 | 9,551 | 9,559 |
| `analyze_change_impact` | n/a | 998 | n/a | 715 | 769 | n/a | 1,477 |
| `get_di_registrations` | 236 | 258 | 236 | 107 | 183 | 7,390 | 7,413 |
| `get_endpoint_map` | 353 | 117 | 372 | 90 | 103 | 431 | 440 |
| `get_test_coverage_map` | 13,446 | 332 | 6,467 | 114 | 140 | 2,013 | 2,111 |
| `detect_antipatterns` | 4,999 | 3,036 | 4,533 | 1,641 | 1,856 | 9,367 | 12,384 |
| `find_dead_code` | 15,498 | 4,227 | 13,880 | 2,170 | 2,317 | 2,649 | 3,118 |

## Slowest tools per corpus (current, cold)

| Tier | Corpus | Slowest five (cold ms) |
|---|---|---|
| small | clean-architecture | `find_references` 151, `get_diagnostics` 149, `find_dead_code` 149, `find_implementations` 130, `find_callers` 101 |
| small | eshop | `find_dead_code` 1,777, `detect_antipatterns` 597, `find_references` 317, `get_diagnostics` 244, `find_callers` 147 |
| medium | jellyfin | `find_dead_code` 6,226, `detect_antipatterns` 2,771, `find_implementations` 990, `get_diagnostics` 815, `find_references` 719 |
| large | orchardcore | `analyze_change_impact` 54,461, `find_dead_code` 4,586, `find_implementations` 2,134, `find_callers` 1,977, `find_references` 1,674 |
| xl | abp | `find_dead_code` 4,227, `find_callers` 4,141, `detect_antipatterns` 3,036, `find_references` 2,206, `analyze_change_impact` 998 |

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
- **abp / current** workspace diagnostics logged by the server (13; non-fatal unless projects loaded < declared): Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.Core\Volo.Abp.Core.csproj' with message: PackageReference System.Linq.Queryable will not be pruned. This package is automatically available and does not need to be referenced explicitly. Remove the PackageReference item.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.Cli.Core\Volo.Abp.Cli.Core.csproj' with message: Package 'LibGit2Sharp 0.31.0' was restored using '.NETPortable,Version=v0.0,Profile=Profile259, .NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework '.NETStandard,Version=v2.0'. This package may not be fully compatible with your project.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.EntityFrameworkCore.MySQL.Pomelo\Volo.Abp.EntityFrameworkCore.MySQL.Pomelo.csproj' with message: Detected package version outside of dependency constraint: Pomelo.EntityFrameworkCore.MySql 9.0.0 requires Microsoft.EntityFrameworkCore.Relational (>= 9.0.0 && <= 9.0.999) but version Microsoft.EntityFrameworkCore.Relational 10.0.11 was resolved.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.EntityFrameworkCore.Oracle.Devart\Volo.Abp.EntityFrameworkCore.Oracle.Devart.csproj' with message: Detected package downgrade: Microsoft.EntityFrameworkCore.Relational from 10.0.11 to 9.0.12. Reference the package directly from the project to select a different version.; Workspace failure: Msbuild failed when processing the file 'abp-10.7.1\framework\src\Volo.Abp.EventBus.Rebus\Volo.Abp.EventBus.Rebus.csproj' with message: Detected package version outside of dependency constraint: Rebus 8.8.0 requires system.text.json (>= 8.0.5 && < 10.0.0) but version System.Text.Json 10.0.11 was resolved.
- **abp** note: Release tag 10.7.1, not the dev branch. ABP's dev head (a840be3) pins SDK 11.0.100-rc.3 and multi-targets net11.0 in 176 of 178 framework/src projects, which .NET SDK 10 cannot build or load. The 10.7.1 release targets net10.0 (plus netstandard2.x/net8/net9) and restores cleanly on SDK 10.0.403, so the xl tier keeps the same codebase.
- **abp** note: Clone with core.longpaths=true on Windows: several test paths exceed 260 characters.

