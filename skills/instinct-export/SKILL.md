---
name: instinct-export
description: >
  Export instincts to a shareable format. Triggers on: /instinct-export,
  "export instincts", "share instincts", "save instincts for another project".
---

# /instinct-export

## What

Export project instincts to a portable format that can be imported into other projects. Only high-confidence instincts (above 0.7) are exported by default, ensuring only battle-tested patterns are shared.

## When

- Sharing proven patterns with a new project
- Team knowledge transfer -- onboarding a new repo with existing learnings
- Backing up instincts before a major project restructuring
- Creating a team-wide instinct baseline

## How

### Step 1: Invoke Skill

- **Skill**: `instinct-system` -- Handles instinct reading, filtering, and serialization

### Step 2: Filter and Export

- Read `.claude/instincts.md` from the current project
- Filter to instincts with confidence score above 0.7 (configurable via argument)
- Strip project-specific context (file paths, line numbers) while preserving the pattern
- Write to `.claude/instincts-export.md` with portable metadata

### Step 3: Report

Display what was exported and what was filtered out, with the output file path.

## Example

```
User: /instinct-export

Claude: Exported 7 of 12 instincts (confidence > 0.7):

Exported:
  - Use vertical slice architecture (0.9)
  - Explicit mapping over AutoMapper (0.8)
  - Always add CancellationToken (0.8)
  - Prefer records for DTOs (0.7)
  - Use TimeProvider over DateTime (0.7)

Skipped (below threshold):
  - Integration tests over unit (0.6)
  - Serilog structured logging (0.5)
  - Avoid MediatR for simple flows (0.4)
  - Prefer Dapper over EF (0.35)

Written to: .claude/instincts-export.md
```

## Related

- `/instinct-status` -- Review all instincts before exporting
- `/instinct-import` -- Import instincts into another project
