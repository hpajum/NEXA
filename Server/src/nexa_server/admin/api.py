"""Admin panel JSON API under /admin/api. Cookie session + CSRF header on every mutation."""

from __future__ import annotations

import hmac
import logging
import time
from typing import Any

from fastapi import APIRouter, Depends, HTTPException, Request, Response
from pydantic import BaseModel, Field

from ..api.health import collect_checks
from ..api.session import client_ip
from ..auth.ldap import DirectoryUnavailable
from .crypto import hash_password, password_problem, verify_password
from .registry import SettingsError, describe, normalise_changes
from .sessions import COOKIE_NAME, AdminClaims, InvalidAdminSession
from .store import UserExists, UserNotFound

logger = logging.getLogger(__name__)
router = APIRouter(prefix="/admin/api")

ACTOR = "admin"
SAFE_METHODS = {"GET", "HEAD", "OPTIONS"}


def rt(request: Request):
    return request.app.state.admin


def _no_store(response: Response) -> None:
    response.headers["Cache-Control"] = "no-store"


def current_admin(request: Request) -> AdminClaims:
    runtime = rt(request)
    token = request.cookies.get(COOKIE_NAME)
    if not token:
        raise HTTPException(status_code=401, detail="not signed in")
    try:
        claims = runtime.sessions.verify(token)
    except InvalidAdminSession:
        raise HTTPException(status_code=401, detail="session expired")
    admin = runtime.store.get_admin()
    if admin is None or admin["version"] != claims.password_version:
        raise HTTPException(status_code=401, detail="session expired")  # password changed since
    if request.method not in SAFE_METHODS:
        sent = request.headers.get("x-csrf-token", "")
        if not hmac.compare_digest(sent.encode(), claims.csrf.encode()):
            raise HTTPException(status_code=403, detail="invalid csrf token")
    return claims


def _set_cookie(response: Response, request: Request, token: str) -> None:
    s = request.app.state.container.settings
    response.set_cookie(
        COOKIE_NAME, token, max_age=rt(request).sessions.ttl, httponly=True,
        secure=s.admin_cookie_secure, samesite="strict", path="/admin",
    )


# --- authentication ------------------------------------------------------------

class LoginBody(BaseModel):
    password: str = Field(min_length=1, max_length=1024, repr=False)


@router.post("/login")
async def login(body: LoginBody, request: Request, response: Response) -> dict:
    _no_store(response)
    runtime, ip = rt(request), client_ip(request)
    wait = runtime.login_limiter.retry_after(ip, ACTOR)
    if wait:
        raise HTTPException(status_code=429, detail="too many attempts", headers={"Retry-After": str(wait)})
    admin = runtime.store.get_admin()
    # Always spend the hashing time, so a missing password is not distinguishable by timing.
    ok = verify_password(body.password, admin["hash"] if admin else "scrypt$16384$8$1$AAAA$AAAA")
    if not admin or not ok:
        runtime.login_limiter.record_failure(ip, ACTOR)
        runtime.store.log(ACTOR, "admin.login_failed", ip=ip)
        raise HTTPException(status_code=401, detail="invalid credentials")
    runtime.login_limiter.record_success(ip, ACTOR)
    token, csrf = runtime.sessions.issue(admin["version"])
    _set_cookie(response, request, token)
    runtime.store.log(ACTOR, "admin.login", ip=ip)
    return {"csrf": csrf, "must_change_password": admin["must_change"]}


@router.post("/logout")
async def logout(request: Request, response: Response) -> dict:
    response.delete_cookie(COOKIE_NAME, path="/admin")
    return {"ok": True}


@router.get("/me")
async def me(request: Request, response: Response, claims: AdminClaims = Depends(current_admin)) -> dict:
    _no_store(response)
    admin = rt(request).store.get_admin()
    return {"csrf": claims.csrf, "expires_at": claims.expires_at,
            "must_change_password": bool(admin and admin["must_change"])}


class PasswordBody(BaseModel):
    current: str = Field(min_length=1, max_length=1024, repr=False)
    new: str = Field(min_length=1, max_length=1024, repr=False)


@router.post("/password")
async def change_password(body: PasswordBody, request: Request, response: Response,
                          claims: AdminClaims = Depends(current_admin)) -> dict:
    _no_store(response)
    runtime, ip = rt(request), client_ip(request)
    wait = runtime.login_limiter.retry_after(ip, ACTOR)
    if wait:
        raise HTTPException(status_code=429, detail="too many attempts", headers={"Retry-After": str(wait)})
    admin = runtime.store.get_admin()
    if not admin or not verify_password(body.current, admin["hash"]):
        runtime.login_limiter.record_failure(ip, ACTOR)
        raise HTTPException(status_code=400, detail="رمز فعلی نادرست است.")
    problem = password_problem(body.new)
    if problem:
        raise HTTPException(status_code=400, detail=problem)
    if body.new == body.current:
        raise HTTPException(status_code=400, detail="رمز جدید باید با رمز فعلی متفاوت باشد.")
    version = runtime.store.set_admin_password(hash_password(body.new), must_change=False)
    runtime.store.log(ACTOR, "admin.password_changed", ip=ip)
    token, csrf = runtime.sessions.issue(version)  # other sessions die, this one continues
    _set_cookie(response, request, token)
    return {"csrf": csrf}


# --- dashboard -----------------------------------------------------------------

@router.get("/overview")
async def overview(request: Request, response: Response, _: AdminClaims = Depends(current_admin)) -> dict:
    _no_store(response)
    c, runtime = request.app.state.container, rt(request)
    return {
        "checks": await collect_checks(c),
        "users": runtime.store.user_counts(),
        "policy_mode": c.settings.policy_mode,
        "server": {"version": request.app.version, "uptime_seconds": int(time.time() - runtime.started_at)},
        "recent": runtime.store.list_audit(limit=8)[0],
    }


# --- users (allow-list) ----------------------------------------------------------

class UserCreate(BaseModel):
    account: str = Field(min_length=1, max_length=260)
    note: str = Field(default="", max_length=500)


class UserPatch(BaseModel):
    enabled: bool | None = None
    note: str | None = Field(default=None, max_length=500)


@router.get("/users")
async def list_users(request: Request, response: Response, q: str = "", status: str = "all",
                     _: AdminClaims = Depends(current_admin)) -> dict:
    _no_store(response)
    if status not in ("all", "enabled", "disabled"):
        status = "all"
    return {"users": rt(request).store.list_users(q[:100], status)}


@router.get("/directory/search")
async def directory_search(request: Request, response: Response, q: str = "",
                           _: AdminClaims = Depends(current_admin)) -> dict:
    """Search AD for people to add. Each hit says whether it is already on the allow-list."""
    _no_store(response)
    c = request.app.state.container
    if c.ldap is None:
        raise HTTPException(status_code=503, detail="اتصال AD پیکربندی نشده است.")
    q = q.strip()
    if len(q) < 2:
        return {"results": []}
    try:
        found = await c.ldap.search_users(q[:100], 20)
    except DirectoryUnavailable:
        raise HTTPException(status_code=503, detail="اتصال به AD برقرار نشد.")
    known = {u["account"].lower() for u in rt(request).store.list_users()}
    for item in found:
        item["added"] = item["account"].lower() in known
    return {"results": found}


@router.post("/users", status_code=201)
async def add_user(body: UserCreate, request: Request, _: AdminClaims = Depends(current_admin)) -> dict:
    c, runtime = request.app.state.container, rt(request)
    if c.ldap is None:
        raise HTTPException(status_code=503, detail="اتصال AD پیکربندی نشده است.")
    try:
        found = await c.ldap.find_user(body.account)
    except DirectoryUnavailable:
        raise HTTPException(status_code=503, detail="اتصال به AD برقرار نشد.")
    if found is None:
        raise HTTPException(status_code=404, detail="کاربری با این نام در AD پیدا نشد.")
    try:
        user = runtime.store.add_user(found["account"], found["display_name"], body.note.strip(), ACTOR)
    except UserExists:
        raise HTTPException(status_code=409, detail="این کاربر قبلاً اضافه شده است.")
    runtime.store.log(ACTOR, "user.add", user["account"], ip=client_ip(request))
    return {"user": user}


@router.patch("/users/{user_id}")
async def patch_user(user_id: int, body: UserPatch, request: Request,
                     _: AdminClaims = Depends(current_admin)) -> dict:
    runtime = rt(request)
    try:
        before = runtime.store.get_user(user_id)
        user = runtime.store.update_user(user_id, body.enabled, body.note.strip() if body.note is not None else None)
    except UserNotFound:
        raise HTTPException(status_code=404, detail="کاربر پیدا نشد.")
    if body.enabled is not None and before and before["enabled"] != body.enabled:
        runtime.store.log(ACTOR, "user.enable" if body.enabled else "user.disable", user["account"],
                          ip=client_ip(request))
    if body.note is not None:
        runtime.store.log(ACTOR, "user.note", user["account"], ip=client_ip(request))
    return {"user": user}


@router.delete("/users/{user_id}")
async def delete_user(user_id: int, request: Request, _: AdminClaims = Depends(current_admin)) -> dict:
    runtime = rt(request)
    try:
        user = runtime.store.delete_user(user_id)
    except UserNotFound:
        raise HTTPException(status_code=404, detail="کاربر پیدا نشد.")
    runtime.store.log(ACTOR, "user.remove", user["account"], ip=client_ip(request))
    return {"ok": True}


# --- settings ------------------------------------------------------------------------

def _settings_payload(request: Request) -> dict[str, Any]:
    c, runtime = request.app.state.container, rt(request)
    return describe(c.settings, runtime.store.get_overrides(), runtime.store.override_meta())


@router.get("/settings")
async def get_settings(request: Request, response: Response, _: AdminClaims = Depends(current_admin)) -> dict:
    _no_store(response)
    return _settings_payload(request)


@router.put("/settings")
async def put_settings(body: dict[str, Any], request: Request, response: Response,
                       _: AdminClaims = Depends(current_admin)) -> dict:
    """Body: {key: value | null}. null removes the panel value (the .env value applies again).
    Blank secrets are ignored. Everything is validated, then applied live; nothing is saved on error."""
    _no_store(response)
    changes, problems = normalise_changes(body)
    if problems:
        raise HTTPException(status_code=422, detail={"fields": problems})
    if not changes:
        return {"changed": [], **_settings_payload(request)}
    runtime = rt(request)
    try:
        await runtime.apply(request.app, changes, ACTOR)
    except SettingsError as exc:
        raise HTTPException(status_code=422, detail={"fields": exc.problems})
    # Audit lists key names only; values (especially secrets) are never written.
    runtime.store.log(ACTOR, "settings.update", detail=", ".join(sorted(changes)), ip=client_ip(request))
    return {"changed": sorted(changes), **_settings_payload(request)}


@router.post("/settings/test")
async def test_connections(request: Request, response: Response, _: AdminClaims = Depends(current_admin)) -> dict:
    _no_store(response)
    return {"checks": await collect_checks(request.app.state.container)}


# --- audit ----------------------------------------------------------------------------

@router.get("/audit")
async def audit(request: Request, response: Response, limit: int = 50, offset: int = 0, action: str = "",
                _: AdminClaims = Depends(current_admin)) -> dict:
    _no_store(response)
    store = rt(request).store
    items, total = store.list_audit(limit, offset, action[:100])
    return {"items": items, "total": total, "actions": store.audit_actions()}
