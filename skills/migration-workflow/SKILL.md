---
name: migration-workflow
description: >
  Safe upgrade workflows across THREE domains: EF Core schema migrations, .NET
  version upgrades, and NuGet dependency updates — each with rollback strategies
  and verification steps. The EF Core path is loaded automatically by the
  /migrate workflow — for "add migration" or "update database", use /migrate.
  Load this skill directly when: "upgrade to .NET", "version upgrade",
  "upgrade nuget", "update packages", "dependency update", or planning a
  framework or dependency migration.
---

# Migration Workflow

## Core Principles

1. **Verify before applying** — Always review generated migration SQL before applying to any database. `dotnet ef migrations script` shows the exact SQL. Never apply blindly.
2. **Rollback plan always** — Every migration has a rollback. For EF Core: `dotnet ef database update <PreviousMigration>`. For packages: git revert. For .NET version: branch-based rollback. Document the rollback before applying.
3. **Test after migration** — Run the full test suite after every migration step. Migrations that break tests are not complete. Integration tests with Testcontainers catch real database issues.
4. **One change per migration** — Each EF Core migration should represent a single logical change (add table, rename column, add index). Multiple unrelated changes in one migration make rollback impossible.
5. **Incremental updates** — Update one package at a time, build, test. Update one target framework at a time, build, test. Never batch unrelated changes — when something breaks, you need to know which change caused it.

## Patterns

### EF Core Migration Workflow

Step-by-step workflow for creating and applying database migrations safely.

**Step 1: Check Current State**
```bash
# List all migrations and their status
dotnet ef migrations list --project src/Infrastructure --startup-project src/Api

# Preview the SQL that would run — `database update` has no dry-run flag;
# generate an idempotent script and review it instead
dotnet ef migrations script --idempotent --project src/Infrastructure --startup-project src/Api
```

**Step 2: Create Migration**
Use descriptive names that explain the change, not the entity:
```bash
# GOOD — Describes the change
dotnet ef migrations add AddOrderShippingAddress --project src/Infrastructure --startup-project src/Api
dotnet ef migrations add RenameCustomerEmailToContactEmail --project src/Infrastructure --startup-project src/Api
dotnet ef migrations add AddIndexOnOrderCreatedAt --project src/Infrastructure --startup-project src/Api

# BAD — Describes the entity, not the change
dotnet ef migrations add Order
dotnet ef migrations add UpdateCustomer
```

**Step 3: Review Generated SQL**
```bash
# Generate the SQL script for review
dotnet ef migrations script --idempotent --project src/Infrastructure --startup-project src/Api

# Or generate from a specific migration
dotnet ef migrations script PreviousMigration AddOrderShippingAddress --project src/Infrastructure --startup-project src/Api
```

Check for:
- ⚠️ Data loss: `DROP COLUMN`, `DROP TABLE`, column type changes that lose precision
- ⚠️ Long locks: `ALTER TABLE` on large tables without concurrent index creation
- ⚠️ Default values: New non-nullable columns need defaults for existing rows

**Step 4: Handle Data Loss Warnings**
If EF Core warns about potential data loss:

```csharp
// In the migration file — explicitly handle data transformation
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Step 1: Add new column as nullable
    migrationBuilder.AddColumn<string>("ContactEmail", "Customers", nullable: true);

    // Step 2: Copy data from old column
    migrationBuilder.Sql("UPDATE \"Customers\" SET \"ContactEmail\" = \"Email\"");

    // Step 3: Make non-nullable after data is copied
    migrationBuilder.AlterColumn<string>("ContactEmail", "Customers", nullable: false);

    // Step 4: Drop old column
    migrationBuilder.DropColumn("Email", "Customers");
}
```

**Step 5: Apply and Verify**
```bash
# Apply to development database
dotnet ef database update --project src/Infrastructure --startup-project src/Api

# Run tests to verify
dotnet test
```

**Step 6: Rollback (if needed)**
```bash
# Rollback to previous migration
dotnet ef database update PreviousMigrationName --project src/Infrastructure --startup-project src/Api

# Remove the failed migration from code
dotnet ef migrations remove --project src/Infrastructure --startup-project src/Api
```

### NuGet Dependency Update Workflow

Safe process for updating NuGet packages without breaking the build.

**Step 1: Audit Current State**
```bash
# List all outdated packages
dotnet list package --outdated

# Check for vulnerable packages
dotnet list package --vulnerable
```

**Step 2: Categorize Updates**
- **Patch updates** (1.0.0 → 1.0.1): Safe, bug fixes only. Update all patches at once.
- **Minor updates** (1.0.0 → 1.1.0): Usually safe, new features. Update one at a time.
- **Major updates** (1.0.0 → 2.0.0): Breaking changes expected. Update one at a time, read release notes.

**Step 3: Update Incrementally**
```bash
# Patch updates — batch is safe
dotnet outdated --upgrade Patch

# Minor updates — one at a time
dotnet add src/Api/Api.csproj package Serilog.AspNetCore --version 9.1.0
dotnet build
dotnet test

# Major updates — one at a time with careful review
dotnet add src/Api/Api.csproj package WolverineFx
dotnet build  # Fix compilation errors
dotnet test   # Fix behavioral changes
```

**Step 4: Reference Package Recommendations**
Check `knowledge/package-recommendations.md` before adding new packages:
- Is there a built-in .NET alternative? (e.g., HybridCache vs third-party cache)
- Is the package actively maintained?
- Does it align with kit recommendations?

**Step 5: Verify**
```bash
dotnet build   # Clean compilation
dotnet test    # All tests pass
```

### .NET Version Migration Workflow

Structured upgrade from older .NET versions to .NET 10.

**Step 1: Assess Current State**
```
→ get_project_graph
  List all projects and their target frameworks.
  Flag: mixed TFMs, test projects on different versions.
```

**Step 2: Pre-Migration Checklist**
- [ ] All tests pass on current version
- [ ] No pending EF Core migrations
- [ ] Dependencies checked for .NET 10 compatibility
- [ ] Branch created for migration work

**Step 3: Update global.json**
```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestMinor"
  }
}
```

**Step 4: Update Target Frameworks**
Update each `.csproj` (or `Directory.Build.props` if centralized):
```xml
<PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14</LangVersion>
</PropertyGroup>
```

**Step 5: Update Packages**
```bash
# Update all Microsoft.* packages to 10.x
dotnet outdated --upgrade Major --include Microsoft.*
dotnet build  # Fix compilation issues
```

**Step 6: Adopt New Features**
Reference `knowledge/dotnet-whats-new.md`:
- Replace `DateTime.Now`/`DateTime.UtcNow` with `TimeProvider`
- Use `HybridCache` instead of `IDistributedCache`
- Convert classes to primary constructors where appropriate
- Use collection expressions: `int[] x = [1, 2, 3]`
- Use the `field` keyword in property accessors

**Step 7: Verify**
```bash
dotnet build                    # Clean build
dotnet test                     # All tests pass
dotnet format --verify-no-changes  # Formatting consistent
```

Then run the health check workflow from the `project-setup` skill to establish the new baseline.

## Anti-patterns

### Applying Migrations Without Reviewing SQL

```bash
# BAD — Blindly applying
dotnet ef database update
# Oops, dropped a column with 10 million rows of data
```

```bash
# GOOD — Review first, then apply
dotnet ef migrations script --idempotent > review.sql
# Read review.sql, check for DROP, ALTER, data loss
dotnet ef database update
```

### Updating All Packages at Once

```bash
# BAD — Update everything, pray it works
dotnet outdated --upgrade Major
dotnet build  # 47 errors — which package caused this?
```

```bash
# GOOD — One at a time, build after each
dotnet add package WolverineFx
dotnet build && dotnet test  # ✅
dotnet add package Serilog --version 5.0.0
dotnet build && dotnet test  # ❌ — Serilog 5.0 broke the sink config
```

### Skipping Tests After Migration

```bash
# BAD
dotnet ef database update  # "It compiled, ship it"
```

```bash
# GOOD
dotnet ef database update
dotnet test  # Run FULL test suite, especially integration tests
# Integration tests with Testcontainers will catch schema mismatches
```

### Multiple Unrelated Changes in One Migration

```bash
# BAD — Three unrelated changes in one migration
dotnet ef migrations add UpdateEverything
# Contains: new table + renamed column + dropped index
# Rollback is all-or-nothing for three unrelated changes
```

```bash
# GOOD — One change per migration
dotnet ef migrations add AddShippingAddressTable
dotnet ef migrations add RenameCustomerEmailColumn
dotnet ef migrations add DropUnusedOrderIndex
# Each can be rolled back independently
```

## Decision Guide

| Scenario | Workflow | Key Step |
|----------|----------|----------|
| New database table | EF Core Migration | Create entity + config + migration |
| Column rename | EF Core Migration | Review SQL for data preservation |
| Add index | EF Core Migration | Check for long locks on large tables |
| Data transformation | EF Core Migration + raw SQL | Custom `Up()` with SQL statements |
| Outdated packages | NuGet Update | One at a time, build + test between each |
| Vulnerable package | NuGet Update (urgent) | Update immediately, test, deploy |
| .NET version upgrade | .NET Migration | Phase 1-4, verify at each phase |
| Add new package | NuGet Update | Check package-recommendations.md first |
| `ExecuteUpdateAsync` vs migration | Depends | Migration for schema; `ExecuteUpdateAsync` for bulk data updates at runtime |
| Modify existing migration | **Never** if already applied | Create new migration instead |
