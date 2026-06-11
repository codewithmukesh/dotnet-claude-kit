---
name: spec
description: >
  Turn a vague feature or product idea into an agreed, persisted specification
  through relentless structured questioning. Never assumes — every gap,
  ambiguity, or "probably" becomes a question to the developer, and the spec
  cannot be approved while open questions remain. Produces
  docs/specs/<NNN>-<slug>.md with acceptance criteria that /plan, /scaffold,
  and /tdd consume. Use when: "spec", "write a spec", "spec this out",
  "requirements", "PRD", "acceptance criteria", "define the feature",
  "user stories", "what should we build", or before planning any feature
  too big to describe in one sentence.
---

# /spec — Relentless Specification Workflow

## What

Converts an idea into a written, versioned specification that both the developer
and Claude explicitly agree on — before any planning or code. The contract:

- **Never assume.** Every gap in the idea becomes a question. If Claude catches
  itself thinking "probably", "presumably", or "the usual way" — that thought is
  a question to ask, not a decision to make.
- **Relentless, but structured.** Questions come in focused rounds (3–5 at a
  time) across nine dimensions — not one overwhelming dump, and not a single
  polite round that stops early.
- **Agreement is explicit.** Specs have a status lifecycle: Draft → In Review →
  Approved. Implementation never starts from a Draft. Approval requires the
  developer to read the final document and say so.
- **Specs are files, not chat.** Output persists to `docs/specs/<NNN>-<slug>.md`
  and survives the session. Plans, tests, and commits reference it.

## When

- Any feature too big to describe completely in one sentence
- New product or module ideas ("I want to add team workspaces")
- Before `/plan` for non-trivial features — plan consumes the approved spec
- When requirements feel fuzzy mid-implementation: stop, `/spec`, re-plan
- Trigger phrases: "spec", "requirements", "PRD", "define the feature", "acceptance criteria"

**Skip for:** bug fixes, refactors, single-endpoint CRUD where the entity is obvious.

## How

### Step 1: Capture and Restate

Take the raw idea and restate it in one paragraph: what Claude understood, in its
own words. End with: "Is this the idea? What did I get wrong?" Do not begin
questioning until the developer confirms the restatement — questioning the wrong
idea wastes everyone's time.

### Step 2: Questioning Rounds

Work through the nine dimensions in order. Each round: pick the 3–5 most
load-bearing unanswered questions (answers that reshape later questions come
first). Where the harness supports selectable options, present choices with
trade-offs — and a recommendation — but **the developer chooses; a recommendation
is never silently applied**.

| # | Dimension | What to pin down |
|---|-----------|------------------|
| 1 | Problem & users | Who hurts today, how they work around it, what success looks like |
| 2 | Scope | What is IN this iteration, what is explicitly OUT, where the MVP line sits |
| 3 | Domain & data | Entities, relationships, lifecycle (create→archive→delete?), retention |
| 4 | API contract | Resources, endpoints, request/response shapes, pagination, versioning |
| 5 | Authorization | Who can do what, role/claim model, tenant boundaries |
| 6 | Edge cases & failure modes | Concurrency, duplicates, idempotency, partial failure, limits |
| 7 | Non-functionals | Expected volume, latency budget, growth assumptions |
| 8 | Integrations | External services, published events, webhooks, side effects |
| 9 | Acceptance criteria | Testable Given/When/Then for every behavior in scope |

**Rules of relentless questioning:**

- Record every answer in the draft spec immediately — answers are requirements, not conversation.
- Challenge contradictions on the spot: "In round 1 you said X; this answer implies not-X. Which wins?"
- "I don't know" is a legal answer → moves to **Deferred Decisions** with an explicit
  fallback the developer chooses now ("default to soft-delete until decided").
  Silent deferral is forbidden.
- A dimension is done when a follow-up round generates zero new questions for it.
- The questioning phase is done when ALL nine dimensions are done. Do not stop
  because the conversation feels long — stopping early is how assumptions sneak in.

### Step 3: Draft the Spec File

Determine the next number from existing files in `docs/specs/` (create the
directory if missing). Write `docs/specs/<NNN>-<slug>.md`:

```markdown
# Spec NNN: <Title>

**Status:** Draft
**Date:** <today>

## Problem            <!-- who hurts, why now -->
## Scope              <!-- ### In / ### Out — both explicit -->
## Domain Model       <!-- entities, relationships, lifecycle -->
## API Contract       <!-- endpoints table: verb, route, request, response, errors -->
## Authorization      <!-- who can do what -->
## Edge Cases & Failure Modes
## Non-Functional Requirements
## Integrations
## Acceptance Criteria  <!-- Given/When/Then, numbered AC-1, AC-2, ... -->
## Deferred Decisions   <!-- decision, chosen fallback, revisit trigger -->
## Open Questions       <!-- MUST be empty before approval -->
```

### Step 4: Review Loop

Set status to **In Review**. Present the complete spec and ask: "Read this
end-to-end. What is wrong, missing, or over-engineered?" Fold corrections in and
re-present. Repeat until the developer has no further changes. New answers may
spawn new questions — that is the process working, not a failure to converge.

### Step 5: The Agreement Gate

Approval is a deliberate act, never inferred from silence or "looks good" in
passing. Ask explicitly: "Do you approve this spec? After approval, code follows
the spec — changes go through the spec first." On approval, set
`**Status:** Approved (<date>)`.

- **Open Questions must be empty.** If any remain, the spec cannot be approved — resolve or defer each one explicitly.
- If implementation later reveals a wrong assumption: stop, set status back to
  In Review, fix the spec with the developer, then resume. Code never silently
  diverges from an approved spec.

### Step 6: Handoff

- `/plan` reads the approved spec and maps acceptance criteria to implementation steps
- `/tdd` turns acceptance criteria into the first failing tests (AC-n → test name)
- Commits for the feature reference the spec: `feat: team workspaces (spec 004)`

## Example

```
User: /spec users should be able to invite teammates

Claude: Here's what I understood: an existing user can invite another person
  to join their... workspace? account? That's already my first gap.
  Is this the idea? What did I get wrong?

User: yes, invite to their organization

Claude: Round 1 — Scope & Users:
  1. Can invitees be people without an existing account, or only registered users?
  2. Who can invite — any member, or only admins/owners?
  3. Is there a limit on pending invites (per org, per plan)?
  4. Out of scope check: bulk invites and SSO-provisioned members — both OUT for now?

User: 1: both. 2: admins only. 3: no limit. 4: correct, both out.

Claude: Round 2 — Edge cases (your "both" in Q1 raises these):
  1. Inviting an email that already has a pending invite — error, or resend?
  2. Invite expiry — 7 days, 30 days, never?
  3. Invitee already belongs to another org — multi-org membership or rejection?
  ...

[…rounds continue until all nine dimensions are exhausted…]

Claude: Spec written to docs/specs/004-team-invitations.md (In Review).
  Read it end-to-end. What is wrong, missing, or over-engineered?

User: looks right. approved.

Claude: Status: Approved (2026-06-12). Next: /plan to map AC-1..AC-9
  to implementation steps.
```

## Related

- `/plan` — Consumes the approved spec; never plan a spec-worthy feature without one
- `/tdd` — Acceptance criteria become the first failing tests
- `/scaffold` — Generates the slices the plan calls for
- `architecture-advisor` — Load during Step 2 if the feature forces architectural decisions
