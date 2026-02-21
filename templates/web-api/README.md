# Web API Template

## When to Use

Use this template when you're building:
- A REST API with ASP.NET Core Minimal APIs
- A backend service with HTTP endpoints
- A microservice in a distributed system

## How to Use

1. Copy `CLAUDE.md` into the root of your .NET Web API project
2. Replace `[ProjectName]` with your actual project name
3. Update the **Tech Stack** section to match your dependencies
4. Choose your handler approach (MediatR, Wolverine, or raw handlers) and update the feature file convention accordingly
5. Remove any skills references that don't apply to your project

## What's Included

This template configures Claude Code to:
- Choose an architecture (VSA, Clean Architecture, or DDD) via the architecture-advisor skill
- Use .NET 10 / C# 14 modern patterns
- Prefer minimal APIs with `TypedResults` and `MapGroup`
- Use EF Core directly (no repository pattern)
- Write integration tests with `WebApplicationFactory` + Testcontainers
- Use the Result pattern for error handling
- Follow structured logging with Serilog

## Customization

### Switching Handler Approach

The template defaults to MediatR. To switch:

**Wolverine:** Remove `IRequest<T>` references, use convention-based `Handle` methods. See the `vertical-slice` skill for Wolverine patterns.

**Raw handlers:** Remove MediatR entirely, register handler classes in DI. See the `vertical-slice` skill for raw handler patterns.

### Adding Authentication

Uncomment the `authentication` skill reference and add your auth configuration to the Tech Stack section.

### Adding Caching

Add `caching` to the skills list and configure HybridCache in your project.
