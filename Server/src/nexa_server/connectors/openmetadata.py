"""OpenMetadata REST client (read-only search / table lookup)."""

from __future__ import annotations

from typing import Any

import httpx

from .errors import ConnectorRejected, ConnectorUnavailable


class OpenMetadataClient:
    connector = "openmetadata"

    def __init__(
        self,
        base_url: str,
        token: str = "",
        timeout: float = 15,
        verify_tls: bool = True,
        transport: httpx.AsyncBaseTransport | None = None,
    ) -> None:
        headers = {"Accept": "application/json"}
        if token:
            headers["Authorization"] = f"Bearer {token}"
        self._http = httpx.AsyncClient(
            base_url=base_url.rstrip("/") + "/api/v1",
            headers=headers,
            timeout=timeout,
            verify=verify_tls,
            transport=transport,
            follow_redirects=False,
        )

    async def aclose(self) -> None:
        await self._http.aclose()

    async def _get(self, path: str, params: dict[str, Any] | None = None) -> dict[str, Any]:
        try:
            response = await self._http.get(path, params=params)
        except httpx.HTTPError as exc:
            raise ConnectorUnavailable(f"openmetadata unreachable ({type(exc).__name__})") from exc
        if response.status_code >= 500:
            raise ConnectorUnavailable(f"openmetadata error {response.status_code}")
        if response.status_code >= 400:
            raise ConnectorRejected(f"openmetadata rejected request ({response.status_code})")
        try:
            data = response.json()
        except ValueError as exc:
            raise ConnectorRejected("openmetadata returned invalid JSON") from exc
        return data if isinstance(data, dict) else {}

    async def health(self) -> None:
        await self._get("/system/version")

    async def search_tables(self, query: str, limit: int = 5) -> list[dict[str, Any]]:
        """Return compact table descriptors: fqn, description, columns (name, type, description)."""
        data = await self._get(
            "/search/query",
            {"q": query, "index": "table_search_index", "from": 0, "size": limit},
        )
        hits = (data.get("hits") or {}).get("hits") or []
        results: list[dict[str, Any]] = []
        for hit in hits:
            source = hit.get("_source") or {}
            results.append(
                {
                    "fqn": source.get("fullyQualifiedName", ""),
                    "description": source.get("description") or "",
                    "columns": [
                        {
                            "name": c.get("name", ""),
                            "type": c.get("dataType", ""),
                            "description": c.get("description") or "",
                        }
                        for c in (source.get("columns") or [])[:60]
                    ],
                }
            )
        return results
