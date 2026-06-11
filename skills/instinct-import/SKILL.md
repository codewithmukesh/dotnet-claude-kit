---
name: instinct-import
description: >
  Import instincts from another project. Triggers on: /instinct-import,
  "import instincts", "load instincts from", "bring in instincts".
---

# /instinct-import

## What

Import instincts from an exported file and merge them with the current project's existing instincts. Handles conflict resolution and applies confidence decay to reflect that imported patterns have not yet been validated in this project.

## When

- Starting a new project with learnings from a previous one
- Onboarding a repository with team-wide instinct baselines
- Restoring instincts after a project restructuring
- Cross-pollinating patterns between related projects

## How

### Step 1: Invoke Skill

- **Skill**: `instinct-system` -- Handles instinct parsing, merging, and conflict resolution

### Step 2: Read and Merge

- Read the exported instincts file (path provided by user or default `.claude/instincts-export.md`)
- Read existing `.claude/instincts.md` if present
- For each imported instinct:
  - **No conflict**: Add with confidence decayed by 0.2 (imported patterns start lower)
  - **Matching instinct exists**: Keep the higher confidence, mark as reinforced
  - **Conflicting instinct exists**: Present both to the user for resolution
- Write merged result to `.claude/instincts.md`

### Step 3: Report

Display what was imported, merged, and any conflicts that need resolution.

## Example

```
User: /instinct-import .claude/instincts-export.md

Claude: Importing instincts from .claude/instincts-export.md...

Imported (5 new, confidence decayed by 0.2):
  - Use vertical slice architecture (0.9 -> 0.7)
  - Explicit mapping over AutoMapper (0.8 -> 0.6)
  - Always add CancellationToken (0.8 -> 0.6)

Merged (2 existing, kept higher confidence):
  - Prefer records for DTOs (existing: 0.7, imported: 0.5 -> kept 0.7)
  - Use TimeProvider over DateTime (existing: 0.6, imported: 0.5 -> kept 0.6)

No conflicts detected. Total instincts: 14
```

## Related

- `/instinct-status` -- Review instincts after import
- `/instinct-export` -- Export instincts from this project
