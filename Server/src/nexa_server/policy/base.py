"""Access-control interface. PLACEHOLDER: the real rules are specified in the next phase.

Every connector use in the graph goes through `PolicyEngine.authorize`. No policy logic
is invented here.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Protocol, runtime_checkable


@dataclass(frozen=True)
class Decision:
    allowed: bool
    reason: str = ""


@runtime_checkable
class PolicyEngine(Protocol):
    async def authorize(self, account: str, action: str, resource: str) -> Decision:
        """Decide whether `account` may perform `action` on `resource`.

        action:   e.g. "data.discover", "data.query"
        resource: e.g. "openmetadata", "cube", "clickhouse" (finer-grained later)
        """
        ...
