---
name: plan
description: >
  Enter plan mode for .NET projects with architecture awareness. Analyzes tasks
  through the lens of supported architectures (VSA, Clean Architecture, DDD,
  Modular Monolith) and produces structured implementation plans before any code
  is written. Use when: "plan", "let's plan", "think through", "design this",
  "how should I implement", or any non-trivial task requiring 3+ steps.
---

# /plan -- Architecture-Aware Planning

## What

Enters a structured planning mode that considers the project's architecture pattern
before producing an implementation plan. Instead of jumping straight to code, this
command forces a deliberate pause to:

- Identify the project's current architecture (or recommend one)
- Map the task to affected layers, modules, and boundaries
- Produce a numbered implementation plan with clear steps
- Iterate on the plan until it is solid before writing any code

Plans are living documents -- if something goes sideways during implementation,
stop and re-plan rather than pushing through a broken approach.

## When

- Non-trivial tasks requiring 3 or more implementation steps
- Tasks involving architectural decisions (new modules, cross-cutting concerns, new bounded contexts)
- Features that touch multiple layers (API, application, domain, infrastructure)
- Refactoring that could affect multiple consumers
- Any time the user says "plan", "think through", "design this", or "how should I approach"

**Skip planning for:** Single-file changes, simple bug fixes, typo corrections, config tweaks.

## How

### Step 1: Understand the Task

Check `docs/specs/` for an approved spec covering this task — if one exists, it is
the source of truth: map its acceptance criteria to plan steps and skip ahead to
Step 2. If the feature is spec-worthy (too big to describe in one sentence) and no
spec exists, recommend `/spec` first instead of planning on assumptions.
Otherwise, clarify what the user wants to build with focused questions.
Do not assume requirements that were not stated.

### Step 2: Detect Architecture

Use the `architecture-advisor` skill to determine the project's architecture:
- Check for existing architecture markers (folder structure, project references, patterns)
- If no architecture is established, run the architecture questionnaire
- Load the appropriate architecture-specific skill (vertical-slice, clean-architecture, ddd)

### Step 3: Map Affected Areas

Identify every layer, module, and boundary the task touches:
- Which projects/folders will have new or modified files?
- Are there cross-cutting concerns (auth, caching, validation, logging)?
- What existing code will be impacted? Use `find_references` and `find_callers` MCP tools for blast radius.
- Are there database migrations needed?

### Step 4: Produce the Plan

Output a numbered plan with this structure:

```
## Plan: [Task Title]

**Architecture:** [Detected architecture]
**Affected layers:** [List]
**Estimated steps:** [Count]

### Steps
1. [Step] -- [Which file/layer] -- [Why this order]
2. ...

### Open Questions
- [Anything that needs user input before proceeding]

### Risks
- [Potential issues and mitigations]
```

### Step 5: Iterate

Present the plan and ask: "Does this plan look right, or should I adjust anything?"
Revise until the user confirms. Only then proceed to implementation.

## Example

```
User: /plan Add a product catalog feature with search and filtering

Claude: Let me analyze your project structure first...

## Plan: Product Catalog Feature

**Architecture:** Vertical Slice Architecture
**Affected layers:** Features/, Infrastructure/Persistence/, Tests/
**Estimated steps:** 6

### Steps
1. Create Product entity + EF config -- Domain layer -- Foundation for everything else
2. Add migration -- Infrastructure -- Schema must exist before features
3. Scaffold GetProducts feature (with filtering) -- Features/Products/ -- Read path first
4. Scaffold CreateProduct feature -- Features/Products/ -- Write path
5. Add integration tests -- Tests/Features/Products/ -- Verify both features
6. Wire up OpenAPI metadata -- Features/Products/ -- Documentation

### Open Questions
- Should search be full-text (PostgreSQL tsvector) or simple LIKE?
- Do products need categories/tags for filtering?

Does this plan look right, or should I adjust anything?
```

## Related

- `/spec` -- Write the spec first for features too big to describe in one sentence
- `/scaffold` -- Generate the files once the plan is approved
- `/verify` -- Run verification after implementing the plan
