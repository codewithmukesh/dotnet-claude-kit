---
name: native-aot
description: >
  Native AOT, trimming, and single-file publishing for .NET 10: PublishAot,
  IsAotCompatible analyzers, source-generated JSON/regex/logging/config binding,
  fixing IL2xxx/IL3xxx trim and AOT warnings, per-RID publishing, and when NOT
  to use AOT. Load when the user mentions "Native AOT", "PublishAot", "trimming",
  "PublishTrimmed", "single-file", "IL2026", "IL3050", "trim warning",
  "startup time", "self-contained", or "JsonSerializerContext".
---

# Native AOT, Trimming & Single-File (.NET 10)

## Core Principles

1. **Decide early** — AOT constrains library choice (no runtime reflection or code generation). Adding it to a mature reflection-heavy app is a rewrite. Adding it on day one costs almost nothing.
2. **A trim/AOT warning is a future runtime crash** — IL2xxx (trimming) and IL3xxx (AOT/single-file) warnings mark code that may throw `MissingMethodException` or silently misbehave in the published binary. Treat them as errors and keep the count at zero.
3. **Source generators replace reflection** — JSON, regex, logging, configuration binding, options validation, and MVVM all have generators. They keep the AOT path fast and analyzable.
4. **Test the published artifact, not `dotnet run`** — `dotnet run` uses the JIT and full metadata, so it hides every trimming bug. Smoke-test the binary that `dotnet publish` produces, in CI.
5. **AOT is a trade, not a free upgrade** — You get fast startup, small memory, and no runtime install. You lose dynamic loading, `Reflection.Emit`, and some steady-state throughput from Dynamic PGO. Pick it where those trade-offs pay.

## Patterns

### Project Setup

```xml
<!-- App (executable) project -->
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <PublishAot>true</PublishAot>                    <!-- also turns on AOT analyzers during build -->
  <InvariantGlobalization>true</InvariantGlobalization> <!-- drop ICU if you don't need cultures -->
  <OptimizationPreference>Size</OptimizationPreference> <!-- or Speed -->
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
</PropertyGroup>

<!-- Every class library the app references -->
<PropertyGroup>
  <IsAotCompatible>true</IsAotCompatible> <!-- implies IsTrimmable + trim/AOT/single-file analyzers -->
</PropertyGroup>
```

```bash
dotnet publish -c Release -r linux-x64   # one RID per publish
```

**Why:** putting `PublishAot` in the project file (not only on the command line) runs the analyzers on every build, so warnings appear while you code. `IsAotCompatible` on libraries surfaces problems at the source instead of in the app. Native AOT needs the platform toolchain (VS "Desktop development with C++" on Windows, `clang` + `zlib` dev packages on Linux, Xcode CLI tools on macOS). Build each OS on that OS in a CI matrix. A Linux binary runs only on the same or newer distro version it was built on.

### Source-Generated JSON

```csharp
public sealed record OrderDto(Guid Id, string Customer, decimal Total, DateTimeOffset CreatedAt);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OrderDto))]
[JsonSerializable(typeof(List<OrderDto>))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

// Usage — pass the generated JsonTypeInfo, never a bare Type
var json = JsonSerializer.Serialize(order, AppJsonContext.Default.OrderDto);
var orders = await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.ListOrderDto, ct);
```

```csharp
// ASP.NET Core minimal APIs: register the context once
var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
```

**Why:** reflection-based `System.Text.Json` is disabled in trimmed/AOT apps. The generated metadata is also faster on the JIT. Register every type that crosses the wire, including collection shapes (`List<OrderDto>`). `CreateSlimBuilder` leaves out non-AOT-friendly defaults. The minimal API Request Delegate Generator turns on automatically with `PublishAot`.

### Generated Regex, Logging, Config Binding, Options Validation

```csharp
internal static partial class Patterns
{
    [GeneratedRegex(@"^ORD-\d{6}$", RegexOptions.CultureInvariant)]
    internal static partial Regex OrderNumber { get; }
}

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Imported {Count} orders in {ElapsedMs} ms")]
    internal static partial void Imported(ILogger logger, int count, long elapsedMs);
}

[OptionsValidator]
internal sealed partial class ImportOptionsValidator : IValidateOptions<ImportOptions>;

public sealed class ImportOptions
{
    [Range(1, 10_000)] public int BatchSize { get; set; } = 500;
}
```

```xml
<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>
```

**Why:** `[GeneratedRegex]` emits matching code at compile time (no `RegexOptions.Compiled`, which needs runtime IL emit). `[LoggerMessage]` avoids boxing and parsing the template on every call. The configuration binding generator replaces the reflection inside `Bind`/`Get<T>`/`Configure<T>`. `[OptionsValidator]` replaces reflection-based data-annotation validation.

### Fixing Trim Warnings Honestly

```csharp
// Propagate the requirement to callers instead of hiding it
[RequiresUnreferencedCode("Loads plugin types by name; not trim-safe.")]
[RequiresDynamicCode("Creates generic types at runtime.")]
public static object CreatePlugin(string typeName) => Activator.CreateInstance(Type.GetType(typeName)!)!;

// Or tell the trimmer exactly what to keep
public static T Create<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>()
    where T : new() => new();

public static object CreateFrom(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type type) =>
    Activator.CreateInstance(type)!;
```

**Why:** `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` move the warning to the call site, where a caller can choose a safe path. `[DynamicallyAccessedMembers]` lets the trimmer keep exactly the members reflection needs. Use `[UnconditionalSuppressMessage]` only when you can explain in the justification why the code is provably safe.

### Single-File Without AOT

```xml
<PropertyGroup>
  <PublishSingleFile>true</PublishSingleFile>
  <SelfContained>true</SelfContained>
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  <PublishTrimmed>true</PublishTrimmed> <!-- optional; requires the same trim discipline -->
</PropertyGroup>
```

```csharp
// BAD in single-file: Assembly.Location is empty (IL3000)
var dir = Path.GetDirectoryName(typeof(Program).Assembly.Location);

// GOOD
var dir = AppContext.BaseDirectory;
```

**Why:** single-file gives one-exe distribution while keeping the JIT and full reflection. Use it when a dependency isn't AOT-compatible but you still want an easy download. File-path APIs are the usual breakage.

### Verifying in CI

```yaml
strategy:
  matrix:
    os: [ubuntu-latest, windows-latest, macos-latest]
steps:
  - run: dotnet publish src/Contoso.Cli -c Release -o out
  - run: ./out/contoso --version && ./out/contoso import --input samples/orders.csv --dry-run
```

**Why:** only the published binary proves trimming kept what you need. A smoke command on each OS catches missing metadata, native library, and globalization problems before release.

## Anti-patterns

### Suppressing Warnings to Get a Green Build

```xml
<!-- BAD — hides the crash until production -->
<NoWarn>$(NoWarn);IL2026;IL3050</NoWarn>
<TrimMode>partial</TrimMode>
```

```csharp
// GOOD — fix the cause: generated serializer instead of reflection
JsonSerializer.Serialize(order, AppJsonContext.Default.OrderDto);
```

### Reflection-Based Serialization Overloads

```csharp
// BAD — IL2026/IL3050: needs runtime reflection over OrderDto
var json = JsonSerializer.Serialize(order);
var dto = await response.Content.ReadFromJsonAsync<OrderDto>(ct);

// GOOD — JsonTypeInfo overloads
var json = JsonSerializer.Serialize(order, AppJsonContext.Default.OrderDto);
var dto = await response.Content.ReadFromJsonAsync(AppJsonContext.Default.OrderDto, ct);
```

### Convention-Based Assembly Scanning

```csharp
// BAD — trimmer removes "unused" types; GetTypes() finds nothing at runtime
foreach (var t in typeof(Program).Assembly.GetTypes().Where(t => t.IsAssignableTo(typeof(ICommandHandler))))
    services.AddTransient(typeof(ICommandHandler), t);

// GOOD — explicit registration (or a source generator that emits it)
services.AddTransient<ICommandHandler, ImportHandler>();
services.AddTransient<ICommandHandler, ExportHandler>();
```

### `RegexOptions.Compiled` and Runtime Expression Compilation

```csharp
// BAD — needs Reflection.Emit; under AOT it falls back to slower interpretation
private static readonly Regex Sku = new(@"^[A-Z]{3}-\d+$", RegexOptions.Compiled);

// GOOD
[GeneratedRegex(@"^[A-Z]{3}-\d+$")]
private static partial Regex Sku { get; }
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| CLI tool shipped to non-.NET users | Native AOT per RID |
| `dotnet run app.cs` script published | Native AOT is already the default for file-based apps |
| Serverless / scale-to-zero / many small containers | Native AOT (fast cold start, low memory); see `container-publish` for `-aot` base images |
| Avalonia desktop app | Native AOT viable with compiled bindings + trim-safe view mapping |
| WPF or WinForms | No trimming/AOT support; use ReadyToRun + single-file for startup |
| Long-running, high-throughput API | Usually JIT (Dynamic PGO wins at steady state); measure before switching |
| EF Core data access | EF Core AOT (precompiled queries) is still experimental; stay JIT for EF-heavy apps |
| MVC controllers, SignalR-heavy, Blazor Server | Not AOT targets; use minimal APIs if AOT is required |
| Plugins / `Assembly.LoadFrom` / `Reflection.Emit` | Not possible under AOT; use JIT |
| One-exe download, AOT-incompatible deps | `PublishSingleFile` + `SelfContained` (JIT) |
| Library meant for AOT consumers | `IsAotCompatible` + zero warnings, multi-target `net8.0`+ |
| Dependency emits trim warnings | Replace it, or isolate behind `[RequiresUnreferencedCode]`; don't blanket-suppress |
