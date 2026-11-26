"""Graph nodes. Pure async functions over (state, deps); no LangGraph import here so they
are unit-testable with fakes.

Flow:  load_memory -> route -> [chat] ------------------------------> answer -> save_memory
                              [data] -> policy_gate -> discover -> plan_query -> execute -> answer
"""

from __future__ import annotations

import json
import logging
import re
from typing import Any

from ..connectors.errors import ConnectorError
from .deps import Dependencies
from .state import AgentState

logger = logging.getLogger(__name__)

MAX_RESULT_ROWS_FOR_LLM = 50

ROUTE_PROMPT = (
    "Classify the user's message. Reply with exactly one word: "
    "'data' if answering needs company data (tables, metrics, reports, numbers), otherwise 'chat'."
)
PLAN_PROMPT = (
    "You translate a business question into ONE read-only query.\n"
    "Prefer the Cube semantic layer when the listed cubes cover the question; otherwise write a "
    "single ClickHouse SELECT over the listed tables only.\n"
    "Reply with JSON only, one of:\n"
    '{"kind":"cube","query":{"measures":[],"dimensions":[],"filters":[],"timeDimensions":[],"limit":100}}\n'
    '{"kind":"sql","sql":"SELECT ... LIMIT 100"}\n'
    '{"kind":"none","reason":"..."}'
)
ANSWER_PROMPT = (
    "You are NEXA, the organisation's internal assistant. Answer in the user's language "
    "(usually Persian). When data results are given, base the answer only on them and say so "
    "if they are empty or truncated. Never invent numbers."
)

# Resources each action needs. Resource names are placeholders for the next-phase policy model.
DISCOVER = ("data.discover", "openmetadata")
QUERY_CUBE = ("data.query", "cube")
QUERY_SQL = ("data.query", "clickhouse")


def _note(state: AgentState, text: str) -> list[str]:
    return [*state.get("notes", []), text]


def extract_json(text: str) -> dict[str, Any] | None:
    """Parse the first JSON object in an LLM reply (tolerates ```json fences)."""
    match = re.search(r"\{.*\}", text, re.DOTALL)
    if not match:
        return None
    try:
        value = json.loads(match.group(0))
    except ValueError:
        return None
    return value if isinstance(value, dict) else None


async def load_memory(state: AgentState, deps: Dependencies) -> dict[str, Any]:
    history = await deps.memory.load(state["account"], state.get("conversation_id", ""))
    return {"history": history, "notes": state.get("notes", [])}


async def route(state: AgentState, deps: Dependencies) -> dict[str, Any]:
    if not (deps.openmetadata or deps.cube or deps.clickhouse):
        return {"intent": "chat"}
    reply = await deps.llm.complete("fast", ROUTE_PROMPT, state["question"])
    return {"intent": "data" if "data" in reply.strip().lower() else "chat"}


async def policy_gate(state: AgentState, deps: Dependencies) -> dict[str, Any]:
    """Ask the PolicyEngine for every connector before any of them is touched."""
    allowed: list[str] = []
    notes = state.get("notes", [])
    for (action, resource), client in (
        (DISCOVER, deps.openmetadata),
        (QUERY_CUBE, deps.cube),
        (QUERY_SQL, deps.clickhouse),
    ):
        if client is None:
            continue
        decision = await deps.policy.authorize(state["account"], action, resource)
        if decision.allowed:
            allowed.append(resource)
        else:
            notes = [*notes, f"access to {resource} denied: {decision.reason}"]
    return {"allowed": allowed, "notes": notes}


async def discover(state: AgentState, deps: Dependencies) -> dict[str, Any]:
    if "openmetadata" not in state.get("allowed", []) or deps.openmetadata is None:
        return {"tables": []}
    try:
        tables = await deps.openmetadata.search_tables(state["question"], limit=5)
    except ConnectorError as exc:
        logger.warning("discover failed: %s", exc)
        return {"tables": [], "notes": _note(state, "catalog search failed")}
    return {"tables": tables}


async def plan_query(state: AgentState, deps: Dependencies) -> dict[str, Any]:
    allowed = state.get("allowed", [])
    cube_ok = "cube" in allowed and deps.cube is not None
    sql_ok = "clickhouse" in allowed and deps.clickhouse is not None
    if not (cube_ok or sql_ok):
        return {"plan": {"kind": "none", "reason": "no data source allowed"}}

    context: dict[str, Any] = {"question": state["question"]}
    if cube_ok:
        try:
            meta = await deps.cube.meta(state["account"])
            context["cubes"] = [
                {
                    "name": c.get("name"),
                    "measures": [m.get("name") for m in c.get("measures", [])][:40],
                    "dimensions": [d.get("name") for d in c.get("dimensions", [])][:40],
                }
                for c in meta.get("cubes", [])[:30]
            ]
        except ConnectorError as exc:
            logger.warning("cube meta failed: %s", exc)
            cube_ok = False
    if sql_ok:
        context["tables"] = state.get("tables", [])
    if not (cube_ok or sql_ok):
        return {"plan": {"kind": "none", "reason": "data sources unavailable"}}
    context["allowed_kinds"] = [k for k, ok in (("cube", cube_ok), ("sql", sql_ok)) if ok]

    reply = await deps.llm.complete("sql", PLAN_PROMPT, json.dumps(context, ensure_ascii=False))
    plan = extract_json(reply) or {"kind": "none", "reason": "planner returned no JSON"}
    kind = plan.get("kind")
    if (kind == "cube" and not cube_ok) or (kind == "sql" and not sql_ok) or kind not in ("cube", "sql", "none"):
        plan = {"kind": "none", "reason": "planner chose an unavailable source"}
    return {"plan": plan}


async def execute(state: AgentState, deps: Dependencies) -> dict[str, Any]:
    plan = state.get("plan") or {}
    allowed = state.get("allowed", [])
    try:
        if plan.get("kind") == "cube" and "cube" in allowed and deps.cube is not None:
            query = plan.get("query")
            if not isinstance(query, dict):
                return {"result": {}, "notes": _note(state, "invalid cube query")}
            data = await deps.cube.load(query, state["account"])
            rows = data.get("data", [])
            return {"result": {"source": "cube", "rows": rows[:MAX_RESULT_ROWS_FOR_LLM], "row_count": len(rows)}}
        if plan.get("kind") == "sql" and "clickhouse" in allowed and deps.clickhouse is not None:
            data = await deps.clickhouse.query(str(plan.get("sql", "")))
            rows = data.get("rows", [])
            return {
                "result": {
                    "source": "clickhouse",
                    "columns": data.get("columns", []),
                    "rows": rows[:MAX_RESULT_ROWS_FOR_LLM],
                    "row_count": len(rows),
                }
            }
    except ConnectorError as exc:
        logger.warning("execute failed: %s", exc)
        return {"result": {}, "notes": _note(state, f"query failed: {type(exc).__name__}")}
    return {"result": {}}


async def answer(state: AgentState, deps: Dependencies) -> dict[str, Any]:
    payload: dict[str, Any] = {"question": state["question"]}
    if state.get("history"):
        payload["history"] = state["history"][-10:]
    if state.get("intent") == "data":
        payload["data_result"] = state.get("result") or {}
        payload["notes"] = state.get("notes", [])
    text = await deps.llm.complete("chat", ANSWER_PROMPT, json.dumps(payload, ensure_ascii=False, default=str))
    return {"answer": text}


async def save_memory(state: AgentState, deps: Dependencies) -> dict[str, Any]:
    await deps.memory.save(
        state["account"],
        state.get("conversation_id", ""),
        [{"role": "user", "content": state["question"]}, {"role": "assistant", "content": state.get("answer", "")}],
    )
    return {}


def after_route(state: AgentState) -> str:
    return "policy_gate" if state.get("intent") == "data" else "answer"
