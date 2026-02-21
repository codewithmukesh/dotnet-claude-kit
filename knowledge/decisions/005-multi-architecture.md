# ADR-005: Multi-Architecture Support

## Status

Accepted (supersedes [ADR-001](001-vsa-default.md))

## Context

ADR-001 established Vertical Slice Architecture (VSA) as the single default architecture for dotnet-claude-kit. While VSA is an excellent choice for many applications, real-world .NET projects span a wide range of complexity and team contexts. Hardcoding a single default led to:

1. **Mismatched guidance** — Teams building complex domains with rich business logic were told to start with VSA, which does not provide the structural guardrails they need for aggregate boundaries and dependency inversion.
2. **Incomplete coverage** — Clean Architecture and DDD are widely used in enterprise .NET development. dotnet-claude-kit had no skills for these architectures, leaving Claude without guidance when users worked in those patterns.
3. **False simplicity** — Recommending one architecture for all projects is simpler to maintain but does not serve users well. Architecture is a context-dependent decision that requires understanding the project's domain, team, and constraints.

## Decision

**dotnet-claude-kit supports four architectures as first-class options, guided by an architecture-advisor skill that recommends the best fit through a structured questionnaire.**

The four supported architectures:

| Architecture | Skill | Best For |
|---|---|---|
| Vertical Slice Architecture | `vertical-slice` | CRUD-heavy apps, APIs, MVPs, small-medium teams |
| Clean Architecture | `clean-architecture` | Medium complexity, long-lived systems, enforced boundaries |
| DDD + Clean Architecture | `ddd` + `clean-architecture` | Complex domains, strict invariants, specialized vocabulary |
| Modular Monolith | `modular-monolith` template | Multiple bounded contexts, team-per-domain |

The `architecture-advisor` skill:
- Asks questions across 6 categories (domain complexity, team, lifetime, compliance, existing codebase, integration)
- Maps answers to a recommendation using a decision matrix
- Documents evolution paths between architectures
- Is always loaded BEFORE any architecture-specific skill

## Consequences

### Positive

- **Better real-world coverage** — Teams working with any of the four major patterns now have first-class guidance from dotnet-claude-kit.
- **Guided selection** — The architecture-advisor questionnaire prevents analysis paralysis and ensures architecture matches project context.
- **Evolution awareness** — Documented migration paths (VSA → CA, CA → DDD, Monolith → Modular Monolith) help teams evolve their architecture as complexity grows.
- **No architecture religion** — By supporting multiple patterns equally, dotnet-claude-kit avoids the trap of defending a single approach regardless of context.

### Negative

- **More skills to maintain** — Three new skills (architecture-advisor, clean-architecture, ddd) increase the maintenance surface.
- **Potential for choice overload** — Users must now choose an architecture rather than accepting a default. The advisor mitigates this by making the recommendation.
- **Skill coordination** — The dotnet-architect agent must now load the advisor first, then conditionally load the appropriate architecture skill.

### Mitigations

- The `architecture-advisor` skill includes a decision guide that recommends VSA for simple/unknown cases, preventing choice paralysis.
- The `dotnet-architect` agent is updated to always start with the architecture questionnaire for new projects.
- Each architecture skill is self-contained with its own patterns, anti-patterns, and decision guide.
