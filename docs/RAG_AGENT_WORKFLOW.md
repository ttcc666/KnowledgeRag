# RAG Agent Workflow

KnowledgeRag uses Microsoft Agent Framework to keep LLM responsibilities explicit while leaving retrieval, ranking, authorization, retry bounds, and failure policy deterministic.

## Turn graph

```text
User
  |
  v
QueryPlannerAgent (structured QueryPlan)
  |
  v
MultiQuerySearchService
  |-- original query (always retained)
  |-- planner query A  -- parallel, isolated DI scope / DbContext
  `-- planner query B
  |
  v
query-level RRF -> dedupe -> final rerank against original question
  |
  v
KnowledgeAnswerAgent
  |-- Evidence only for knowledge facts
  `-- analyze_evidence tool -> EvidenceAnalysisSpecialist
  |
  v
EvidenceReviewerAgent (structured EvidenceReview)
  |-- pass -> final answer
  `-- fail -> supplemental retrieval -> regenerate from original session snapshot
                  (bounded by Rag:ReviewerMaxRetries)
                  -> review again -> pass or safe insufficient-evidence response
```

## Why search remains code instead of an Agent

Vector retrieval, lexical retrieval, RRF, reranking, authorization boundaries, deduplication, and retry limits are deterministic operations. Keeping them as executors/services makes them testable, observable, and resistant to an LLM silently skipping retrieval. Agents are used only where language reasoning adds value: query planning, answer composition, specialist evidence analysis, and evidence review.

## Model routing

All role-specific model values are optional and fall back to `AI:ChatModel`.

```json
{
  "AI": {
    "ChatModel": "gpt-4o-mini",
    "PlannerModel": "",
    "AnswerModel": "",
    "ReviewerModel": ""
  }
}
```

Recommended production routing:

- Planner: small/fast model with reliable structured output.
- Answer: strongest model justified by quality/cost targets.
- Reviewer: small/fast model with reliable structured output.
- EvidenceAnalysisSpecialist: currently uses the reviewer model.

## Workflow controls

```json
{
  "Rag": {
    "EnableAgentWorkflow": true,
    "EnablePlanner": true,
    "PlannerMaxQueries": 3,
    "MultiQueryTopK": 6,
    "MaxMergedEvidence": 10,
    "EnableReviewer": true,
    "ReviewerMaxRetries": 1,
    "ReviewerMinConfidence": 0.65
  }
}
```

`EnableAgentWorkflow=false` is the rollout rollback switch. It keeps the new grounded Answer Agent but falls back to one deterministic retrieval query.

## Streaming policy

When Reviewer is disabled, the Answer Agent can stream token updates normally. When Reviewer is enabled, draft output is buffered and is not released to the client until review/repair finishes. This prevents an unsupported draft from being shown and then replaced.

## Session policy

The accepted Answer Agent session is persisted in the existing `Conversation.AgentSessionJson` field. Planner reads the session for follow-up context but does not persist planner state. A rejected answer draft is never used as the base session for repair; regeneration starts from the original turn session snapshot.

## Failure policy

- Planner failure: fall back to the original user query so planning is not a single point of failure.
- Retrieval returns no evidence: Answer must report insufficient knowledge evidence.
- Reviewer failure: fail closed; treat the answer as unverified and use the bounded repair path.
- Reviewer still rejects after the configured retry budget: return the deterministic safe insufficient-evidence response.
- Original user query always participates in retrieval even when the planner fills all query slots.

## Observability

The workflow uses the existing `KnowledgeRag.NonAzure` `ActivitySource` and Agent Framework workflow OpenTelemetry instrumentation. Important spans/tags include:

- `rag.workflow.turn`
- `rag.workflow.plan` (`rag.plan.intent`, `rag.plan.query_count`, fallback)
- `rag.multi_query_search` (`rag.query_count`, merged/result counts)
- existing `rag.search` per query variant
- `rag.workflow.answer`
- `rag.workflow.review` (`attempt`, `confidence`, `accepted`)
- `rag.retrieval.rounds`, `rag.evidence.count`, `rag.review.passed`

Because the API already exports the shared ActivitySource, these spans flow through the existing OTLP/console configuration.

## Evaluation

The benchmark is `evaluation/rag-benchmark.jsonl`. It deliberately covers fact lookup, comparison, multi-hop retrieval, summary, no-answer behavior, conflicting sources, prompt injection, follow-up context, extraction, and citation accuracy.

CI runs deterministic policy tests plus an Agent Framework `LocalEvaluator` smoke check without network calls. The live benchmark runner uses the real conversation API and saves answers, source evidence, deterministic checks, and latency for later quality judging.

```bash
export RAG_BASE_URL=http://localhost:5000
export RAG_TOKEN='<access-token>'
export RAG_KB_ID='<indexed-knowledge-base-guid>'
python3 evaluation/run_benchmark.py
```

The runner writes `evaluation/results.jsonl` by default. It returns a non-zero exit code if any deterministic safety/grounding/no-answer check fails. `follow-up` cases can include a `setup` prompt and are executed in the same conversation to exercise persisted Agent session context.

For model-backed evaluation, feed the saved results to Microsoft Agent Framework / `Microsoft.Extensions.AI.Evaluation` and use at least:

- RelevanceEvaluator
- GroundednessEvaluator
- CompletenessEvaluator
- RetrievalEvaluator

Compare at least these variants before changing production defaults:

1. single-query fallback (`EnableAgentWorkflow=false`)
2. planner + multi-query + answer, reviewer disabled
3. full workflow with reviewer enabled

Track Retrieval Recall@K, answer groundedness, citation correctness/completeness, no-answer accuracy, P50/P95 latency, token cost, reviewer repair rate, and reviewer final rejection rate.

## Specialist expansion

The Answer Agent currently exposes `EvidenceAnalysisSpecialist` as an Agent-as-Tool named `analyze_evidence`. Add another specialist only when a stable task boundary appears (for example structured extraction or policy comparison). Do not add Group Chat, Handoff, or Magentic orchestration for ordinary RAG QA unless a new product requirement genuinely needs dynamic ownership or unconstrained collaboration.
