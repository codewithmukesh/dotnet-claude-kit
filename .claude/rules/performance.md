---
alwaysApply: true
description: >
  Enforces performance best practices for .NET applications including async
  patterns, caching, resource management, and hot-path optimizations.
---

# Performance Rules

## Async Patterns

- **Always propagate `CancellationToken` through the call chain.** Dropped tokens mean cancelled requests continue burning server resources.

```csharp
// DO
public Task<Order?> GetOrderAsync(Guid id, CancellationToken ct) =>
    db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);

// DON'T — token silently ignored
public Task<Order?> GetOrderAsync(Guid id, CancellationToken ct) =>
    db.Orders.FirstOrDefaultAsync(o => o.Id == id);
```

- **Async all the way — no `.Result` or `.Wait()`.** Synchronously blocking on async code causes thread pool starvation and deadlocks. The only acceptable sync-over-async location is `Program.cs` top-level statements.

## Time and Clock

- **`TimeProvider` over `DateTime.Now` / `DateTime.UtcNow`.** `TimeProvider` is injectable and testable. `DateTime.Now` is a static dependency that makes time-sensitive logic untestable.

```csharp
// DO
public sealed class AuditService(TimeProvider clock)
{
    public DateTimeOffset Now => clock.GetUtcNow();
}

// DON'T
var now = DateTime.UtcNow;
```

## Resource Management

- **`IHttpClientFactory` over `new HttpClient()`.** Direct instantiation causes socket exhaustion under load. The factory manages connection pooling and DNS rotation.
- **Use `ArrayPool<T>` / `MemoryPool<T>` for buffer-heavy operations.** Renting from a pool avoids GC pressure from frequent large allocations.

## Caching

- **`HybridCache` over `IMemoryCache` / `IDistributedCache`.** `HybridCache` provides stampede protection, L1+L2 caching, and tag-based invalidation out of the box.

```csharp
// DO
var order = await cache.GetOrCreateAsync(
    $"order:{id}",
    async ct => await db.Orders.FindAsync([id], ct),
    cancellationToken: ct);

// DON'T — manual cache-aside with no stampede protection
if (!memoryCache.TryGetValue(key, out var order))
{
    order = await db.Orders.FindAsync(id);
    memoryCache.Set(key, order, TimeSpan.FromMinutes(5));
}
```

## EF Core and Hot Paths

- **Use compiled queries for hot-path EF Core queries.** Compiled queries skip expression tree translation on every call.

```csharp
private static readonly Func<AppDbContext, Guid, CancellationToken, Task<Order?>> GetById =
    EF.CompileAsyncQuery((AppDbContext db, Guid id, CancellationToken ct) =>
        db.Orders.FirstOrDefault(o => o.Id == id));
```

- **Prefer `ValueTask<T>` over `Task<T>` for high-throughput paths that often complete synchronously.** Avoids `Task` allocation when the result is already available. Use `Task` for general-purpose code where simplicity matters more.
