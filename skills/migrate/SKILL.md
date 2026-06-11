---
name: migrate
description: >
  Guided, safe migration workflow covering EF Core schema migrations, .NET
  version upgrades, and NuGet dependency updates — each with rollback
  strategies and verification steps. Invoke when: "add migration",
  "update database", "create migration", "schema change", "new table",
  "rename column", "upgrade nuget", "update packages", "dependency update",
  "version upgrade", ".NET upgrade".
---

# /migrate

## What

The single migration workflow for three change types, with EF Core schema
migrations as the primary flow:

1. **EF Core schema** — review pending model changes, generate a descriptively
   named migration, review the SQL for data loss and locking risks, apply with
   a documented rollback path.
2. **.NET version upgrade** — phased TFM/SDK/package upgrade with verification
   at each phase.
3. **NuGet updates** — incremental, one-package-at-a-time updates so breakage
   is always attributable.

Shared principles: verify before applying, rollback plan always, test after
every step, one logical change per migration.

## When

- After modifying entity classes, DbContext configuration, or relationships
- "add migration", "update database", "create migration", "new table", "rename column"
- When generating SQL scripts for DBA review
- "upgrade to .NET 10", "version upgrade", ".NET upgrade"
- "upgrade nuget", "update packages", "dependency update", vulnerable package alerts

## How

First, classify the request: schema change → Flow A; framework upgrade → Flow B;
package update → Flow C. Then follow that flow end to end.

### Flow A: EF Core Schema Migration (primary)

**Step 1: Assess current state**

```bash
dotnet ef migrations list --project <InfraProject> --startup-project <ApiProject>
```

Check for pending migrations and uncaptured model changes.

**Step 2: Review model changes**

Use MCP tools instead of reading whole files:

```
find_symbol(name: entity or DbSet)        -- locate the changed entity
get_type_hierarchy(typeName: entity)      -- check TPH/TPT/TPC inheritance changes
find_references(symbolName: property)     -- assess downstream query impact
```

Confirm the change is one logical unit. If not, split into multiple migrations —
mixed migrations make rollback all-or-nothing.

**Step 3: Generate migration**

Name describes the change, not the entity: `Add|Remove|Rename|Modify` + `WhatChanged`.

```bash
# GOOD
dotnet ef migrations add AddOrderShippingAddress --project <Infra> --startup-project <Api>
# BAD — names the entity, not the change
dotnet ef migrations add Order
```

**Step 4: Review generated SQL**

`database update` has no dry-run flag — preview by generating an idempotent
script and reading it:

```bash
dotnet ef migrations script --idempotent --project <Infra> --startup-project <Api>
```

Flag and report:
- **DROP COLUMN / DROP TABLE** — confirm data loss is intentional
- **ALTER COLUMN** type changes — check precision loss or truncation
- **ALTER on large tables** — warn about lock duration
- **New non-nullable columns** — need defaults for existing rows

If data must survive a rename/retype, use a multi-step migration with raw SQL:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<string>("ContactEmail", "Customers", nullable: true);
    migrationBuilder.Sql("UPDATE \"Customers\" SET \"ContactEmail\" = \"Email\"");
    migrationBuilder.AlterColumn<string>("ContactEmail", "Customers", nullable: false);
    migrationBuilder.DropColumn("Email", "Customers");
}
```

**Step 5: Apply and verify**

```bash
dotnet ef database update --project <Infra> --startup-project <Api>
dotnet build && dotnet test   # integration tests catch schema mismatches
```

**Step 6: Document rollback**

```bash
dotnet ef database update <PreviousMigrationName> --project <Infra> --startup-project <Api>
dotnet ef migrations remove --project <Infra> --startup-project <Api>   # if unapplying from code
```

Never modify a migration that is already applied — create a new one.

### Flow B: .NET Version Upgrade

1. **Assess** — `get_project_graph` to list all TFMs; flag mixed versions.
2. **Pre-flight** — all tests green, no pending EF migrations, dependencies
   checked for target-version compatibility, dedicated branch created
   (branch IS the rollback plan).
3. **Update `global.json`** — SDK version with `"rollForward": "latestMinor"`.
4. **Update TFMs** — `<TargetFramework>net10.0</TargetFramework>` and
   `<LangVersion>14</LangVersion>` in `.csproj` or `Directory.Build.props`.
5. **Update packages** — `dotnet outdated --upgrade Major --include Microsoft.*`,
   then build and fix.
6. **Adopt new features** — per `knowledge/dotnet-whats-new.md`: `TimeProvider`,
   `HybridCache`, primary constructors, collection expressions.
7. **Verify** — `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`.

### Flow C: NuGet Package Updates

1. **Audit** — `dotnet list package --outdated` and `dotnet list package --vulnerable`.
   Vulnerable packages are urgent: update, test, deploy.
2. **Categorize** — patch (batch-safe), minor (one at a time), major (one at a
   time, read release notes first).
3. **Update incrementally** — one package, then `dotnet build && dotnet test`
   before the next. Batched updates make failures unattributable.
4. **Check fit** — consult `knowledge/package-recommendations.md` before adding
   new packages; prefer built-in .NET alternatives.
5. **Rollback** — git revert the package bump; never downgrade other packages
   to compensate.

### MCP Tools Used

- `find_symbol` / `find_references` — locate entities, assess schema-change impact
- `get_type_hierarchy` — entity inheritance for TPH/TPT/TPC
- `get_project_graph` — TFM audit before version upgrades
- `get_diagnostics` — catch warnings after migration generation or upgrades

## Example

```
User: /migrate

Claude: Checking migration state...
  Last applied: 20250710_AddOrderTable
  Model changes detected: new ShippingAddress property on Order entity.

  Creating migration: AddOrderShippingAddress
  Reviewing SQL (dotnet ef migrations script --idempotent)...
  - ALTER TABLE "Orders" ADD "ShippingStreet" text NULL
  - ALTER TABLE "Orders" ADD "ShippingCity" text NULL
  No data loss. No locking risk. Safe to apply.

  Applying migration... Done. Build: green. Tests: 34/34 passed.

  Rollback (if needed):
  dotnet ef database update AddOrderTable --project src/Infrastructure --startup-project src/Api
```

## Related

- `ef-core` — entity configuration, query patterns, migration internals
- `/verify` — full verification pipeline after migration work
- `/checkpoint` — commit a safe state before risky migrations
