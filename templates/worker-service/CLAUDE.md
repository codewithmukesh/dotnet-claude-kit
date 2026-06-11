# [Project Name] — Worker Service

> Copy this file into your project root and customize the sections below.

## Project Context

This is a .NET 10 Worker Service that runs long-lived background tasks. It may process messages from a broker (RabbitMQ, Azure Service Bus), execute scheduled/recurring jobs, or run as a hosted service performing periodic work. The application runs as a headless process with no HTTP endpoints (unless health checks are added).

## Tech Stack

- **.NET 10** / C# 14
- **BackgroundService / IHostedService** — hosting model for background workers
- **Wolverine** (or MassTransit) — message consumption from RabbitMQ or Azure Service Bus
- **Serilog** — structured logging with console and sink targets
- **Hangfire** (optional) — recurring/scheduled job processing
- **Entity Framework Core** (optional) — data access with PostgreSQL/SQL Server
- **Polly** — resilience and retry policies for transient failures
- **xUnit v3** + **Testcontainers** — testing

## Architecture

```
src/
  [ProjectName].Worker/
    Consumers/
      [Message]Consumer.cs          # Wolverine/MassTransit message consumers
    Jobs/
      [JobName]Job.cs               # Scheduled/recurring job logic
    Workers/
      [WorkerName]Worker.cs         # BackgroundService implementations
    Services/
      [ServiceName]Service.cs       # Domain/business logic services
    Common/
      Persistence/                  # DbContext, configurations (if needed)
      Extensions/                   # Service registration helpers
    Program.cs
    appsettings.json
tests/
  [ProjectName].Worker.Tests/
    Consumers/
      [Message]ConsumerTests.cs
    Workers/
      [WorkerName]WorkerTests.cs
    Jobs/
      [JobName]JobTests.cs
    Fixtures/
      WorkerFixture.cs              # Host setup for integration tests
```

### BackgroundService Convention

Each worker should inherit from `BackgroundService` and handle cancellation properly:

```csharp
public sealed class OrderProcessingWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OrderProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OrderProcessingWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IOrderService>();
                await service.ProcessPendingOrdersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown — not an error
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing orders");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
```

### Message Consumer Convention

Each consumer lives in its own file under `Consumers/`. Wolverine uses convention-based handlers (no interface); MassTransit uses `IConsumer<T>`:

```csharp
// Wolverine — convention-based, no interface needed
public static class OrderCreatedHandler
{
    public static async Task Handle(
        OrderCreated message,
        IOrderService orderService,
        ILogger logger,
        CancellationToken ct)
    {
        logger.LogInformation("Processing OrderCreated {OrderId}", message.OrderId);
        await orderService.HandleOrderCreatedAsync(message, ct);
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
- **Scoped services in workers** — Always create a scope via `IServiceScopeFactory` inside `ExecuteAsync`; never inject scoped services directly into a `BackgroundService`
- **Seal worker and consumer classes** — Use `sealed` on classes that are not designed for inheritance

## Skills

Load these dotnet-claude-kit skills for context:

- `modern-csharp` — C# 14 language features and idioms
- `architecture-advisor` — Architecture guidance for structuring worker internals
- `messaging` — Wolverine/MassTransit consumers, outbox, saga patterns, broker configuration
- `logging` — Serilog, structured logging, OpenTelemetry
- `docker` — Multi-stage Dockerfile, health checks, Docker Compose
- `configuration` — Options pattern, secrets management, environment-specific config
- `dependency-injection` — Service lifetimes, keyed services, registration patterns
- `testing` — xUnit v3, test harness, Testcontainers
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

# Run (development)
dotnet run --project src/[ProjectName].Worker

# Run tests
dotnet test

# Run with specific environment
DOTNET_ENVIRONMENT=Development dotnet run --project src/[ProjectName].Worker

# Docker build
docker build -t [project-name]-worker .

# Docker run
docker run --rm -e DOTNET_ENVIRONMENT=Production [project-name]-worker

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

- Uses `async void` in `BackgroundService` — `ExecuteAsync` returns `Task`; all internal methods must also return `Task`
- Ignores `CancellationToken` — Always pass `stoppingToken` through the entire call chain; check `IsCancellationRequested` in loops
- Swallows `OperationCanceledException` silently — Only catch it when `stoppingToken.IsCancellationRequested` is true (graceful shutdown); rethrow otherwise
- Injects scoped services into `BackgroundService` directly — Create a scope with `IServiceScopeFactory` per iteration
- Uses `Thread.Sleep` — Use `Task.Delay` with cancellation token
- Uses `Task.Run` to wrap synchronous work inside `ExecuteAsync` — Run CPU-bound work properly or redesign
- Uses `DateTime.Now` — Use `TimeProvider` injection instead
- Creates `new HttpClient()` — Use `IHttpClientFactory`
- Blocks with `.Result` or `.Wait()` — Await instead
- Uses bare `while (true)` loops — Always use `while (!stoppingToken.IsCancellationRequested)`
- Uses string interpolation in log messages — Use structured logging templates (`LogInformation("Processing {OrderId}", id)`)
- Catches bare `Exception` without logging — Always log the exception; let fatal errors propagate
- Creates fire-and-forget tasks with `_ = DoWorkAsync()` — Always await or track tasks to observe exceptions
- Registers message consumers as singletons — Consumers must be scoped or transient
