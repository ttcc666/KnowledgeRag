# RAG Agent Workflow

KnowledgeRag uses Microsoft Agent Framework to keep LLM responsibilities explicit while leaving retrieval, ranking, authorization, retry bounds, citation validation, and failure policy deterministic.

## Turn graph

```text
User
  |
  v
QueryPlannerAgent (structured QueryPlan)
  |
  v
MultiQuerySearchService
  |-- original query (mandatory)
  |-- planner query A  -- parallel, isolated DI scope / DbContext, best-effort
  `-- planner query B  -- parallel, isolated DI scope / DbContext, best-effort
  |
  v
query-level RRF -> dedupe -> final rerank against original question
  |
  v
Evidence context budget
  |-- total token budget
  `-- per-document token budget
  |
  v
KnowledgeAnswerAgent
  |-- Evidence only for knowledge facts
  `-- specialist Agent-as-Tool calls where configured
  |
  v
EvidenceReviewerAgent (structured EvidenceReview)
  |-- pass ------------------------------.
  `-- fail -> supplemental retrieval     |
              -> deterministic cross-round evidence merge
              -> regenerate from original session snapshot
              -> review again
              -> pass or safe insufficient-evidence response
                                                |
                                                v
                                      Final Answer Citation Gate
                                      |-- normalize [E#] -> [n]
                                      |-- reject invalid Evidence IDs
                                      |-- require valid citation for
                                      |   knowledge-backed non-abstention
                                      `-- expose cited Sources only
                                                |
                                                v
                                           SSE / persist
```

## Why search remains code instead of an Agent

Vector retrieval, lexical retrieval, RRF, reranking, authorization boundaries, deduplication, context-budget enforcement, citation validation, evaluation ranking metrics, and retry limits are deterministic operations. Keeping them as executors/services makes them testable, observable, and resistant to an LLM silently skipping retrieval. Agents are used only where language reasoning adds value: query planning, answer composition, specialist evidence analysis, and evidence review.

## Execution snapshot

`RagRuntimeProfileService` is scoped and resolves each Knowledge Base profile once per request scope. Budget reservation, Agent execution, and Evaluation therefore observe the same immutable execution snapshot for:

- Planner / Answer / Reviewer model routing.
- Planner, multi-query, evidence, and Reviewer limits.
- Agent mode.
- Prompt profile and managed prompt bodies.
- Budget and daily-run policy.

A concurrent administrator update takes effect on a later request rather than changing a run halfway through Reservation -> Execute -> Settle.

## Model routing

RAG role routing is deployment-first. A Knowledge Base may bind Planner, Answer, Reviewer/Specialist, and Reranker to tenant-scoped Model Deployment IDs. Each run resolves those bindings once at request start into immutable endpoint snapshots.

A Deployment owns provider connection identity, remote model name, health state, and an optional explicit same-type fallback. If the selected Deployment is disabled or persistently unhealthy, resolution may follow only that configured fallback chain; it never silently jumps to another Provider. Fallback cycles are rejected.

When a KB role has no Deployment binding, routing falls back to the tenant default Deployment and then to the platform `AI` configuration. Existing raw `PlannerModel` / `AnswerModel` / `ReviewerModel` fields remain only as backward-compatible model-name overrides when no Deployment is bound.

Cost governance follows the same identity model: deployment-specific prices are preferred over legacy `(tenant, model-name)` prices, so two Providers exposing the same model string can be priced independently. Budget Reservation and usage Settle carry the resolved `DeploymentId` / `ProviderType` snapshot.

Recommended production routing:

- Planner: small/fast chat Deployment with reliable structured output.
- Answer: strongest chat Deployment justified by quality/cost targets.
- Reviewer/Specialists: chat Deployment optimized for structured verification.
- Reranker: explicit rerank Deployment when LLM reranking is enabled.

Health checks are administrative observations, not an unconstrained runtime load balancer. A successful check stores `healthy`; a failed check stores `unhealthy`; edits that change provider/model/type/enablement reset health to `unknown`.

## Workflow controls

```json
{
  "Rag": {
    "EnableAgentWorkflow": true,
    "EnablePlanner": true,
    "PlannerMaxQueries": 3,
    "MultiQueryTopK": 6,
    "MaxMergedEvidence": 10,
    "MaxEvidenceTokens": 6000,
    "MaxEvidenceTokensPerDocument": 2200,
    "EnableReviewer": true,
    "ReviewerMaxRetries": 1,
    "ReviewerMinConfidence": 0.65
  }
}
```

`EnableAgentWorkflow=false` is the rollout rollback switch. It keeps the grounded Answer Agent but falls back to one deterministic retrieval query.

## Retrieval failure policy

The original user query is always the first retrieval query and is mandatory. Planner-generated variants improve recall but are best-effort:

- Original query fails: the retrieval round fails instead of silently answering from incomplete context.
- Planner variant fails: emit a safe failed-query progress event and continue with successful ranked lists.
- Empty evidence after retrieval: the Answer Agent must abstain rather than use model memory for knowledge facts.

This prevents one optional expansion query from becoming a reliability single point of failure while preserving the user's own query as the required search path.

## Evidence context budget

The final ranked Evidence list is bounded by both count and approximate context tokens. Evidence is consumed in deterministic rerank order with:

- `MaxEvidenceTokens` total budget.
- `MaxEvidenceTokensPerDocument` per-document budget.
- deterministic truncation of an oversized final chunk.

Reviewer supplemental rounds are deduplicated by PointId. Cross-round candidates are ordered from normalized fusion evidence rather than directly comparing round-local reranker scores.

## Citation contract and final gate

Model-facing Evidence is labeled `[E1]`, `[E2]`, ... and is always treated as untrusted data. The managed Answer policy requires knowledge-backed claims to use those Evidence IDs; the Reviewer treats missing, nonexistent, or mismatched Evidence IDs as citation errors.

After the Agent workflow completes, `FinalAnswerPolicy` runs before user-visible answer text is persisted or emitted:

1. `CitationContract` resolves `[E#]` against the exact Evidence list used by the run.
2. Invalid/out-of-range IDs fail closed and are never exposed as trusted Sources.
3. A knowledge-backed non-abstention answer must contain at least one valid citation.
4. Only actually cited Evidence becomes a `ChatSource`.
5. Model-facing IDs are normalized to stable user-facing `[1]`, `[2]`, ... numbering.
6. A failed gate returns the deterministic safe insufficient-evidence response with no trusted Source.

This gate does not depend on the Reviewer noticing every formatting mistake; Reviewer quality and deterministic citation validity are independent protections.

## Streaming policy

Agent execution progress remains real-time through structured SSE Timeline events. Answer text is buffered until the complete workflow has finished and the deterministic Final Answer Citation Gate has accepted/normalized the result. The client therefore never sees an unreviewed draft or a transient raw/invalid Evidence ID.

The current correctness-first policy emits the accepted answer as a final SSE delta rather than exposing token-by-token model text. Restoring token streaming requires a citation-aware protocol that preserves the same pre-release validation guarantee.

## Session policy

The accepted Answer Agent session is persisted in `Conversation.AgentSessionJson`. Planner reads the session for follow-up context but does not persist planner state. A rejected answer draft is never used as the base session for repair; regeneration starts from the original turn session snapshot.

The final Conversation gate additionally avoids advancing persisted session state when the final answer is rejected or is a deterministic insufficient-evidence abstention.

## Failure policy

- Planner failure: fall back to the original user query so planning is not a single point of failure.
- Original retrieval query failure: fail the retrieval round.
- Optional planner query failure: continue with successful lists and record the failure.
- Retrieval returns no evidence: Answer must report insufficient knowledge evidence.
- Reviewer failure: fail closed; treat the answer as unverified and use the bounded repair path.
- Reviewer still rejects after the configured retry budget: return the deterministic safe insufficient-evidence response.
- Final citation gate fails: return the deterministic safe insufficient-evidence response with no trusted Sources.
- Original user query always participates in retrieval even when the planner fills all query slots.

## Observability

The workflow uses the existing `KnowledgeRag.NonAzure` `ActivitySource` and Agent Framework workflow OpenTelemetry instrumentation. Important spans/tags include:

- `rag.workflow.turn`
- `rag.workflow.plan` (`rag.plan.intent`, `rag.plan.query_count`, fallback)
- `rag.multi_query_search` (`rag.query_count`, failed-query count, merged/result counts, evidence token estimate)
- existing `rag.search` per query variant
- `rag.workflow.answer`
- `rag.workflow.review` (`attempt`, `confidence`, `accepted`)
- `rag.retrieval.rounds`, `rag.evidence.count`, `rag.review.passed`

The persisted Run Trace also records a safe `citation.validation` event with gate status, citation counts, invalid citation counts, retrieved evidence count, and cited source count. It never stores chain-of-thought or system prompts.

## Evaluation v3

The persisted Evaluation pipeline uses `schemaVersion=3` / `evaluatorVersion=retrieval-citation-v3`.

Existing cases remain backward compatible: a plain `ExpectedAnswerContains` string still works unchanged. Cases may additionally provide expected sources (file name plus optional chunk index); the API stores the advanced expectation in a versioned envelope in the existing field, so no database migration is required.

For cases with expected sources, deterministic retrieval scoring includes:

- Hit rate.
- Recall@K.
- Mean Reciprocal Rank (MRR).
- nDCG.

These metrics are separate from answer/citation metrics, allowing retrieval regressions to be distinguished from Answer/Reviewer regressions. Run summaries, per-category results, per-case results, and `evaluation.completed` webhook metadata carry the retrieval metrics.

The benchmark file `evaluation/rag-benchmark.jsonl` continues to cover fact lookup, comparison, multi-hop retrieval, summary, no-answer behavior, conflicting sources, prompt injection, follow-up context, extraction, and citation accuracy.

Compare at least these variants before changing production defaults:

1. single-query fallback (`EnableAgentWorkflow=false`)
2. planner + multi-query + answer, reviewer disabled
3. full workflow with reviewer enabled

Track Retrieval Recall@K / MRR / nDCG, answer groundedness, citation correctness/completeness, no-answer accuracy, P50/P95 latency, token cost, reviewer repair rate, and reviewer final rejection rate.

## Chat runtime and history

The admin Chat keeps assistant-ui `ExternalStoreRuntime` as the UI runtime while TanStack Query and the server remain the source of truth. Long threads use bounded server pagination with `hasEarlier` / `onLoadEarlier`; SQLite performs timestamp ordering/comparison with `julianday` in SQL rather than loading an unbounded conversation into memory.

Conversation rename, archive/unarchive, delete, and feedback are wired through assistant-ui thread/action adapters. Archive state is persisted as an internal `__meta` ChatMessage event and is filtered from normal message history, avoiding a schema migration while keeping the state server-persistent. Feedback is written to the existing Audit Log.

The Chat renderer deliberately does not expose Chain of Thought. It renders only the trusted final answer, validated inline citation links, Source evidence, and safe structured Workflow Timeline metadata.

## Specialist expansion

The Answer Agent exposes stable evidence-analysis specialists as Agent-as-Tool functions for comparison, summarization, extraction, and research when the KB profile enables specialists. Add another specialist only when a stable task boundary appears. Do not add Group Chat, Handoff, or Magentic orchestration for ordinary RAG QA unless a new product requirement genuinely needs dynamic ownership or unconstrained collaboration.


## Implementation source of truth

When this guide needs an update, verify these current implementation and regression-test entry points first:

- Workflow orchestration: `src/KnowledgeRag.Infrastructure/Conversations/RagAgentService.cs`
- Deterministic plan normalization / retry policy: `src/KnowledgeRag.Infrastructure/Conversations/RagWorkflowPolicy.cs`
- Evidence ID mapping and Citation contract: `src/KnowledgeRag.Infrastructure/Conversations/CitationContract.cs`
- Final fail-closed answer validation: `src/KnowledgeRag.Infrastructure/Conversations/FinalAnswerPolicy.cs`
- Multi-query retrieval / fusion: `src/KnowledgeRag.Infrastructure/Search/MultiQuerySearchService.cs`
- Per-run profile, deployment routing and budget snapshot: `src/KnowledgeRag.Infrastructure/Governance/RagRuntimeProfileService.cs`
- Evaluation execution and scoring: `src/KnowledgeRag.Infrastructure/Governance/EvaluationService.cs`
- Conversation REST / SSE boundary: `src/KnowledgeRag.Api/Endpoints/ConversationEndpoints.cs`
- Regression tests: `tests/KnowledgeRag.Api.IntegrationTests/FinalAnswerPolicyTests.cs`, `RagWorkflowPolicyTests.cs`, `EvaluationRetrievalScoringTests.cs`

Related documentation: [system architecture](../ARCHITECTURE.md), [Tenant AI Provider configuration and model routing](AI_PROVIDER_CONFIGURATION.md), and [validation / CI](../VALIDATION.md).
