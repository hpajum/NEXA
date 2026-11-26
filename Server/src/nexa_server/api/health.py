"""Liveness and readiness. Readiness never exposes URLs, credentials or exception text."""

from __future__ import annotations

from fastapi import APIRouter, Request, Response

router = APIRouter()


@router.get("/healthz")
async def healthz() -> dict[str, str]:
    return {"status": "ok"}


@router.get("/readyz")
async def readyz(request: Request, response: Response) -> dict:
    c = request.app.state.container
    checks = {
        "ldap": c.ldap,
        "openmetadata": c.openmetadata,
        "cube": c.cube,
        "clickhouse": c.clickhouse,
    }
    report: dict[str, str] = {}
    for name, client in checks.items():
        if client is None:
            report[name] = "not_configured"
            continue
        try:
            await client.health()
            report[name] = "ok"
        except Exception:  # status only; details stay out of the response
            report[name] = "unavailable"
    report["llm"] = "configured" if c.settings.llm_configured else "not_configured"
    # LDAP is mandatory for sign-in; the data connectors are optional.
    ready = report["ldap"] == "ok"
    if not ready:
        response.status_code = 503
    return {"status": "ready" if ready else "not_ready", "checks": report}
