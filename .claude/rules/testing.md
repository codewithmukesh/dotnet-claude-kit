---
alwaysApply: true
description: >
  Enforces testing strategy, patterns, and naming conventions for .NET
  projects using xUnit v3, WebApplicationFactory, and Testcontainers.
---

# Testing Rules

## Strategy

- **Integration tests first.** Use `WebApplicationFactory` + Testcontainers to test real HTTP pipelines against real databases. Integration tests catch the bugs that unit tests miss — serialization, middleware, DI wiring, and query behavior.
- **No in-memory database for testing.** `UseInMemoryDatabase` has different behavior from real providers (no constraints, no transactions, no SQL translation). Use Testcontainers to spin up the real database engine.

```csharp
// DO — real PostgreSQL via Testcontainers
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().Build();
    public string ConnectionString => _container.GetConnectionString();
    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

// DON'T
options.UseInMemoryDatabase("TestDb");
```

## Test Structure

- **AAA pattern with clear separation.** Arrange, Act, Assert — separated by blank lines. Each section should be immediately identifiable.

```csharp
[Fact]
public async Task CreateOrder_ValidRequest_ReturnsCreated()
{
    // Arrange
    var client = _factory.CreateClient();
    var request = new CreateOrderRequest("SKU-1", Quantity: 2);

    // Act
    var response = await client.PostAsJsonAsync("/orders", request);

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Created);
}
```

- **One assertion concept per test.** You may assert multiple properties of the same result, but do not test two separate behaviors in one test. Separate behaviors need separate tests so failures are specific.

## Naming

- **Test naming: `MethodName_Scenario_ExpectedResult`.** Clear, searchable, and self-documenting. The test name is the specification.

```
GetOrderAsync_OrderDoesNotExist_ReturnsNull
CreateOrder_DuplicateSku_ThrowsConflictException
```

## Fixtures and Mocking

- **Shared fixtures for expensive setup.** Database containers, HTTP servers, and message brokers should be shared across tests using `IClassFixture<T>` or `ICollectionFixture<T>`. Starting a container per test is wasteful.
- **No mocking frameworks for things you own.** If you control the code, use a real or test implementation. Mocking your own interfaces couples tests to implementation details and makes refactoring painful. Reserve mocks for third-party boundaries you cannot control.

## Behavior Over Implementation

- **Test behavior, not implementation details.** Assert on the observable outcome (HTTP response, database state, published event), not on which internal methods were called. Tests coupled to internals break on every refactor.
