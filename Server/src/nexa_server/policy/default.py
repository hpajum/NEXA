"""Default policies. Safe default = deny; permissive is an explicit development switch."""

from __future__ import annotations

import logging

from .base import Decision, PolicyEngine

logger = logging.getLogger(__name__)


class DenyAllPolicy:
    """Default (NEXA_POLICY_MODE=deny): no data connector may be used."""

    async def authorize(self, account: str, action: str, resource: str) -> Decision:
        return Decision(False, "access policy not configured")


class PermissiveDevPolicy:
    """NEXA_POLICY_MODE=permissive: allow everything. Development only."""

    async def authorize(self, account: str, action: str, resource: str) -> Decision:
        return Decision(True, "permissive development mode")


def build_policy(mode: str) -> PolicyEngine:
    if mode == "permissive":
        logger.warning("NEXA_POLICY_MODE=permissive: all data access is allowed. Do not use in production.")
        return PermissiveDevPolicy()
    return DenyAllPolicy()
