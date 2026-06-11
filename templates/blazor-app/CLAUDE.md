# [Project Name] — Blazor Application

> Copy this file into your project root and customize the sections below.

## Project Context

This is a .NET 10 Blazor application using [Server / WebAssembly / Auto] interactive render mode. Components are organized by feature with shared UI components in a common library. The app uses [SSR with enhanced navigation / full interactivity / a mix of static and interactive pages].

## Tech Stack

- **.NET 10** / C# 14
- **Blazor** — [Server / WebAssembly / Auto] interactive render mode
- **ASP.NET Core** — hosting, authentication, middleware
- **Entity Framework Core** — default ORM with PostgreSQL/SQL Server
- **ASP.NET Core Identity** — authentication and user management
- **Serilog** — structured logging
- **xUnit v3** + **bUnit** — component and integration testing

## Architecture

```
src/
  [ProjectName]/
    Components/
      Layout/
        MainLayout.razor
        NavMenu.razor
      Pages/
        Home.razor
        [Feature]/
          [FeaturePage].razor
      Shared/
        [ReusableComponent].razor
      _Imports.razor
      App.razor
      Routes.razor
    Services/
      [Feature]/
        [Feature]Service.cs
        I[Feature]Service.cs
    Models/
      [Entity].cs
    Data/
      AppDbContext.cs
      Configurations/
        [Entity]Configuration.cs
    Program.cs
  [ProjectName].Client/                 # Only for WebAssembly/Auto mode
    Pages/
      [InteractivePage].razor
    Services/
      [ClientService].cs
    _Imports.razor
tests/
  [ProjectName].Tests/
    Components/
      [Component]Tests.cs
    Services/
      [Service]Tests.cs
    Fixtures/
      TestContext.cs
```

### Component Organization

Components follow a feature-based folder structure under `Components/Pages/`. Each feature gets its own subfolder. Reusable UI elements live in `Components/Shared/`.

### Render Mode Strategy

Choose render modes at the component level, not globally, for maximum flexibility:

```razor
@* Static SSR by default — opt in to interactivity per component *@
@rendermode InteractiveServer

@* Or for Auto mode (Server first, then WASM after download) *@
@rendermode InteractiveAuto
```

### Service Pattern

Services encapsulate business logic and data access. Components inject services, never DbContext directly:

```csharp
public interface IOrderService
{
    Task<Result<List<OrderSummary>>> GetOrdersAsync(CancellationToken ct = default);
    Task<Result<OrderDetail>> GetOrderByIdAsync(int id, CancellationToken ct = default);
}

internal class OrderService(AppDbContext db, TimeProvider time) : IOrderService
{
    public async Task<Result<List<OrderSummary>>> GetOrdersAsync(CancellationToken ct = default)
    {
        var orders = await db.Orders
            .Select(o => new OrderSummary(o.Id, o.CustomerName, o.Total))
            .ToListAsync(ct);
        return Result.Success(orders);
    }
}
```

## Coding Standards

- **C# 14 features** — Use primary constructors, collection expressions, `field` keyword, records, pattern matching
- **File-scoped namespaces** — Always
- **`var` for obvious types** — Use explicit types when the type isn't clear from context
- **Naming** — PascalCase for public members, `_camelCase` for private fields, suffix async methods with `Async`
- **No regions** — Ever
- **No comments for obvious code** — Only comment "why", never "what"
- **Component parameters** — Use `[Parameter]` for parent-to-child, `[CascadingParameter]` sparingly, prefer DI for shared state
- **One component per file** — Except tiny render fragments

### Razor Component Conventions

- Place `@code` blocks at the bottom of `.razor` files
- Keep `@code` blocks small — extract logic to services or code-behind (`.razor.cs`) when they exceed ~30 lines
- Use `@inject` at the top of the file, one per line
- Prefer `EventCallback<T>` over `Action<T>` for component events

## Skills

Load these dotnet-claude-kit skills for context:

- `modern-csharp` — C# 14 language features and idioms
- `architecture-advisor` — Architecture guidance for structuring the backend
- `authentication` — ASP.NET Core Identity, cookie auth, authorization policies
- `error-handling` — Result pattern, error boundaries, ProblemDetails for API endpoints
- `testing` — xUnit v3, bUnit component testing, WebApplicationFactory
- `configuration` — Options pattern, secrets management
- `dependency-injection` — Service registration, scoped vs transient lifetimes
- `ef-core` — DbContext patterns, query optimization, migrations
- `logging` — Serilog, OpenTelemetry
- `workflow-mastery` — Parallel worktrees, verification loops, subagent patterns, context discipline
- `instinct-system` — Capture corrections, instincts, and discoveries as persistent learning
- `wrap-up` — Structured session handoff to `.claude/handoff.md`

## MCP Tools

> **Setup:** Install once globally with `dotnet tool install -g CWM.RoslynNavigator` and register with `claude mcp add --scope user cwm-roslyn-navigator -- cwm-roslyn-navigator --solution ${workspaceFolder}`. After that, these tools are available in every .NET project.

Use `cwm-roslyn-navigator` tools to minimize token consumption:

- **Before modifying a type** — Use `find_symbol` to locate it, `get_public_api` to understand its surface
- **Before adding a reference** — Use `find_references` to understand existing usage
- **To understand architecture** — Use `get_project_graph` to see project dependencies
- **To find implementations** — Use `find_implementations` instead of grep for interface/abstract class implementations
- **To check for errors** — Use `get_diagnostics` after changes

## Commands

```bash
# Build
dotnet build

# Run (development with hot reload)
dotnet watch --project src/[ProjectName]

# Run without hot reload
dotnet run --project src/[ProjectName]

# Run tests
dotnet test

# Run bUnit tests only
dotnet test --filter "Category=Component"

# Add EF migration
dotnet ef migrations add [Name] --project src/[ProjectName]

# Apply migrations
dotnet ef database update --project src/[ProjectName]

# Format check
dotnet format --verify-no-changes
```

## Workflow

- **Plan first** — Enter plan mode for any non-trivial task (3+ steps or architecture decisions). Iterate until the plan is solid before writing code.
- **Verify before done** — Run `dotnet build` and `dotnet test` after changes. Use `get_diagnostics` via MCP to catch warnings. Ask: "Would a staff engineer approve this?"
- **Fix bugs autonomously** — When given a bug report, investigate and fix it without hand-holding. Check logs, errors, failing tests — then resolve them.
- **Stop and re-plan** — If implementation goes sideways, STOP and re-plan. Don't push through a broken approach.
- **Use subagents** — Offload research, exploration, and parallel analysis to subagents. One task per subagent for focused execution.
- **Learn from corrections** — After any correction, capture the pattern in memory so the same mistake never recurs.

## Anti-patterns

Do NOT generate code that:

- **Uses `DateTime.Now`** — Use `TimeProvider` injection instead
- **Creates `new HttpClient()`** — Use `IHttpClientFactory` or typed HTTP clients
- **Uses `async void`** — Always return `Task`; the sole exception is component lifecycle event handlers where the framework requires `async void`
- **Blocks with `.Result` or `.Wait()`** — Await instead
- **Injects `DbContext` into components directly** — Use a service layer; DbContext lifetime doesn't align with component lifetime
- **Uses `JSRuntime` for things CSS or Blazor can do natively** — Prefer CSS classes, `@bind`, `NavigationManager`, `FocusAsync()` over JS interop
- **Mixes render modes without understanding the boundary** — Interactive Server and WASM components cannot share state via DI; data must cross the boundary via parameters, API calls, or persistent storage
- **Puts heavy logic in `OnInitializedAsync`** — Long-running operations block rendering; use streaming rendering with `[StreamRendering]` or load data progressively
- **Uses `StateHasChanged()` excessively** — The framework calls it automatically after event handlers and lifecycle methods; manual calls indicate a design problem
- **Stores large data in component state** — Use `PersistentComponentState` for prerendering or a dedicated state container service
- **Returns domain entities to the UI** — Always map to view models or DTOs
- **Uses `[CascadingParameter]` as a general state management solution** — Cascading values cause re-renders of the entire subtree; prefer scoped DI services
- **Creates repository abstractions over EF Core** — Use DbContext directly in services
- **Uses in-memory database for tests** — Use Testcontainers for integration tests, bUnit for component tests
- **Catches bare `Exception`** — Catch specific types; use `ErrorBoundary` components for UI-level error handling
- **Uses string interpolation in log messages** — Use structured logging templates
