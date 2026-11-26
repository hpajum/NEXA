"""ClickHouse client: guarded, read-only, row- and time-limited queries."""

from __future__ import annotations

import asyncio
from typing import Any

from .errors import ConnectorRejected, ConnectorUnavailable
from .sql_guard import validate_select


class ClickHouseClient:
    connector = "clickhouse"

    def __init__(
        self,
        host: str,
        port: int = 8123,
        user: str = "default",
        password: str = "",
        database: str = "default",
        secure: bool = False,
        verify: bool = True,
        readonly: bool = True,
        max_rows: int = 10000,
        max_execution_time: int = 30,
    ) -> None:
        self._params = dict(
            host=host, port=port, username=user, password=password,
            database=database, secure=secure, verify=verify,
            connect_timeout=10, send_receive_timeout=max_execution_time + 10,
        )
        self._readonly = readonly
        self._max_rows = max_rows
        self._max_time = max_execution_time
        self._client = None

    def _get_client(self):
        if self._client is None:
            import clickhouse_connect

            self._client = clickhouse_connect.get_client(**self._params)
        return self._client

    def _settings(self) -> dict[str, Any]:
        settings: dict[str, Any] = {
            "max_result_rows": self._max_rows,
            "result_overflow_mode": "break",
            "max_execution_time": self._max_time,
        }
        if self._readonly:
            settings["readonly"] = 2  # read-only, but allows setting the limits above
        return settings

    async def aclose(self) -> None:
        if self._client is not None:
            await asyncio.to_thread(self._client.close)
            self._client = None

    async def health(self) -> None:
        def _ping() -> None:
            try:
                self._get_client().query("SELECT 1", settings=self._settings())
            except Exception as exc:  # clickhouse-connect raises many types
                raise ConnectorUnavailable(f"clickhouse unreachable ({type(exc).__name__})") from exc

        await asyncio.to_thread(_ping)

    async def query(self, sql: str) -> dict[str, Any]:
        """Validate with the SQL guard, then run. Returns {"columns": [...], "rows": [[...]]}."""
        safe_sql = validate_select(sql)  # raises QueryNotAllowed

        def _run() -> dict[str, Any]:
            try:
                result = self._get_client().query(safe_sql, settings=self._settings())
            except Exception as exc:
                name = type(exc).__name__
                if "Database" in name or "Programming" in name:
                    raise ConnectorRejected(f"clickhouse rejected query ({name})") from exc
                raise ConnectorUnavailable(f"clickhouse error ({name})") from exc
            rows = [list(r) for r in result.result_rows[: self._max_rows]]
            return {"columns": list(result.column_names), "rows": rows}

        return await asyncio.to_thread(_run)
