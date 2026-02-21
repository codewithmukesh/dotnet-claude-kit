# [Project Name] -- Class Library / NuGet Package

> Copy this file into your project root and customize the sections below.

## Project Context

This is a reusable .NET 10 class library distributed as a NuGet package. The library exposes a well-defined public API surface while keeping implementation details internal. It is designed to be consumed by other .NET projects with minimal dependencies and maximum compatibility.

## Tech Stack

- **.NET 10** / C# 14
- **xUnit v3** -- testing
- **Verify** -- snapshot testing for complex outputs (optional)
- **BenchmarkDotNet** -- performance benchmarks (optional)
- **Microsoft.Extensions.DependencyInjection.Abstractions** -- for DI registration extensions (optional)

## Architecture

```
src/
  [ProjectName]/
    [ProjectName].csproj
    Abstractions/                  # Public interfaces and abstract types
    Extensions/                    # Public extension methods, DI registration helpers
    Models/                        # Public DTOs, options, value objects
    Internal/                      # Internal implementation details
    [FeatureArea]/                 # Public feature-specific types
tests/
  [ProjectName].Tests/
    [ProjectName].Tests.csproj
    [FeatureArea]/
      [TypeOrFeature]Tests.cs
    Fixtures/                      # Shared test fixtures
```

### Public API Surface

The public API is the contract with consumers. Every `public` type and member is part of this contract.

```csharp
// GOOD -- public interface defines the contract
public interface ITokenizer
{
    IReadOnlyList<Token> Tokenize(ReadOnlySpan<char> input);
}

// GOOD -- public options type for configuration
public class TokenizerOptions
{
    public required bool PreserveWhitespace { get; init; }
    public int MaxTokens { get; init; } = 1000;
}

// GOOD -- DI registration extension for consumers
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTokenizer(
        this IServiceCollection services,
        Action<TokenizerOptions>? configure = null)
    {
        services.AddSingleton<ITokenizer, DefaultTokenizer>();
        if (configure is not null)
        {
            services.Configure(configure);
        }
        return services;
    }
}
```

### Internal Implementation

Implementation details are `internal` and invisible to consumers.

```csharp
// GOOD -- internal implementation hidden from consumers
internal class DefaultTokenizer(IOptions<TokenizerOptions> options) : ITokenizer
{
    public IReadOnlyList<Token> Tokenize(ReadOnlySpan<char> input)
    {
        // Implementation details stay internal
    }
}
```

Use `[assembly: InternalsVisibleTo("[ProjectName].Tests")]` in the library project to allow the test project to access internal types.

## Coding Standards

- **C# 14 features** -- Use primary constructors, collection expressions, `field` keyword, records, pattern matching
- **File-scoped namespaces** -- Always
- **XML documentation on all public members** -- Every public type, method, property, and parameter must have `<summary>`, `<param>`, `<returns>`, and `<exception>` tags as appropriate
- **`var` for obvious types** -- Use explicit types when the type is not clear from context
- **Naming** -- PascalCase for public members, `_camelCase` for private fields, suffix async methods with `Async`
- **No regions** -- Ever
- **No comments for obvious code** -- Only comment "why", never "what"
- **Strong naming** -- Optional. Enable with `<SignAssembly>` in the .csproj if consumers require strong-named dependencies
- **`<GenerateDocumentationFile>true</GenerateDocumentationFile>`** -- Enable in the .csproj so missing XML docs produce build warnings

### XML Documentation Example

```csharp
/// <summary>
/// Splits the input text into a sequence of tokens using the configured rules.
/// </summary>
/// <param name="input">The text to tokenize. Must not be empty.</param>
/// <returns>
/// A read-only list of tokens extracted from <paramref name="input"/>.
/// Returns an empty list if the input contains no recognizable tokens.
/// </returns>
/// <exception cref="ArgumentException">
/// Thrown when <paramref name="input"/> is empty.
/// </exception>
IReadOnlyList<Token> Tokenize(ReadOnlySpan<char> input);
```

## Skills

Load these dotnet-claude-kit skills for context:

- `modern-csharp` -- C# 14 language features and idioms
- `architecture-advisor` -- Architecture guidance for structuring the library
- `project-structure` -- Solution layout, Directory.Build.props, central package management
- `testing` -- xUnit v3, test patterns, snapshot testing
- `ci-cd` -- GitHub Actions / Azure DevOps pipelines with pack + push stages

## MCP Tools

Use `cwm-roslyn-navigator` tools to minimize token consumption:

- **Before modifying a type** -- Use `find_symbol` to locate it, `get_public_api` to understand its surface
- **Before adding a reference** -- Use `find_references` to understand existing usage
- **To understand architecture** -- Use `get_project_graph` to see project dependencies
- **To find implementations** -- Use `find_implementations` instead of grep for interface/abstract class implementations
- **To check for errors** -- Use `get_diagnostics` after changes

## Commands

```bash
# Build
dotnet build

# Run tests
dotnet test

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Pack (create NuGet package)
dotnet pack src/[ProjectName] -c Release -o ./nupkg

# Push to NuGet.org
dotnet nuget push ./nupkg/*.nupkg --api-key <API_KEY> --source https://api.nuget.org/v3/index.json

# Push to a private feed
dotnet nuget push ./nupkg/*.nupkg --api-key <API_KEY> --source https://pkgs.dev.azure.com/<org>/_packaging/<feed>/nuget/v3/index.json

# Format check
dotnet format --verify-no-changes

# Validate the package contents
dotnet pack src/[ProjectName] -c Release -o ./nupkg && dotnet nuget verify ./nupkg/*.nupkg
```

## NuGet Package Configuration

Ensure the `.csproj` includes proper package metadata:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- Package metadata -->
    <PackageId>[ProjectName]</PackageId>
    <Version>1.0.0</Version>
    <Authors>[Author]</Authors>
    <Description>[Description of the library]</Description>
    <PackageTags>[tag1];[tag2]</PackageTags>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/[org]/[repo]</PackageProjectUrl>
    <RepositoryUrl>https://github.com/[org]/[repo]</RepositoryUrl>

    <!-- Documentation and source linking -->
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>

    <!-- Deterministic builds for reproducibility -->
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.SourceLink.GitHub" PrivateAssets="All" />
  </ItemGroup>
</Project>
```

## Anti-patterns

Do NOT generate code that:

- **Exposes implementation details** -- Keep internal types `internal`. Do not leak implementation interfaces, helper classes, or infrastructure types into the public API
- **Takes unnecessary dependencies** -- Minimize NuGet dependencies. Each dependency is a transitive burden on every consumer. Prefer framework types over third-party libraries when the functionality is similar
- **Uses `DateTime.Now`** -- Use `TimeProvider` injection instead
- **Creates `new HttpClient()`** -- Use `IHttpClientFactory`
- **Uses `async void`** -- Always return `Task`
- **Blocks with `.Result` or `.Wait()`** -- Await instead
- **Throws from constructors for normal flow** -- Use factory methods or the Result pattern for operations that can fail
- **Breaks semantic versioning** -- Removing or renaming a public member is a breaking change (major version bump). Adding members to a public interface is a breaking change for implementers
- **Uses `static` mutable state** -- Class libraries must be thread-safe. Avoid static mutable fields
- **Returns mutable collections from public API** -- Return `IReadOnlyList<T>`, `IReadOnlyDictionary<TKey, TValue>`, or `ImmutableArray<T>` instead of `List<T>` or `Dictionary<TKey, TValue>`
- **Catches bare `Exception`** -- Catch specific types. Let the consumer's global handler deal with unexpected exceptions
- **Uses string interpolation in log messages** -- Use structured logging templates
- **Depends on ASP.NET Core from a general-purpose library** -- If the library is not ASP.NET-specific, depend on `Microsoft.Extensions.*` abstractions instead of `Microsoft.AspNetCore.*` packages
