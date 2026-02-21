# ADR-001: Vertical Slice Architecture as the Default

## Status

Superseded by [ADR-005](005-multi-architecture.md)

> **Note:** VSA remains a fully supported architecture in dotnet-claude-kit. It is no longer the hardcoded default — the `architecture-advisor` skill now recommends the best fit based on project context. See ADR-005 for details.

## Context

dotnet-claude-kit needs a default architectural pattern for structuring .NET applications. The primary candidates are:

- **Clean Architecture (CA):** Organizes code into concentric layers (Domain, Application, Infrastructure, Presentation). Widely taught and documented. Enforces dependency inversion via project references.
- **Vertical Slice Architecture (VSA):** Organizes code by feature. Each feature is a self-contained slice containing its endpoint, handler, validation, data access, and DTOs. Cross-cutting concerns are shared via middleware, pipeline behaviors, or base classes.
- **N-Tier / Layered Architecture:** Traditional horizontal layering (Controller, Service, Repository, Data). Still the most common pattern in legacy .NET applications.

We evaluated these patterns against the following criteria:

1. **Cognitive load for AI-assisted development.** Claude Code operates on context windows. Patterns that keep related code close together reduce the number of files Claude needs to load.
2. **Feature velocity.** How quickly can a developer (or Claude) add a new feature end-to-end?
3. **Merge conflict frequency.** In team settings, patterns where features touch separate files reduce conflicts.
4. **Suitability for modern .NET.** Minimal APIs, records, and primary constructors favor compact, co-located code.
5. **Escape hatches.** Can the pattern scale to modular monoliths or be refactored toward Clean Architecture if the domain grows complex?

### Observations

- **Clean Architecture** requires touching 4+ projects and 6+ files to add a single endpoint. The abstraction layers (IRepository, IUnitOfWork) often add indirection without value in small-to-medium apps. However, it shines in complex domains with rich business logic that benefits from strict dependency inversion.
- **Vertical Slice Architecture** requires 1-2 files per feature. Related code lives together. Adding a feature is a self-contained operation. It maps naturally to minimal API endpoint groups and MediatR/Wolverine handlers.
- **N-Tier** leads to "God service" classes that accumulate methods for every operation on an entity. It does not scale well and encourages tight coupling between layers.

### Guiding principle

dotnet-claude-kit is opinionated. We pick the best default for the majority of .NET applications and provide escape hatches for the rest. We do not present all options as equal.

## Decision

**Vertical Slice Architecture is the default architectural pattern in dotnet-claude-kit.**

All templates, skills, agents, and code examples use VSA as the primary structure. Features are organized into feature folders, each containing:

```
Features/
  Orders/
    CreateOrder/
      CreateOrderEndpoint.cs    # Minimal API endpoint
      CreateOrderHandler.cs     # Business logic (MediatR, Wolverine, or raw)
      CreateOrderRequest.cs     # Input DTO
      CreateOrderValidator.cs   # FluentValidation rules
    GetOrder/
      ...
```

The handler approach (MediatR, Wolverine, or plain handler classes) is left to the user's choice. dotnet-claude-kit provides patterns for all three.

### When to deviate

- **Complex domains with rich business logic:** If you find yourself needing a shared Domain layer with aggregates, value objects, and domain events that multiple features depend on, introduce a `Domain` project. This is a natural evolution, not a contradiction of VSA.
- **Modular monoliths:** Each module uses VSA internally. Modules communicate via explicit contracts (events, interfaces). dotnet-claude-kit supports this as an optional scaling pattern.
- **Shared infrastructure:** Cross-cutting concerns (auth, logging, error handling, persistence configuration) belong in a shared `Infrastructure` or `ServiceDefaults` project, not duplicated per feature.

## Consequences

### Positive

- **Reduced context window usage.** Claude loads 1-2 files per feature instead of 4-6 across layers. This directly improves AI-assisted development quality.
- **Faster feature delivery.** Adding a new feature is a self-contained folder creation. No need to update repository interfaces, service abstractions, or mapping profiles in separate layers.
- **Fewer merge conflicts.** Features are isolated in separate folders and files. Two developers working on different features rarely touch the same files.
- **Natural fit for minimal APIs.** .NET 10 minimal APIs map directly to thin endpoint classes that delegate to handlers.
- **Easy to understand.** New team members can read a single folder to understand an entire feature.

### Negative

- **Less enforcement of architectural boundaries.** VSA relies on team discipline (and linting) rather than project reference constraints to prevent features from reaching into each other's internals. Clean Architecture enforces boundaries at the compiler level.
- **Cross-feature logic requires careful placement.** When multiple features share business logic, developers must decide where that shared code lives. Without a `Domain` layer, shared logic can drift into utility classes.
- **Less industry recognition.** Clean Architecture has more books, courses, and blog posts. Some teams may resist VSA because it is less widely documented.
- **Not all teams are familiar with it.** Developers coming from traditional layered architectures may need time to adjust to feature-folder thinking.

### Mitigations

- The `vertical-slice` skill includes a decision guide for when to introduce a Domain layer or split into modules.
- The `dotnet-architect` agent detects when a project's complexity warrants deviating from pure VSA and recommends appropriate structural changes.
- The `project-structure` skill provides conventions for shared code placement.
