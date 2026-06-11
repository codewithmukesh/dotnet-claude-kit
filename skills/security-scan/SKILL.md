---
name: security-scan
description: >
  Deep security scanning for .NET applications across 6 layers: vulnerable packages,
  secrets detection, OWASP code patterns, auth configuration, CORS policy, and
  data protection. Produces severity-rated findings with specific remediation steps.
  Load this skill when: "security scan", "security audit", "check for vulnerabilities",
  "find secrets", "OWASP", "auth review", "CORS check", "security review",
  "penetration test prep", "CVE check", "vulnerability scan", "hardcoded password",
  "data protection", "security posture".
---

# Security Scan

## Core Principles

1. **Defense in depth** — Scan multiple layers: packages, source code, configuration, and infrastructure. A project with zero CVEs can still have hardcoded secrets, SQL injection, and missing auth. Each layer catches different vulnerability classes.

2. **Prioritize by exploitability** — A Critical SQL injection in a public endpoint is more urgent than a Low-severity info disclosure in an admin-only page. Prioritize findings by: exploitability (how easy to exploit), impact (what an attacker gains), and exposure (public vs internal endpoint).

3. **No false sense of security** — This is static analysis, not a penetration test. It catches known patterns but misses business logic flaws, authorization bypass through complex flows, and runtime-only vulnerabilities. State this clearly in every report.

4. **Actionable findings** — Every issue includes severity, file and line, description of the vulnerability, impact if exploited, and specific remediation code. "Fix the security issue" is not a finding. "OrderController.cs:23 — Missing `[Authorize]` on `DELETE /orders/{id}`. Impact: unauthenticated users can delete orders. Fix: Add `[Authorize(Policy = \"OrderAdmin\")]`" is.

5. **Follow OWASP Top 10** — Structure the scan around known vulnerability categories. The OWASP Top 10 is the industry baseline for web application security. Every finding should map to an OWASP category.

## Patterns

### 6-Layer Security Scan Pipeline

Execute all 6 layers. Each produces findings rated Critical, High, Medium, or Low. Delegate deep auth and secrets review to the `security-auditor` agent, pairing the `authentication` and `configuration` skills.

**Layer 1: Package Vulnerabilities**
```bash
dotnet list package --vulnerable --include-transitive
```
Check for known CVEs in direct and transitive dependencies.

Severity mapping:
- **Critical**: CVSS 9.0-10.0 — Remote code execution, authentication bypass
- **High**: CVSS 7.0-8.9 — Privilege escalation, data exposure
- **Medium**: CVSS 4.0-6.9 — Denial of service, information disclosure
- **Low**: CVSS 0.1-3.9 — Minor information leakage

Remediation pattern:
```xml
<!-- BEFORE — vulnerable package -->
<PackageReference Include="System.Text.Json" Version="8.0.0" />

<!-- AFTER — patched version -->
<PackageReference Include="System.Text.Json" Version="10.0.0" />
```

If a patch isn't available, document the risk and apply compensating controls.

**Layer 2: Secrets Detection**

Scan all `.cs`, `.json`, `.yml`, `.yaml`, `.xml`, and `.config` files for hardcoded secrets.

Patterns to detect:
```
HIGH-CONFIDENCE PATTERNS (almost always a real secret):
- "Password=" or "Pwd=" in connection strings outside appsettings.Development.json
- "Bearer " followed by a base64 token in source code
- "-----BEGIN PRIVATE KEY-----" or "-----BEGIN RSA PRIVATE KEY-----"
- AWS: "AKIA" followed by 16 alphanumeric characters
- Azure: strings matching Azure Storage/Service Bus key patterns

MEDIUM-CONFIDENCE PATTERNS (need context to determine):
- "ApiKey", "Secret", "Token" as variable names with string literal assignments
- Base64-encoded strings longer than 40 characters in source code
- Connection strings with server addresses in non-Development config files

FALSE-POSITIVE INDICATORS (skip these):
- Values in appsettings.Development.json (development-only)
- Placeholder values: "your-key-here", "changeme", "TODO", empty strings
- Test fixtures with obviously fake values
- User secrets references: "UserSecretsId" in .csproj
```

Remediation:
```csharp
// BAD — hardcoded connection string
var connectionString = "Server=prod-db;Database=Orders;User=admin;Password=S3cret!";

// GOOD — configuration with user secrets / environment variables
var connectionString = builder.Configuration.GetConnectionString("OrdersDb");
// Store actual values in:
// - Development: dotnet user-secrets set "ConnectionStrings:OrdersDb" "..."
// - Production: Environment variable or Azure Key Vault
```

**Layer 3: OWASP Code Patterns**

Scan source code for vulnerability patterns mapped to OWASP Top 10.

```
A03:2021 — Injection
  Detect: String concatenation in SQL queries, raw SQL with user input
  Pattern: FromSqlRaw($"SELECT * FROM Orders WHERE Id = '{userInput}'")
  Fix: FromSqlInterpolated($"SELECT * FROM Orders WHERE Id = {userInput}")
       or use parameterized queries / LINQ

A07:2021 — Cross-Site Scripting (XSS)
  Detect: Raw HTML output without encoding in Razor/Blazor
  Pattern: @Html.Raw(userInput)
  Fix: Use Razor's default encoding (@userInput) or sanitize explicitly

A08:2021 — Insecure Deserialization
  Detect: BinaryFormatter, JsonConvert with TypeNameHandling.All
  Pattern: JsonConvert.DeserializeObject<T>(json, new JsonSerializerSettings
           { TypeNameHandling = TypeNameHandling.All })
  Fix: Use System.Text.Json (no type name handling by default)
       If Newtonsoft is required: TypeNameHandling.None + explicit type converters

A02:2021 — Cryptographic Failures
  Detect: MD5, SHA1 for security purposes, ECB mode, hardcoded encryption keys
  Pattern: MD5.Create().ComputeHash(...)
  Fix: Use SHA256 minimum, prefer HMACSHA256 for authentication
       Use AES-GCM for encryption, derive keys from passwords using Rfc2898DeriveBytes

A04:2021 — Insecure Direct Object References
  Detect: Endpoints that use user-supplied IDs without ownership verification
  Pattern: GET /orders/{id} — returns any order regardless of who owns it
  Fix: Add ownership check: where o.Id == id && o.CustomerId == currentUser.Id
```

**Layer 4: Auth Configuration**

Review authentication and authorization setup across the application.

```
CHECKLIST:
1. All endpoints have explicit auth attributes
   - MCP: find_references(symbolName: "AllowAnonymous") — list deliberately public endpoints
   - MCP: find_references(symbolName: "Authorize") — list protected endpoints
   - Gap: endpoints with neither attribute (implicit policy depends on global config)

2. JWT validation settings are secure
   - ValidateIssuer: true (prevents token from wrong issuer)
   - ValidateAudience: true (prevents token meant for another app)
   - ValidateLifetime: true (prevents expired tokens)
   - ValidateIssuerSigningKey: true (prevents tampered tokens)
   - ClockSkew: TimeSpan.FromMinutes(1) max (default 5 min is too generous)

3. Authorization policies are specific
   - BAD: [Authorize] with no policy — just checks "is authenticated"
   - GOOD: [Authorize(Policy = "OrderAdmin")] — role/claim-based authorization

4. No auth bypass patterns
   - Middleware ordering: UseAuthentication() before UseAuthorization()
   - No global AllowAnonymous that accidentally opens everything
   - API key validation in middleware, not in each controller
```

```csharp
// BAD — JWT configuration with weak validation
builder.Services.AddAuthentication().AddJwtBearer(options =>
{
    options.TokenValidationParameters = new()
    {
        ValidateIssuer = false,          // Anyone can issue tokens
        ValidateAudience = false,        // Token works for any app
        ValidateLifetime = false,        // Expired tokens accepted
        IssuerSigningKey = new SymmetricSecurityKey(
            "short-key"u8.ToArray())     // Key too short (< 256 bits)
    };
});

// GOOD — secure JWT configuration
builder.Services.AddAuthentication().AddJwtBearer(options =>
{
    options.TokenValidationParameters = new()
    {
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(
            Convert.FromBase64String(builder.Configuration["Jwt:Key"]!))
    };
});
```

**Layer 5: CORS Configuration**

Review Cross-Origin Resource Sharing policy for misconfigurations.

```csharp
// CRITICAL — wildcard origin with credentials
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()      // Any website can call this API
              .AllowCredentials();    // ...and send cookies/auth headers
        // This is a security vulnerability — browsers block this combo,
        // but it signals a misunderstanding of CORS
    });
});

// HIGH — wildcard origin without credentials
policy.AllowAnyOrigin()             // Any website can read API responses
      .AllowAnyHeader()
      .AllowAnyMethod();
// Acceptable ONLY for truly public APIs (e.g., public data feeds)

// GOOD — explicit origins
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()!)
              .AllowCredentials()
              .WithMethods("GET", "POST", "PUT", "DELETE")
              .WithHeaders("Content-Type", "Authorization");
    });
});
```

Check for:
- Wildcard origins (`AllowAnyOrigin`) — should be restricted to specific domains
- Exposed headers that leak internal information
- Overly broad methods (allowing PATCH, OPTIONS when only GET/POST needed)
- CORS in development vs production — different policies for different environments

**Layer 6: Data Protection**

Scan for PII and sensitive data handling issues.

```
CHECKS:
1. PII in logs — email, phone, SSN, credit card in logging statements
   Pattern: logger.LogInformation("User {Email} placed order", user.Email)
   Fix: logger.LogInformation("User {UserId} placed order", user.Id)
   Rule: Log identifiers (IDs), not identity data (email, name, phone)

2. Sensitive data in responses — returning more data than needed
   Pattern: Returning full User entity (with password hash) from an endpoint
   Fix: Use a response DTO that excludes sensitive fields

3. Missing Data Protection API — storing sensitive data without encryption
   Pattern: Storing API keys as plain text in the database
   Fix: Use IDataProtector to encrypt before storage

4. Unencrypted sensitive configuration — secrets in appsettings.json
   Pattern: "SmtpPassword": "actualpassword" in appsettings.json
   Fix: Use user secrets (dev), Key Vault (prod), or environment variables
```

```csharp
// BAD — PII in logs
logger.LogInformation("Order placed by {Email} for {CreditCard}",
    order.CustomerEmail, order.PaymentCard);

// GOOD — identifiers only
logger.LogInformation("Order {OrderId} placed by customer {CustomerId}",
    order.Id, order.CustomerId);
```

### Full Scan Report

Each finding uses format: `#### [SEVERITY] File:Line — Title` with OWASP Category, Description, Impact, and Remediation (with code before/after).

```markdown
## Security Scan Report

**Project:** MyApp | **Date:** 2026-03-04 | **Scanner:** Claude (static analysis)

> This is a static analysis scan. It catches known patterns but does not replace
> penetration testing, dynamic analysis, or threat modeling.

### Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| High | 2 |
| Medium | 3 |
| Low | 1 |

### Findings

#### [HIGH] src/Orders/Features/SearchOrders.cs:34 — SQL Injection
...

#### [HIGH] src/Api/Program.cs:12 — Missing Authorization on DELETE endpoint
...

### Layer Results

| Layer | Status | Findings |
|-------|--------|----------|
| 1. Package Vulnerabilities | PASS | 0 CVEs |
| 2. Secrets Detection | PASS | No hardcoded secrets |
| 3. OWASP Code Patterns | FAIL | 1 SQL injection, 1 insecure deserialization |
| 4. Auth Configuration | WARN | 2 endpoints missing explicit auth |
| 5. CORS Configuration | PASS | Origins properly restricted |
| 6. Data Protection | WARN | PII found in 2 log statements |
```

## Anti-patterns

### Only Scanning Packages

```
# BAD — NuGet packages are clean, declare victory
dotnet list package --vulnerable → "No vulnerable packages found"
"Security scan passed!"
# Missed: hardcoded password in appsettings.json, SQL injection in SearchOrders,
# missing [Authorize] on 3 endpoints, PII in logs

# GOOD — all 6 layers for complete coverage
Layer 1 (Packages): PASS
Layer 2 (Secrets): Found connection string in appsettings.Production.json
Layer 3 (OWASP): SQL injection in SearchOrders.cs:34
Layer 4 (Auth): 3 endpoints without [Authorize]
Layer 5 (CORS): Wildcard origin in production config
Layer 6 (Data): Customer email logged at Information level
```

### Everything is Critical

```
# BAD — alert fatigue from over-classification
[CRITICAL] Missing XML comment on public method
[CRITICAL] Using var instead of explicit type
[CRITICAL] Connection string in appsettings.Development.json

# GOOD — severity matches actual risk
[LOW] Missing XML comment on public method (not a security issue)
[INFO] appsettings.Development.json has connection string (expected for dev)
[HIGH] appsettings.Production.json has hardcoded password (real secret exposure)
```

### Scanning Without Context

```
# BAD — flagging test fixtures as security issues
[HIGH] Tests/OrderTests.cs:15 — Hardcoded API key: "test-key-12345"
# This is a test fixture with a fake value, not a real secret

# GOOD — context-aware scanning
Skip files in test projects for secret detection (test data is expected to be fake).
Flag only if the pattern matches a real secret format (e.g., starts with "AKIA" for AWS).
```

### No Remediation Steps

```
# BAD — finding without a fix
[HIGH] SQL Injection in SearchOrders.cs:34
"Fix this."

# GOOD — finding with specific remediation
[HIGH] SQL Injection in SearchOrders.cs:34
Current: FromSqlRaw($"SELECT * FROM Orders WHERE Name LIKE '%{search}%'")
Fix: Use parameterized query:
  db.Orders.Where(o => EF.Functions.Like(o.Name, $"%{search}%"))
Impact: Attacker can read/modify/delete any data in the database.
```

## Decision Guide

| Scenario | Layers | Notes |
|----------|--------|-------|
| Pre-release security gate | All 6 | Full scan, non-negotiable before production |
| After dependency update | 1 | Package vulnerabilities only |
| New endpoint added | 3, 4, 5 | OWASP, auth, CORS for the new endpoint |
| Auth system changes | 4 | Deep auth configuration review |
| Config file changes | 2 | Secrets detection in changed configs |
| Logging changes | 6 | Check for PII in new log statements |
| Pre-pentest preparation | All 6 | Fix static issues before paying for a pentest |
| Incident response | All 6 | Full scan after a security incident |
| Quarterly security review | All 6 | Regular cadence, also useful for onboarding |
| Public API exposure | 3, 4, 5 | Focus on external attack surface |
| Internal service | 1, 2, 3 | Lower CORS/auth scrutiny if truly internal |
