---
name: scaffold
description: >
  Architecture-aware feature scaffolding for .NET 10 projects. Detects the
  project's architecture (VSA, Clean Architecture, DDD, Modular Monolith) and
  generates complete feature slices with all required layers: endpoint, handler,
  validator, DTOs, EF configuration, and integration tests.
  Use when: "scaffold", "create feature", "add feature", "new endpoint",
  "generate", "add entity", "scaffold a module", "add module".
---

# /scaffold -- Architecture-Aware Feature Scaffolding

## What

Generates a complete feature with all required files based on the project's
architecture. Never generates half a feature -- every scaffold includes the
endpoint, handler, validation, DTOs, EF configuration, and at least one
integration test as a single unit.

Supported architectures:
- **Vertical Slice Architecture (VSA)** -- Single-file features in `Features/`
- **Clean Architecture (CA)** -- Files across Domain, Application, Infrastructure, API layers
- **DDD + Clean Architecture** -- Aggregate roots, value objects, domain events, plus CA layers
- **Modular Monolith** -- Self-contained modules with internal layering

## When

- "Scaffold a [feature name]", "create an endpoint for", "add a feature"
- "Generate CRUD for", "add entity", "new module"
- Starting a new feature after `/plan` has produced an approved plan
- Any time the user wants a complete, working feature skeleton

## How

### Step 1: Detect Architecture

Use the `architecture-advisor` skill to determine the project's architecture:
- Examine folder structure, project references, and existing patterns
- If architecture is ambiguous, ask the user rather than guessing
- Load the corresponding architecture skill (vertical-slice, clean-architecture, ddd)

### Step 2: Learn Conventions

Before generating, use the `convention-learner` and MCP tools to check:
- Naming patterns (`*Handler`, `*Service`, `*Endpoint`, `*Command`, `*Query`)
- Folder structure and file organization
- Access modifiers, sealed/unsealed conventions
- Existing validation approach (FluentValidation, data annotations, manual)
- Test project structure and naming (`*Tests`, `*IntegrationTests`)

Match what exists. Do not impose new conventions on an established codebase.

### Step 3: Generate All Layers

Generate files for every layer the architecture requires. For example, in VSA:

| File | Purpose |
|------|---------|
| `Features/{Name}/Create{Name}.cs` | Command, handler, response, endpoint |
| `Features/{Name}/Get{Name}.cs` | Query, handler, response, endpoint |
| `Features/{Name}/{Name}Configuration.cs` | EF Core entity configuration |
| `Features/{Name}/Create{Name}Validator.cs` | FluentValidation rules |
| `Tests/Features/{Name}/Create{Name}Tests.cs` | Integration test |
| `Tests/Features/{Name}/Get{Name}Tests.cs` | Integration test |

### Step 4: Verify

Run a quick build check to ensure the scaffolded code compiles:
```bash
dotnet build --no-restore
```

## Example

```
User: /scaffold a Product Catalog feature with CRUD operations

Claude: Detected architecture: Vertical Slice Architecture

Generating Product Catalog feature...

Created files:
  src/Features/Products/CreateProduct.cs     -- Command + handler + endpoint
  src/Features/Products/GetProduct.cs        -- Query by ID + handler + endpoint
  src/Features/Products/ListProducts.cs      -- Paginated list + handler + endpoint
  src/Features/Products/UpdateProduct.cs     -- Command + handler + endpoint
  src/Features/Products/DeleteProduct.cs     -- Command + handler + endpoint
  src/Features/Products/ProductConfig.cs     -- EF Core configuration
  src/Features/Products/CreateProductValidator.cs
  src/Features/Products/UpdateProductValidator.cs
  tests/Features/Products/CreateProductTests.cs
  tests/Features/Products/GetProductTests.cs
  tests/Features/Products/ListProductsTests.cs

Build check: PASS

All files follow your existing conventions (sealed handlers,
primary constructors, TypedResults return types).
```

## Related

- `/plan` -- Plan the feature before scaffolding
- `/tdd` -- Use TDD to drive the implementation after scaffolding the skeleton
