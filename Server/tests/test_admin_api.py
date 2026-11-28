"""Admin settings registry and full HTTP flow. Need the server dependencies (`pip install -e .[dev]`)."""

import os
import tempfile

import pytest

pytest.importorskip("pydantic_settings")
pytest.importorskip("fastapi")
pytest.importorskip("httpx")

from nexa_server.admin.registry import (  # noqa: E402
    BY_KEY,
    SettingsError,
    build_settings,
    describe,
    normalise_changes,
)

SECRET = "s" * 40
ADMIN_PW = "initial-admin-pass"


def clean_env(monkeypatch, **env):
    for key in list(os.environ):
        if key.startswith("NEXA_"):
            monkeypatch.delenv(key)
    monkeypatch.setenv("NEXA_TOKEN_SECRET", SECRET)
    for key, value in env.items():
        monkeypatch.setenv(f"NEXA_{key}", value)


def test_overrides_beat_environment(monkeypatch):
    clean_env(monkeypatch, MODEL_CHAT="from-env")
    assert build_settings({}).model_chat == "from-env"
    assert build_settings({"model_chat": "from-panel"}).model_chat == "from-panel"


def test_invalid_values_are_reported_per_field(monkeypatch):
    clean_env(monkeypatch)
    with pytest.raises(SettingsError) as info:
        build_settings({"clickhouse_port": "abc", "policy_mode": "nope"})
    assert set(info.value.problems) == {"clickhouse_port", "policy_mode"}


def test_normalise_rejects_unknown_keys_and_keeps_blank_secrets():
    changes, problems = normalise_changes({"token_secret": "x", "port": 1, "ldap_bind_password": "", "model_chat": " a ", "ldap_url": None})
    assert set(problems) == {"token_secret", "port"}  # never editable from the panel
    assert changes == {"model_chat": "a", "ldap_url": None}


def test_describe_never_returns_secret_values(monkeypatch):
    clean_env(monkeypatch, LDAP_BIND_PASSWORD="hunter2hunter2")
    payload = describe(build_settings({}), {}, {})
    assert "hunter2" not in repr(payload)
    item = next(f for f in payload["fields"] if f["key"] == "ldap_bind_password")
    assert item["is_set"] is True and "value" not in item
    assert all(k in BY_KEY for k in (f["key"] for f in payload["fields"]))


# --- HTTP flow ---------------------------------------------------------------------------

class FakeLdap:
    def __init__(self):
        self.people = {"jsmith": "John Smith", "mary": "Mary Major"}

    async def authenticate(self, username, password):
        from nexa_server.auth.ldap import AuthenticationFailed

        name = username.split("\\")[-1].lower()
        if name not in self.people or password != "pw":
            raise AuthenticationFailed()
        return f"EXAMPLE\\{name}"

    async def health(self):
        return None

    async def search_users(self, q, limit=20):
        return [{"account": f"EXAMPLE\\{k}", "display_name": v, "mail": "", "ad_disabled": False}
                for k, v in self.people.items() if q.lower() in k or q.lower() in v.lower()]

    async def find_user(self, login):
        name = login.split("\\")[-1].lower()
        return {"account": f"EXAMPLE\\{name}", "display_name": self.people[name]} if name in self.people else None


@pytest.fixture
def client(monkeypatch):
    from fastapi.testclient import TestClient

    import nexa_server.deps as deps
    from nexa_server.main import create_app

    db = os.path.join(tempfile.mkdtemp(), "nexa.db")
    clean_env(monkeypatch, ADMIN_DB_PATH=db, ADMIN_INITIAL_PASSWORD=ADMIN_PW, ADMIN_COOKIE_SECURE="false")
    original = deps.build_container

    def with_fake_ldap(settings, store=None):
        c = original(settings, store)
        c.ldap = FakeLdap()
        return c

    monkeypatch.setattr(deps, "build_container", with_fake_ldap)
    monkeypatch.setattr("nexa_server.main.build_container", with_fake_ldap)
    monkeypatch.setattr("nexa_server.admin.runtime.build_container", with_fake_ldap)
    with TestClient(create_app()) as tc:
        yield tc


def admin_login(client, password=ADMIN_PW):
    r = client.post("/admin/api/login", json={"password": password})
    assert r.status_code == 200, r.text
    return {"X-CSRF-Token": r.json()["csrf"]}


def test_panel_requires_login_and_csrf(client):
    assert client.get("/admin/api/users").status_code == 401
    assert client.post("/admin/api/login", json={"password": "wrong"}).status_code == 401
    headers = admin_login(client)
    assert client.get("/admin/api/users").status_code == 200
    assert client.post("/admin/api/users", json={"account": "jsmith"}).status_code == 403  # no CSRF header
    assert client.post("/admin/api/users", json={"account": "jsmith"}, headers=headers).status_code == 201


def test_only_allow_listed_users_can_sign_in(client):
    assert client.post("/api/v1/session", json={"username": "jsmith", "password": "pw"}).status_code == 403
    headers = admin_login(client)
    user = client.post("/admin/api/users", json={"account": "EXAMPLE\\JSmith"}, headers=headers).json()["user"]
    assert user["account"] == "EXAMPLE\\jsmith" and user["display_name"] == "John Smith"
    r = client.post("/api/v1/session", json={"username": "jsmith", "password": "pw"})
    assert r.status_code == 200
    token = r.json()["token"]

    client.patch(f"/admin/api/users/{user['id']}", json={"enabled": False}, headers=headers)
    assert client.post("/api/v1/session", json={"username": "jsmith", "password": "pw"}).status_code == 403
    # An already issued token stops working immediately as well.
    chat = client.post("/api/v1/chat", json={"message": "hi"}, headers={"Authorization": f"Bearer {token}"})
    assert chat.status_code == 403
    assert client.post("/admin/api/users", json={"account": "nobody"}, headers=headers).status_code == 404


def test_settings_apply_live_and_secrets_hidden(client):
    headers = admin_login(client)
    r = client.put("/admin/api/settings", json={"model_chat": "alias-x", "clickhouse_password": "Secret-XYZ"}, headers=headers)
    assert r.status_code == 200, r.text
    assert "Secret-XYZ" not in r.text
    assert client.app.state.container.settings.model_chat == "alias-x"
    assert client.app.state.container.settings.clickhouse_password.get_secret_value() == "Secret-XYZ"
    bad = client.put("/admin/api/settings", json={"clickhouse_port": "abc"}, headers=headers)
    assert bad.status_code == 422 and "clickhouse_port" in bad.json()["detail"]["fields"]
    assert client.app.state.container.settings.clickhouse_port == 8123  # nothing applied
    audit = client.get("/admin/api/audit").json()
    assert "Secret-XYZ" not in str(audit)


def test_password_change_invalidates_old_sessions(client):
    headers = admin_login(client)
    old_cookie = client.cookies.get("nexa_admin")
    r = client.post("/admin/api/password", json={"current": ADMIN_PW, "new": "a-brand-new-password"}, headers=headers)
    assert r.status_code == 200
    assert client.get("/admin/api/me").status_code == 200  # this session got a fresh cookie
    client.cookies.set("nexa_admin", old_cookie, path="/admin")
    assert client.get("/admin/api/me").status_code == 401


def test_static_panel_served_with_csp(client):
    r = client.get("/admin/")
    assert r.status_code == 200 and "frame-ancestors 'none'" in r.headers["content-security-policy"]
    assert client.get("/admin/assets/app.js").status_code == 200
    assert client.get("/admin/assets/../../api.py").status_code == 404
