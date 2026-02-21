---
name: caching
description: >
  Caching strategies for .NET 10 applications. Covers HybridCache (the default),
  output caching, response caching, and distributed cache patterns.
  Load this skill when implementing caching, optimizing read performance, reducing
  database load, or when the user mentions "cache", "HybridCache", "Redis",
  "output cache", "response cache", "distributed cache", "IMemoryCache",
  "cache invalidation", "stampede protection", or "cache-aside".
---

# Caching

## Core Principles

1. **HybridCache is the default** — .NET 9+ introduced `HybridCache` as the unified caching abstraction. It combines in-memory (L1) and distributed (L2) caching with stampede protection. See ADR-004.
2. **Cache reads, not writes** — Cache GET operations. Invalidate on mutations. Never cache POST/PUT/DELETE responses.
3. **Output caching for entire responses** — When the full HTTP response can be cached (public APIs, static data), use output caching middleware.
4. **Set explicit TTLs** — Every cached item needs an expiration. No unbounded caches.

## Patterns

### HybridCache (Recommended Default)

```csharp
// Program.cs
builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(2)
    };
});

// Optional: Add Redis as the L2 distributed cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
});
```

```csharp
// Usage in a handler
public class GetProduct
{
    public record Query(Guid Id);
    public record Response(Guid Id, string Name, decimal Price);

    internal class Handler(AppDbContext db, HybridCache cache)
    {
        public async Task<Response?> Handle(Query query, CancellationToken ct)
        {
            return await cache.GetOrCreateAsync(
                $"products:{query.Id}",
                async token => await db.Products
                    .Where(p => p.Id == query.Id)
                    .Select(p => new Response(p.Id, p.Name, p.Price))
                    .FirstOrDefaultAsync(token),
                new HybridCacheEntryOptions
                {
                    Expiration = TimeSpan.FromMinutes(10)
                },
                cancellationToken: ct);
        }
    }
}
```

### Cache Invalidation

```csharp
// Invalidate on mutation
public class UpdateProduct
{
    internal class Handler(AppDbContext db, HybridCache cache)
    {
        public async Task<Result> Handle(Command command, CancellationToken ct)
        {
            var product = await db.Products.FindAsync([command.Id], ct);
            if (product is null) return Result.Failure("Product not found");

            product.Update(command.Name, command.Price);
            await db.SaveChangesAsync(ct);

            // Invalidate the cached entry
            await cache.RemoveAsync($"products:{command.Id}", ct);

            return Result.Success();
        }
    }
}
```

### Output Caching (Full Response Caching)

```csharp
// Program.cs
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(b => b.NoCache()); // Don't cache by default

    options.AddPolicy("ProductList", b => b
        .Expire(TimeSpan.FromMinutes(5))
        .Tag("products"));

    options.AddPolicy("ProductById", b => b
        .Expire(TimeSpan.FromMinutes(10))
        .SetVaryByRouteValue("id")
        .Tag("products"));
});

app.UseOutputCache();

// Apply to endpoints
group.MapGet("/", ListProducts).CacheOutput("ProductList");
group.MapGet("/{id:guid}", GetProduct).CacheOutput("ProductById");

// Invalidate by tag on mutations
group.MapPut("/{id:guid}", async (Guid id, UpdateProductRequest request,
    IOutputCacheStore store, CancellationToken ct) =>
{
    // ... update logic ...
    await store.EvictByTagAsync("products", ct);
    return TypedResults.NoContent();
});
```

### Cache-Aside Pattern (Manual)

When you need more control than HybridCache provides.

```csharp
public class ProductCache(IDistributedCache cache, AppDbContext db)
{
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
        SlidingExpiration = TimeSpan.FromMinutes(2)
    };

    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var key = $"products:{id}";
        var cached = await cache.GetStringAsync(key, ct);

        if (cached is not null)
            return JsonSerializer.Deserialize<Product>(cached);

        var product = await db.Products.FindAsync([id], ct);
        if (product is not null)
        {
            await cache.SetStringAsync(key,
                JsonSerializer.Serialize(product), CacheOptions, ct);
        }

        return product;
    }
}
```

## Anti-patterns

### Don't Cache Without Expiration

```csharp
// BAD — cache lives forever, stale data guaranteed
await cache.SetStringAsync(key, value);

// GOOD — always set TTL
await cache.SetStringAsync(key, value, new DistributedCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
});
```

### Don't Cache Mutable User-Specific Data

```csharp
// BAD — caching user's cart with a global key
await cache.GetOrCreateAsync("shopping-cart", ...);

// GOOD — include user ID in key
await cache.GetOrCreateAsync($"shopping-cart:{userId}", ...);
```

### Don't Build Your Own Stampede Protection

```csharp
// BAD — manual lock to prevent cache stampede
private static readonly SemaphoreSlim Lock = new(1, 1);
await Lock.WaitAsync();
try { /* check cache, populate if missing */ }
finally { Lock.Release(); }

// GOOD — HybridCache has built-in stampede protection
await hybridCache.GetOrCreateAsync(key, factory);
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| General data caching | HybridCache (`GetOrCreateAsync`) |
| Full HTTP response | Output caching with `.CacheOutput()` |
| Frequently read, rarely written | HybridCache with longer TTL |
| User-specific data | HybridCache with user-scoped key |
| Cache invalidation on write | `cache.RemoveAsync()` or output cache tags |
| Distributed deployment | HybridCache + Redis L2 backend |
| Single-server deployment | HybridCache with in-memory only |
