---
alwaysApply: true
description: >
  Enforces security best practices for .NET applications including secrets
  management, input validation, auth patterns, and OWASP compliance.
---

# Security Rules

## Secrets Management

- **Never hardcode secrets in source code.** Use `dotnet user-secrets` for local development, Azure Key Vault or environment variables for deployed environments. Hardcoded secrets end up in git history and are nearly impossible to fully remove.

```csharp
// DO
builder.Configuration.AddAzureKeyVault(vaultUri, credential);
var conn = builder.Configuration.GetConnectionString("Default");

// DON'T
var conn = "Server=prod;Password=hunter2";
```

- **Never commit `.env` files, `appsettings.Development.json` with real credentials, or `credentials.json`.** Add them to `.gitignore`.

## Input Validation

- **Validate all external input at system boundaries.** API endpoints, message handlers, and file uploads are trust boundaries. Use FluentValidation or the built-in validation attributes before data reaches domain logic.
- **Use parameterized queries — never string concatenation for SQL.** EF Core parameterizes by default, but raw SQL, Dapper, and ADO.NET require explicit parameterization.

```csharp
// DO
db.Database.SqlQuery<Order>($"SELECT * FROM Orders WHERE Id = {id}");
// EF Core interpolation is parameterized — the above is safe

// DON'T
db.Database.ExecuteSqlRaw("SELECT * FROM Orders WHERE Id = '" + id + "'");
```

## Authentication and Authorization

- **Always add `[Authorize]` or `[AllowAnonymous]` explicitly on every controller or endpoint.** Ambiguous auth is a security hole. Never rely on a global default without also making intent explicit at the endpoint level.

```csharp
// DO
[Authorize(Policy = "AdminOnly")]
public sealed class AdminController : ControllerBase { }

[AllowAnonymous]
app.MapGet("/health", () => Results.Ok());

// DON'T — unmarked endpoint inherits whatever the global default is
app.MapGet("/orders", GetOrders);
```

## Transport and Data Protection

- **Use HTTPS everywhere.** Enforce via HSTS in production. Redirect HTTP to HTTPS. No exceptions.

```csharp
app.UseHsts();
app.UseHttpsRedirection();
```

- **Use Data Protection API for encrypting user data at rest.** Never roll your own encryption. The Data Protection API handles key rotation and algorithm selection correctly.
- **CORS: explicit origins only, never wildcard in production.** `AllowAnyOrigin()` in production exposes your API to every domain on the internet.

```csharp
// DO
builder.Services.AddCors(o => o.AddPolicy("Web", p =>
    p.WithOrigins("https://app.example.com")
     .AllowAnyMethod()
     .AllowAnyHeader()));

// DON'T
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin()));
```

## Logging

- **Do not log PII at Information level or below.** Emails, names, IP addresses, and tokens must stay at `Debug` level at most, and only in development. Production log aggregators are often broadly accessible, making PII in logs a compliance liability.
