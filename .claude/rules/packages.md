---
alwaysApply: true
description: >
  Enforces latest stable NuGet package versions and proper dependency management
  for .NET 10 projects. Prevents outdated package references from training data.
---

# Package Management Rules

## Always Use Latest Stable Versions

- **Never hardcode package versions from memory.** Your training data contains outdated 8.x/9.x versions. Always verify the latest stable version before adding a package.
- **Run `dotnet add package <name>` without a `--version` flag** to automatically pull the latest stable release from NuGet.org. This is the safest default.

```bash
# DO — gets latest stable automatically
dotnet add package MassTransit
dotnet add package Serilog.AspNetCore
dotnet add package FluentValidation

# DON'T — hardcoded version likely outdated
dotnet add package MassTransit --version 9.0.0
dotnet add package Serilog.AspNetCore --version 8.0.0
```

- **For Microsoft.* packages targeting .NET 10, use 10.x versions.** These track the runtime: `Microsoft.EntityFrameworkCore` 10.x, `Microsoft.Extensions.*` 10.x, `Microsoft.AspNetCore.*` 10.x.
- **When writing `<PackageReference>` in .csproj files**, use `dotnet add package` first to resolve the correct version, then copy it into the project file.

## Central Package Management

- **Use `Directory.Packages.props` for multi-project solutions.** Centralizes all version pins in one file, preventing version drift across projects.
- When a solution has `Directory.Packages.props` with `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`, individual .csproj files must NOT specify `Version=` on `<PackageReference>`.

## Version Verification

- If unsure about the latest version, suggest the user verify on NuGet.org or run `dotnet package search <name>`.
- **Never downgrade a package** that is already in the project unless explicitly asked or there is a known compatibility issue.
- Prefer release versions over preview/RC unless the project explicitly targets preview features.

## .NET 10 Alignment

- Target framework: `net10.0`
- All `Microsoft.*` and `System.*` packages: 10.x
- EF Core and providers: 10.x
- ASP.NET Core testing packages: 10.x
- Third-party packages: latest stable that supports .NET 10
