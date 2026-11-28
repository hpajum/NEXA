"""Admin session cookies (signed, short-lived) with a CSRF token bound to each session."""

from __future__ import annotations

import hashlib
import hmac
import secrets
import time
from dataclasses import dataclass

import jwt

AUDIENCE = "nexa-admin"
ISSUER = "nexa-server"
COOKIE_NAME = "nexa_admin"


class InvalidAdminSession(Exception):
    pass


@dataclass(frozen=True)
class AdminClaims:
    password_version: int
    csrf: str
    expires_at: int


class AdminSessions:
    def __init__(self, token_secret: str, ttl_seconds: int) -> None:
        # Derived key: an admin cookie can never be mistaken for a user bearer token.
        self._key = hmac.new(token_secret.encode("utf-8"), b"nexa-admin-session-v1", hashlib.sha256).digest()
        self._ttl = max(60, ttl_seconds)

    @property
    def ttl(self) -> int:
        return self._ttl

    def issue(self, password_version: int, now: float | None = None) -> tuple[str, str]:
        issued = int(now if now is not None else time.time())
        csrf = secrets.token_urlsafe(32)
        token = jwt.encode(
            {"sub": "admin", "iss": ISSUER, "aud": AUDIENCE, "iat": issued, "exp": issued + self._ttl,
             "pv": password_version, "csrf": csrf},
            self._key,
            algorithm="HS256",
        )
        return token, csrf

    def verify(self, token: str) -> AdminClaims:
        try:
            data = jwt.decode(
                token, self._key, algorithms=["HS256"], issuer=ISSUER, audience=AUDIENCE,
                options={"require": ["exp", "iat", "sub", "pv", "csrf"]},
            )
        except jwt.PyJWTError as exc:
            raise InvalidAdminSession() from exc
        if data.get("sub") != "admin" or not isinstance(data.get("pv"), int) or not isinstance(data.get("csrf"), str):
            raise InvalidAdminSession()
        return AdminClaims(password_version=data["pv"], csrf=data["csrf"], expires_at=int(data["exp"]))
