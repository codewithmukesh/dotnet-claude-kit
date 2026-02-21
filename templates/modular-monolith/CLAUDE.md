# [Project Name] — Modular Monolith

> Copy this file into your project root and customize the sections below.

## Project Context

This is a .NET 10 modular monolith with each module using its own internal architecture (VSA, Clean Architecture, or DDD — run the `architecture-advisor` skill per module if needed). The application is composed of independent modules that run in a single deployable unit (the Host) but maintain strict boundaries — each module owns its features, data, and domain logic. Modules communicate through integration events, never by direct cross-module method calls or shared database tables.

## Tech Stack

- **.NET 10** / C# 14
- **ASP.NET Core Minimal APIs** — endpoint routing with `MapGroup`, one route group per module
- **Entity Framework Core** — one DbContext per module, PostgreSQL/SQL Server
- **MassTransit** — inter-module messaging via integration events, transactional outbox
- **MediatR** (or Wolverine or raw handlers) — intra-module command/query dispatch
- **FluentValidation** — request validation
- **Serilog** — structured logging
- **xUnit v3** + **Testcontainers** — testing

## Architecture

```
src/
  [ProjectName].Host/                    # Startup host — wires all modules together
    Program.cs                           # Service registration, middleware, module mapping
    appsettings.json

  [ProjectName].Shared/                  # Shared kernel (thin!)
    Contracts/
      Events/                            # Integration event records (pure data)
    Common/
      Result.cs                          # Result pattern
      Behaviors/                         # MediatR pipeline behaviors
      Extensions/                        # Shared extension methods

  Modules/
    [Module]/                            # e.g., Orders, Catalog, Identity
      [ProjectName].Modules.[Module]/
        Features/
          [Feature]/
            [Operation].cs               # Command/Query + Handler + Response
        Persistence/
          [Module]DbContext.cs            # Module-scoped DbContext
          Configurations/                # EF entity configurations
          Migrations/                    # Module-scoped migrations
        Consumers/                       # MassTransit event consumers
        [Module]Module.cs                # IServiceCollection + IEndpointRouteBuilder extensions

tests/
  Modules/
    [ProjectName].Modules.[Module].Tests/
      Features/
        [Feature]/
          [Operation]Tests.cs
      Fixtures/
        [Module]Fixture.cs               # WebApplicationFactory scoped to module
  [ProjectName].Integration.Tests/       # Cross-module integration tests
    Fixtures/
      AppFixture.cs                      # Full application fixture with Testcontainers
```

### Module Structure Convention

Each module is a standalone class library that exposes a single registration extension:

```csharp
// Modules/Orders/[ProjectName].Modules.Orders/OrdersModule.cs
public static class OrdersModule
{
    public static IServiceCollection AddOrdersModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<OrdersDbContext>(options =>
            options.UseNpgsql(config.GetConnectionString("OrdersDb")));

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(OrdersModule).Assembly));
        services.AddValidatorsFromAssembly(typeof(OrdersModule).Assembly);

        return services;
    }

    public static IEndpointRouteBuilder MapOrdersModule(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/orders")
            .WithTags("Orders")
            .MapOrderEndpoints();

        return app;
    }
}
```

### Feature File Convention

Each feature operation lives in a single file using a static class wrapper:

```csharp
public static class CreateOrder
{
    public record Command(...) : IRequest<Result<Response>>;
    public record Response(...);
    public class Validator : AbstractValidator<Command> { }
    internal class Handler(OrdersDbContext db, IPublishEndpoint publisher, TimeProvider clock)
        : IRequestHandler<Command, Result<Response>> { }
}
```

### Module Communication

Modules communicate exclusively through integration events in the Shared contracts project:

```csharp
// Shared/Contracts/Events/OrderCreated.cs — pure data, no behavior
public record OrderCreated(Guid OrderId, string CustomerId, decimal Total, DateTimeOffset CreatedAt);

// Orders module publishes
await publisher.Publish(new OrderCreated(order.Id, order.CustomerId, order.Total, clock.GetUtcNow()), ct);

// Notifications module consumes
public class OrderCreatedConsumer(NotificationsDbContext db) : IConsumer<OrderCreated>
{
    public async Task Consume(ConsumeContext<OrderCreated> context) { /* ... */ }
}
```

### Database Isolation

Each module owns its own DbContext targeting a separate schema (or separate database):

```csharp
public class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("orders");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrdersDbContext).Assembly);
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
- **Module prefix** — Namespace all module types under `[ProjectName].Modules.[Module]`
- **Internal by default** — Module handlers, consumers, and DbContexts should be `internal` where possible; expose only the module registration extension as `public`

## Skills

Load these dotnet-claude-kit skills for context:

- `modern-csharp` — C# 14 language features and idioms
- `architecture-advisor` — Choose the internal architecture for each module
- `vertical-slice` — Feature folder structure and handler patterns (if using VSA)
- `clean-architecture` — Layered project structure with dependency inversion (if using CA)
- `ddd` — Aggregates, value objects, domain events (if using DDD)
- `project-structure` — Solution layout, Directory.Build.props, central package management
- `ef-core` — DbContext patterns, query optimization, migrations (one DbContext per module)
- `messaging` — MassTransit, transactional outbox, integration events between modules
- `dependency-injection` — Service registration patterns, module-scoped DI
- `error-handling` — Result pattern, ProblemDetails
- `testing` — xUnit v3, WebApplicationFactory, Testcontainers
- `configuration` — Options pattern, per-module configuration sections
- `logging` — Serilog, structured logging, OpenTelemetry

## MCP Tools

Use `cwm-roslyn-navigator` tools to minimize token consumption:

- **Before modifying a type** — Use `find_symbol` to locate it, `get_public_api` to understand its surface
- **Before adding a reference** — Use `find_references` to understand existing usage
- **To understand architecture** — Use `get_project_graph` to see project dependencies and verify module boundaries
- **To find implementations** — Use `find_implementations` instead of grep for interface/abstract class implementations
- **To check for errors** — Use `get_diagnostics` after changes
- **To verify module isolation** — Use `get_project_graph` to confirm modules do not reference each other directly

## Commands

```bash
# Build entire solution
dotnet build

# Run the host (development)
dotnet run --project src/[ProjectName].Host

# Run all tests
dotnet test

# Run tests for a specific module
dotnet test tests/Modules/[ProjectName].Modules.[Module].Tests

# Add EF migration for a specific module
dotnet ef migrations add [Name] \
  --project src/Modules/[Module]/[ProjectName].Modules.[Module] \
  --startup-project src/[ProjectName].Host \
  --context [Module]DbContext

# Apply migrations for a specific module
dotnet ef database update \
  --project src/Modules/[Module]/[ProjectName].Modules.[Module] \
  --startup-project src/[ProjectName].Host \
  --context [Module]DbContext

# Format check
dotnet format --verify-no-changes
```

## Anti-patterns

Do NOT generate code that:

- Uses `DateTime.Now` — use `TimeProvider` injection instead
- Creates `new HttpClient()` — use `IHttpClientFactory`
- Uses `async void` — always return `Task`
- Blocks with `.Result` or `.Wait()` — await instead
- Uses `Results.Ok()` — use `TypedResults.Ok()` for OpenAPI
- Returns domain entities from endpoints — always map to response DTOs
- Creates repository abstractions over EF Core — use the module's DbContext directly
- Uses in-memory database for tests — use Testcontainers
- Catches bare `Exception` — catch specific types, let the global handler catch the rest
- Uses string interpolation in log messages — use structured logging templates
- References another module's DbContext or internal types — communicate via integration events only
- Shares database tables between modules — each module owns its schema
- Calls another module's handler directly — use MassTransit publish/send for cross-module communication
- Puts business logic in the Shared project — the shared kernel contains only contracts, primitives, and cross-cutting infrastructure
- Creates a single "god" DbContext for the entire application — each module gets its own DbContext
- Publishes events without the transactional outbox — use `AddEntityFrameworkOutbox` for reliability
