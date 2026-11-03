# Skill Benchmark Report

> **Point-in-time snapshot** — generated 2026-03-06, before the v0.8.0 commands→skills consolidation. Several skills analyzed here were since merged or renamed (e.g. autonomous-loops → build-fix, verification-loop → verify, scaffolding → scaffold); see the README for the current set. Kept for methodology and historical reference.

> Generated: 2026-03-06 | Skills analyzed: 47 | Total estimated tokens: ~91,700

## Executive Summary

All 47 skills are **structurally compliant** — every one has YAML frontmatter, Core Principles, Patterns, Anti-patterns (BAD/GOOD), and Decision Guide tables. The format is consistent and well-executed across the board.

However, the skills vary dramatically in **value-add over Claude's base knowledge**:
- **16 skills** are HIGH value-add (Claude would produce significantly worse output without them)
- **18 skills** are MEDIUM value-add (useful guardrails and opinionated defaults)
- **13 skills** are LOW value-add (high overlap with Claude's training data)

**Key findings:**
1. ~12,000 tokens could be saved through deduplication and trimming without losing value
2. 3 cross-skill inconsistencies found (endpoint wiring pattern conflicts)
3. Meta/workflow skills average 25% more tokens but carry less unique information than technical skills
4. The most impactful skills are the ones covering post-training-data APIs (.NET 10, Polly v8, Wolverine, HybridCache)

---

## Master Ranking Table

Skills ranked by **value-per-token** (combining value-add and information density against token cost).

### Tier 1: Essential — HIGH Value, LOW Overlap

These skills fundamentally change Claude's output quality. Worth every token.

| # | Skill | Lines | Est. Tokens | Density | Claude Overlap | Key Value |
|---|-------|-------|-------------|---------|----------------|-----------|
| 1 | **openapi** | 287 | ~2,060 | HIGH | LOW | Prevents Swashbuckle; .NET 10 built-in OpenAPI transformers |
| 2 | **opentelemetry** | 271 | ~1,217 | HIGH | LOW | UseOtlpExporter() conflicts, IMeterFactory, TagList perf |
| 3 | **container-publish** | 235 | ~1,150 | HIGH | LOW | SDK container publishing, deprecated property names, chiseled variants |
| 4 | **messaging** | 297 | ~1,573 | HIGH | LOW | Wolverine patterns, outbox, sagas — minimal training data |
| 5 | **caching** | 210 | ~1,530 | HIGH | LOW-MED | HybridCache (.NET 9+), stampede protection, tag-based eviction |
| 6 | **architecture-advisor** | 249 | ~1,929 | HIGH | LOW | 16-question questionnaire prevents "default to Clean Arch" |
| 7 | **instinct-system** | 323 | ~2,742 | HIGH | LOW | Novel confidence-scoring learning framework |
| 8 | **health-check** | 320 | ~2,571 | HIGH | LOW | 8-dimension grading rubric with MCP tool mapping |
| 9 | **autonomous-loops** | 393 | ~3,525 | HIGH | LOW | Bounded iteration protocol, regression guards |
| 10 | **resilience** | 389 | ~1,909 | HIGH | LOW-MED | Polly v8 API (Claude defaults to v7), hedging, keyed services |
| 11 | **scalar** | 259 | ~1,810 | MED | LOW | Scalar replaces Swagger UI; proxy security, auth prefill |
| 12 | **de-sloppify** | 277 | ~2,210 | HIGH | LOW | Ordered cleanup pipeline with dead code safety checks |
| 13 | **ddd** | 346 | ~2,727 | HIGH | MED | ComplexProperty, Guid.CreateVersion7(), domain event dispatch |
| 14 | **scaffolding** | 398 | ~3,325 | HIGH | LOW-MED | Mandatory 9-item checklist, 4-architecture scaffold templates |
| 15 | **code-review-workflow** | 233 | ~1,637 | MED | LOW | MCP tool orchestration for automated code review |
| 16 | **error-handling** | 254 | ~1,189 | HIGH | MED | ValidationFilter<T>, typed Error hierarchy, Result impl |

### Tier 2: Valuable — MEDIUM Value, Moderate Overlap

Useful guardrails and opinionated defaults. Worth loading for relevant tasks.

| # | Skill | Lines | Est. Tokens | Density | Claude Overlap | Key Value |
|---|-------|-------|-------------|---------|----------------|-----------|
| 17 | **ef-core** | 309 | ~1,298 | HIGH | MED | No-repository stance, ExecuteUpdate/Delete, interceptors |
| 18 | **testing** | 334 | ~1,359 | HIGH | MED | Testcontainers fixture, FakeTimeProvider, Verify snapshots |
| 19 | **vertical-slice** | 297 | ~2,595 | HIGH | MED | 3-handler comparison (Mediator/Wolverine/Raw) |
| 20 | **serilog** | 278 | ~2,100 | HIGH | MED | AddSerilog() vs UseSerilog(), OTLP sink, two-stage bootstrap |
| 21 | **httpclient-factory** | 258 | ~1,970 | HIGH | MED | Keyed clients (.NET 10), resilience handler defaults table |
| 22 | **minimal-api** | 318 | ~2,725 | HIGH | MED-HIGH | IEndpointGroup auto-discovery pattern (custom convention) |
| 23 | **migration-workflow** | 284 | ~1,853 | HIGH | MED | Data loss migration pattern, NuGet update strategy |
| 24 | **verification-loop** | 265 | ~2,054 | HIGH | MED | 7-phase pipeline with short-circuit logic |
| 25 | **80-20-review** | 276 | ~2,177 | HIGH | MED | Top 10 .NET batch review checklist, blast radius scoring |
| 26 | **security-scan** | 400 | ~2,711 | HIGH | MED | 6-layer security pipeline, OWASP-mapped patterns |
| 27 | **convention-learner** | 272 | ~2,330 | MED | MED | MCP-integrated detection workflow |
| 28 | **self-correction-loop** | 218 | ~1,940 | MED | MED | Generalization methodology, audit protocol |
| 29 | **aspire** | 212 | ~1,596 | MED | LOW | Service discovery URIs, AppHost wiring (newer API) |
| 30 | **session-management** | 369 | ~2,752 | MED | MED | .NET solution detection, multi-developer handoff |
| 31 | **clean-architecture** | 359 | ~3,060 | HIGH | MED-HIGH | IAppDbContext-over-repository stance |
| 32 | **learning-log** | 254 | ~2,035 | MED | MED-HIGH | 6-category taxonomy, log vs memory vs handoff distinction |
| 33 | **project-setup** | 282 | ~1,846 | MED | MED | Health check grading, MCP orchestration |

### Tier 3: Low Priority — HIGH Overlap with Claude's Training Data

Claude already knows most of this. Load only when you need the opinionated defaults.

| # | Skill | Lines | Est. Tokens | Density | Claude Overlap | Key Value |
|---|-------|-------|-------------|---------|----------------|-----------|
| 34 | **modern-csharp** | 316 | ~1,625 | HIGH | HIGH | Only `field` keyword + extension members are novel (C# 14) |
| 35 | **configuration** | 232 | ~1,729 | MED-HIGH | MED-HIGH | ValidateOnStart() is the main differentiator |
| 36 | **authentication** | 235 | ~1,796 | MED | HIGH | AddAuthorizationBuilder() fluent API is the main new bit |
| 37 | **logging** | 206 | ~861 | MED | MED | 45 lines duplicate the opentelemetry skill |
| 38 | **dependency-injection** | 211 | ~930 | MED | HIGH | Keyed services is the only novel pattern |
| 39 | **docker** | 218 | ~899 | MED | HIGH | .slnx-based restore is the only novel pattern |
| 40 | **ci-cd** | 239 | ~1,729 | MED | HIGH | .NET 10 version pin + format check step only |
| 41 | **project-structure** | 224 | ~1,795 | MED | HIGH | .slnx format and .NET 10 version pins only |
| 42 | **api-versioning** | 158 | ~1,397 | HIGH | MED | NewApiVersionSet builder API specifics |
| 43 | **model-selection** | 246 | ~2,073 | MED | HIGH | Only CLI commands (/model, /fast) are novel |
| 44 | **workflow-mastery** | 277 | ~2,008 | MED | HIGH | .claude/settings.json hook config is the main value |
| 45 | **wrap-up-ritual** | 246 | ~1,995 | MED | MED-HIGH | Handoff template structure is the main value |
| 46 | **split-memory** | 285 | ~2,195 | MED | HIGH | Precedence rules and 300-line threshold heuristic |
| 47 | **context-discipline** | 260 | ~2,220 | MED | HIGH | MCP tool cost estimates (30-150 vs 500-2000+ tokens) |

---

## Token Budget Analysis

| Category | Skills | Total Lines | Est. Tokens | Avg Tokens/Skill |
|----------|--------|-------------|-------------|------------------|
| Tier 1 (Essential) | 16 | 4,731 | ~34,174 | ~2,136 |
| Tier 2 (Valuable) | 17 | 4,864 | ~36,291 | ~2,135 |
| Tier 3 (Low Priority) | 14 | 3,373 | ~23,252 | ~1,661 |
| **TOTAL** | **47** | **12,968** | **~93,717** | **~1,994** |

### By Skill Type

| Type | Count | Avg Tokens | Avg Density | Avg Overlap |
|------|-------|-----------|-------------|-------------|
| .NET Technical (code-heavy) | 27 | ~1,820 | HIGH | MED |
| Meta/Workflow (process-heavy) | 14 | ~2,270 | MED | MED-HIGH |
| Hybrid (MCP + process) | 6 | ~2,180 | HIGH | LOW-MED |

**Observation:** Meta/workflow skills average **25% more tokens** than technical skills but carry **less unique information per token**. The hybrid skills (code-review-workflow, de-sloppify, health-check, security-scan, verification-loop, 80-20-review) that combine MCP tool orchestration with .NET knowledge are the best-optimized category.

---

## Critical Issues Found

### 1. Cross-Skill Inconsistency: Endpoint Wiring

The `clean-architecture` skill (lines 227-254) uses a manual `MapOrders()` extension method pattern, which **directly contradicts** the `IEndpointGroup` auto-discovery pattern mandated by:
- `minimal-api` skill
- `architecture.md` rule
- `scaffolding` skill

**Fix:** Update `clean-architecture` to use `IEndpointGroup`.

### 2. Cross-Skill Duplication: Migration Workflow

The migration workflow appears in **both**:
- `project-setup/SKILL.md` (lines 146-204, ~60 lines)
- `migration-workflow/SKILL.md` (canonical)

**Fix:** Remove from `project-setup`, add a cross-reference.

### 3. Cross-Skill Duplication: OpenTelemetry

The `logging` skill (lines 92-136, ~45 lines) duplicates OpenTelemetry setup from the dedicated `opentelemetry` skill.

**Fix:** Remove from `logging`, add cross-reference.

### 4. Cross-Skill Duplication: Verification Loop

The `workflow-mastery` skill contains a verification loop section that duplicates `verification-loop` skill.

**Fix:** Remove from `workflow-mastery`, add cross-reference.

---

## Token Optimization Opportunities

### Quick Wins (high confidence, low risk)

| Skill | Action | Lines Saved | Tokens Saved |
|-------|--------|-------------|--------------|
| logging | Remove OTel duplication | ~45 | ~300 |
| project-setup | Remove migration duplication | ~60 | ~400 |
| workflow-mastery | Remove verification loop dup | ~30 | ~200 |
| session-management | Remove duplicate handoff template | ~30 | ~200 |
| modern-csharp | Condense well-known features to table | ~120 | ~800 |
| context-discipline | Merge overlapping sections | ~80 | ~530 |
| ci-cd | Collapse Azure DevOps to diff from GHA | ~30 | ~200 |
| scalar | Collapse theme list to one line | ~10 | ~65 |
| security-scan | Deduplicate finding report format | ~20 | ~130 |
| **Total quick wins** | | **~425 lines** | **~2,825 tokens** |

### Medium-Effort Optimizations

| Skill | Action | Lines Saved | Tokens Saved |
|-------|--------|-------------|--------------|
| aspire | Trim Service Defaults to key highlights | ~40 | ~260 |
| authentication | Deduplicate token validation params | ~15 | ~100 |
| caching | Trim manual cache-aside (recommend HybridCache) | ~25 | ~165 |
| ddd | Drop second value object example | ~15 | ~100 |
| docker | Trim generic Docker Compose boilerplate | ~40 | ~260 |
| resilience | Extract rate limiting to separate skill | ~65 | ~430 |
| split-memory | Fold thin patterns into shorter notes | ~40 | ~260 |
| instinct-system | Trim storage format to 3-4 categories | ~20 | ~130 |
| self-correction-loop | Condense rule generalization to one example | ~20 | ~130 |
| convention-learner | Shorten detected conventions template | ~25 | ~165 |
| **Total medium-effort** | | **~305 lines** | **~2,000 tokens** |

### Aggressive Optimization (consider carefully)

| Skill | Action | Lines Saved | Tokens Saved |
|-------|--------|-------------|--------------|
| dependency-injection | Strip basic lifetime definitions | ~30 | ~200 |
| docker | Strip obvious anti-patterns | ~20 | ~130 |
| project-structure | Strip well-known Directory.Build.props | ~40 | ~260 |
| configuration | Strip environment layering docs | ~20 | ~130 |
| model-selection | Strip to CLI commands + routing table only | ~80 | ~530 |
| wrap-up-ritual | Remove Next-Session duplicate template | ~40 | ~260 |
| learning-log | Trim example entries and monthly review | ~50 | ~330 |
| **Total aggressive** | | **~280 lines** | **~1,840 tokens** |

### Grand Total Savings Potential

| Level | Lines | Tokens | % of Total Budget |
|-------|-------|--------|-------------------|
| Quick wins | ~425 | ~2,825 | 3.0% |
| + Medium-effort | ~730 | ~4,825 | 5.1% |
| + Aggressive | ~1,010 | ~6,665 | 7.1% |

---

## Structural Quality Scorecard

| Dimension | Score | Notes |
|-----------|-------|-------|
| Frontmatter compliance | 47/47 (100%) | All skills have valid YAML frontmatter |
| Core Principles present | 47/47 (100%) | All have 3-5 principles |
| Patterns with code | 47/47 (100%) | All have patterns; 40 use C#, 7 use pseudocode/markdown |
| Anti-patterns BAD/GOOD | 47/47 (100%) | All have comparisons |
| Decision Guide table | 47/47 (100%) | All have tables |
| Under 400-line limit | 46/47 (98%) | security-scan is at exactly 400 |
| No cross-skill conflicts | 44/47 (94%) | 3 inconsistencies found |
| No content duplication | 43/47 (91%) | 4 duplication issues found |

---

## Recommendations

### Priority 1: Fix Inconsistencies
1. Update `clean-architecture` endpoint wiring to use `IEndpointGroup`
2. Resolve the 4 cross-skill duplications (logging, project-setup, workflow-mastery, session-management)

### Priority 2: Quick Win Trimming
Apply the ~425 lines of quick-win optimizations to save ~2,825 tokens with zero information loss.

### Priority 3: Consider Tier 3 Consolidation
The 14 Tier 3 skills consume ~23,252 tokens with high Claude overlap. Consider:
- **Merge** `dependency-injection` + `configuration` into a single "DI & Config" skill focused on novel patterns only
- **Merge** `docker` + `container-publish` into a single "Containerization" skill
- **Merge** `ci-cd` + `project-structure` into a single "Project Infrastructure" skill
- **Refocus** `modern-csharp` to C# 14 features only (~100 lines)
- **Refocus** `model-selection` + `workflow-mastery` + `context-discipline` into a single "Claude Code Workflow" skill

This would reduce Tier 3 from 14 skills (~23,252 tokens) to ~7 skills (~12,000 tokens), saving ~11,000 tokens.

### Priority 4: Evaluate Meta/Workflow Skill ROI
The 14 meta/workflow skills (context-discipline, split-memory, wrap-up-ritual, learning-log, model-selection, 80-20-review, workflow-mastery, verification-loop, instinct-system, session-management, autonomous-loops, self-correction-loop, convention-learner, health-check) consume ~31,000 tokens total. While some are essential (instinct-system, autonomous-loops, health-check), others overlap significantly with Claude's built-in behavior. Consider gating these behind explicit loading rather than including in templates.
