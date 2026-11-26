"""Conversation memory interface. PLACEHOLDER: storage and semantics come in the next phase."""

from __future__ import annotations

from typing import Any, Protocol, runtime_checkable


@runtime_checkable
class MemoryStore(Protocol):
    async def load(self, account: str, conversation_id: str) -> list[dict[str, Any]]:
        """Return previous entries for this conversation (oldest first)."""
        ...

    async def save(self, account: str, conversation_id: str, entries: list[dict[str, Any]]) -> None:
        """Persist new entries for this conversation."""
        ...
