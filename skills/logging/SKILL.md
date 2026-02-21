---
name: logging
description: >
  Observability for .NET 10 applications. Covers Serilog structured logging,
  OpenTelemetry traces and metrics, health checks, and correlation IDs.
  Load this skill when setting up logging, tracing, metrics, or health monitoring,
  or when the user mentions "Serilog", "logging", "structured log", "OpenTelemetry",
  "traces", "metrics", "health check", "correlation ID", "observability",
  "telemetry", "log enrichment", or "ILogger".
---

# Logging & Observability

## Core Principles

1. **Structured logging with Serilog** — Every log entry is a structured event with named properties, not a formatted string. This enables searching, filtering, and alerting.
2. **OpenTelemetry for distributed tracing** — Traces connect requests across services. Metrics track system health over time.
3. **Health checks for operational readiness** — Every service exposes `/health` endpoints for load balancers and orchestrators.
4. **Correlation IDs for request tracing** — Every request gets a unique ID that flows through all log entries and downstream service calls.

## Patterns

### Serilog Setup

```csharp
// Program.cs
builder.Host.UseSerilog((context, loggerConfig) =>
{
    loggerConfig
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithProperty("Application", "MyApp.Api")
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
        .WriteTo.Seq(context.Configuration["Seq:Url"] ?? "http://localhost:5341");
});

// After building the app
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("UserId",
            httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous");
    };
});
```

### Structured Logging (Correct Usage)

```csharp
// GOOD — structured logging with message template
logger.LogInformation("Processing order {OrderId} for customer {CustomerId}",
    orderId, customerId);

// GOOD — include relevant context
logger.LogWarning("Payment failed for order {OrderId}. Attempt {Attempt} of {MaxAttempts}",
    orderId, attempt, maxAttempts);

// GOOD — log exceptions with structured data
logger.LogError(exception, "Failed to process order {OrderId}", orderId);
```

### Correlation IDs

```csharp
// Middleware to set correlation ID
public class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string CorrelationIdHeader = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationIdHeader].FirstOrDefault()
            ?? Guid.NewGuid().ToString();

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers[CorrelationIdHeader] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}

// Program.cs
app.UseMiddleware<CorrelationIdMiddleware>();
```

### OpenTelemetry Setup

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("MyApp.Api"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter();
    });
```

### Custom Metrics

```csharp
public class OrderMetrics
{
    private readonly Counter<int> _ordersCreated;
    private readonly Histogram<double> _orderProcessingDuration;

    public OrderMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("MyApp.Orders");
        _ordersCreated = meter.CreateCounter<int>("orders.created", "orders",
            "Number of orders created");
        _orderProcessingDuration = meter.CreateHistogram<double>("orders.processing.duration",
            "ms", "Order processing duration");
    }

    public void OrderCreated() => _ordersCreated.Add(1);

    public void RecordProcessingDuration(double milliseconds) =>
        _orderProcessingDuration.Record(milliseconds);
}
```

### Health Checks

```csharp
// Program.cs
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Default")!,
        name: "database", tags: ["ready"])
    .AddRedis(builder.Configuration.GetConnectionString("Redis")!,
        name: "redis", tags: ["ready"])
    .AddRabbitMQ(builder.Configuration.GetConnectionString("RabbitMq")!,
        name: "rabbitmq", tags: ["ready"]);

// Map endpoints
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false // No dependency checks — just "am I running?"
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
```

## Anti-patterns

### Don't Use String Interpolation in Log Messages

```csharp
// BAD — allocates string even if level is disabled, breaks structured logging
logger.LogInformation($"Order {orderId} created for {customerId}");

// GOOD — message template with named parameters
logger.LogInformation("Order {OrderId} created for {CustomerId}", orderId, customerId);
```

### Don't Log Sensitive Data

```csharp
// BAD — logging credentials
logger.LogInformation("User logged in: {Email} with password {Password}", email, password);

// GOOD — never log secrets, passwords, tokens, or PII
logger.LogInformation("User logged in: {Email}", email);
```

### Don't Skip Health Check Tags

```csharp
// BAD — all checks run for liveness AND readiness
app.MapHealthChecks("/health");

// GOOD — separate liveness (am I running?) from readiness (can I serve traffic?)
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") });
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| Application logging | Serilog with structured logging |
| Distributed tracing | OpenTelemetry with OTLP exporter |
| Custom business metrics | `IMeterFactory` + counters/histograms |
| Request tracing | Correlation ID middleware |
| Container health | `/health/live` and `/health/ready` endpoints |
| Log storage | Seq (development), Elastic/Grafana (production) |
| Log levels | Debug in dev, Information in staging, Warning in production |
