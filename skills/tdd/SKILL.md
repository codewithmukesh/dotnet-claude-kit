---
name: tdd
description: >
  Guided test-driven development workflow for .NET 10 using xUnit v3,
  WebApplicationFactory, Testcontainers, and Verify snapshots. Follows the
  strict red-green-refactor cycle. Use when: "TDD", "test-driven", "let's TDD
  this", "red green refactor", "write the test first", or when building a
  feature with clear acceptance criteria.
---

# /tdd -- Red-Green-Refactor for .NET

## What

Guides a strict test-driven development cycle for .NET features. Instead of
writing implementation first and bolting on tests after, this command flips the
order: write a failing test that defines the desired behavior, implement the
minimum code to make it pass, then refactor with confidence.

Every cycle uses the .NET testing stack:
- **xUnit v3** -- Test framework with `[Fact]` and `[Theory]`
- **WebApplicationFactory** -- Integration tests against the real HTTP pipeline
- **Testcontainers** -- Real databases (PostgreSQL, SQL Server) in tests
- **Verify** -- Snapshot testing for complex response structures
- **FakeTimeProvider** -- Deterministic time in tests

## When

- User says "TDD", "test-driven", "let's TDD this", "write the test first"
- Building a new feature with clear acceptance criteria
- Fixing a bug (write a test that reproduces the bug first, then fix)
- Adding behavior to an existing feature (test the new behavior first)
- Any time the user wants proof that code works before it ships

**Skip TDD for:** Trivial config changes, scaffolding without logic, documentation.

## How

### Cycle: Red -> Green -> Refactor

Each feature goes through one or more TDD cycles. A cycle covers one discrete behavior.

#### Step 1: Red -- Write the Failing Test

Write a test that describes the desired behavior. The test MUST fail because the
implementation does not exist yet.

```csharp
[Fact]
public async Task CreateOrder_WithValidItems_Returns201WithOrderId()
{
    // Arrange
    var client = _factory.CreateClient();
    var request = new CreateOrderRequest([
        new OrderItemRequest("SKU-001", 2, 29.99m)
    ]);

    // Act
    var response = await client.PostAsJsonAsync("/api/orders", request);

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var result = await response.Content.ReadFromJsonAsync<CreateOrderResponse>();
    result!.OrderId.Should().NotBeEmpty();
}
```

Run the test and confirm it fails:
```bash
dotnet test --filter "CreateOrder_WithValidItems_Returns201WithOrderId"
```

If the test passes without implementation, the test is not testing what you think.
Rewrite it.

#### Step 2: Green -- Minimal Implementation

Write the minimum code to make the test pass. Do not add features, optimizations,
or edge case handling. The goal is a green test, nothing more.

- Create the endpoint, handler, request/response types, and EF config as needed
- Use the simplest logic that satisfies the test assertion
- Do not refactor yet -- ugly passing code is fine at this stage

Run the test and confirm it passes:
```bash
dotnet test --filter "CreateOrder_WithValidItems_Returns201WithOrderId"
```

#### Step 3: Refactor -- Clean Up with Confidence

Now that the test is green, refactor freely:
- Extract methods, rename variables, improve structure
- Apply modern C# patterns (primary constructors, records, collection expressions)
- Add validation, error handling, and edge cases (with new tests for each)
- Run the full test suite after each refactor step to catch regressions

```bash
dotnet test
```

If any test goes red during refactoring, undo the last change and try a smaller step.

### Multi-Cycle Features

Most features require multiple TDD cycles. Plan the cycles upfront:

```
Feature: Order Management

Cycle 1: Create order with valid items -> 201
Cycle 2: Create order with empty items -> 400 validation error
Cycle 3: Create order with invalid SKU -> 400 with specific error
Cycle 4: Get order by ID -> 200 with full order details
Cycle 5: Get order that does not exist -> 404
```

Each cycle adds one behavior. Never combine multiple behaviors in a single cycle.

### Test Infrastructure Setup

If the project lacks test infrastructure, set it up before the first cycle:

1. Create test project with xUnit v3 and required packages
2. Configure `WebApplicationFactory` with Testcontainers for the real database
3. Add a shared fixture to avoid spinning up containers per test class
4. Verify the infrastructure with a simple health-check test

## Example

```
User: /tdd Let's TDD a product search endpoint

Claude: I'll plan the TDD cycles for product search:

Cycle 1: Search with matching term returns matching products
Cycle 2: Search with no matches returns empty list
Cycle 3: Search with pagination returns correct page
Cycle 4: Search with filters narrows results

Starting Cycle 1: Red phase
Writing a failing integration test for basic product search...

[writes test, runs it, confirms failure]

Cycle 1: Green phase
Implementing the minimum to make the search test pass...

[implements, runs test, confirms pass]

Cycle 1: Refactor phase
The implementation works but the query could use a projection
instead of loading full entities. Refactoring...

[refactors, runs full suite, all green]

Cycle 1 complete. Moving to Cycle 2...
```

## Related

- `/verify` -- Run full verification after completing all TDD cycles
- `/scaffold` -- Generate initial feature structure that tests will drive
