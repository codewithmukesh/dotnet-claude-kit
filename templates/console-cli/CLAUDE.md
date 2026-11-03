# [Project Name] — Console App / CLI Tool

> Copy this file into your project root and customize the sections below.

## Project Context

This is a .NET 10 command-line application. It is invoked by people and by scripts/CI, runs to completion, and communicates through arguments, stdin/stdout/stderr, and exit codes. It is not a web server and not a long-running daemon.

## Tech Stack

- **.NET 10** / C# 14
- **System.CommandLine 2.x** — command tree, parsing, help, validation, Ctrl+C handling
- **Microsoft.Extensions.Hosting** (optional) — DI, configuration, logging, `IHttpClientFactory`
- **Spectre.Console** (optional) — tables, progress, prompts (rendered to stderr unless it *is* the output)
- **Distribution** — [dotnet tool via NuGet | Native AOT binaries per OS | file-based script]
- **xUnit v3** — tests that invoke commands in-process

## Architecture

```
src/
  [ProjectName].Cli/
    Commands/
      [Verb]Command.cs          # builds the Command + options, calls SetAction
      [Verb]Handler.cs          # the work: (inputs, CancellationToken) → Task<int>
    Services/                   # domain logic, no Console access
    ExitCodes.cs                # documented, stable exit codes
    AppHost.cs                  # Generic Host builder (logs → stderr)
    Program.cs                  # RootCommand + Parse(args).InvokeAsync()
tests/
  [ProjectName].Cli.Tests/
    Commands/
      [Verb]CommandTests.cs     # invoke with args, assert exit code + output
```

### Command Convention

```csharp
public static class ImportCommand
{
    public static Command Create()
    {
        Option<FileInfo> input = new("--input", "-i") { Description = "CSV to import", Required = true };
        input.AcceptExistingOnly();

        Command command = new("import", "Import orders") { input };
        command.SetAction(async (parseResult, ct) =>
        {
            using var host = AppHost.Build();
            var handler = host.Services.GetRequiredService<ImportHandler>();
            return await handler.RunAsync(parseResult.GetValue(input)!, ct);
        });
        return command;
    }
}

// Program.cs
RootCommand root = new("[Project Name] CLI") { ImportCommand.Create() };
return await root.Parse(args).InvokeAsync();
```

### Testing Convention

```csharp
[Fact]
public async Task Import_MissingFile_ReturnsParseError()
{
    // Arrange
    RootCommand root = new() { ImportCommand.Create() };
    var error = new StringWriter();

    // Act
    var exitCode = await root.Parse(["import", "-i", "missing.csv"])
        .InvokeAsync(new InvocationConfiguration { Error = error });

    // Assert
    Assert.Equal(1, exitCode);
}
```

## Coding Standards

- **C# 14 features** — Primary constructors, collection expressions, records, pattern matching
- **File-scoped namespaces**, `sealed` classes, `internal` by default
- **stdout = data only** — Results to stdout; logs, progress, prompts, errors to stderr
- **Exit codes are a contract** — Named constants in `ExitCodes`; never change a published code's meaning
- **CancellationToken everywhere** — The action's token flows into every async call
- **No `Console` in services** — Only command/handler code touches the console, so services stay testable
- **`TimeProvider`** — Never `DateTime.Now`
- **Non-interactive safe** — Every prompt has a flag that bypasses it (`--yes`/`--force`); detect redirected I/O and CI

## Skills

Load these dotnet-claude-kit skills for context:

- `modern-csharp` — C# 14 language features and idioms
- `console-cli` — System.CommandLine 2.x, exit codes, stdio discipline, dotnet tools, file-based apps
- `native-aot` — AOT/trimmed/single-file publishing (if shipping native binaries)
- `dependency-injection` — Service lifetimes and registration
- `configuration` — Options pattern and secrets
- `testing` — xUnit v3 patterns
- `ci-cd` — Per-OS publish matrix, NuGet tool publishing
- `workflow-mastery` — Parallel worktrees, verification loops, context discipline

## MCP Tools

> **Setup:** Install once globally with `dotnet tool install -g CWM.RoslynNavigator` and register with `claude mcp add --scope user cwm-roslyn-navigator -- cwm-roslyn-navigator --solution ${workspaceFolder}`.

- **Before modifying a type** — `find_symbol`, then `get_public_api`
- **To audit packages** — `get_nuget_packages` (confirm System.CommandLine is 2.x, not a beta)
- **To check for errors and trim/AOT warnings** — `get_diagnostics` after changes
- **To catch async mistakes** — `detect_antipatterns`

## Commands

```bash
# Build / test
dotnet build
dotnet test

# Run with arguments
dotnet run --project src/[ProjectName].Cli -- import -i samples/orders.csv

# Pack and install as a local tool for manual testing
dotnet pack src/[ProjectName].Cli -c Release
dotnet tool install --global --add-source src/[ProjectName].Cli/bin/Release [PackageId]

# Native AOT publish (run on each target OS)
dotnet publish src/[ProjectName].Cli -c Release -r linux-x64

# Format check
dotnet format --verify-no-changes
```

## Workflow

- **Plan first** — Enter plan mode for any non-trivial task (3+ steps or design decisions).
- **Verify before done** — `dotnet build` + `dotnet test`; for AOT builds, also run the published binary.
- **Define the contract first** — For a new command, write its usage line, output format, and exit codes before the code.
- **Learn from corrections** — Capture corrections in memory so the same mistake never recurs.

## Anti-patterns

Do NOT generate code that:

- Writes logs or status messages to stdout — use stderr
- Hand-parses `args` — use System.CommandLine
- Uses pre-2.0 System.CommandLine APIs (`SetHandler`, `InvocationContext`, `CommandLineBuilder`)
- Throws exceptions for expected failures (missing file, invalid input) — print to stderr and return an exit code
- Blocks on async (`.Result`, `.Wait()`) or ignores the action's `CancellationToken`
- Prompts without checking `Console.IsInputRedirected` / CI, or without a bypass flag
- Calls `host.RunAsync()` for a run-to-completion tool
- Uses reflection-based JSON when publishing AOT — use a `JsonSerializerContext`
- Uses `DateTime.Now` or `new HttpClient()`
