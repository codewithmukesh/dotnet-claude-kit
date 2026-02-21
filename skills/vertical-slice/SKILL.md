---
name: vertical-slice
description: >
  Vertical Slice Architecture (VSA) for .NET applications — one of several
  supported architectures in dotnet-claude-kit. Covers feature folders, endpoint
  grouping, and handler patterns for MediatR, Wolverine, and raw handler classes.
  Load this skill when the architecture-advisor recommends VSA, when working in
  an existing VSA codebase, when adding features to a feature-folder project,
  or when discussing vertical slice patterns, feature folders, or handler patterns.
---

# Vertical Slice Architecture (VSA)

## Core Principles

1. **Organize by feature, not by layer** — Each feature is a self-contained vertical slice containing its endpoint, handler, request/response types, and validation. No more jumping between Controllers/, Services/, Repositories/ folders.
2. **Minimize cross-feature coupling** — Features should not reference each other directly. Shared concerns go in a `Common/` or `Shared/` directory.
3. **One file per feature is fine** — A simple CRUD endpoint doesn't need 5 files spread across layers. Start with everything in one file, extract only when complexity demands it.
4. **The handler is the unit of work** — Each handler does one thing. No god-services with 20 methods.

## Patterns

### Feature Folder Structure

```
src/
  MyApp.Api/
    Features/
      Orders/
        CreateOrder.cs          # Request, Handler, Response, Endpoint — all in one file
        GetOrder.cs
        ListOrders.cs
        CancelOrder.cs
        Shared/
          OrderMapper.cs        # Shared within the Orders feature only
      Products/
        CreateProduct.cs
        GetProduct.cs
    Common/
      Behaviors/
        ValidationBehavior.cs   # Cross-cutting pipeline behavior
      Persistence/
        AppDbContext.cs
      Extensions/
        ServiceCollectionExtensions.cs
    Program.cs
```

### Pattern A: MediatR Handlers

The most popular approach. Uses `IRequest<T>` / `IRequestHandler<TRequest, TResponse>` with pipeline behaviors for cross-cutting concerns.

```csharp
// Features/Orders/CreateOrder.cs

public static class CreateOrder
{
    public record Command(string CustomerId, List<OrderItemDto> Items) : IRequest<Result<OrderResponse>>;

    public record OrderItemDto(string ProductId, int Quantity);

    public record OrderResponse(Guid Id, decimal Total, DateTime CreatedAt);

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.Items).NotEmpty();
            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(x => x.ProductId).NotEmpty();
                item.RuleFor(x => x.Quantity).GreaterThan(0);
            });
        }
    }

    internal class Handler(AppDbContext db, TimeProvider clock) : IRequestHandler<Command, Result<OrderResponse>>
    {
        public async Task<Result<OrderResponse>> Handle(Command request, CancellationToken ct)
        {
            var order = Order.Create(request.CustomerId, request.Items, clock.GetUtcNow());
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);

            return Result.Success(new OrderResponse(order.Id, order.Total, order.CreatedAt));
        }
    }
}

// Endpoint registration (in Program.cs or a MapGroup extension)
public static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrders(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders").WithTags("Orders");

        group.MapPost("/", async (CreateOrder.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/orders/{result.Value.Id}", result.Value)
                : result.ToProblemDetails();
        });

        return group;
    }
}
```

### Pattern B: Wolverine Handlers

Convention-based — no interfaces to implement. Wolverine discovers handlers by method signature.

```csharp
// Features/Orders/CreateOrder.cs

public static class CreateOrder
{
    public record Command(string CustomerId, List<OrderItemDto> Items);

    public record OrderItemDto(string ProductId, int Quantity);

    public record OrderResponse(Guid Id, decimal Total, DateTime CreatedAt);

    // Wolverine discovers this by convention (static Handle method)
    public static async Task<OrderResponse> Handle(
        Command command,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var order = Order.Create(command.CustomerId, command.Items, clock.GetUtcNow());
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        return new OrderResponse(order.Id, order.Total, order.CreatedAt);
    }
}
```

### Pattern C: Raw Handler Classes (No Library)

Direct handler classes with no external dependency. Good for small projects or teams that want full control.

```csharp
// Features/Orders/CreateOrder.cs

public static class CreateOrder
{
    public record Command(string CustomerId, List<OrderItemDto> Items);

    public record OrderItemDto(string ProductId, int Quantity);

    public record OrderResponse(Guid Id, decimal Total, DateTime CreatedAt);

    internal class Handler(AppDbContext db, TimeProvider clock)
    {
        public async Task<Result<OrderResponse>> ExecuteAsync(Command command, CancellationToken ct)
        {
            var order = Order.Create(command.CustomerId, command.Items, clock.GetUtcNow());
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);

            return Result.Success(new OrderResponse(order.Id, order.Total, order.CreatedAt));
        }
    }
}

// Endpoint wiring
group.MapPost("/", async (CreateOrder.Command command, CreateOrder.Handler handler, CancellationToken ct) =>
{
    var result = await handler.ExecuteAsync(command, ct);
    return result.IsSuccess
        ? Results.Created($"/orders/{result.Value.Id}", result.Value)
        : result.ToProblemDetails();
});
```

### Adding Module Boundaries (Optional)

For larger applications that grow beyond a single project, introduce module boundaries. Each module is a separate class library with its own features and DbContext.

```
src/
  MyApp.Api/                      # Host — wires modules together
    Program.cs
    Modules/
      ModuleExtensions.cs         # app.MapOrderModule(), app.MapCatalogModule()
  MyApp.Orders/                   # Module — own features, own DbContext
    Features/
      CreateOrder.cs
    Persistence/
      OrdersDbContext.cs
    OrdersModule.cs               # IServiceCollection + IEndpointRouteBuilder extensions
  MyApp.Catalog/                  # Module
    Features/
      CreateProduct.cs
    Persistence/
      CatalogDbContext.cs
    CatalogModule.cs
```

Modules communicate via:
- **Integration events** (preferred) — async, decoupled via MassTransit or Wolverine
- **Shared contracts** — a `MyApp.Contracts` project with DTOs/interfaces (use sparingly)

### Shared Concerns

Cross-cutting concerns live outside feature folders:

```csharp
// Common/Behaviors/ValidationBehavior.cs (MediatR pipeline)
public class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var context = new ValidationContext<TRequest>(request);
        var failures = validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next();
    }
}
```

## Anti-patterns

### Don't Create Layered Abstractions Within a Slice

```csharp
// BAD — a feature folder with its own service layer and repository
Features/
  Orders/
    CreateOrder.cs
    IOrderService.cs         # unnecessary abstraction
    OrderService.cs          # unnecessary abstraction
    IOrderRepository.cs      # unnecessary abstraction
    OrderRepository.cs       # unnecessary abstraction

// GOOD — handler talks directly to DbContext
Features/
  Orders/
    CreateOrder.cs           # handler uses AppDbContext directly
```

### Don't Cross-reference Features Directly

```csharp
// BAD — CreateOrder directly calls GetProduct handler
var product = await _getProductHandler.Handle(new GetProduct.Query(productId));

// GOOD — query the database directly or use a shared read model
var product = await db.Products.FindAsync(productId, ct);
```

### Don't Put Everything in One God Feature File

```csharp
// BAD — 500-line file with CRUD + business logic + mapping
public static class Orders
{
    // Create, Read, Update, Delete, Cancel, Refund, Export...
}

// GOOD — one file per operation
Features/Orders/CreateOrder.cs
Features/Orders/GetOrder.cs
Features/Orders/CancelOrder.cs
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| New project, team knows MediatR | Pattern A — MediatR with pipeline behaviors |
| New project, want minimal deps | Pattern C — Raw handler classes |
| High-throughput messaging | Pattern B — Wolverine (doubles as message bus) |
| Monolith growing complex | Add module boundaries, keep VSA within each module |
| Simple CRUD feature | Single file: request + handler + endpoint |
| Complex feature (saga, events) | Multiple files in feature folder, still colocated |
| Sharing logic between features | Extract to `Common/` — not to another feature |
| When to split into modules | When teams own different features, or when a bounded context boundary is clear |
