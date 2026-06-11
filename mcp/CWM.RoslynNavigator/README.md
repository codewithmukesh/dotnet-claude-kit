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
Tools/                  → MCP tool implementations (15 read-only tools)
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

### 0.7.1

- **Fixed: logs corrupted the MCP stdio stream** ([#10](https://github.com/codewithmukesh/dotnet-claude-kit/issues/10)) — All console logging now goes to stderr. The MCP stdio transport reserves stdout for JSON-RPC; log lines on stdout caused clients to drop the connection with "JSON Parse error".
- **Fixed: "No .NET SDKs were found" on macOS/Linux** ([#9](https://github.com/codewithmukesh/dotnet-claude-kit/issues/9)) — When `MSBuildLocator.RegisterDefaults()` fails (wrapper-script `dotnet` on PATH with `DOTNET_ROOT` unset), the server falls back to resolving the SDK via `dotnet --list-sdks` and registers it with `RegisterMSBuildPath`.

### 0.7.0

- **Performance optimizations across all tools:**
  - `find_references` — Document text caching (200 async calls → ~10) + `maxResults` cap (default 100)
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
