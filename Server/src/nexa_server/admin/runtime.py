"""Runtime wiring for the admin panel: bootstrap, and applying saved settings without a restart.

`app.state.container` is the single place request handlers read the live clients from. Applying
settings builds a complete new container, swaps the reference, and closes the old one after a
grace period so in-flight requests finish on the clients they started with.
"""

from __future__ import annotations

import asyncio
import logging
import time
from dataclasses import dataclass, field
from typing import Any

from ..auth.ratelimit import LoginRateLimiter
from ..config import Settings
from ..deps import Container, build_container
from .crypto import SecretBox, hash_password, password_problem
from .registry import SECRET_KEYS, SettingsError, build_settings
from .sessions import AdminSessions
from .store import Store

logger = logging.getLogger(__name__)

CLOSE_GRACE_SECONDS = 30


@dataclass
class AdminRuntime:
    store: Store
    sessions: AdminSessions
    login_limiter: LoginRateLimiter
    started_at: float = field(default_factory=time.time)
    lock: asyncio.Lock = field(default_factory=asyncio.Lock)
    _closing: set = field(default_factory=set)

    def schedule_close(self, container: Container) -> None:
        async def _later() -> None:
            await asyncio.sleep(CLOSE_GRACE_SECONDS)
            try:
                await container.aclose()
            except Exception:
                logger.exception("closing the previous container failed")

        task = asyncio.get_running_loop().create_task(_later())
        self._closing.add(task)
        task.add_done_callback(self._closing.discard)

    async def apply(self, app: Any, changes: dict[str, str | None], actor: str) -> Container:
        """Validate -> build a candidate container -> persist -> swap. Raises SettingsError."""
        async with self.lock:
            overrides = self.store.get_overrides()
            for key, value in changes.items():
                if value is None:
                    overrides.pop(key, None)
                else:
                    overrides[key] = value
            settings = build_settings(overrides)  # SettingsError on bad values
            try:
                candidate = build_container(settings, self.store)
            except ValueError as exc:
                raise SettingsError({"_": _friendly(str(exc))}) from None
            self.store.apply_overrides(changes, SECRET_KEYS, actor)
            old: Container = app.state.container
            candidate.limiter = old.limiter  # keep failure counters across the swap
            candidate.limiter.reconfigure(settings.rate_limit_max_failures, settings.rate_limit_window_seconds)
            app.state.container = candidate
            self.schedule_close(old)
            return candidate

    async def reload_from_store(self, app: Any) -> Container:
        return await self.apply(app, {}, "system")


def _friendly(message: str) -> str:
    if "NETBIOS" in message.upper():
        return "نام NetBIOS دامنه برای فعال‌سازی LDAP لازم است."
    return "ترکیب تنظیمات معتبر نیست."


def create_runtime(base: Settings) -> AdminRuntime:
    store = Store(base.admin_db_path, SecretBox(base.encryption_key_material))
    if store.get_admin() is None:
        initial = base.admin_initial_password.get_secret_value()
        if initial:
            problem = password_problem(initial)
            if problem:
                logger.error("NEXA_ADMIN_INITIAL_PASSWORD rejected: %s", problem)
            else:
                store.set_admin_password(hash_password(initial), must_change=True)
                store.log("system", "admin.bootstrap", detail="initial admin password set from environment")
                logger.warning("admin password initialised from NEXA_ADMIN_INITIAL_PASSWORD; "
                               "change it after the first sign-in and remove the variable from .env")
        else:
            logger.warning("admin panel has no password yet: set NEXA_ADMIN_INITIAL_PASSWORD "
                           "or run `nexa-admin-password`")
    return AdminRuntime(
        store=store,
        sessions=AdminSessions(base.token_secret.get_secret_value(), base.admin_session_ttl_seconds),
        login_limiter=LoginRateLimiter(5, 300),
    )


def initial_settings(base: Settings, store: Store) -> Settings:
    """Settings at startup = env + saved overrides. A bad saved value must not stop the server:
    fall back to the environment alone and say so."""
    try:
        return build_settings(store.get_overrides())
    except SettingsError as exc:
        logger.error("saved panel settings are invalid (%s); starting from environment only",
                     ", ".join(sorted(exc.problems)))
        return base
