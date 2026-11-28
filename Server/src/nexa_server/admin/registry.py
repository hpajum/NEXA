"""Which settings the panel can edit, how they are grouped and labelled.

Precedence (highest first): value saved in the panel (database) > NEXA_* environment / .env > default.
Fields not listed in `FIELDS` are read-only in the panel (server socket, TLS, secrets of the panel
itself) because changing them needs a restart.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any

from pydantic import SecretStr, ValidationError

from ..config import Settings


@dataclass(frozen=True)
class Field:
    key: str  # Settings attribute name
    group: str
    label: str
    kind: str  # text | number | bool | select | secret
    help: str = ""
    options: tuple[str, ...] = ()
    textarea: bool = False


GROUPS: list[tuple[str, str]] = [
    ("access", "دسترسی و نشست"),
    ("ldap", "Active Directory"),
    ("llm", "مدل زبانی (krouter)"),
    ("openmetadata", "OpenMetadata"),
    ("cube", "Cube"),
    ("clickhouse", "ClickHouse"),
]

FIELDS: list[Field] = [
    Field("policy_mode", "access", "حالت سیاست دسترسی", "select",
          "deny: هیچ اتصال دادهٔ سازمانی استفاده نمی‌شود. permissive: فقط برای توسعه.", ("deny", "permissive")),
    Field("token_ttl_seconds", "access", "عمر توکن ورود (ثانیه)", "number"),
    Field("rate_limit_max_failures", "access", "حداکثر ورود ناموفق در بازه", "number"),
    Field("rate_limit_window_seconds", "access", "بازهٔ محدودیت ورود (ثانیه)", "number"),

    Field("ldap_url", "ldap", "نشانی LDAP", "text", "مثال: ldaps://dc01.example.local:636"),
    Field("ldap_base_dn", "ldap", "Base DN", "text"),
    Field("ldap_bind_dn", "ldap", "حساب سرویس (Bind DN)", "text"),
    Field("ldap_bind_password", "ldap", "رمز حساب سرویس", "secret"),
    Field("ldap_user_filter", "ldap", "فیلتر جستجوی کاربر", "text", "{username} جایگزین می‌شود.", textarea=True),
    Field("ldap_required_group_dn", "ldap", "گروه مجاز AD (اختیاری)", "text",
          "اگر پر باشد، علاوه بر فهرست کاربران مجاز، عضویت در این گروه هم لازم است."),
    Field("ldap_netbios_domain", "ldap", "نام NetBIOS دامنه", "text", "مثال: EXAMPLE"),
    Field("ldap_tls_verify", "ldap", "اعتبارسنجی گواهی TLS", "bool"),
    Field("ldap_ca_file", "ldap", "مسیر فایل CA", "text"),
    Field("ldap_timeout_seconds", "ldap", "مهلت اتصال (ثانیه)", "number"),

    Field("llm_base_url", "llm", "نشانی krouter", "text", "مثال: http://krouter.internal:20128/v1"),
    Field("llm_api_key", "llm", "کلید API", "secret"),
    Field("llm_timeout_seconds", "llm", "مهلت پاسخ (ثانیه)", "number"),
    Field("model_chat", "llm", "نام مستعار مدل گفتگو", "text"),
    Field("model_sql", "llm", "نام مستعار مدل SQL", "text"),
    Field("model_fast", "llm", "نام مستعار مدل سریع", "text"),
    Field("model_embedding", "llm", "نام مستعار مدل embedding", "text"),

    Field("openmetadata_url", "openmetadata", "نشانی", "text"),
    Field("openmetadata_token", "openmetadata", "توکن (JWT)", "secret"),
    Field("openmetadata_timeout_seconds", "openmetadata", "مهلت (ثانیه)", "number"),
    Field("openmetadata_verify_tls", "openmetadata", "اعتبارسنجی TLS", "bool"),

    Field("cube_api_url", "cube", "نشانی REST API", "text", "شامل /cubejs-api/v1"),
    Field("cube_api_token", "cube", "توکن آماده", "secret"),
    Field("cube_api_secret", "cube", "API Secret", "secret", "اگر توکن آماده خالی باشد، برای هر درخواست توکن امضا می‌شود."),
    Field("cube_timeout_seconds", "cube", "مهلت (ثانیه)", "number"),
    Field("cube_verify_tls", "cube", "اعتبارسنجی TLS", "bool"),
    Field("cube_max_rows", "cube", "حداکثر ردیف", "number"),

    Field("clickhouse_host", "clickhouse", "میزبان", "text"),
    Field("clickhouse_port", "clickhouse", "درگاه", "number"),
    Field("clickhouse_user", "clickhouse", "کاربر", "text"),
    Field("clickhouse_password", "clickhouse", "رمز", "secret"),
    Field("clickhouse_database", "clickhouse", "پایگاه داده", "text"),
    Field("clickhouse_secure", "clickhouse", "اتصال HTTPS", "bool"),
    Field("clickhouse_verify", "clickhouse", "اعتبارسنجی گواهی", "bool"),
    Field("clickhouse_readonly", "clickhouse", "فقط‌خواندنی (readonly=2)", "bool"),
    Field("clickhouse_max_rows", "clickhouse", "حداکثر ردیف", "number"),
    Field("clickhouse_max_execution_time_seconds", "clickhouse", "حداکثر زمان اجرا (ثانیه)", "number"),
]

BY_KEY = {f.key: f for f in FIELDS}
SECRET_KEYS = {f.key for f in FIELDS if f.kind == "secret"}

# Shown read-only: changing these needs a restart (or is managed outside the panel).
RESTART_ONLY = ["host", "port", "log_level", "cors_origins", "tls_cert_file", "tls_key_file"]


def _to_text(value: Any) -> str:
    if isinstance(value, SecretStr):
        return value.get_secret_value()
    if isinstance(value, bool):
        return "true" if value else "false"
    return str(value)


class SettingsError(Exception):
    def __init__(self, problems: dict[str, str]) -> None:
        super().__init__("invalid settings")
        self.problems = problems


def build_settings(overrides: dict[str, str]) -> Settings:
    """Environment/.env first, then overrides on top. Raises SettingsError with per-field messages."""
    try:
        return Settings(**overrides)  # type: ignore[arg-type]
    except ValidationError as exc:
        problems: dict[str, str] = {}
        for err in exc.errors():
            loc = str(err["loc"][0]) if err["loc"] else ""
            problems[loc] = _short_message(err)
        raise SettingsError(problems) from None


def _short_message(err: dict) -> str:
    kind = err.get("type", "")
    if kind.startswith("int_") or kind.startswith("float_"):
        return "مقدار باید عدد معتبر باشد."
    if kind.startswith("bool"):
        return "مقدار باید true یا false باشد."
    if kind == "literal_error":
        return "مقدار مجاز نیست."
    return "مقدار نامعتبر است."


def describe(effective: Settings, overrides: dict[str, str], meta: dict[str, dict[str, Any]]) -> dict[str, Any]:
    """The payload the settings page renders. Secret values are never included, only whether one is set."""
    fields = []
    for f in FIELDS:
        value = getattr(effective, f.key)
        item: dict[str, Any] = {
            "key": f.key,
            "group": f.group,
            "label": f.label,
            "kind": f.kind,
            "help": f.help,
            "options": list(f.options),
            "textarea": f.textarea,
            "source": "panel" if f.key in overrides else "env",
        }
        if f.kind == "secret":
            item["is_set"] = bool(_to_text(value))
        else:
            item["value"] = _to_text(value)
        if f.key in meta:
            item["updated_at"] = meta[f.key]["updated_at"]
            item["updated_by"] = meta[f.key]["updated_by"]
        fields.append(item)
    readonly = [{"key": k, "value": _to_text(getattr(effective, k)) or "—"} for k in RESTART_ONLY]
    return {"groups": [{"id": g, "label": label} for g, label in GROUPS], "fields": fields, "readonly": readonly}


def normalise_changes(raw: dict[str, Any]) -> tuple[dict[str, str | None], dict[str, str]]:
    """Validate the shape of a PUT body: {key: value | null}. null = remove the panel override.
    For secrets, an empty string means "unchanged" (the UI never receives the old value)."""
    changes: dict[str, str | None] = {}
    problems: dict[str, str] = {}
    for key, value in raw.items():
        field = BY_KEY.get(key)
        if field is None:
            problems[key] = "این تنظیم از پنل قابل ویرایش نیست."
            continue
        if value is None:
            changes[key] = None
            continue
        if isinstance(value, bool):
            text = "true" if value else "false"
        elif isinstance(value, (int, float, str)):
            text = str(value).strip() if field.kind != "secret" else str(value)
        else:
            problems[key] = "نوع مقدار نامعتبر است."
            continue
        if len(text) > 4000:
            problems[key] = "مقدار بیش از حد طولانی است."
            continue
        if field.kind == "secret" and text == "":
            continue  # keep the stored secret
        if field.kind == "select" and text not in field.options:
            problems[key] = "مقدار مجاز نیست."
            continue
        changes[key] = text
    return changes, problems
