"""Graph state."""

from __future__ import annotations

from typing import Any, Literal, TypedDict


class AgentState(TypedDict, total=False):
    # inputs
    account: str
    conversation_id: str
    question: str
    history: list[dict[str, Any]]
    # produced by nodes
    intent: Literal["chat", "data"]
    allowed: list[str]  # connector names the PolicyEngine allowed for this turn
    tables: list[dict[str, Any]]
    plan: dict[str, Any]  # {"kind": "cube", "query": {...}} | {"kind": "sql", "sql": "..."}
    result: dict[str, Any]
    notes: list[str]  # user-safe notes about skipped/failed steps
    answer: str
