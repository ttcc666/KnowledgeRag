#!/usr/bin/env python3
"""Run the KnowledgeRag benchmark against a live API using only Python stdlib.

Required environment variables:
  RAG_BASE_URL   e.g. http://localhost:5000
  RAG_TOKEN      bearer access token
  RAG_KB_ID      knowledge base UUID with indexed benchmark content

Optional:
  RAG_BENCHMARK  path to JSONL dataset (default: evaluation/rag-benchmark.jsonl)
  RAG_RESULTS    output JSONL path (default: evaluation/results.jsonl)

The runner intentionally performs deterministic checks only. Use the saved results as
input to Microsoft Agent Framework / Microsoft.Extensions.AI.Evaluation model-backed
Relevance, Groundedness, Completeness and Retrieval evaluators when judge credentials
are available.
"""

from __future__ import annotations

import json
import os
import statistics
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

BASE_URL = os.environ.get("RAG_BASE_URL", "").rstrip("/")
TOKEN = os.environ.get("RAG_TOKEN", "")
KB_ID = os.environ.get("RAG_KB_ID", "")
BENCHMARK = Path(os.environ.get("RAG_BENCHMARK", "evaluation/rag-benchmark.jsonl"))
RESULTS = Path(os.environ.get("RAG_RESULTS", "evaluation/results.jsonl"))

INSUFFICIENT_SIGNALS = ("知识库中没有找到足够信息", "没有足够信息", "无法可靠", "证据不足")
SECRET_MARKERS = ("api_key=", "apikey=", "password=", "connectionstring=", "bearer ey")


def request_json(method: str, path: str, payload: dict | None = None) -> dict:
    data = None if payload is None else json.dumps(payload).encode("utf-8")
    request = urllib.request.Request(
        f"{BASE_URL}{path}",
        data=data,
        method=method,
        headers={
            "Authorization": f"Bearer {TOKEN}",
            "Content-Type": "application/json",
            "Accept": "application/json",
        },
    )
    with urllib.request.urlopen(request, timeout=120) as response:
        return json.loads(response.read().decode("utf-8"))


def create_conversation() -> str:
    result = request_json("POST", "/api/conversations", {"knowledgeBaseId": KB_ID})
    return result["id"]


def ask(conversation_id: str, query: str) -> tuple[dict, float]:
    started = time.perf_counter()
    response = request_json(
        "POST",
        f"/api/conversations/{conversation_id}/messages",
        {"message": query},
    )
    return response, (time.perf_counter() - started) * 1000


def load_cases() -> list[dict]:
    cases: list[dict] = []
    with BENCHMARK.open("r", encoding="utf-8") as handle:
        for line_number, line in enumerate(handle, 1):
            line = line.strip()
            if not line:
                continue
            item = json.loads(line)
            for required in ("id", "category", "query", "expectation"):
                if required not in item:
                    raise ValueError(f"{BENCHMARK}:{line_number}: missing {required}")
            cases.append(item)
    return cases


def source_count(response: dict) -> int:
    sources = response.get("sources") or []
    return len(sources) if isinstance(sources, list) else 0


def deterministic_checks(case: dict, response: dict) -> dict[str, bool]:
    answer = str(response.get("answer") or "").strip()
    lowered = answer.lower()
    has_sources = source_count(response) > 0
    insufficient = any(signal in answer for signal in INSUFFICIENT_SIGNALS)
    requires_knowledge = bool(case.get("requiresKnowledge", True))
    return {
        "non_empty": bool(answer),
        "no_obvious_secret_leak": not any(marker in lowered for marker in SECRET_MARKERS),
        "grounding_signal": (not requires_knowledge) or has_sources or insufficient,
        "no_answer_behavior": case.get("category") != "no-answer" or insufficient,
    }


def percentile(values: list[float], fraction: float) -> float:
    if not values:
        return 0.0
    ordered = sorted(values)
    index = min(len(ordered) - 1, max(0, round((len(ordered) - 1) * fraction)))
    return ordered[index]


def main() -> int:
    if not BASE_URL or not TOKEN or not KB_ID:
        print("RAG_BASE_URL, RAG_TOKEN and RAG_KB_ID are required.", file=sys.stderr)
        return 2

    cases = load_cases()
    RESULTS.parent.mkdir(parents=True, exist_ok=True)
    latencies: list[float] = []
    passed = 0

    with RESULTS.open("w", encoding="utf-8") as output:
        for index, case in enumerate(cases, 1):
            conversation_id = create_conversation()
            setup_response = None
            if case.get("setup"):
                setup_response, _ = ask(conversation_id, str(case["setup"]))

            try:
                response, latency_ms = ask(conversation_id, case["query"])
                checks = deterministic_checks(case, response)
                ok = all(checks.values())
                passed += int(ok)
                latencies.append(latency_ms)
                record = {
                    **case,
                    "conversationId": conversation_id,
                    "answer": response.get("answer"),
                    "sources": response.get("sources") or [],
                    "sourceCount": source_count(response),
                    "latencyMs": round(latency_ms, 2),
                    "checks": checks,
                    "passed": ok,
                    "setupAnswer": None if setup_response is None else setup_response.get("answer"),
                }
                print(f"[{index}/{len(cases)}] {case['id']}: {'PASS' if ok else 'FAIL'} ({latency_ms:.0f} ms)")
            except (urllib.error.URLError, TimeoutError) as error:
                record = {**case, "passed": False, "error": str(error)}
                print(f"[{index}/{len(cases)}] {case['id']}: ERROR {error}", file=sys.stderr)

            output.write(json.dumps(record, ensure_ascii=False) + "\n")
            output.flush()

    print()
    print(f"deterministic pass: {passed}/{len(cases)}")
    if latencies:
        print(f"latency mean: {statistics.mean(latencies):.0f} ms")
        print(f"latency p50:  {percentile(latencies, 0.50):.0f} ms")
        print(f"latency p95:  {percentile(latencies, 0.95):.0f} ms")
    print(f"results: {RESULTS}")
    return 0 if passed == len(cases) else 1


if __name__ == "__main__":
    raise SystemExit(main())
