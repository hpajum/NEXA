"""Config tests. Need pydantic-settings (installed with `pip install -e .[dev]`)."""

import pytest

pytest.importorskip("pydantic_settings")

from pydantic import ValidationError  # noqa: E402

from nexa_server.config import Settings  # noqa: E402

SECRET = "s" * 40


def make(monkeypatch, **env):
    for key in list(__import__("os").environ):
        if key.startswith("NEXA_"):
            monkeypatch.delenv(key)
    for key, value in env.items():
        monkeypatch.setenv(f"NEXA_{key}", value)
    return Settings(_env_file=None)


def test_secret_required_and_long_enough(monkeypatch):
    with pytest.raises(ValidationError):
        make(monkeypatch)
    with pytest.raises(ValidationError):
        make(monkeypatch, TOKEN_SECRET="short")


def test_defaults_are_safe(monkeypatch):
    s = make(monkeypatch, TOKEN_SECRET=SECRET)
    assert s.policy_mode == "deny"
    assert s.cors_origin_list == []
    assert s.clickhouse_readonly is True
    assert s.ldap_tls_verify is True
    assert not (s.ldap_configured or s.cube_configured or s.clickhouse_configured or s.llm_configured)


def test_aliases_and_flat_names(monkeypatch):
    s = make(
        monkeypatch,
        TOKEN_SECRET=SECRET,
        MODEL_CHAT="alias-chat",
        LLM_BASE_URL="http://router/v1",
        CLICKHOUSE_HOST="ch",
        CORS_ORIGINS="https://a, https://b",
    )
    assert s.model_chat == "alias-chat"
    assert s.llm_configured and s.clickhouse_configured
    assert s.cors_origin_list == ["https://a", "https://b"]


def test_secrets_not_in_repr(monkeypatch):
    s = make(monkeypatch, TOKEN_SECRET=SECRET, LDAP_BIND_PASSWORD="hunter2hunter2")
    assert "hunter2" not in repr(s) and SECRET not in repr(s)


def test_invalid_policy_mode(monkeypatch):
    with pytest.raises(ValidationError):
        make(monkeypatch, TOKEN_SECRET=SECRET, POLICY_MODE="allow-all")
