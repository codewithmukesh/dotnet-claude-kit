---
name: ddd
description: >
  Domain-Driven Design tactical patterns for .NET applications. Covers aggregates,
  aggregate roots, value objects, domain events, domain services, strongly-typed IDs,
  and repository patterns for aggregate persistence.
  Load this skill when implementing DDD, working with aggregates, value objects,
  domain events, bounded contexts, or when the architecture-advisor recommends
  DDD + Clean Architecture. Pair with the clean-architecture skill.
---

# Domain-Driven Design (DDD)

## Core Principles

1. **Aggregates define consistency boundaries** — An aggregate is a cluster of entities and value objects treated as a single unit for data changes. All invariants within an aggregate are enforced in a single transaction. Cross-aggregate consistency is eventual.
2. **Value objects over primitives** — Replace primitive obsession with value objects. `Money`, `EmailAddress`, `OrderNumber` are not strings — they carry validation, equality, and behavior. Use C# records for immutable value objects.
3. **Domain events decouple side effects** — When something meaningful happens in the domain (OrderPlaced, PaymentReceived), raise a domain event. Side effects (send email, update read model, notify another aggregate) subscribe to these events. The aggregate stays focused on its own rules.
4. **Aggregate root is the sole entry point** — External code accesses an aggregate only through its root entity. Child entities are never loaded or modified independently. The root enforces all invariants for the entire aggregate.
5. **Repositories persist aggregates, not entities** — One repository per aggregate root. The repository loads and saves the entire aggregate as a unit. No repository for child entities.

## Patterns

### Aggregate Root

The aggregate root owns all access to its children and enforces invariants:

```csharp
// Domain/Orders/Order.cs
public sealed class Order : AggregateRoot
{
    private readonly List<OrderLine> _lines = [];

    private Order() { } // EF Core

    public OrderNumber Number { get; private set; } = null!;
    public CustomerId CustomerId { get; private set; }
    public Money Total { get; private set; } = Money.Zero("USD");
    public OrderStatus Status { get; private set; }
    public DateTimeOffset PlacedAt { get; private set; }
    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();

    public static Order Place(CustomerId customerId, OrderNumber number, DateTimeOffset now)
    {
        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customerId,
            Number = number,
            Status = OrderStatus.Placed,
            PlacedAt = now
        };

        order.RaiseDomainEvent(new OrderPlaced(order.Id, customerId, now));
        return order;
    }

    public Result AddLine(ProductId productId, int quantity, Money unitPrice)
    {
        if (Status is not OrderStatus.Placed)
            return Result.Failure("Cannot modify a confirmed or cancelled order");

        if (quantity <= 0)
            return Result.Failure("Quantity must be positive");

        var existing = _lines.FirstOrDefault(l => l.ProductId == productId);
        if (existing is not null)
        {
            existing.IncreaseQuantity(quantity);
        }
        else
        {
            _lines.Add(new OrderLine(productId, quantity, unitPrice));
        }

        RecalculateTotal();
        return Result.Success();
    }

    public Result Confirm()
    {
        if (Status is not OrderStatus.Placed)
            return Result.Failure("Only placed orders can be confirmed");

        if (_lines.Count == 0)
            return Result.Failure("Cannot confirm an order with no lines");

        Status = OrderStatus.Confirmed;
        RaiseDomainEvent(new OrderConfirmed(Id));
        return Result.Success();
    }

    private void RecalculateTotal()
    {
        Total = _lines.Aggregate(Money.Zero(Total.Currency), (sum, line) => sum + line.Subtotal);
    }
}
```

### Value Objects as Records

Use C# records for immutable value objects with structural equality:

```csharp
// Domain/Common/Money.cs
public sealed record Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    public static Money Zero(string currency) => new(0, currency);

    public static Money operator +(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            throw new InvalidOperationException($"Cannot add {left.Currency} and {right.Currency}");
        return new Money(left.Amount + right.Amount, left.Currency);
    }
}

// Domain/Common/EmailAddress.cs
public sealed record EmailAddress
{
    public string Value { get; }

    public EmailAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains('@'))
            throw new DomainException("Invalid email address");
        Value = value.Trim().ToLowerInvariant();
    }

    public override string ToString() => Value;
}
```

### Strongly-Typed IDs with EF Core Converters

Prevent mixing up GUIDs from different entities:

```csharp
// Domain/Common/StronglyTypedId.cs
public readonly record struct CustomerId(Guid Value)
{
    public static CustomerId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

public readonly record struct ProductId(Guid Value)
{
    public static ProductId New() => new(Guid.CreateVersion7());
}

public readonly record struct OrderNumber(string Value)
{
    public override string ToString() => Value;
}

// Infrastructure/Persistence/Configurations/OrderConfiguration.cs
public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);

        builder.Property(o => o.CustomerId)
            .HasConversion(id => id.Value, value => new CustomerId(value));

        builder.Property(o => o.Number)
            .HasConversion(n => n.Value, value => new OrderNumber(value))
            .HasMaxLength(50);

        builder.ComplexProperty(o => o.Total, money =>
        {
            money.Property(m => m.Amount).HasColumnName("Total").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3);
        });

        builder.HasMany(o => o.Lines).WithOne().HasForeignKey("OrderId");
        builder.Navigation(o => o.Lines).AutoInclude();
    }
}
```

### Domain Event Dispatching

Raise events in the aggregate, dispatch in SaveChangesAsync:

```csharp
// Domain/Common/AggregateRoot.cs
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}

public interface IDomainEvent : INotification
{
    DateTimeOffset OccurredAt => TimeProvider.System.GetUtcNow();
}

// Domain/Orders/Events/OrderPlaced.cs
public sealed record OrderPlaced(Guid OrderId, CustomerId CustomerId, DateTimeOffset PlacedAt) : IDomainEvent;

// Infrastructure/Persistence/AppDbContext.cs
public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    var aggregates = ChangeTracker.Entries<AggregateRoot>()
        .Where(e => e.Entity.DomainEvents.Count > 0)
        .Select(e => e.Entity)
        .ToList();

    var events = aggregates.SelectMany(a => a.DomainEvents).ToList();

    var result = await base.SaveChangesAsync(ct);

    foreach (var @event in events)
        await _publisher.Publish(@event, ct);

    foreach (var aggregate in aggregates)
        aggregate.ClearDomainEvents();

    return result;
}
```

### Domain Services

For logic that does not belong to a single aggregate:

```csharp
// Domain/Orders/Services/PricingService.cs
public sealed class PricingService(IDiscountPolicy discountPolicy)
{
    public Money CalculatePrice(ProductId productId, int quantity, Money unitPrice, CustomerId customerId)
    {
        var subtotal = new Money(unitPrice.Amount * quantity, unitPrice.Currency);
        var discount = discountPolicy.GetDiscount(customerId, productId, quantity);
        return new Money(subtotal.Amount * (1 - discount), subtotal.Currency);
    }
}

// Domain/Orders/Interfaces/IDiscountPolicy.cs
public interface IDiscountPolicy
{
    decimal GetDiscount(CustomerId customerId, ProductId productId, int quantity);
}
```

## Anti-patterns

### Oversized Aggregates

```csharp
// BAD — Customer aggregate owns everything the customer touches
public class Customer : AggregateRoot
{
    public List<Order> Orders { get; } = [];        // should be separate aggregate
    public List<Payment> Payments { get; } = [];     // should be separate aggregate
    public List<Address> Addresses { get; } = [];    // might be OK as child
    public ShoppingCart Cart { get; set; }            // should be separate aggregate
}

// GOOD — small, focused aggregates linked by ID
public class Customer : AggregateRoot
{
    public CustomerName Name { get; private set; }
    public EmailAddress Email { get; private set; }
    // Orders, Payments, Cart are separate aggregates referencing CustomerId
}
```

### Domain Events for Intra-Aggregate Logic

```csharp
// BAD — using events for logic within the same aggregate
order.RaiseDomainEvent(new OrderLineAdded(line));
// Then a handler recalculates the total... but you're in the same aggregate!

// GOOD — just call the method directly within the aggregate
_lines.Add(line);
RecalculateTotal();  // private method, no event needed
```

### Value Objects with Identity

```csharp
// BAD — value object with an Id (it's an entity then!)
public record Address
{
    public Guid Id { get; init; }  // value objects don't have identity
    public string Street { get; init; }
}

// GOOD — value objects are defined by their attributes, not an Id
public record Address(string Street, string City, string PostalCode, string Country);
```

### Anemic Aggregates

```csharp
// BAD — aggregate is just a data bag, service does all the work
public class Order : AggregateRoot
{
    public OrderStatus Status { get; set; }  // public setter!
    public List<OrderLine> Lines { get; set; } = [];
}

// Service directly manipulates order state
order.Status = OrderStatus.Confirmed;  // no invariant check!
order.Lines.Add(newLine);              // no validation!

// GOOD — aggregate encapsulates rules (see Aggregate Root pattern above)
order.Confirm();  // validates status, raises event
order.AddLine(productId, quantity, unitPrice);  // validates, recalculates
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| When to use DDD | Complex domain with business rules that go beyond CRUD |
| When to use value objects | Any concept with validation rules or equality based on attributes, not identity |
| Aggregate size | Keep small — typically 1 root entity + 0-3 child entities. Load the whole aggregate every time |
| Domain events vs integration events | Domain events: within bounded context, same transaction. Integration events: cross-context, via message bus |
| Strongly-typed IDs | Always for aggregate root IDs that cross boundaries. Optional for child entity IDs |
| When NOT to use DDD | Simple CRUD, settings, audit logs, read models — use plain entities |
| Repository vs DbContext | Repository per aggregate root for complex aggregates; IAppDbContext for simpler queries |
| Domain services | Only when logic requires multiple aggregates or external data the aggregate should not know about |
