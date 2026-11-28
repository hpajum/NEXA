"""SQLite persistence for the admin panel: admin credential, setting overrides, the AD
allow-list (users) and the audit log. Plain sqlite3, one connection guarded by a lock;
every statement is tiny, so calling it from request handlers is fine."""

from __future__ import annotations

import logging
import os
import sqlite3
import threading
import time
from contextlib import contextmanager
from typing import Any, Iterator

from .crypto import SecretBox, SecretUnreadable

logger = logging.getLogger(__name__)

AUDIT_KEEP = 50_000

# Each entry upgrades the schema by one version (PRAGMA user_version). Append only.
MIGRATIONS: list[str] = [
    """
    CREATE TABLE admin (
        id            INTEGER PRIMARY KEY CHECK (id = 1),
        password_hash TEXT    NOT NULL,
        version       INTEGER NOT NULL DEFAULT 1,
        must_change   INTEGER NOT NULL DEFAULT 0,
        updated_at    INTEGER NOT NULL
    );
    CREATE TABLE settings (
        key        TEXT PRIMARY KEY,
        value      TEXT    NOT NULL,
        secret     INTEGER NOT NULL DEFAULT 0,
        updated_at INTEGER NOT NULL,
        updated_by TEXT    NOT NULL
    );
    CREATE TABLE users (
        id            INTEGER PRIMARY KEY AUTOINCREMENT,
        account_key   TEXT    NOT NULL UNIQUE,
        account       TEXT    NOT NULL,
        display_name  TEXT    NOT NULL DEFAULT '',
        note          TEXT    NOT NULL DEFAULT '',
        enabled       INTEGER NOT NULL DEFAULT 1,
        created_at    INTEGER NOT NULL,
        created_by    TEXT    NOT NULL,
        updated_at    INTEGER NOT NULL,
        last_login_at INTEGER,
        login_count   INTEGER NOT NULL DEFAULT 0
    );
    CREATE TABLE audit (
        id     INTEGER PRIMARY KEY AUTOINCREMENT,
        ts     INTEGER NOT NULL,
        actor  TEXT    NOT NULL,
        action TEXT    NOT NULL,
        target TEXT    NOT NULL DEFAULT '',
        detail TEXT    NOT NULL DEFAULT '',
        ip     TEXT    NOT NULL DEFAULT ''
    );
    CREATE INDEX audit_action ON audit (action, id);
    """,
]


class UserExists(Exception):
    pass


class UserNotFound(Exception):
    pass


def account_key(account: str) -> str:
    return account.strip().lower()


def _like_escape(text: str) -> str:
    return text.replace("\\", "\\\\").replace("%", "\\%").replace("_", "\\_")


class Store:
    def __init__(self, path: str, box: SecretBox) -> None:
        directory = os.path.dirname(os.path.abspath(path))
        os.makedirs(directory, exist_ok=True)
        self._box = box
        self._lock = threading.RLock()
        self._db = sqlite3.connect(path, check_same_thread=False, isolation_level=None)
        self._db.row_factory = sqlite3.Row
        self._db.execute("PRAGMA journal_mode=WAL")
        self._db.execute("PRAGMA busy_timeout=5000")
        self._db.execute("PRAGMA foreign_keys=ON")
        self._migrate()
        try:
            os.chmod(path, 0o600)
        except OSError:
            pass

    # --- infrastructure ------------------------------------------------------

    def _migrate(self) -> None:
        with self._lock:
            current = self._db.execute("PRAGMA user_version").fetchone()[0]
            for version in range(current, len(MIGRATIONS)):
                self._db.execute("BEGIN IMMEDIATE")
                try:
                    for statement in MIGRATIONS[version].split(";"):
                        if statement.strip():
                            self._db.execute(statement)
                    self._db.execute(f"PRAGMA user_version = {version + 1}")
                    self._db.execute("COMMIT")
                except Exception:
                    self._db.execute("ROLLBACK")
                    raise

    @contextmanager
    def _tx(self) -> Iterator[sqlite3.Connection]:
        with self._lock:
            self._db.execute("BEGIN IMMEDIATE")
            try:
                yield self._db
            except Exception:
                self._db.execute("ROLLBACK")
                raise
            else:
                self._db.execute("COMMIT")

    def _all(self, sql: str, params: tuple = ()) -> list[sqlite3.Row]:
        with self._lock:
            return self._db.execute(sql, params).fetchall()

    def _one(self, sql: str, params: tuple = ()) -> sqlite3.Row | None:
        with self._lock:
            return self._db.execute(sql, params).fetchone()

    def close(self) -> None:
        with self._lock:
            self._db.close()

    # --- admin credential ----------------------------------------------------

    def get_admin(self) -> dict[str, Any] | None:
        row = self._one("SELECT password_hash, version, must_change FROM admin WHERE id = 1")
        if not row:
            return None
        return {"hash": row["password_hash"], "version": row["version"], "must_change": bool(row["must_change"])}

    def set_admin_password(self, password_hash: str, must_change: bool = False) -> int:
        """Store a new hash and bump the version (which invalidates every admin session)."""
        now = int(time.time())
        with self._tx() as db:
            row = db.execute("SELECT version FROM admin WHERE id = 1").fetchone()
            version = (row["version"] + 1) if row else 1
            db.execute(
                "INSERT INTO admin (id, password_hash, version, must_change, updated_at) VALUES (1, ?, ?, ?, ?) "
                "ON CONFLICT(id) DO UPDATE SET password_hash=excluded.password_hash, "
                "version=excluded.version, must_change=excluded.must_change, updated_at=excluded.updated_at",
                (password_hash, version, 1 if must_change else 0, now),
            )
        return version

    # --- setting overrides ---------------------------------------------------

    def get_overrides(self) -> dict[str, str]:
        """key -> text value, secrets decrypted. Unreadable secrets are skipped."""
        out: dict[str, str] = {}
        for row in self._all("SELECT key, value, secret FROM settings"):
            value = row["value"]
            if row["secret"]:
                try:
                    value = self._box.decrypt(value)
                except SecretUnreadable:
                    logger.error("stored secret for %s cannot be decrypted; treating it as unset", row["key"])
                    continue
            out[row["key"]] = value
        return out

    def override_meta(self) -> dict[str, dict[str, Any]]:
        return {
            row["key"]: {"updated_at": row["updated_at"], "updated_by": row["updated_by"]}
            for row in self._all("SELECT key, updated_at, updated_by FROM settings")
        }

    def apply_overrides(self, changes: dict[str, str | None], secret_keys: set[str], actor: str) -> None:
        """changes: key -> text to store, or None to delete the override (back to .env)."""
        now = int(time.time())
        with self._tx() as db:
            for key, text in changes.items():
                if text is None:
                    db.execute("DELETE FROM settings WHERE key = ?", (key,))
                    continue
                is_secret = key in secret_keys
                stored = self._box.encrypt(text) if is_secret else text
                db.execute(
                    "INSERT INTO settings (key, value, secret, updated_at, updated_by) VALUES (?, ?, ?, ?, ?) "
                    "ON CONFLICT(key) DO UPDATE SET value=excluded.value, secret=excluded.secret, "
                    "updated_at=excluded.updated_at, updated_by=excluded.updated_by",
                    (key, stored, 1 if is_secret else 0, now, actor),
                )

    # --- users (allow-list) --------------------------------------------------

    @staticmethod
    def _user(row: sqlite3.Row) -> dict[str, Any]:
        d = dict(row)
        d["enabled"] = bool(d["enabled"])
        d.pop("account_key", None)
        return d

    def list_users(self, query: str = "", status: str = "all") -> list[dict[str, Any]]:
        where, params = [], []
        if query.strip():
            like = f"%{_like_escape(query.strip().lower())}%"
            where.append("(account_key LIKE ? ESCAPE '\\' OR LOWER(display_name) LIKE ? ESCAPE '\\' "
                         "OR LOWER(note) LIKE ? ESCAPE '\\')")
            params += [like, like, like]
        if status == "enabled":
            where.append("enabled = 1")
        elif status == "disabled":
            where.append("enabled = 0")
        sql = "SELECT * FROM users"
        if where:
            sql += " WHERE " + " AND ".join(where)
        sql += " ORDER BY account_key"
        return [self._user(r) for r in self._all(sql, tuple(params))]

    def get_user(self, user_id: int) -> dict[str, Any] | None:
        row = self._one("SELECT * FROM users WHERE id = ?", (user_id,))
        return self._user(row) if row else None

    def add_user(self, account: str, display_name: str, note: str, actor: str) -> dict[str, Any]:
        now = int(time.time())
        try:
            with self._tx() as db:
                cur = db.execute(
                    "INSERT INTO users (account_key, account, display_name, note, enabled, created_at, "
                    "created_by, updated_at) VALUES (?, ?, ?, ?, 1, ?, ?, ?)",
                    (account_key(account), account, display_name, note, now, actor, now),
                )
                user_id = cur.lastrowid
        except sqlite3.IntegrityError as exc:
            raise UserExists(account) from exc
        user = self.get_user(user_id)
        assert user is not None
        return user

    def update_user(self, user_id: int, enabled: bool | None = None, note: str | None = None) -> dict[str, Any]:
        sets, params = ["updated_at = ?"], [int(time.time())]
        if enabled is not None:
            sets.append("enabled = ?")
            params.append(1 if enabled else 0)
        if note is not None:
            sets.append("note = ?")
            params.append(note)
        with self._tx() as db:
            cur = db.execute(f"UPDATE users SET {', '.join(sets)} WHERE id = ?", (*params, user_id))
            if cur.rowcount == 0:
                raise UserNotFound(user_id)
        user = self.get_user(user_id)
        assert user is not None
        return user

    def delete_user(self, user_id: int) -> dict[str, Any]:
        user = self.get_user(user_id)
        if user is None:
            raise UserNotFound(user_id)
        with self._tx() as db:
            db.execute("DELETE FROM users WHERE id = ?", (user_id,))
        return user

    def is_allowed(self, account: str) -> bool:
        row = self._one("SELECT enabled FROM users WHERE account_key = ?", (account_key(account),))
        return bool(row and row["enabled"])

    def record_login(self, account: str) -> None:
        with self._tx() as db:
            db.execute(
                "UPDATE users SET last_login_at = ?, login_count = login_count + 1 WHERE account_key = ?",
                (int(time.time()), account_key(account)),
            )

    def user_counts(self) -> dict[str, int]:
        row = self._one(
            "SELECT COUNT(*) AS total, COALESCE(SUM(enabled), 0) AS enabled, "
            "COALESCE(SUM(CASE WHEN last_login_at > ? THEN 1 ELSE 0 END), 0) AS active_24h FROM users",
            (int(time.time()) - 86400,),
        )
        assert row is not None
        return {
            "total": row["total"],
            "enabled": row["enabled"],
            "disabled": row["total"] - row["enabled"],
            "active_24h": row["active_24h"],
        }

    # --- audit ---------------------------------------------------------------

    def log(self, actor: str, action: str, target: str = "", detail: str = "", ip: str = "") -> None:
        """Never pass secret values in `detail`."""
        with self._tx() as db:
            db.execute(
                "INSERT INTO audit (ts, actor, action, target, detail, ip) VALUES (?, ?, ?, ?, ?, ?)",
                (int(time.time()), actor[:200], action[:100], target[:300], detail[:1000], ip[:64]),
            )
            db.execute("DELETE FROM audit WHERE id <= (SELECT MAX(id) FROM audit) - ?", (AUDIT_KEEP,))

    def list_audit(self, limit: int = 50, offset: int = 0, action: str = "") -> tuple[list[dict[str, Any]], int]:
        where, params = "", ()
        if action:
            where, params = " WHERE action = ?", (action,)
        total = self._one(f"SELECT COUNT(*) AS n FROM audit{where}", params)["n"]  # type: ignore[index]
        rows = self._all(
            f"SELECT * FROM audit{where} ORDER BY id DESC LIMIT ? OFFSET ?",
            (*params, max(1, min(limit, 200)), max(0, offset)),
        )
        return [dict(r) for r in rows], total

    def audit_actions(self) -> list[str]:
        return [r["action"] for r in self._all("SELECT DISTINCT action FROM audit ORDER BY action")]
