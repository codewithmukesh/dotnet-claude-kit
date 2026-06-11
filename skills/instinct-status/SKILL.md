---
name: instinct-status
description: >
  Display all learned instincts with confidence scores. Triggers on:
  /instinct-status, "show instincts", "what have you learned",
  "list instincts", "review learned patterns".
---

# /instinct-status

## What

Display all learned instincts from `.claude/instincts.md` as a formatted table showing:

- Instinct name and description
- Confidence score (0.3 = tentative, 0.9 = battle-tested)
- Category (architecture, testing, style, performance, error-handling)
- Evolution status (new, reinforced, decayed, promoted)
- Source (user correction, pattern observation, explicit instruction)

## When

- Reviewing what Claude has learned about the project
- Before sharing instincts with another project (pre-export review)
- Debugging unexpected behavior -- "why did you do it that way?"
- Periodic review of accumulated project knowledge

## How

### Step 1: Invoke Skill

- **Skill**: `instinct-system` -- Reads, parses, and formats instinct data

### Step 2: Read and Format

- Read `.claude/instincts.md` from the project root
- Parse each instinct entry with its metadata
- Sort by confidence score (highest first)
- Format as a table grouped by category

### Step 3: Report

Display the table and a brief summary of instinct health:

- Total instinct count
- Average confidence across all instincts
- Recently reinforced vs decaying instincts
- Any conflicts between instincts

## Example

```
User: /instinct-status

Claude: ## Project Instincts (12 total, avg confidence: 0.72)

| Instinct                        | Confidence | Category     | Status     |
|---------------------------------|------------|--------------|------------|
| Use vertical slice architecture | 0.9        | architecture | reinforced |
| Explicit mapping over AutoMapper| 0.8        | style        | reinforced |
| Always add CancellationToken    | 0.8        | performance  | promoted   |
| Prefer records for DTOs         | 0.7        | style        | stable     |
| Use TimeProvider over DateTime  | 0.7        | testing      | stable     |
| Integration tests over unit     | 0.6        | testing      | new        |
| Serilog structured logging      | 0.5        | style        | new        |
| Avoid MediatR for simple flows  | 0.4        | architecture | tentative  |

2 instincts reinforced this session.
1 instinct decaying (no recent reinforcement): "Prefer Dapper over EF" (0.35)
```

## Related

- `/instinct-export` -- Export high-confidence instincts to a shareable format
- `/instinct-import` -- Import instincts from another project
