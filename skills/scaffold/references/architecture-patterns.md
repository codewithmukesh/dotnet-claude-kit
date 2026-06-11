# Architecture Patterns — /scaffold Reference

Loaded by `/scaffold` Step 4. Shared shapes are stated once below; each
architecture section then gives only its file placement map and the deltas
from the shared shapes.

## Shared Shapes (All Architectures)

These shapes are identical everywhere — only file placement and the handler's
dispatch mechanism change per architecture.

### Endpoint Group

Auto-discovered via `IEndpointGroup`. Full OpenAPI metadata, validation filter
on mutating endpoints, Result mapped to TypedResults, bounded pagination on
lists:

```csharp
// {placement}/OrderEndpoints.cs — auto-discovered via IEndpointGroup
public sealed class OrderEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders").WithTags("Orders");

        group.MapPost("/", CreateOrderHandler)
            .WithName("CreateOrder").WithSummary("Create a new order")
            .Produces<CreateOrder.Response>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .AddEndpointFilter<ValidationFilter<CreateOrder.Command>>();

        group.MapGet("/", ListOrdersHandler)
            .WithName("ListOrders").WithSummary("List orders with pagination")
            .Produces<PagedList<OrderSummary>>();

        group.MapGet("/{id:guid}", GetOrderHandler)
            .WithName("GetOrder")
            .Produces<OrderDetail>().ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> CreateOrderHandler(
        CreateOrder.Command cmd, CreateOrder.Handler handler, CancellationToken ct)
    {
        var result = await handler.HandleAsync(cmd, ct);
        return result.IsSuccess
            ? TypedResults.Created($"/api/orders/{result.Value.Id}", result.Value)
            : result.ToProblemDetails();
    }

    private static async Task<Ok<PagedList<OrderSummary>>> ListOrdersHandler(
        [AsParameters] PaginationQuery paging, AppDbContext db, CancellationToken ct)
    {
        var query = db.Orders.OrderByDescending(o => o.CreatedAt);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((paging.Page - 1) * paging.PageSize).Take(paging.PageSize)
            .Select(o => new OrderSummary(o.Id, o.Total, o.CreatedAt)).ToListAsync(ct);
        return TypedResults.Ok(new PagedList<OrderSummary>(items, total, paging.Page, paging.PageSize));
    }

    private static async Task<Results<Ok<OrderDetail>, NotFound>> GetOrderHandler(
        Guid id, AppDbContext db, CancellationToken ct)
    {
        var order = await db.Orders.Where(o => o.Id == id)
            .Select(o => new OrderDetail(o.Id, o.CustomerId, o.Total, o.CreatedAt)).FirstOrDefaultAsync(ct);
        return order is not null ? TypedResults.Ok(order) : TypedResults.NotFound();
    }
}

// Common/PaginationQuery.cs — bounded pagination, shared across features
public record PaginationQuery(int Page = 1, int PageSize = 20)
{
    public int Page { get; init; } = Math.Max(1, Page);
    public int PageSize { get; init; } = Math.Clamp(PageSize, 1, 50);
}
public record PagedList<T>(List<T> Items, int TotalCount, int Page, int PageSize);
```

### Validator

FluentValidation with meaningful rules — ranges, required fields, max lengths.
Never an empty validator:

```csharp
internal sealed class Validator : AbstractValidator<Command>
{
    public Validator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.Quantity).InclusiveBetween(1, 1000);
            item.RuleFor(x => x.UnitPrice).GreaterThan(0);
        });
    }
}
```

### Entity + EF Configuration

Always pair entity + `IEntityTypeConfiguration<T>`. No data annotations on
entities:

```csharp
// Domain/Entities/Product.cs — clean, no attributes
public sealed class Product
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Sku { get; private set; } = string.Empty;
    public decimal Price { get; private set; }

    public static Product Create(string name, string sku, decimal price) =>
        new() { Id = Guid.NewGuid(), Name = name, Sku = sku, Price = price };
}

// Persistence/Configurations/ProductConfiguration.cs — all EF config here
internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Sku).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Sku).IsUnique();
        builder.Property(x => x.Price).HasPrecision(18, 2);
    }
}
```

After creating entity + config: `dotnet ef migrations add AddProduct`

### Integration Test

Fixture with real database via Testcontainers and proper DI replacement
(`RemoveAll`, not fragile name matching), plus one happy-path and one
validation-failure test per scaffolded operation:

```csharp
// Tests/Fixtures/ApiFixture.cs
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:17").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_postgres.GetConnectionString()));
        });
    }

    public async Task InitializeAsync() { await _postgres.StartAsync(); /* apply migrations */ }
    public new async Task DisposeAsync() { await _postgres.DisposeAsync(); await base.DisposeAsync(); }
}

// Tests/Features/Orders/CreateOrderTests.cs
public sealed class CreateOrderTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task CreateOrder_ValidRequest_Returns201()
    {
        // Arrange
        var command = new { CustomerId = "CUST-001", Items = new[] { new { ProductId = Guid.NewGuid(), Quantity = 2, UnitPrice = 29.99m } } };

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", command);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(Guid.Empty, result.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task CreateOrder_EmptyItems_ReturnsValidationProblem()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", new { CustomerId = "CUST-001", Items = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

## Vertical Slice Architecture (VSA)

### File Placement

| File | Purpose |
|------|---------|
| `Features/{Name}/Create{Name}.cs` | Command + handler + validator + response in one file |
| `Features/{Name}/Get{Name}.cs` | Query + handler + response |
| `Features/{Name}/{Name}Endpoints.cs` | Endpoint group (shared shape above) |
| `Features/{Name}/{Name}Configuration.cs` | EF configuration (shared shape above) |
| `Tests/Features/{Name}/Create{Name}Tests.cs` | Integration tests (shared shape above) |

### Delta: Single-File Feature

Everything for one operation lives in a static wrapper class. Handler is
plain (no Mediator) and returns `Result<T>`:

```csharp
// Features/Orders/CreateOrder.cs
namespace MyApp.Features.Orders;

public static class CreateOrder
{
    public record Command(string CustomerId, List<ItemDto> Items);
    public record ItemDto(Guid ProductId, int Quantity, decimal UnitPrice);
    public record Response(Guid Id, decimal Total, DateTimeOffset CreatedAt);

    internal sealed class Handler(AppDbContext db, TimeProvider clock)
    {
        public async Task<Result<Response>> HandleAsync(Command command, CancellationToken ct)
        {
            var order = Order.Create(command.CustomerId, command.Items, clock.GetUtcNow());
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
            return Result.Success(new Response(order.Id, order.Total, order.CreatedAt));
        }
    }

    internal sealed class Validator : AbstractValidator<Command> { /* shared validator shape */ }
}
```

## Clean Architecture (CA)

### File Placement

| File | Purpose |
|------|---------|
| `Domain/Orders/Order.cs` | Entity (shared entity shape) |
| `Application/Orders/CreateOrder/CreateOrderCommand.cs` | Command + response records |
| `Application/Orders/CreateOrder/CreateOrderHandler.cs` | Mediator handler |
| `Application/Orders/CreateOrder/CreateOrderValidator.cs` | Validator (shared shape) |
| `Infrastructure/Persistence/Configurations/OrderConfiguration.cs` | EF configuration (shared shape) |
| `Api/Endpoints/OrderEndpoints.cs` | Endpoint group (shared shape) |
| `Tests/Application/Orders/CreateOrderTests.cs` | Integration tests (shared shape) |

### Deltas

- Commands implement `IRequest<Result<T>>` using **Mediator** (source-generated, MIT); handlers return `ValueTask`.
- Handlers depend on the `IAppDbContext` abstraction, not the concrete DbContext.
- Endpoints dispatch through `ISender` instead of injecting a handler directly.

```csharp
// Application/Orders/CreateOrder/CreateOrderCommand.cs
public record CreateOrderCommand(string CustomerId, List<OrderItemDto> Items) : IRequest<Result<CreateOrderResponse>>;
public record CreateOrderResponse(Guid Id, decimal Total, DateTimeOffset CreatedAt);

// Application/Orders/CreateOrder/CreateOrderHandler.cs
internal sealed class CreateOrderHandler(IAppDbContext db, TimeProvider clock)
    : IRequestHandler<CreateOrderCommand, Result<CreateOrderResponse>>
{
    public async ValueTask<Result<CreateOrderResponse>> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        var order = Order.Create(request.CustomerId, request.Items, clock.GetUtcNow());
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return new CreateOrderResponse(order.Id, order.Total, order.CreatedAt);
    }
}

// Api/Endpoints/OrderEndpoints.cs — endpoint dispatches via ISender
group.MapPost("/", async (CreateOrderCommand cmd, ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(cmd, ct);
    return result.IsSuccess
        ? TypedResults.Created($"/api/orders/{result.Value.Id}", result.Value)
        : result.ToProblemDetails();
})
.WithName("CreateOrder").Produces<CreateOrderResponse>(201)
.ProducesValidationProblem()
.AddEndpointFilter<ValidationFilter<CreateOrderCommand>>();
```

## DDD + Clean Architecture

Uses the CA file placement above, plus aggregates in `Domain/`.

### Deltas

- Domain logic lives in the aggregate root; the Application handler only
  orchestrates persistence and stays thin.
- Aggregates enforce invariants in factory methods and raise domain events.

```csharp
// Domain/Orders/Order.cs — aggregate root with invariant enforcement
public sealed class Order : AggregateRoot
{
    private readonly List<OrderItem> _items = [];
    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();
    public decimal Total { get; private set; }
    public OrderStatus Status { get; private set; }

    public static Order Place(string customerId, List<(Guid ProductId, int Qty, decimal Price)> items, DateTimeOffset now)
    {
        if (items.Count == 0) throw new DomainException("Order must have at least one item.");
        var order = new Order { Id = Guid.NewGuid(), Status = OrderStatus.Placed };
        foreach (var (productId, qty, price) in items)
            order._items.Add(OrderItem.Create(productId, qty, price));
        order.Total = order._items.Sum(i => i.LineTotal);
        order.AddDomainEvent(new OrderPlacedEvent(order.Id, customerId, order.Total, now));
        return order;
    }
}
```

## Modular Monolith

### File Placement

| File | Purpose |
|------|---------|
| `Modules/{Name}/{Name}Module.cs` | DI registration class |
| `Modules/{Name}/Features/{Operation}.cs` | Single-file feature (VSA shape, module DbContext) |
| `Modules/{Name}/Endpoints/{Name}Endpoints.cs` | Endpoint group (shared shape) |
| `Modules/{Name}/Persistence/{Name}DbContext.cs` | Module-scoped DbContext, isolated schema |
| `Modules/{Name}/Persistence/Configurations/*.cs` | EF configurations (shared shape) |
| `Tests/Modules/{Name}/*Tests.cs` | Integration tests (shared shape) |

### Deltas

- Each module owns its DbContext with an isolated schema; no cross-module
  project references — cross-module communication via integration events.
- Features follow the VSA single-file shape but inject the module DbContext
  and publish integration events through `IEventBus`.

```csharp
// Modules/Inventory/InventoryModule.cs — DI only, no endpoint wiring
public static class InventoryModule
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<InventoryDbContext>(o => o.UseNpgsql(config.GetConnectionString("Inventory")));
        return services;
    }
}

// Modules/Inventory/Persistence/InventoryDbContext.cs — isolated schema
internal sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<StockItem> StockItems => Set<StockItem>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("inventory");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);
    }
}

// Modules/Orders/Features/PlaceOrder.cs — feature delta: module DbContext + IEventBus
internal sealed class Handler(OrdersDbContext db, TimeProvider clock, IEventBus bus)
{
    public async Task<Response> HandleAsync(Command command, CancellationToken ct)
    {
        var order = Order.Place(command.CustomerId, command.Items, clock.GetUtcNow());
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(new OrderPlacedIntegrationEvent(order.Id, order.Total), ct);
        return new Response(order.Id, order.Total);
    }
}
```

## Anti-patterns

### Scaffolding Without Architecture

```csharp
// BAD — generating code without knowing if project uses VSA, CA, or DDD
public class CreateOrderHandler { /* random structure */ }

// GOOD — detect first: "I see feature folders, so I'll scaffold using VSA patterns."
public static class CreateOrder { /* VSA single-file feature */ }
```

### Feature Without Tests

Always scaffold feature + test as a single unit. `CreateOrder.cs` +
`CreateOrderTests.cs` are never generated separately.

### Entity Without EF Configuration

```csharp
// BAD — data annotations scattered in entity
public class Product { [Key] public Guid Id { get; set; } [MaxLength(200)] public string Name { get; set; } = ""; }

// GOOD — clean entity + separate IEntityTypeConfiguration<T>
public sealed class Product { /* no attributes */ }
internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product> { /* all EF config */ }
```

### Anemic DTOs That Mirror Entities 1:1

```csharp
// BAD — DTO mirrors entity with no purpose
public record ProductDto(Guid Id, string Name, string Sku, decimal Price, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);

// GOOD — response shaped for the consumer
public record ProductSummary(Guid Id, string Name, decimal Price);
```

## Decision Guide

| Scenario | Architecture | Scaffold Pattern |
|----------|-------------|-----------------|
| New CRUD endpoint | VSA | Single-file feature (Command + Handler + Validator + Response) |
| New business operation | CA | Command + Handler in Application/, Endpoint in Api/ |
| Complex domain logic | DDD | Aggregate method + thin Application handler + domain event |
| Feature in a module | Modular Monolith | Feature file in Modules/{Name}/Features/ with module DbContext |
| New entity | Any | Entity class + `IEntityTypeConfiguration<T>` + migration |
| New module | Modular Monolith | Module folder + DbContext + DI registration + integration events |
| Architecture unknown | Any | **Ask first** — run architecture-advisor questionnaire |
