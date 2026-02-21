---
name: messaging
description: >
  Asynchronous messaging patterns for .NET applications. Covers MassTransit,
  outbox pattern, saga and choreography, and broker configuration for RabbitMQ
  and Azure Service Bus.
  Load this skill when implementing event-driven communication, background
  processing, module-to-module messaging, or when the user mentions "MassTransit",
  "message bus", "RabbitMQ", "Azure Service Bus", "event", "publish", "consumer",
  "outbox", "saga", "integration event", "queue", or "pub/sub".
---

# Messaging

## Core Principles

1. **MassTransit is the default abstraction** — It provides a broker-agnostic API, built-in outbox, saga support, and excellent DI integration. Don't build your own message bus.
2. **Outbox pattern for reliability** — Always use the transactional outbox to ensure messages are published only when the database transaction succeeds.
3. **Choreography for simple flows, saga for complex** — If a workflow has 2-3 steps, use event choreography. If it has compensating actions or complex state, use a saga.
4. **Messages are contracts** — Put message types in a shared contracts project. Keep them as simple records with primitive types.

## Patterns

### MassTransit Setup

```csharp
// Program.cs
builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    x.AddConsumers(typeof(Program).Assembly);

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("RabbitMq"));
        cfg.ConfigureEndpoints(context);
    });
});
```

### Publishing Events

```csharp
// Message contract (in shared Contracts project)
public record OrderCreated(Guid OrderId, string CustomerId, decimal Total, DateTimeOffset CreatedAt);

// Publishing from a handler
public class CreateOrder
{
    internal class Handler(AppDbContext db, IPublishEndpoint publisher, TimeProvider clock)
    {
        public async Task<Result<OrderResponse>> Handle(Command command, CancellationToken ct)
        {
            var order = Order.Create(command.CustomerId, command.Items, clock.GetUtcNow());
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);

            await publisher.Publish(new OrderCreated(
                order.Id, order.CustomerId, order.Total, order.CreatedAt), ct);

            return Result.Success(new OrderResponse(order.Id, order.Total));
        }
    }
}
```

### Consuming Events

```csharp
public class OrderCreatedConsumer(ILogger<OrderCreatedConsumer> logger, AppDbContext db)
    : IConsumer<OrderCreated>
{
    public async Task Consume(ConsumeContext<OrderCreated> context)
    {
        var message = context.Message;
        logger.LogInformation("Processing OrderCreated: {OrderId}", message.OrderId);

        // Handle the event — e.g., send confirmation email, update inventory
        var notification = new OrderNotification(message.OrderId, message.CustomerId);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
```

### Transactional Outbox

Ensures messages are only published if the database transaction succeeds.

```csharp
builder.Services.AddMassTransit(x =>
{
    x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    {
        o.UsePostgres(); // or o.UseSqlServer();
        o.UseBusOutbox();
    });

    // ... rest of configuration
});

// In DbContext — add outbox tables
public class AppDbContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
```

### Saga (Stateful Orchestration)

```csharp
public class OrderSaga : MassTransitStateMachine<OrderSagaState>
{
    public OrderSaga()
    {
        InstanceState(x => x.CurrentState);

        Event(() => OrderCreated, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => PaymentCompleted, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => PaymentFailed, x => x.CorrelateById(m => m.Message.OrderId));

        Initially(
            When(OrderCreated)
                .Then(context => context.Saga.CustomerId = context.Message.CustomerId)
                .PublishAsync(context => context.Init<ProcessPayment>(new
                {
                    context.Message.OrderId,
                    context.Message.Total
                }))
                .TransitionTo(AwaitingPayment));

        During(AwaitingPayment,
            When(PaymentCompleted)
                .TransitionTo(Completed),
            When(PaymentFailed)
                .PublishAsync(context => context.Init<CancelOrder>(new
                {
                    context.Saga.CorrelationId
                }))
                .TransitionTo(Cancelled));
    }

    public State AwaitingPayment { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Cancelled { get; private set; } = null!;

    public Event<OrderCreated> OrderCreated { get; private set; } = null!;
    public Event<PaymentCompleted> PaymentCompleted { get; private set; } = null!;
    public Event<PaymentFailed> PaymentFailed { get; private set; } = null!;
}

public class OrderSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = null!;
    public string? CustomerId { get; set; }
}
```

## Anti-patterns

### Don't Publish Events Without Outbox

```csharp
// BAD — if SaveChanges succeeds but Publish fails, data is inconsistent
await db.SaveChangesAsync(ct);
await publisher.Publish(new OrderCreated(...), ct);

// GOOD — use transactional outbox (messages are in the same transaction)
// Configure AddEntityFrameworkOutbox<AppDbContext>() and it handles this automatically
```

### Don't Put Complex Logic in Message Contracts

```csharp
// BAD — behavior in a message
public record OrderCreated(Guid OrderId)
{
    public decimal CalculateShipping() => /* logic */; // DON'T
}

// GOOD — messages are pure data
public record OrderCreated(Guid OrderId, string CustomerId, decimal Total, DateTimeOffset CreatedAt);
```

### Don't Use Fire-and-Forget for Important Events

```csharp
// BAD — no guarantee of delivery
_ = Task.Run(() => publisher.Publish(new OrderCreated(...)));

// GOOD — await the publish (with outbox, this is transactional)
await publisher.Publish(new OrderCreated(...), ct);
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| Module-to-module communication | MassTransit with events |
| Reliable event publishing | Transactional outbox |
| Simple 2-3 step workflow | Event choreography |
| Complex workflow with compensation | MassTransit saga |
| Local development broker | RabbitMQ (via Docker or Aspire) |
| Production cloud broker | Azure Service Bus or RabbitMQ |
| Request-reply between services | MassTransit request client |
| Scheduled messages | MassTransit scheduler with Quartz |
