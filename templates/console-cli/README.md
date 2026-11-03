# Console / CLI Template

## When to Use

Use this template when you're building:
- A command-line tool with options and subcommands (internal tooling, developer CLIs)
- A .NET tool distributed through NuGet (`dotnet tool install`, `dnx`)
- A console utility or automation script that grew beyond a single file
- A native, self-contained binary for users without .NET installed (Native AOT)

For a long-running background process, use `worker-service` instead. For a one-file script, a file-based app (`dotnet run app.cs`) may be enough. See the `console-cli` skill.

## How to Use

1. Copy `CLAUDE.md` into the root of your project
2. Replace `[Project Name]`, `[ProjectName]`, and `[PackageId]`
3. Pick the distribution model in **Tech Stack** and delete the others
4. Remove optional items (Generic Host, Spectre.Console) if not used

Or run `/dotnet-init`. It detects console projects (`OutputType=Exe`, `PackAsTool`, System.CommandLine) and generates this file for you.

## What's Included

This template configures Claude Code to:
- Build command trees with System.CommandLine 2.x (`RootCommand`, `Option<T>`, `SetAction`, `InvokeAsync`)
- Keep stdout for data and stderr for logs, progress, and errors
- Return documented, stable exit codes
- Propagate Ctrl+C cancellation through every async call
- Use the Generic Host for DI/config/HTTP only where needed, with logging routed to stderr
- Behave correctly in non-interactive environments (CI, pipes)
- Package as a .NET tool or publish Native AOT binaries per OS
- Test commands in-process by asserting exit codes and captured output

## Customization

### Native AOT Distribution

Add `<PublishAot>true</PublishAot>`, load the `native-aot` skill, and add a CI matrix that publishes and smoke-tests on each target OS.

### No Generic Host

For small tools, delete `AppHost.cs` and call static handlers directly from `SetAction`. Startup is faster and there's less to maintain.
