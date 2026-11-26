"""POST /api/v1/session: the contract the Windows client already implements.

Request:  {"username": "...", "password": "..."}
200:      {"account": "DOMAIN\\sam", "token": "<bearer>"}   (`account` is stable for every login format)
401 bad credentials, 403 not in the required AD group, 429 too many attempts.
The request body is never logged.
"""

from __future__ import annotations

import logging

from fastapi import APIRouter, HTTPException, Request
from pydantic import BaseModel, Field

from ..auth.ldap import AuthenticationFailed, DirectoryUnavailable, NotAuthorized

logger = logging.getLogger(__name__)
router = APIRouter()


class SessionRequest(BaseModel):
    username: str = Field(min_length=1, max_length=260)
    password: str = Field(min_length=1, max_length=1024, repr=False)


class SessionResponse(BaseModel):
    account: str  # keep first: the client reads this field
    token: str


def client_ip(request: Request) -> str:
    return request.client.host if request.client else "unknown"


@router.post("/api/v1/session", response_model=SessionResponse)
async def create_session(body: SessionRequest, request: Request) -> SessionResponse:
    c = request.app.state.container
    if c.ldap is None:
        raise HTTPException(status_code=503, detail="authentication is not configured")

    ip = client_ip(request)
    wait = c.limiter.retry_after(ip, body.username)
    if wait:
        raise HTTPException(status_code=429, detail="too many attempts", headers={"Retry-After": str(wait)})

    try:
        account = await c.ldap.authenticate(body.username, body.password)
    except AuthenticationFailed:
        c.limiter.record_failure(ip, body.username)
        raise HTTPException(status_code=401, detail="invalid credentials")
    except NotAuthorized:
        raise HTTPException(status_code=403, detail="not authorized for NEXA")
    except DirectoryUnavailable:
        logger.error("directory unavailable during sign-in")
        raise HTTPException(status_code=503, detail="directory unavailable")

    c.limiter.record_success(ip, body.username)
    logger.info("sign-in ok account=%s", account)
    return SessionResponse(account=account, token=c.tokens.issue(account))
