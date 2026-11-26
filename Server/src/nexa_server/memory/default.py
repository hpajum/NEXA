from __future__ import annotations

from typing import Any


class NoopMemory:
    """Remembers nothing."""

    async def load(self, account: str, conversation_id: str) -> list[dict[str, Any]]:
        return []

    async def save(self, account: str, conversation_id: str, entries: list[dict[str, Any]]) -> None:
        return None
