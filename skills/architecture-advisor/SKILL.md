---
name: architecture-advisor
description: >
  Architecture selection advisor for .NET applications. Asks structured questions
  about domain complexity, team size, system lifetime, compliance, and integration
  needs, then recommends the best-fit architecture: Vertical Slice, Clean Architecture,
  DDD + Clean Architecture, or Modular Monolith.
  Load this skill when the user asks "which architecture", "choose architecture",
  "set up project", "new project", "architecture decision", "restructure", or
  "how should I organize". Always load BEFORE any architecture-specific skill.
---

# Architecture Advisor

## Core Principles

1. **Ask before recommending** — Never prescribe an architecture without understanding the project. Run the questionnaire first to gather context about domain, team, lifetime, and constraints.
2. **Right-size the architecture** — The best architecture is the simplest one that handles the project's actual complexity. CRUD apps do not need DDD. Startups do not need Clean Architecture. Match complexity to real requirements, not aspirations.
3. **Architecture is not permanent** — Every architecture has an evolution path. Start simple and add structure when complexity demands it. Document the decision so the team knows when to evolve.
4. **Four supported architectures** — dotnet-claude-kit provides first-class patterns for Vertical Slice Architecture (VSA), Clean Architecture (CA), DDD + Clean Architecture, and Modular Monolith. Each has specific strengths and trade-offs.

## The Architecture Questionnaire

Before recommending an architecture, ask questions across these 6 categories. Not every question applies to every project — skip irrelevant ones.

### Category 1: Domain Complexity

| # | Question | Low Signal | High Signal |
|---|----------|-----------|-------------|
| 1 | How many distinct business entities does the system manage? | < 10 entities | 20+ entities with relationships |
| 2 | Do business rules involve multiple entities interacting? | Rules are per-entity CRUD | Complex invariants across entity groups |
| 3 | Are there business workflows with multiple steps? | Simple request → response | Sagas, approval chains, state machines |
| 4 | Do domain experts use specialized vocabulary? | Generic terms (create, update) | Ubiquitous language (underwrite, adjudicate) |

### Category 2: Team & Organization

| # | Question | Low Signal | High Signal |
|---|----------|-----------|-------------|
| 5 | How large is the development team? | 1-3 developers | 8+ developers, multiple teams |
| 6 | Do different teams own different parts of the system? | Single team owns everything | Teams aligned to business domains |
| 7 | What is the team's experience level with .NET? | Junior or mixed | Senior, experienced with patterns |

### Category 3: System Lifetime & Scale

| # | Question | Low Signal | High Signal |
|---|----------|-----------|-------------|
| 8 | Expected system lifetime? | < 2 years, MVP/prototype | 5+ years, long-lived product |
| 9 | How many concurrent users or requests per second? | < 100 RPS | 1000+ RPS, variable load |
| 10 | Will the system need to scale independently by feature area? | Uniform load | Hot spots need independent scaling |

### Category 4: Regulatory & Compliance

| # | Question | Low Signal | High Signal |
|---|----------|-----------|-------------|
| 11 | Are there audit trail or compliance requirements? | Basic logging sufficient | Full audit trail, SOX/HIPAA/PCI |
| 12 | Do different parts of the system have different security boundaries? | Single auth boundary | Multi-tenant, data isolation |

### Category 5: Existing Codebase

| # | Question | Low Signal | High Signal |
|---|----------|-----------|-------------|
| 13 | Is this greenfield or brownfield? | Greenfield, starting fresh | Brownfield, migrating from legacy |
| 14 | Are there existing architectural patterns the team follows? | No established patterns | Strong conventions in place |

### Category 6: Integration Complexity

| # | Question | Low Signal | High Signal |
|---|----------|-----------|-------------|
| 15 | How many external systems does this integrate with? | 0-2 simple APIs | 5+ systems with complex contracts |
| 16 | Are there event-driven or async communication needs? | Synchronous request/response | Event sourcing, pub/sub, eventual consistency |

## Decision Matrix

Map questionnaire answers to architecture recommendations:

| Profile | Recommended Architecture | Why |
|---------|------------------------|-----|
| Low domain complexity, small team, short lifetime | **Vertical Slice Architecture** | Minimal ceremony, fast feature delivery, easy to understand |
| Low-medium domain complexity, any team size, API-focused | **Vertical Slice Architecture** | Feature cohesion, one file per operation, natural fit for minimal APIs |
| Medium domain complexity, medium team, long lifetime | **Clean Architecture** | Enforced boundaries via project references, testable domain, clear dependency direction |
| High domain complexity, specialized vocabulary, complex invariants | **DDD + Clean Architecture** | Aggregates protect invariants, value objects model domain concepts, domain events decouple side effects |
| Multiple bounded contexts, team-per-domain, independent deployment potential | **Modular Monolith** | Module isolation, independent data stores, evolution path to microservices |
| Brownfield with existing layered architecture | **Clean Architecture** | Familiar to teams coming from N-tier, preserves layer separation with better dependency direction |

### When Signals Conflict

If signals point to different architectures:

1. **Default to simpler** — When in doubt, start with VSA and evolve
2. **Domain complexity wins** — High domain complexity overrides team size and lifetime signals
3. **Team familiarity matters** — A team experienced with CA will be more productive with CA than learning VSA, even if VSA is technically simpler
4. **Compliance drives structure** — Regulatory requirements often force stricter boundaries (CA or DDD)

## Architecture Summaries

### Vertical Slice Architecture (VSA)

Organize by feature, not by layer. Each operation is a self-contained slice.

```
src/MyApp.Api/
  Features/
    Orders/CreateOrder.cs    # Request + Handler + Response + Endpoint
    Orders/GetOrder.cs
  Common/
    Behaviors/ValidationBehavior.cs
    Persistence/AppDbContext.cs
```

**Best for:** CRUD-heavy apps, APIs, MVPs, small-medium teams, short-medium lifetime.
**Load skill:** `vertical-slice`

### Clean Architecture (CA)

Concentric layers with dependency inversion. Domain at the center, infrastructure at the edge.

```
src/
  MyApp.Domain/              # Entities, interfaces, domain logic
  MyApp.Application/         # Use cases, DTOs, validation
  MyApp.Infrastructure/      # EF Core, external services
  MyApp.Api/                 # Endpoints, middleware
```

**Best for:** Medium complexity, long-lived systems, teams familiar with layered patterns.
**Load skill:** `clean-architecture`

### DDD + Clean Architecture

Clean Architecture with tactical DDD patterns: aggregates, value objects, domain events.

```
src/
  MyApp.Domain/              # Aggregates, value objects, domain events, domain services
  MyApp.Application/         # Use cases orchestrating aggregates
  MyApp.Infrastructure/      # Persistence, external service adapters
  MyApp.Api/                 # Thin endpoints
```

**Best for:** Complex domains, specialized vocabulary, strict invariants, experienced teams.
**Load skill:** `ddd` + `clean-architecture`

### Modular Monolith

Independent modules in a single deployable unit, each with its own architecture internally.

```
src/
  MyApp.Host/                # Wires modules together
  Modules/
    Orders/                  # Own features, own DbContext, own architecture
    Catalog/                 # Can use VSA, CA, or DDD internally
  MyApp.Shared/              # Integration event contracts only
```

**Best for:** Multiple bounded contexts, team-per-domain, future microservices extraction.
**Load template:** `modular-monolith`

## Evolution Paths

Architecture is not a one-time decision. Systems evolve. Here are the common migration paths:

| From | To | Trigger | How |
|------|----|---------|-----|
| VSA | CA | Domain logic growing beyond handlers | Extract Domain + Application layers, keep features as use cases |
| VSA | Modular Monolith | Multiple bounded contexts emerging | Group features into modules, add module boundaries |
| CA | DDD + CA | Invariants becoming complex, primitive obsession | Introduce aggregates, value objects, domain events |
| Monolith | Modular Monolith | Teams stepping on each other, shared database coupling | Split into modules with own DbContexts and schemas |
| Modular Monolith | Microservices | Independent scaling needs, independent deployment | Extract modules into separate deployable services |

## Anti-patterns

### Picking Clean Architecture for a CRUD App

```
// BAD — 4 projects, 6+ files per feature for simple CRUD
src/MyApp.Domain/Entities/Product.cs
src/MyApp.Application/Products/CreateProduct/CreateProductCommand.cs
src/MyApp.Application/Products/CreateProduct/CreateProductHandler.cs
src/MyApp.Application/Products/CreateProduct/CreateProductValidator.cs
src/MyApp.Infrastructure/Persistence/ProductRepository.cs
src/MyApp.Api/Endpoints/ProductEndpoints.cs

// GOOD — VSA: 1 file for a simple CRUD feature
src/MyApp.Api/Features/Products/CreateProduct.cs
```

### DDD Everywhere

```
// BAD — value objects and aggregates for a settings table
public class UserSettings : AggregateRoot  // overkill
{
    public ThemeName Theme { get; private set; }  // value object for "dark"/"light"?
    public void ChangeTheme(ThemeName theme) { /* domain event? really? */ }
}

// GOOD — simple entity for simple data
public class UserSettings
{
    public Guid UserId { get; init; }
    public string Theme { get; set; } = "light";
}
```

### Premature Microservices

```
// BAD — splitting into 5 microservices on day one with 2 developers
OrderService (own repo, own DB, own CI/CD)
CatalogService (own repo, own DB, own CI/CD)
IdentityService (own repo, own DB, own CI/CD)
NotificationService (own repo, own DB, own CI/CD)
GatewayService (own repo, own CI/CD)

// GOOD — start as a modular monolith, extract when you have evidence
src/
  Modules/Orders/
  Modules/Catalog/
  Modules/Identity/
  Modules/Notifications/
```

### Skipping the Questionnaire

```
// BAD — "I always use Clean Architecture"
User: "Set up a new project for a todo app"
Agent: *immediately scaffolds 4-project CA solution*

// GOOD — ask first, then recommend
User: "Set up a new project for a todo app"
Agent: "Let me ask a few questions about your project to recommend the best architecture..."
Agent: *runs questionnaire, recommends VSA for low-complexity app*
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| New project, unknown requirements | Run the questionnaire |
| Simple CRUD API, 1-3 developers | VSA |
| Medium complexity, long-lived, experienced team | Clean Architecture |
| Complex domain, specialized vocabulary | DDD + Clean Architecture |
| Multiple bounded contexts, multiple teams | Modular Monolith |
| Existing N-tier codebase, needs modernization | Clean Architecture (familiar migration) |
| MVP / startup, speed is priority | VSA |
| Regulatory / compliance-heavy | Clean Architecture or DDD (enforced boundaries) |
| Will need independent scaling later | Modular Monolith (extraction-ready) |
