---
name: aspire
description: >
  .NET Aspire for cloud-native orchestration. Covers AppHost configuration,
  service defaults, resource configuration, service discovery, and the Aspire
  dashboard.
  Load this skill when setting up local development orchestration, service
  discovery, or Aspire-managed infrastructure, or when the user mentions
  "Aspire", "AppHost", "service defaults", "service discovery", "orchestration",
  "Aspire dashboard", "AddProject", "WithReference", or "cloud-native .NET".
---

# .NET Aspire

## Core Principles

1. **Aspire is for orchestration, not deployment** — Aspire manages your local development experience: starting services, databases, and message brokers together. Production deployment is a separate concern.
2. **Service defaults are your baseline** — The `ServiceDefaults` project configures OpenTelemetry, health checks, and resilience for all services in one place.
3. **Use Aspire integrations** — Aspire has built-in integrations for PostgreSQL, Redis, RabbitMQ, SQL Server, and more. They handle connection strings, health checks, and tracing automatically.
4. **The dashboard is your observability tool** — Use the Aspire dashboard for local development tracing, logging, and metrics instead of setting up Seq/Grafana locally.

## Patterns

### AppHost Configuration

```csharp
// AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure resources
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .AddDatabase("myappdb");

var redis = builder.AddRedis("redis")
    .WithRedisInsight();

var rabbitmq = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin();

// Application projects
var api = builder.AddProject<Projects.MyApp_Api>("api")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(rabbitmq)
    .WithExternalHttpEndpoints();

var worker = builder.AddProject<Projects.MyApp_Worker>("worker")
    .WithReference(postgres)
    .WithReference(rabbitmq);

builder.Build().Run();
```

### Service Defaults

```csharp
// ServiceDefaults/Extensions.cs
public static class Extensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation();
            });

        builder.AddOpenTelemetryExporters();
        return builder;
    }

    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        var useOtlp = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        if (useOtlp)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }
        return builder;
    }
}
```

### Using Service Defaults in a Project

```csharp
// MyApp.Api/Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Add Aspire integrations
builder.AddNpgsqlDbContext<AppDbContext>("myappdb");
builder.AddRedisDistributedCache("redis");

var app = builder.Build();
app.MapDefaultEndpoints(); // health check endpoints
app.Run();
```

### Service-to-Service Communication

```csharp
// AppHost — configure service references
var orderApi = builder.AddProject<Projects.OrderApi>("order-api");
var paymentApi = builder.AddProject<Projects.PaymentApi>("payment-api")
    .WithReference(orderApi); // paymentApi can discover orderApi

// In PaymentApi — use service discovery
builder.Services.AddHttpClient<OrderClient>(client =>
{
    client.BaseAddress = new Uri("https+http://order-api");
});
```

### Solution Structure with Aspire

```
MyApp.slnx
├── MyApp.AppHost/               # Aspire orchestrator
│   └── Program.cs
├── MyApp.ServiceDefaults/       # Shared service configuration
│   └── Extensions.cs
├── src/
│   ├── MyApp.Api/               # Web API project
│   └── MyApp.Worker/            # Background worker
└── tests/
    └── MyApp.Api.Tests/
```

## Anti-patterns

### Don't Use Aspire for Production Deployment

```csharp
// BAD — Aspire AppHost is not a production deployment tool
// Don't try to deploy the AppHost to Kubernetes

// GOOD — Use Aspire for local dev, deploy with Docker/K8s/Azure separately
```

### Don't Hardcode Connection Strings with Aspire

```csharp
// BAD — hardcoding connection strings defeats Aspire's purpose
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql("Host=localhost;Database=myapp;..."));

// GOOD — use Aspire integration (connection string injected automatically)
builder.AddNpgsqlDbContext<AppDbContext>("myappdb");
```

### Don't Skip Service Defaults

```csharp
// BAD — manually configuring each service
builder.Services.AddOpenTelemetry()...
builder.Services.AddHealthChecks()...

// GOOD — use shared service defaults
builder.AddServiceDefaults();
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| Local dev with multiple services | Aspire AppHost |
| Single-project local dev | `dotnet run` is fine, Aspire optional |
| Shared service configuration | ServiceDefaults project |
| Database for local dev | Aspire `AddPostgres()` / `AddSqlServer()` |
| Service discovery | Aspire's built-in service discovery |
| Production deployment | Docker / Kubernetes / Azure Container Apps |
| Observability in local dev | Aspire dashboard (auto-configured) |
