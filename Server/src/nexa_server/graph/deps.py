"""Dependency container used by the graph (and built once in the app lifespan)."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Protocol

from ..llm.factory import LlmGateway
from ..memory.base import MemoryStore
from ..policy.base import PolicyEngine


class DiscoveryClient(Protocol):
    async def search_tables(self, query: str, limit: int = 5) -> list[dict[str, Any]]: ...


class SemanticClient(Protocol):
    async def meta(self, account: str | None = None) -> dict[str, Any]: ...
    async def load(self, query: dict[str, Any], account: str | None = None) -> dict[str, Any]: ...


class SqlClient(Protocol):
    async def query(self, sql: str) -> dict[str, Any]: ...


@dataclass
class Dependencies:
    llm: LlmGateway
    policy: PolicyEngine
    memory: MemoryStore
    openmetadata: DiscoveryClient | None = None
    cube: SemanticClient | None = None
    clickhouse: SqlClient | None = None
