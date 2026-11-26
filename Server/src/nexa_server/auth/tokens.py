"""Short-lived signed bearer tokens (HS256) issued after a successful AD login."""

from __future__ import annotations

import time
from dataclasses import dataclass

import jwt

ISSUER = "nexa-server"
AUDIENCE = "nexa-client"


class InvalidToken(Exception):
    pass


@dataclass(frozen=True)
class TokenClaims:
    account: str
    expires_at: int


class TokenService:
    def __init__(self, secret: str, ttl_seconds: int = 3600) -> None:
        if len(secret) < 32:
            raise ValueError("token secret must be at least 32 characters")
        self._secret = secret
        self._ttl = ttl_seconds

    def issue(self, account: str, now: float | None = None) -> str:
        issued = int(now if now is not None else time.time())
        payload = {
            "sub": account,
            "iss": ISSUER,
            "aud": AUDIENCE,
            "iat": issued,
            "exp": issued + self._ttl,
        }
        return jwt.encode(payload, self._secret, algorithm="HS256")

    def verify(self, token: str) -> TokenClaims:
        try:
            data = jwt.decode(
                token,
                self._secret,
                algorithms=["HS256"],
                issuer=ISSUER,
                audience=AUDIENCE,
                options={"require": ["exp", "iat", "sub"]},
            )
        except jwt.PyJWTError as exc:
            raise InvalidToken("invalid or expired token") from exc
        sub = data.get("sub")
        if not isinstance(sub, str) or not sub:
            raise InvalidToken("invalid token subject")
        return TokenClaims(account=sub, expires_at=int(data["exp"]))
