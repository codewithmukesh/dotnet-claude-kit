---
alwaysApply: true
description: >
  Enforces dependency direction, feature organization, and module boundary
  rules for .NET solution architecture.
---

# Architecture Rules

## Ask First, Recommend Second

- **Never assume an architecture — use the architecture-advisor skill.** Every project has different constraints. Ask about team size, domain complexity, and deployment model before recommending Clean Architecture, VSA, DDD, or Modular Monolith.

## Data Access

- **No repository pattern over EF Core.** `DbContext` is already a Unit of Work + Repository. Wrapping it adds indirection with no value and prevents access to EF Core features like change tracking, batching, and compiled queries.

```csharp
// DO — inject DbContext directly
public sealed class OrderService(AppDbContext db)
{
    public Task<Order?> GetAsync(Guid id, CancellationToken ct) =>
        db.Orders.FindAsync([id], ct).AsTask();
}

// DON'T — generic repository wrapping EF
public interface IRepository<T> { Task<T?> GetByIdAsync(Guid id); }
```

## Project Organization

- **Feature folders over layer folders.** Vertical slices keep related code together, reducing the number of files you touch per feature.

```
# DO                          # DON'T
Features/                     Controllers/
  Orders/                       OrdersController.cs
    CreateOrder.cs              ProductsController.cs
    GetOrder.cs               Services/
  Products/                     OrderService.cs
    CreateProduct.cs            ProductService.cs
```

- **Dependency direction is inward.** Domain depends on nothing. Application depends on Domain. Infrastructure depends on Application. Presentation depends on Application. Never reverse these arrows.
- **Module boundaries enforced through project references.** If two modules should not couple, they must not have a project reference. Use integration events or a shared contracts project for cross-module communication.

## Shared Kernel

- **Shared kernel contains only contracts, never business logic.** Shared projects hold interfaces, DTOs, and integration event definitions. Domain logic belongs in the owning module. Leaking logic into shared projects creates hidden coupling.

```csharp
// DO — shared kernel has contracts
public interface IOrderPlaced
{
    Guid OrderId { get; }
    DateTimeOffset OccurredAt { get; }
}

// DON'T — shared kernel has domain logic
public static class PricingCalculator { /* business rules */ }
```
