---
name: project-structure
description: >
  .NET solution and project structure conventions. Covers .slnx format,
  Directory.Build.props, Directory.Packages.props for central package management,
  global usings, and naming conventions.
  Load this skill when setting up a new solution, adding projects, configuring
  build properties, or when the user mentions "solution structure", ".slnx",
  "Directory.Build.props", "central package management", "Directory.Packages.props",
  "global usings", ".editorconfig", "project layout", or "naming conventions".
---

# Project Structure

## Core Principles

1. **Central package management** — Use `Directory.Packages.props` to manage NuGet package versions in one place. No version numbers in individual `.csproj` files.
2. **Shared build properties** — Use `Directory.Build.props` for common settings (target framework, nullable, implicit usings). Don't repeat in every project.
3. **.slnx for solutions** — The new XML-based solution format is cleaner and more merge-friendly than the legacy `.sln` format.
4. **src/tests separation** — Source projects in `src/`, test projects in `tests/`. Clear boundary.

## Patterns

### Solution Layout

```
MyApp/
├── MyApp.slnx                       # Solution file
├── Directory.Build.props             # Shared MSBuild properties
├── Directory.Packages.props          # Central package management
├── .editorconfig                     # Code style rules
├── .gitignore
├── global.json                       # SDK version pinning
├── src/
│   ├── MyApp.Api/                    # Web API (entry point)
│   │   ├── MyApp.Api.csproj
│   │   ├── Program.cs
│   │   └── Features/
│   ├── MyApp.Domain/                 # Domain entities, value objects (optional)
│   │   └── MyApp.Domain.csproj
│   └── MyApp.Infrastructure/         # EF Core, external services (optional)
│       └── MyApp.Infrastructure.csproj
└── tests/
    └── MyApp.Api.Tests/
        └── MyApp.Api.Tests.csproj
```

### Directory.Build.props

Shared build properties applied to all projects in the directory tree.

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

### Directory.Packages.props (Central Package Management)

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup>
    <!-- ASP.NET Core -->
    <PackageVersion Include="MediatR" Version="13.0.0" />
    <PackageVersion Include="FluentValidation.DependencyInjectionExtensions" Version="12.0.0" />

    <!-- Data -->
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.0" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />

    <!-- Observability -->
    <PackageVersion Include="Serilog.AspNetCore" Version="9.0.0" />
    <PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.10.0" />

    <!-- Testing -->
    <PackageVersion Include="xunit.v3" Version="1.0.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
    <PackageVersion Include="Testcontainers.PostgreSql" Version="4.0.0" />
  </ItemGroup>
</Project>
```

### Project File (.csproj) with Central Package Management

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <!-- No TargetFramework here — inherited from Directory.Build.props -->

  <ItemGroup>
    <!-- No Version attribute — managed centrally -->
    <PackageReference Include="MediatR" />
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Serilog.AspNetCore" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MyApp.Domain\MyApp.Domain.csproj" />
    <ProjectReference Include="..\MyApp.Infrastructure\MyApp.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

### global.json (SDK Pinning)

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

### Global Usings

```csharp
// GlobalUsings.cs (per project, or in Directory.Build.props)
global using System.ComponentModel.DataAnnotations;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.AspNetCore.Http.HttpResults;
```

Or in Directory.Build.props:

```xml
<ItemGroup>
  <Using Include="System.ComponentModel.DataAnnotations" />
</ItemGroup>
```

### .slnx Solution Format

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/MyApp.Api/MyApp.Api.csproj" />
    <Project Path="src/MyApp.Domain/MyApp.Domain.csproj" />
    <Project Path="src/MyApp.Infrastructure/MyApp.Infrastructure.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/MyApp.Api.Tests/MyApp.Api.Tests.csproj" />
  </Folder>
</Solution>
```

### Naming Conventions

| Element | Convention | Example |
|---------|-----------|---------|
| Solution | `CompanyName.AppName` or `AppName` | `MyApp.slnx` |
| Project | `AppName.Layer` | `MyApp.Api`, `MyApp.Domain` |
| Namespace | Matches folder path | `MyApp.Api.Features.Orders` |
| Feature folder | PascalCase, plural | `Features/Orders/` |
| Test project | `ProjectName.Tests` | `MyApp.Api.Tests` |

## Anti-patterns

### Don't Scatter Package Versions

```xml
<!-- BAD — version in every .csproj, version drift -->
<PackageReference Include="MediatR" Version="12.0.0" />  <!-- in Project A -->
<PackageReference Include="MediatR" Version="13.0.0" />  <!-- in Project B -->

<!-- GOOD — central management, one version -->
<!-- Directory.Packages.props: <PackageVersion Include="MediatR" Version="13.0.0" /> -->
<!-- .csproj: <PackageReference Include="MediatR" /> -->
```

### Don't Repeat Build Properties

```xml
<!-- BAD — same properties in every .csproj -->
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>

<!-- GOOD — once in Directory.Build.props, inherited everywhere -->
```

### Don't Mix Source and Test Projects

```
# BAD — tests mixed with source
src/
  MyApp.Api/
  MyApp.Api.Tests/    # test project in src/

# GOOD — clear separation
src/
  MyApp.Api/
tests/
  MyApp.Api.Tests/
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| New solution | `.slnx` format |
| Package version management | `Directory.Packages.props` (central) |
| Shared build settings | `Directory.Build.props` |
| SDK version pinning | `global.json` |
| Common using directives | Global usings in `Directory.Build.props` |
| Small API (1-2 devs) | Single project (`MyApp.Api`) |
| Medium API (3-5 devs) | 2-3 projects (`Api`, `Domain`, `Infrastructure`) |
| Large / modular app | Module-per-project with shared `Contracts` |
