---
name: migrate
description: >
  Guided, safe EF Core migration workflow. Reviews pending model changes, generates
  a migration with a descriptive name, reviews the generated SQL for safety, and
  applies with rollback readiness. Invoke when: "add migration", "update database",
  "create migration", "schema change", "new table", "rename column".
---

# /migrate

## What

Guided EF Core migration workflow that takes you from model change to applied
migration safely. It reviews what changed in your entity model, creates a
properly named migration, reviews the generated SQL for data loss and locking
risks, and applies it with a documented rollback path.

This command enforces the "one logical change per migration" principle and
prevents the most common migration mistakes: blind application, data loss,
and un-rollbackable batched changes.

## When

- After modifying entity classes, DbContext configuration, or relationships
- User says: "add migration", "update database", "create migration"
- After adding a new entity or table
- After renaming, removing, or changing column types
- After modifying indexes or constraints
- When the user needs to generate SQL scripts for DBA review

## How

### Step 1: Assess Current State

```bash
dotnet ef migrations list --project <InfraProject> --startup-project <ApiProject>
```

Check for:
- Pending migrations not yet applied to the development database
- Model changes that have not been captured in a migration yet

### Step 2: Review Model Changes

Use MCP tools to understand what changed:

```
find_symbol(name: entity or DbSet name) -- locate the changed entity
get_type_hierarchy(typeName: entity) -- check inheritance changes
find_references(symbolName: changed property) -- assess downstream impact
```

Confirm the change is a single logical unit. If not, guide the user to split
into multiple migrations.

### Step 3: Generate Migration

Create with a descriptive name that explains the change, not the entity:

```bash
dotnet ef migrations add <DescriptiveName> --project <InfraProject> --startup-project <ApiProject>
```

Naming convention: `Add|Remove|Rename|Modify` + `WhatChanged`
- `AddOrderShippingAddress`
- `RenameCustomerEmailToContactEmail`
- `AddIndexOnOrderCreatedAt`
- `RemoveDeprecatedProductSku`

### Step 4: Review Generated SQL

```bash
dotnet ef migrations script --idempotent --project <InfraProject> --startup-project <ApiProject>
```

Flag and report:
- **DROP COLUMN / DROP TABLE** -- Confirm data loss is intentional
- **ALTER COLUMN** type changes -- Check for precision loss or truncation
- **Large table ALTER** -- Warn about potential lock duration
- **Missing DEFAULT** -- New non-nullable columns need defaults for existing rows

If data transformation is needed, guide the user through a multi-step migration
(add nullable column, copy data, set non-nullable, drop old column).

### Step 5: Apply Migration

```bash
dotnet ef database update --project <InfraProject> --startup-project <ApiProject>
```

### Step 6: Verify

```bash
dotnet build
dotnet test
```

Document the rollback command for this migration:

```bash
dotnet ef database update <PreviousMigrationName> --project <InfraProject> --startup-project <ApiProject>
```

## Invokes

| Type  | Name                  | Purpose                                      |
|-------|-----------------------|----------------------------------------------|
| Skill | `migration-workflow`  | Safe migration patterns and rollback strategy |
| Skill | `ef-core`             | Entity configuration and query patterns       |
| Agent | `ef-core-specialist`  | Database design and migration expertise       |

### MCP Tools Used

- `find_symbol` -- Locate entity types, DbContext, and configurations
- `get_type_hierarchy` -- Understand entity inheritance for TPH/TPT/TPC
- `find_references` -- Assess impact of schema changes on queries
- `get_diagnostics` -- Catch EF Core warnings after migration generation

## Example

```
User: /migrate

Claude: Checking migration state...
  Last applied: 20250710_AddOrderTable
  Model changes detected: new ShippingAddress property on Order entity.

  Creating migration: AddOrderShippingAddress
  Reviewing generated SQL...

  SQL Review:
  - ALTER TABLE "Orders" ADD "ShippingStreet" text NULL
  - ALTER TABLE "Orders" ADD "ShippingCity" text NULL
  - ALTER TABLE "Orders" ADD "ShippingZip" text NULL
  No data loss. No locking risk. Safe to apply.

  Applying migration... Done.
  Build: green. Tests: 34/34 passed.

  Rollback command (if needed):
  dotnet ef database update AddOrderTable --project src/Infrastructure --startup-project src/Api
```

## Related

- `/build-fix` -- Fix build errors that may arise after migration changes
