# Modular Monolith Template

## When to Use

Use this template when you're building:

- A modular monolith with multiple bounded contexts deployed as a single unit
- An application that needs strict module boundaries but doesn't yet justify separate microservices
- A system where modules communicate through integration events rather than direct method calls
- A monolith designed for eventual extraction into microservices

## How to Use

1. Copy `CLAUDE.md` into the root of your .NET modular monolith project
2. Replace `[ProjectName]` with your actual project name
3. Replace `[Module]` references with your actual module names (e.g., Orders, Catalog, Identity)
4. Update the **Tech Stack** section to match your dependencies
5. Choose your handler approach (MediatR, Wolverine, or raw handlers) and update the feature file convention accordingly
6. Remove any skills references that don't apply to your project

## What's Included

This template configures Claude Code to:

- Follow a module-per-project structure with each module using its own internal architecture
- Enforce strict module boundaries — no direct cross-module references
- Use one DbContext per module with separate database schemas
- Use MassTransit integration events for inter-module communication with the transactional outbox
- Keep the Shared project thin — only contracts, primitives, and cross-cutting infrastructure
- Use .NET 10 / C# 14 modern patterns
- Prefer minimal APIs with `TypedResults` and `MapGroup`
- Write tests scoped to individual modules and cross-module integration tests
- Use the Result pattern for error handling
- Follow structured logging with Serilog

## Customization

### Switching Handler Approach

The template defaults to MediatR for intra-module dispatch. To switch:

**Wolverine:** Remove `IRequest<T>` references, use convention-based `Handle` methods. Wolverine also doubles as a message bus, which can replace MassTransit for inter-module messaging. See the `vertical-slice` and `messaging` skills.

**Raw handlers:** Remove MediatR entirely, register handler classes in DI. See the `vertical-slice` skill for raw handler patterns.

### Switching Messaging

The template defaults to MassTransit for inter-module events. Alternatives:

**Wolverine:** If you chose Wolverine for handlers, you can also use it as the message bus, simplifying the stack to a single library for both intra-module dispatch and inter-module messaging.

**In-process mediator:** For simpler applications where all modules run in the same process and you don't need durable messaging, you can use MediatR notifications. Note that this sacrifices the reliability of the transactional outbox.

### Adding Modules

When adding a new module:

1. Create a new class library: `src/Modules/[NewModule]/[ProjectName].Modules.[NewModule]/`
2. Add `Features/`, `Persistence/`, and `Consumers/` folders
3. Create `[NewModule]Module.cs` with `Add[NewModule]Module` and `Map[NewModule]Module` extension methods
4. Create a module-scoped `[NewModule]DbContext` with its own schema
5. Register the module in `Program.cs` of the Host project
6. Add integration event records to the Shared contracts project as needed
7. Create a corresponding test project under `tests/Modules/`

### Database Strategy

The template defaults to schema-per-module within a single database. Alternatives:

**Separate databases per module:** Change each module's connection string in `appsettings.json`. This provides stronger isolation but adds operational complexity.

**Single schema (not recommended):** Loses data isolation between modules. Only consider this for very small applications where module boundaries are informal.
