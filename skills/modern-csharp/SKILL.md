---
name: modern-csharp
description: >
  Modern C# language features for .NET 10 and C# 14. Covers primary constructors,
  collection expressions, the field keyword, extension members, records, pattern
  matching, spans, and raw string literals.
  Load this skill when writing any new C# code, reviewing existing code for
  modernization, using "modern C#", "C# 14", "primary constructor", "collection
  expression", "records", "pattern matching", "span", "field keyword", or
  "extension members". Always loaded as the baseline for all agents.
---

# Modern C# (C# 14 / .NET 10)

## Core Principles

1. **Use the newest stable features** — C# 14 is the target. Prefer language-level constructs over library workarounds.
2. **Readability over cleverness** — Pattern matching and expression-bodied members improve readability when used appropriately; deeply nested patterns do not.
3. **Value types where possible** — Prefer `record struct`, `Span<T>`, and stack allocation to reduce GC pressure.
4. **Immutability by default** — Use `record`, `readonly`, `init`, and `required` to make illegal states unrepresentable.

## Patterns

### Primary Constructors (Classes and Structs)

Use primary constructors to eliminate boilerplate field assignments. The parameters are available throughout the class body.

```csharp
// GOOD — primary constructor with DI
public class OrderService(IOrderRepository repository, TimeProvider clock)
{
    public async Task<Result<Order>> CreateAsync(CreateOrderRequest request)
    {
        var order = Order.Create(request, clock.GetUtcNow());
        await repository.AddAsync(order);
        return Result.Success(order);
    }
}
```

When you need validation or transformation on constructor parameters, use a regular constructor instead.

### Collection Expressions

Use `[]` syntax for creating collections. The compiler picks the optimal backing type.

```csharp
// GOOD — collection expressions
int[] numbers = [1, 2, 3, 4, 5];
List<string> names = ["Alice", "Bob"];
ReadOnlySpan<byte> bytes = [0x00, 0xFF];
ImmutableArray<int> immutable = [10, 20, 30];

// Spread operator
int[] combined = [..first, ..second, 99];

// Empty collection
List<Order> orders = [];
```

### The `field` Keyword (C# 14)

Access the auto-generated backing field in property accessors without declaring it manually.

```csharp
// GOOD — field keyword for validation in auto-property
public class Product
{
    public string Name
    {
        get => field;
        set => field = value?.Trim() ?? throw new ArgumentNullException(nameof(value));
    }

    public decimal Price
    {
        get => field;
        set => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
}
```

### Extension Members (C# 14)

Extension members replace static extension method classes with a cleaner syntax.

```csharp
// GOOD — extension members (C# 14)
public extension OrderExtensions for Order
{
    public decimal TotalWithTax => Total * 1.2m;

    public bool IsHighValue => Total > 1000m;

    public string ToSummary() => $"Order #{Id}: {Total:C} ({Items.Count} items)";
}
```

### Records

Use records for DTOs, value objects, and any type where equality is based on data rather than identity.

```csharp
// GOOD — record for API request/response
public record CreateOrderRequest(string CustomerId, List<OrderItem> Items);

// GOOD — record struct for small value types (stack-allocated)
public readonly record struct Money(decimal Amount, string Currency);

// GOOD — record with validation via required + init
public record Address
{
    public required string Street { get; init; }
    public required string City { get; init; }
    public required string PostalCode { get; init; }
    public string? State { get; init; }
}
```

### Pattern Matching

Use pattern matching for type checks, deconstruction, and conditional logic.

```csharp
// GOOD — switch expression with patterns
public static string Classify(Order order) => order switch
{
    { Total: 0 } => "Empty",
    { Total: > 1000, Items.Count: > 10 } => "Bulk",
    { Total: > 500 } => "Premium",
    { Status: OrderStatus.Cancelled } => "Cancelled",
    _ => "Standard"
};

// GOOD — list patterns
public static string DescribeItems(int[] items) => items switch
{
    [] => "No items",
    [var single] => $"Single item: {single}",
    [var first, .., var last] => $"From {first} to {last}",
};

// GOOD — is pattern for null/type checking
if (result is { IsSuccess: true, Value: var order })
{
    await ProcessOrder(order);
}
```

### Spans and Memory

Use `Span<T>` and `ReadOnlySpan<T>` for slicing without allocation.

```csharp
// GOOD — span for parsing without allocation
public static bool TryParseOrderId(ReadOnlySpan<char> input, out int id)
{
    var trimmed = input.Trim();
    if (trimmed.StartsWith("ORD-"))
    {
        return int.TryParse(trimmed[4..], out id);
    }
    id = 0;
    return false;
}
```

### Raw String Literals

Use raw string literals for multi-line strings and content with special characters.

```csharp
// GOOD — raw string for SQL, JSON, XML
var sql = """
    SELECT o.Id, o.Total, c.Name
    FROM Orders o
    JOIN Customers c ON o.CustomerId = c.Id
    WHERE o.Status = @status
    ORDER BY o.CreatedAt DESC
    """;

// Interpolated raw strings
var json = $$"""
    {
        "orderId": "{{orderId}}",
        "total": {{total}}
    }
    """;
```

### `required` Members

Use `required` to enforce initialization at construction time.

```csharp
// GOOD — required with class
public class AppSettings
{
    public required string ConnectionString { get; init; }
    public required string JwtSecret { get; init; }
    public int MaxRetries { get; init; } = 3;
}

// Caller must provide required properties
var settings = new AppSettings
{
    ConnectionString = "Server=...",
    JwtSecret = "secret"
};
```

## Anti-patterns

### Don't Use Obsolete Patterns When Modern Alternatives Exist

```csharp
// BAD — manual backing field when field keyword works
private string _name;
public string Name
{
    get => _name;
    set => _name = value ?? throw new ArgumentNullException();
}

// BAD — old-style collection initialization
var list = new List<int>() { 1, 2, 3 };

// BAD — Tuple instead of record for domain types
(string Name, decimal Price) product = ("Widget", 9.99m);
// GOOD — record
public record Product(string Name, decimal Price);
```

### Don't Over-pattern-match

```csharp
// BAD — deeply nested pattern that's hard to read
if (order is { Customer: { Address: { Country: { Code: "US" } } } })

// GOOD — extract to a clear method or use sequential checks
if (order.Customer.Address.Country.Code == "US")
```

### Don't Use `var` When the Type Is Not Obvious

```csharp
// BAD — what type is this?
var result = Process(order);

// GOOD — explicit type when not obvious
Result<Order> result = Process(order);
// Also GOOD — var is fine when type is apparent
var orders = new List<Order>();
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| DTO / API contract | `record` (reference type) |
| Small value object (2-3 fields) | `readonly record struct` |
| Service with DI | Primary constructor |
| Collection creation | Collection expression `[]` |
| Property with validation | `field` keyword |
| Multi-line string (SQL, JSON) | Raw string literal `"""` |
| Slicing strings/arrays | `Span<T>` |
| Type checking + extraction | Pattern matching with `is` / `switch` |
| Enforced initialization | `required` modifier |
| Adding methods to external types | Extension members |
