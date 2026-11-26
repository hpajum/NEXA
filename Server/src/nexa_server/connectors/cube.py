"""Cube REST API client. Auth: a ready token, or a JWT signed per request from the API secret."""

from __future__ import annotations

import time
from typing import Any

import httpx
import jwt

from .errors import ConnectorRejected, ConnectorUnavailable


class CubeClient:
    connector = "cube"

    def __init__(
        self,
        api_url: str,
        token: str = "",
        secret: str = "",
        timeout: float = 60,
        verify_tls: bool = True,
        max_rows: int = 10000,
        transport: httpx.AsyncBaseTransport | None = None,
    ) -> None:
        self._token = token
        self._secret = secret
        self._max_rows = max_rows
        self._http = httpx.AsyncClient(
            base_url=api_url.rstrip("/"),
            timeout=timeout,
            verify=verify_tls,
            transport=transport,
            follow_redirects=False,
        )

    async def aclose(self) -> None:
        await self._http.aclose()

    def _auth_header(self, account: str | None = None) -> dict[str, str]:
        if self._token:
            return {"Authorization": self._token}
        if self._secret:
            now = int(time.time())
            claims: dict[str, Any] = {"iat": now, "exp": now + 300}
            if account:
                # Available to Cube as securityContext.account (for row-level rules, next phase).
                claims["account"] = account
            return {"Authorization": jwt.encode(claims, self._secret, algorithm="HS256")}
        return {}

    async def _request(self, method: str, path: str, account: str | None = None, **kwargs: Any) -> Any:
        try:
            response = await self._http.request(method, path, headers=self._auth_header(account), **kwargs)
        except httpx.HTTPError as exc:
            raise ConnectorUnavailable(f"cube unreachable ({type(exc).__name__})") from exc
        if response.status_code >= 500:
            raise ConnectorUnavailable(f"cube error {response.status_code}")
        if response.status_code >= 400:
            detail = ""
            try:
                detail = str(response.json().get("error", ""))[:300]
            except (ValueError, AttributeError):
                pass
            raise ConnectorRejected(f"cube rejected request ({response.status_code}) {detail}".strip())
        try:
            return response.json()
        except ValueError as exc:
            raise ConnectorRejected("cube returned invalid JSON") from exc

    async def health(self) -> None:
        await self._request("GET", "/meta")

    async def meta(self, account: str | None = None) -> dict[str, Any]:
        data = await self._request("GET", "/meta", account)
        return data if isinstance(data, dict) else {}

    async def load(self, query: dict[str, Any], account: str | None = None) -> dict[str, Any]:
        """Run a Cube JSON query. The row limit is always capped by NEXA_CUBE_MAX_ROWS."""
        bounded = dict(query)
        requested = bounded.get("limit")
        bounded["limit"] = min(int(requested), self._max_rows) if isinstance(requested, int) and requested > 0 else self._max_rows
        data = await self._request("POST", "/load", account, json={"query": bounded})
        return data if isinstance(data, dict) else {}
