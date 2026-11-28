"""Encryption of secret settings at rest (Fernet) and admin password hashing (scrypt)."""

from __future__ import annotations

import base64
import hashlib
import hmac
import os


class SecretUnreadable(Exception):
    """A stored secret could not be decrypted (the key changed or the data is damaged)."""


class SecretBox:
    """Encrypts short secrets. The key is derived from `key_material` (NEXA_SETTINGS_KEY, or
    NEXA_TOKEN_SECRET when that is empty). Changing the key makes stored secrets unreadable;
    they are then treated as "not set" and must be entered again."""

    def __init__(self, key_material: str) -> None:
        if len(key_material) < 32:
            raise ValueError("encryption key material must be at least 32 characters")
        from cryptography.fernet import Fernet

        raw = hmac.new(key_material.encode("utf-8"), b"nexa-settings-v1", hashlib.sha256).digest()
        self._fernet = Fernet(base64.urlsafe_b64encode(raw))

    def encrypt(self, text: str) -> str:
        return self._fernet.encrypt(text.encode("utf-8")).decode("ascii")

    def decrypt(self, token: str) -> str:
        from cryptography.fernet import InvalidToken

        try:
            return self._fernet.decrypt(token.encode("ascii")).decode("utf-8")
        except (InvalidToken, ValueError) as exc:
            raise SecretUnreadable() from exc


# --- admin password ---------------------------------------------------------

_N, _R, _P = 2**14, 8, 1
MIN_PASSWORD_LENGTH = 12
MAX_PASSWORD_LENGTH = 256


def _scrypt(password: str, salt: bytes, n: int, r: int, p: int) -> bytes:
    return hashlib.scrypt(password.encode("utf-8"), salt=salt, n=n, r=r, p=p, dklen=32, maxmem=128 * 1024 * 1024)


def hash_password(password: str) -> str:
    salt = os.urandom(16)
    digest = _scrypt(password, salt, _N, _R, _P)
    b64 = lambda b: base64.b64encode(b).decode("ascii")  # noqa: E731
    return f"scrypt${_N}${_R}${_P}${b64(salt)}${b64(digest)}"


def verify_password(password: str, stored: str) -> bool:
    try:
        scheme, n, r, p, salt_b64, digest_b64 = stored.split("$")
        if scheme != "scrypt":
            return False
        salt = base64.b64decode(salt_b64)
        expected = base64.b64decode(digest_b64)
        actual = _scrypt(password, salt, int(n), int(r), int(p))
    except (ValueError, TypeError):
        return False
    return hmac.compare_digest(actual, expected)


def password_problem(password: str) -> str | None:
    """Return a (Persian) reason if the password is not acceptable, else None."""
    if len(password) < MIN_PASSWORD_LENGTH:
        return f"رمز عبور باید دست‌کم {MIN_PASSWORD_LENGTH} نویسه باشد."
    if len(password) > MAX_PASSWORD_LENGTH:
        return "رمز عبور بیش از حد طولانی است."
    if len(set(password)) < 4:
        return "رمز عبور بیش از حد ساده است."
    return None
