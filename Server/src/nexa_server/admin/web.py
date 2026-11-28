"""Serves the admin single-page app (static files, no build step) with strict security headers."""

from __future__ import annotations

from pathlib import Path

from fastapi import APIRouter
from fastapi.responses import FileResponse, RedirectResponse, Response

STATIC_DIR = Path(__file__).parent / "static"

CSP = (
    "default-src 'none'; script-src 'self'; style-src 'self'; font-src 'self'; img-src 'self' data:; "
    "connect-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'"
)
HEADERS = {
    "Content-Security-Policy": CSP,
    "X-Content-Type-Options": "nosniff",
    "X-Frame-Options": "DENY",
    "Referrer-Policy": "no-referrer",
    "Cache-Control": "no-cache",
}

router = APIRouter()


@router.get("/admin", include_in_schema=False)
async def admin_root() -> RedirectResponse:
    return RedirectResponse("/admin/", status_code=307)


@router.get("/admin/", include_in_schema=False)
async def admin_index() -> FileResponse:
    return FileResponse(STATIC_DIR / "index.html", headers=HEADERS)


@router.get("/admin/assets/{path:path}", include_in_schema=False)
async def admin_asset(path: str) -> Response:
    base = (STATIC_DIR / "assets").resolve()
    target = (base / path).resolve()
    if base not in target.parents or not target.is_file():  # blocks ../ traversal
        return Response(status_code=404)
    return FileResponse(target, headers=HEADERS)
