---
name: api-versioning
description: >
  API versioning strategies for ASP.NET Core. Covers Asp.Versioning library,
  URL segment, header, and query string strategies, version deprecation, and
  OpenAPI integration.
  Load this skill when adding versioning to an API, evolving an API with breaking
  changes, or when the user mentions "API version", "versioning", "v1/v2",
  "Asp.Versioning", "deprecation", "breaking change", or "backward compatibility".
---

# API Versioning

## Core Principles

1. **Version from day one** — Adding versioning later is painful. Start with a version in the URL even if you only have v1.
2. **URL segment versioning is the default** — `/api/v1/orders` is the most discoverable and cache-friendly strategy.
3. **Never break existing versions** — Add a new version for breaking changes. Deprecate the old version with a timeline.
4. **Version the API, not individual endpoints** — All endpoints in a version group share the same version number.

## Patterns

### Setup with Asp.Versioning

```csharp
// Program.cs
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});
```

### URL Segment Versioning (Recommended)

```csharp
var v1 = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1, 0))
    .Build();

var v2 = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(2, 0))
    .Build();

app.MapGroup("/api/v{version:apiVersion}/orders")
    .WithApiVersionSet(v1)
    .WithTags("Orders")
    .MapOrderEndpointsV1();

app.MapGroup("/api/v{version:apiVersion}/orders")
    .WithApiVersionSet(v2)
    .WithTags("Orders")
    .MapOrderEndpointsV2();
```

### Header Versioning (Alternative)

```csharp
options.ApiVersionReader = new HeaderApiVersionReader("X-Api-Version");

// Client sends: X-Api-Version: 2.0
```

### Deprecating a Version

```csharp
var v1 = app.NewApiVersionSet()
    .HasDeprecatedApiVersion(new ApiVersion(1, 0))
    .HasApiVersion(new ApiVersion(2, 0))
    .Build();

// Response headers will include: api-deprecated-versions: 1.0
```

### Version-Specific Endpoint Groups

```csharp
public static class OrderEndpointsV1
{
    public static RouteGroupBuilder MapOrderEndpointsV1(this RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", GetOrderV1);
        group.MapPost("/", CreateOrderV1);
        return group;
    }

    private static async Task<Results<Ok<OrderResponseV1>, NotFound>> GetOrderV1(
        Guid id, ISender sender, CancellationToken ct)
    {
        // V1 response shape
        var result = await sender.Send(new GetOrder.Query(id), ct);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value.ToV1())
            : TypedResults.NotFound();
    }
}

public static class OrderEndpointsV2
{
    public static RouteGroupBuilder MapOrderEndpointsV2(this RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", GetOrderV2);
        group.MapPost("/", CreateOrderV2);
        return group;
    }

    private static async Task<Results<Ok<OrderResponseV2>, NotFound>> GetOrderV2(
        Guid id, ISender sender, CancellationToken ct)
    {
        // V2 response shape — includes new fields
        var result = await sender.Send(new GetOrder.Query(id), ct);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value.ToV2())
            : TypedResults.NotFound();
    }
}
```

## Anti-patterns

### Don't Version Individual Endpoints

```csharp
// BAD — inconsistent versioning within a group
app.MapGet("/api/v1/orders", ListOrdersV1);
app.MapGet("/api/v2/orders/{id}", GetOrderV2); // V2 only for this endpoint?

// GOOD — version the entire group
app.MapGroup("/api/v1/orders").MapOrderEndpointsV1();
app.MapGroup("/api/v2/orders").MapOrderEndpointsV2();
```

### Don't Use Query String Versioning as Default

```csharp
// BAD for REST APIs — version hidden in query string, not cache-friendly
GET /api/orders?api-version=2.0

// GOOD — version in URL, discoverable and cacheable
GET /api/v2/orders
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| New public API | URL segment versioning from day one |
| Internal API between services | Header versioning (cleaner URLs) |
| Breaking response shape change | New version |
| Adding new optional fields | Same version (backwards compatible) |
| Deprecating a version | Mark deprecated, set sunset date, document migration path |
