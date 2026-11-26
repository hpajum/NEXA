"""LLM access through krouter (OpenAI-compatible). Only model ALIASES are ever referenced."""

from __future__ import annotations

from typing import Protocol

ROLES = ("chat", "sql", "fast")


class LlmGateway(Protocol):
    async def complete(self, role: str, system: str, user: str) -> str:
        """role is one of ROLES; the gateway maps it to the configured alias."""
        ...


class KrouterLlm:
    """One ChatOpenAI client per alias, all pointed at the router."""

    def __init__(
        self,
        base_url: str,
        api_key: str,
        aliases: dict[str, str],
        timeout: float = 60,
    ) -> None:
        from langchain_openai import ChatOpenAI

        self._models = {
            role: ChatOpenAI(
                base_url=base_url,
                api_key=api_key or "not-needed",
                model=alias,
                timeout=timeout,
                temperature=0,
                max_retries=1,
            )
            for role, alias in aliases.items()
        }

    async def complete(self, role: str, system: str, user: str) -> str:
        from langchain_core.messages import HumanMessage, SystemMessage

        response = await self._models[role].ainvoke([SystemMessage(content=system), HumanMessage(content=user)])
        content = response.content
        return content if isinstance(content, str) else "".join(
            part.get("text", "") if isinstance(part, dict) else str(part) for part in content
        )


def build_llm(settings) -> LlmGateway | None:
    if not settings.llm_configured:
        return None
    return KrouterLlm(
        base_url=settings.llm_base_url,
        api_key=settings.llm_api_key.get_secret_value(),
        aliases={"chat": settings.model_chat, "sql": settings.model_sql, "fast": settings.model_fast},
        timeout=settings.llm_timeout_seconds,
    )
