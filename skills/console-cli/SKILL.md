---
name: console-cli
description: >
  Console apps and CLI tools on .NET 10: System.CommandLine 2.x (RootCommand,
  Option<T>, SetAction, InvokeAsync), Generic Host + DI in console apps, exit
  codes, stdout/stderr discipline, Ctrl+C cancellation, interactive vs piped
  use, dotnet tool packaging, and `dotnet run app.cs` file-based apps. Load when
  the user mentions "console app", "CLI", "command line", "System.CommandLine",
  "dotnet tool", "exit code", "stdin", "args parsing", or "file-based app".
---

# Console Apps & CLI Tools (.NET 10)

## Core Principles

1. **stdout is data, stderr is everything else** — Results go to stdout so `mytool list | jq` works. Logs, progress, prompts and errors go to stderr. Mixing them breaks every pipe and script that consumes your tool.
2. **Exit codes are the API** — `0` = success, non-zero = failure, and each failure class gets a stable code. Scripts and CI branch on exit codes, never on parsing your error text.
3. **Use System.CommandLine 2.x, not hand-rolled `args` parsing** — It went stable at 2.0 and gives help, `--version`, validation, tab completion and Ctrl+C handling for free. Hand parsing always misses `--opt=value`, quoting, and help output.
4. **Cancellation flows from Ctrl+C to every await** — System.CommandLine turns SIGINT/SIGTERM into a `CancellationToken`. Pass it all the way down, or Ctrl+C becomes a hard kill that corrupts half-written files.
5. **Generic Host only when you need it** — DI, configuration, logging and `IHttpClientFactory` justify `Host.CreateApplicationBuilder`. A 50-line utility does not; startup time matters for CLIs.

## Patterns

### Command Tree with System.CommandLine 2.x

Verified against System.CommandLine 2.0.x (stable). The beta-era APIs (`SetHandler`, `InvocationContext`, `CommandLineBuilder`) are gone.

```csharp
using System.CommandLine;

Option<FileInfo> inputOption = new("--input", "-i")
{
    Description = "File to import",
    Required = true
};
inputOption.AcceptExistingOnly();

Option<int> batchOption = new("--batch-size")
{
    Description = "Rows per batch",
    DefaultValueFactory = _ => 500
};
batchOption.Validators.Add(result =>
{
    if (result.GetValue(batchOption) is < 1 or > 10_000)
        result.AddError("--batch-size must be between 1 and 10000");
});

Option<bool> dryRunOption = new("--dry-run") { Description = "Validate without writing" };

Command importCommand = new("import", "Import orders from a CSV file")
{
    inputOption, batchOption, dryRunOption
};
importCommand.SetAction((parseResult, ct) => ImportCommand.RunAsync(
    parseResult.GetValue(inputOption)!,
    parseResult.GetValue(batchOption),
    parseResult.GetValue(dryRunOption),
    ct));

RootCommand root = new("Order tooling") { importCommand };
return await root.Parse(args).InvokeAsync();
```

**Why:** options are typed objects, so `GetValue` is compile-time checked. `InvokeAsync` prints parse errors to stderr, shows help, and returns exit code `1` without your code running. `Required`, `AcceptExistingOnly()`, and `Validators` keep input validation at the boundary.

### Exit Codes as a Contract

```csharp
public static class ExitCodes
{
    public const int Success = 0;
    public const int Failure = 1;          // also what System.CommandLine returns on parse errors
    public const int ValidationFailed = 2;
    public const int NotFound = 3;
    public const int Cancelled = 130;      // 128 + SIGINT, the shell convention
}

public static class ImportCommand
{
    public static async Task<int> RunAsync(FileInfo input, int batchSize, bool dryRun, CancellationToken ct)
    {
        try
        {
            var rows = await CsvReader.ReadAsync(input, ct);
            if (rows.Errors is [_, ..])
            {
                foreach (var error in rows.Errors)
                    await Console.Error.WriteLineAsync(error);
                return ExitCodes.ValidationFailed;
            }

            if (!dryRun)
                await OrderImporter.ImportAsync(rows.Items, batchSize, ct);

            Console.WriteLine(rows.Items.Count); // the data result → stdout
            return ExitCodes.Success;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await Console.Error.WriteLineAsync("Cancelled.");
            return ExitCodes.Cancelled;
        }
    }
}
```

**Why:** named constants document the contract and stop magic numbers drifting. Catching `OperationCanceledException` only when the token fired distinguishes a user Ctrl+C from a real timeout bug. Document the codes in `--help` text or the README.

### Ctrl+C and Graceful Shutdown

`ParseResult.InvokeAsync` cancels the action's token on Ctrl+C, SIGINT and SIGTERM. `InvocationConfiguration.ProcessTerminationTimeout` (default 2 seconds) is how long the action gets to finish before the process is killed.

```csharp
InvocationConfiguration config = new()
{
    ProcessTerminationTimeout = TimeSpan.FromSeconds(10) // time to flush a partial batch
};
return await root.Parse(args).InvokeAsync(config);
```

**Why:** the default 2 seconds suits most tools; raise it only when cleanup (flushing, releasing a lock file) genuinely takes longer. Never install your own `Console.CancelKeyPress` handler alongside it — two owners of Ctrl+C race each other.

### Generic Host + DI Inside a CLI

There is no stable `System.CommandLine.Hosting` package (it is still alpha), so compose them yourself: parse first, then build the host inside the action that needs services.

```csharp
Option<bool> verboseOption = new("--verbose", "-v") { Recursive = true }; // added to root, inherited by subcommands

importCommand.SetAction(async (parseResult, ct) =>
{
    using var host = AppHost.Build(parseResult.GetValue(verboseOption));
    var handler = host.Services.GetRequiredService<ImportHandler>();
    return await handler.RunAsync(parseResult.GetValue(inputOption)!, ct);
});

internal static class AppHost
{
    public static IHost Build(bool verbose)
    {
        // No Args: System.CommandLine owns the command line, not configuration.
        // ContentRoot = install dir, so a stray appsettings.json in the user's cwd is never loaded.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace); // logs → stderr
        builder.Logging.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Warning);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHttpClient<OrdersApiClient>();
        builder.Services.AddTransient<ImportHandler>();
        return builder.Build();
    }
}

internal sealed class ImportHandler(OrdersApiClient api, TimeProvider clock, ILogger<ImportHandler> logger)
{
    public async Task<int> RunAsync(FileInfo input, CancellationToken ct) { /* ... */ return ExitCodes.Success; }
}
```

**Why:** `--help` and parse errors stay instant because no host is built for them. The default console logger writes to stdout. Setting `LogToStandardErrorThreshold = LogLevel.Trace` sends every log line to stderr and keeps stdout clean. You never call `host.RunAsync()` for a run-to-completion CLI; you only want its container.

### Interactive vs Non-Interactive

```csharp
public static class Terminal
{
    public static bool IsInteractive =>
        !Console.IsInputRedirected && !Console.IsOutputRedirected &&
        Environment.GetEnvironmentVariable("CI") is null;
}

if (!force && Terminal.IsInteractive)
{
    Console.Error.Write($"Delete {count} orders? [y/N] ");
    if (Console.ReadLine()?.Trim() is not ("y" or "Y"))
        return ExitCodes.Cancelled;
}
else if (!force)
{
    await Console.Error.WriteLineAsync("Refusing to delete without --force in non-interactive mode.");
    return ExitCodes.ValidationFailed;
}
```

**Why:** a prompt in CI hangs the job forever. Detect redirection and fail fast with an actionable message. Every prompt needs a flag that bypasses it (`--force`, `--yes`). Prompts go to stderr so they never pollute piped stdout.

### Reading stdin

```csharp
Argument<FileInfo?> fileArgument = new("file") { Arity = ArgumentArity.ZeroOrOne };

TextReader reader = parseResult.GetValue(fileArgument) is { } file
    ? file.OpenText()
    : Console.In; // `cat orders.csv | mytool import`

while (await reader.ReadLineAsync(ct) is { } line)
    Process(line);
```

**Why:** accepting a file or stdin (the Unix `-` convention) makes the tool composable. Stream line by line; never `ReadToEnd` input of unknown size.

### Packaging as a .NET Tool

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net10.0</TargetFramework>
  <PackAsTool>true</PackAsTool>
  <ToolCommandName>orders</ToolCommandName>
  <PackageId>Contoso.Orders.Cli</PackageId>
</PropertyGroup>
```

```bash
dotnet pack -c Release
dotnet tool install --global --add-source ./bin/Release Contoso.Orders.Cli
dotnet tool exec Contoso.Orders.Cli -- import -i orders.csv   # .NET 10: run without installing
dnx Contoso.Orders.Cli import -i orders.csv                   # .NET 10 shorthand
```

**Why:** a tool package is versioned, installable and updatable through NuGet with no installer. In .NET 10 a tool can also ship platform-specific (RID) builds, including Native AOT, in one package. List the RIDs in `<RuntimeIdentifiers>` and add `any` as a framework-dependent fallback. See the `native-aot` skill.

### File-Based Apps for Scripts (`dotnet run app.cs`)

```csharp
#!/usr/bin/env -S dotnet --
#:package System.CommandLine@2.0.14

using System.CommandLine;

Argument<string> nameArgument = new("name");
RootCommand root = new("Greets someone") { nameArgument };
root.SetAction(parseResult => Console.WriteLine($"Hello, {parseResult.GetValue(nameArgument)}"));
return root.Parse(args).Invoke();
```

```bash
dotnet run greet.cs -- Mukesh     # or: dotnet greet.cs Mukesh
dotnet publish greet.cs           # Native AOT executable by default
dotnet project convert greet.cs   # graduate to a .csproj when it grows
```

**Why:** single-file apps replace PowerShell/bash glue scripts with typed C#, with no `.csproj`. `#:package` without a version only works under central package management, so pin a version or use `@*`. Keep them outside any project folder so that project's `Directory.Build.props` doesn't leak in. Convert once the script needs tests or more than one file.

## Anti-patterns

### Logging to stdout

```csharp
// BAD — default console logger + status text on stdout corrupts piped output
logger.LogInformation("Exporting...");
Console.WriteLine("Done!");
Console.WriteLine(json);

// GOOD — only the data goes to stdout
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
await Console.Error.WriteLineAsync("Exporting...");
Console.WriteLine(json);
```

### Throwing for Expected Failures

```csharp
// BAD — stack trace for a missing file, exit code is whatever the default handler picks
var text = File.ReadAllText(path);

// GOOD — validate at the boundary, return a documented exit code
if (!File.Exists(path))
{
    await Console.Error.WriteLineAsync($"File not found: {path}");
    return ExitCodes.NotFound;
}
```

### Sync-over-Async and Ignored Tokens

```csharp
// BAD — blocks a thread, Ctrl+C cannot interrupt the download
root.SetAction(parseResult => Console.WriteLine(client.GetStringAsync(url).Result));

// GOOD — async action, token flows to every call
root.SetAction(async (parseResult, ct) =>
{
    var body = await client.GetStringAsync(parseResult.GetValue(urlOption), ct);
    Console.WriteLine(body);
    return ExitCodes.Success;
});
return await root.Parse(args).InvokeAsync();
```

Don't mix sync `Invoke()` and async actions in one app. Pick async throughout.

### Using Pre-2.0 System.CommandLine APIs

```csharp
// BAD — beta API, removed in 2.0
rootCommand.SetHandler(async (FileInfo file) => { ... }, fileOption);
await new CommandLineBuilder(rootCommand).UseDefaults().Build().InvokeAsync(args);

// GOOD — 2.x API
rootCommand.SetAction((parseResult, ct) => RunAsync(parseResult.GetValue(fileOption)!, ct));
await rootCommand.Parse(args).InvokeAsync();
```

### `DateTime.Now` in Tools

```csharp
// BAD — untestable "older than 30 days" logic
var cutoff = DateTime.Now.AddDays(-30);

// GOOD — injectable clock (FakeTimeProvider in tests)
var cutoff = clock.GetUtcNow().AddDays(-30);
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| One-off script / automation glue | File-based app (`dotnet run app.cs`) |
| Script grows past one file or needs tests | `dotnet project convert`, then a normal console project |
| Any tool with options or subcommands | System.CommandLine 2.x |
| Tool needs HTTP, config, or DI | Build a `Host.CreateApplicationBuilder()` inside the action, logs to stderr |
| Simple tool with no services | No host; plain `static` handlers |
| Rich terminal UI (tables, spinners) | Spectre.Console, rendered to stderr unless it *is* the output |
| Distribute to .NET developers | `PackAsTool` + NuGet (`dotnet tool install` / `dnx`) |
| Distribute to non-.NET users | Native AOT single binary per RID (see `native-aot`) |
| Long-running daemon, not run-to-completion | Worker Service with `host.RunAsync()`, not a CLI |
| Destructive command | Interactive confirm + `--force` flag for CI |
| Testing a command | Call `root.Parse([...]).InvokeAsync(new() { Output = sw, Error = errSw })` and assert exit code + output |
