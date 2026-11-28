"""Admin panel building blocks that need only the standard library, cryptography and PyJWT."""

import os
import tempfile

import pytest

pytest.importorskip("cryptography")

from nexa_server.admin.crypto import (  # noqa: E402
    SecretBox,
    SecretUnreadable,
    hash_password,
    password_problem,
    verify_password,
)
from nexa_server.admin.sessions import AdminSessions, InvalidAdminSession  # noqa: E402
from nexa_server.admin.store import Store, UserExists  # noqa: E402

KEY = "k" * 40


def new_store():
    path = os.path.join(tempfile.mkdtemp(), "sub", "nexa.db")
    return Store(path, SecretBox(KEY)), path


def test_secret_box_roundtrip_and_wrong_key():
    box = SecretBox(KEY)
    token = box.encrypt("p@ss")
    assert "p@ss" not in token and box.decrypt(token) == "p@ss"
    with pytest.raises(SecretUnreadable):
        SecretBox("z" * 40).decrypt(token)


def test_password_hash_and_policy():
    stored = hash_password("correct horse battery")
    assert stored.startswith("scrypt$") and "correct" not in stored
    assert verify_password("correct horse battery", stored)
    assert not verify_password("wrong", stored)
    assert not verify_password("x", "garbage")
    assert password_problem("short") is not None
    assert password_problem("aaaaaaaaaaaaaa") is not None  # too few distinct characters
    assert password_problem("a-Long-enough-pass") is None


def test_admin_password_version_bumps():
    store, _ = new_store()
    assert store.get_admin() is None
    v1 = store.set_admin_password(hash_password("first-password-1"), must_change=True)
    assert store.get_admin()["must_change"] is True
    v2 = store.set_admin_password(hash_password("second-password-2"))
    assert v2 == v1 + 1 and store.get_admin()["must_change"] is False


def test_allow_list_is_case_insensitive_and_unique():
    store, _ = new_store()
    assert not store.is_allowed("EXAMPLE\\jsmith")
    user = store.add_user("EXAMPLE\\jsmith", "John Smith", "", "admin")
    assert store.is_allowed("example\\JSMITH")
    with pytest.raises(UserExists):
        store.add_user("example\\JSmith", "", "", "admin")
    store.update_user(user["id"], enabled=False)
    assert not store.is_allowed("EXAMPLE\\jsmith")
    store.update_user(user["id"], enabled=True)
    store.record_login("EXAMPLE\\jsmith")
    assert store.get_user(user["id"])["login_count"] == 1
    assert store.user_counts() == {"total": 1, "enabled": 1, "disabled": 0, "active_24h": 1}
    store.delete_user(user["id"])
    assert not store.is_allowed("EXAMPLE\\jsmith")


def test_user_search_escapes_like_wildcards():
    store, _ = new_store()
    store.add_user("EXAMPLE\\a_b", "", "", "admin")
    store.add_user("EXAMPLE\\axb", "", "", "admin")
    assert [u["account"] for u in store.list_users("a_b")] == ["EXAMPLE\\a_b"]
    assert len(store.list_users("", "disabled")) == 0


def test_secret_overrides_are_encrypted_on_disk():
    store, path = new_store()
    store.apply_overrides({"ldap_bind_password": "TopSecret123", "model_chat": "alias"}, {"ldap_bind_password"}, "admin")
    assert store.get_overrides() == {"ldap_bind_password": "TopSecret123", "model_chat": "alias"}
    store.close()
    with open(path, "rb") as fh:
        raw = fh.read()
    for suffix in ("-wal", ""):
        if os.path.exists(path + suffix):
            with open(path + suffix, "rb") as fh:
                raw += fh.read()
    assert b"TopSecret123" not in raw
    reopened = Store(path, SecretBox(KEY))
    reopened.apply_overrides({"model_chat": None}, set(), "admin")
    assert "model_chat" not in reopened.get_overrides()
    # A different key: the secret is skipped, not crashed on.
    assert Store(path, SecretBox("q" * 40)).get_overrides() == {}


def test_audit_log_and_paging():
    store, _ = new_store()
    for i in range(5):
        store.log("admin", "user.add", f"EXAMPLE\\u{i}")
    store.log("admin", "settings.update", detail="model_chat")
    items, total = store.list_audit(limit=2)
    assert total == 6 and len(items) == 2 and items[0]["action"] == "settings.update"
    items, total = store.list_audit(action="user.add")
    assert total == 5
    assert store.audit_actions() == ["settings.update", "user.add"]


def test_reopen_keeps_schema():
    store, path = new_store()
    store.add_user("EXAMPLE\\x", "", "", "admin")
    store.close()
    assert Store(path, SecretBox(KEY)).is_allowed("EXAMPLE\\x")


def test_admin_session_tokens():
    sessions = AdminSessions(KEY, 600)
    token, csrf = sessions.issue(3)
    claims = sessions.verify(token)
    assert claims.password_version == 3 and claims.csrf == csrf
    with pytest.raises(InvalidAdminSession):
        sessions.verify(token + "x")
    with pytest.raises(InvalidAdminSession):
        sessions.verify(sessions.issue(3, now=1000)[0])  # expired


def test_admin_cookie_is_not_a_user_token():
    from nexa_server.auth.tokens import InvalidToken, TokenService

    admin_token, _ = AdminSessions(KEY, 600).issue(1)
    with pytest.raises(InvalidToken):
        TokenService(KEY).verify(admin_token)
    with pytest.raises(InvalidAdminSession):
        AdminSessions(KEY, 600).verify(TokenService(KEY).issue("EXAMPLE\\x"))
