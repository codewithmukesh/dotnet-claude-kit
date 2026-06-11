---
name: serilog
description: >
  Structured logging with Serilog for .NET 10 applications. Covers two-stage
  bootstrap, appsettings configuration, enrichers, sinks, request logging,
  destructuring, and Serilog.Expressions.
  Load this skill when setting up Serilog, configuring log sinks, enrichers,
  or structured logging, or when the user mentions "Serilog", "structured
  logging", "log enrichment", "Seq", "LogContext", "UseSerilog",
  "WriteTo", "message template", "Serilog.Expressions", "request logging",
  "log sink", "rolling file", or "audit log".
---

# Serilog

## Core Principles

1. **Two-stage initialization** — Create a bootstrap logger for startup, then replace it with the full logger after DI is ready. This captures startup errors that would otherwise be lost.
2. **`AddSerilog()` over `UseSerilog()`** — Use `builder.Services.AddSerilog()` (the modern API) instead of `builder.Host.UseSerilog()`. It integrates with DI services via `ReadFrom.Services(services)`.
3. **Message templates, not interpolation** — `{PropertyName}` syntax creates structured data that can be queried. String interpolation (`$"..."`) breaks structure and allocates even when the log level is disabled.
4. **Configure via appsettings.json** — Keep log levels, sinks, and overrides in configuration so they can change per environment without redeployment.

## Patterns

### Two-Stage Bootstrap Setup

```csharp
using Serilog;

// Stage 1: Bootstrap logger — captures startup errors before DI
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting application");

    var builder = WebApplication.CreateBuilder(args);

    // Stage 2: Full logger with DI and configuration
    builder.Services.AddSerilog((services, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithEnvironmentName()
        .Enrich.WithProperty("Application", "MyApp.Api"));

    var app = builder.Build();

    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("UserAgent",
                httpContext.Request.Headers.UserAgent.ToString());
        };
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
```

### appsettings.json Configuration

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.EntityFrameworkCore": "Warning",
        "Microsoft.Hosting.Lifetime": "Information",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/app-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30,
          "fileSizeLimitBytes": 104857600
        }
      },
      {
        "Name": "Seq",
        "Args": { "serverUrl": "http://localhost:5341" }
      }
    ],
    "Enrich": ["FromLogContext", "WithMachineName", "WithEnvironmentName"],
    "Destructure": [
      { "Name": "ToMaximumDepth", "Args": { "maximumDestructuringDepth": 4 } },
      { "Name": "ToMaximumStringLength", "Args": { "maximumStringLength": 1024 } },
      { "Name": "ToMaximumCollectionCount", "Args": { "maximumCollectionCount": 10 } }
    ]
  }
}
```

Override section uses namespace prefixes matched against `SourceContext`. More specific prefixes take precedence.

### Request Logging Middleware

Replaces the multiple per-request log events from ASP.NET Core with a single summary event.

```csharp
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

    options.GetLevel = (httpContext, elapsed, ex) => ex is not null
        ? LogEventLevel.Error
        : httpContext.Response.StatusCode >= 500
            ? LogEventLevel.Error
            : LogEventLevel.Information;

    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("UserId",
            httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous");
    };
});
```

### Structured Logging and Destructuring

```csharp
// Named properties — creates queryable structured data
logger.LogInformation("Order {OrderId} placed by {CustomerId} for {Total:C}",
    orderId, customerId, total);

// @ operator preserves object structure as properties
logger.LogInformation("Processing {@SensorInput}", sensorInput);
// Output: Processing {"Latitude": 25, "Longitude": 134}

// $ operator forces ToString()
logger.LogInformation("Received {$Data}", new[] { 1, 2, 3 });
// Output: Received "System.Int32[]"
```

### Scoped Properties with LogContext

```csharp
using (LogContext.PushProperty("CorrelationId", correlationId))
using (LogContext.PushProperty("TenantId", tenantId))
{
    logger.LogInformation("Processing order {OrderId}", orderId);
    // CorrelationId and TenantId attached to ALL log events in this scope
}
```

Requires `.Enrich.FromLogContext()` on the logger configuration.

### OpenTelemetry Sink (OTLP Export)

Export Serilog events directly to any OTLP backend without the OpenTelemetry SDK:

```csharp
.WriteTo.OpenTelemetry(options =>
{
    options.Endpoint = "http://localhost:4317";
    options.Protocol = OtlpProtocol.Grpc;
    options.ResourceAttributes = new Dictionary<string, object>
    {
        ["service.name"] = "MyApp.Api",
        ["deployment.environment"] = "production"
    };
})
```

### Serilog.Expressions for Filtering

Requires the `Serilog.Expressions` package.

```csharp
// Exclude health check noise
.Filter.ByExcluding("RequestPath like '/health%'")

// Route errors to a separate file
.WriteTo.Conditional("@l = 'Error'",
    wt => wt.File("logs/errors-.log", rollingInterval: RollingInterval.Day))
```

### [LoggerMessage] Source Generator for Hot Paths

Built into `Microsoft.Extensions.Logging.Abstractions` — compile-time generated, zero allocations when the level is disabled.

```csharp
public static partial class OrderLogs
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Order {OrderId} created for {CustomerId}")]
    public static partial void OrderCreated(this ILogger logger, Guid orderId, Guid customerId);
}

// Usage
logger.OrderCreated(order.Id, order.CustomerId);
```

## Anti-patterns

### Don't Use String Interpolation

```csharp
// BAD — breaks structured logging, allocates even when level is disabled
logger.LogInformation($"Order {orderId} created for {customerId}");

// GOOD — message template with named parameters
logger.LogInformation("Order {OrderId} created for {CustomerId}", orderId, customerId);
```

### Don't Skip CloseAndFlush

```csharp
// BAD — async sinks (Seq, OTLP, Elasticsearch) lose buffered events
app.Run();

// GOOD — wrap in try/finally
try { app.Run(); }
catch (Exception ex) { Log.Fatal(ex, "Unhandled exception"); }
finally { await Log.CloseAndFlushAsync(); }
```

### Don't Log Sensitive Data

```csharp
// BAD — passwords and tokens in logs
logger.LogInformation("Login: {Email} with password {Password}", email, password);

// GOOD — never log secrets, passwords, tokens, or PII
logger.LogInformation("Login: {Email}", email);
```

### Don't Destructure Without Limits

```csharp
// BAD — large object graphs cause memory issues and massive log entries
logger.LogInformation("Request: {@Request}", httpContext.Request);

// GOOD — configure destructuring limits
.Destructure.ToMaximumDepth(4)
.Destructure.ToMaximumStringLength(1024)
.Destructure.ToMaximumCollectionCount(10)

// BETTER — destructure to specific properties
.Destructure.ByTransforming<HttpRequest>(r => new { r.Method, r.Path })
```

### Don't Use the Deprecated Elasticsearch Sink

```csharp
// BAD — the Serilog.Sinks.Elasticsearch PACKAGE is deprecated
// <PackageReference Include="Serilog.Sinks.Elasticsearch" />
.WriteTo.Elasticsearch("http://localhost:9200")

// GOOD — same method name, but from the official Elastic.Serilog.Sinks
// package, which writes ECS-formatted documents to data streams
// <PackageReference Include="Elastic.Serilog.Sinks" />
.WriteTo.Elasticsearch([new Uri("https://elastic.example.com:9200")], opts =>
    opts.DataStream = new DataStreamName("logs", "myapp"))
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| Application logging | Serilog with `AddSerilog()` and appsettings.json |
| Log storage (development) | Seq (free single-user) or Aspire Dashboard |
| Log storage (production) | Seq, Elasticsearch (Elastic sink), or OTLP backend |
| Request logging | `UseSerilogRequestLogging()` (replaces per-request noise) |
| Scoped properties | `LogContext.PushProperty()` in middleware |
| Log filtering | `Serilog.Expressions` for expression-based filtering |
| High-performance paths | `[LoggerMessage]` source generator |
| Audit trails | `AuditTo` (synchronous, exceptions propagate) |
| Log levels by environment | `MinimumLevel.Override` per namespace in appsettings |
| OpenTelemetry integration | `Serilog.Sinks.OpenTelemetry` (no SDK dependency) |
