"""Environment-driven settings. Flat names: field `ldap_url` <-> env `NEXA_LDAP_URL`."""

from __future__ import annotations

from typing import Literal

from pydantic import SecretStr, field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

DEFAULT_USER_FILTER = (
    "(&(objectClass=user)(objectCategory=person)"
    "(|(sAMAccountName={username})(userPrincipalName={username})))"
)


class Settings(BaseSettings):
    model_config = SettingsConfigDict(
        env_prefix="NEXA_",
        env_file=".env",
        extra="ignore",
        case_sensitive=False,
        protected_namespaces=(),  # we have fields named model_*
    )

    # Server
    host: str = "0.0.0.0"
    port: int = 8000
    log_level: str = "INFO"
    cors_origins: str = ""
    tls_cert_file: str = ""
    tls_key_file: str = ""

    # Admin panel (web). Disabled = no /admin routes and no allow-list (every AD user passes).
    admin_enabled: bool = True
    admin_db_path: str = "/data/nexa.db"
    # Used once, when no admin password exists yet. Remove it from .env afterwards.
    admin_initial_password: SecretStr = SecretStr("")
    admin_session_ttl_seconds: int = 3600
    # Secure cookie flag. Keep true behind HTTPS; set false only for plain-http development.
    admin_cookie_secure: bool = True
    # Key for encrypting secret settings stored in the database. Empty = derived from token_secret.
    settings_key: SecretStr = SecretStr("")

    # Sessions
    token_secret: SecretStr
    token_ttl_seconds: int = 3600
    rate_limit_max_failures: int = 5
    rate_limit_window_seconds: float = 300

    # Access control / memory (next phase)
    policy_mode: Literal["deny", "permissive"] = "deny"

    # LLM via krouter: only aliases, never real model names
    llm_base_url: str = ""
    llm_api_key: SecretStr = SecretStr("")
    llm_timeout_seconds: float = 60
    model_chat: str = "nexa-chat"
    model_sql: str = "nexa-sql"
    model_fast: str = "nexa-fast"
    model_embedding: str = ""

    # Active Directory
    ldap_url: str = ""
    ldap_base_dn: str = ""
    ldap_bind_dn: str = ""
    ldap_bind_password: SecretStr = SecretStr("")
    ldap_user_filter: str = DEFAULT_USER_FILTER
    ldap_required_group_dn: str = ""
    ldap_netbios_domain: str = ""
    ldap_tls_verify: bool = True
    ldap_ca_file: str = ""
    ldap_timeout_seconds: float = 10

    # OpenMetadata
    openmetadata_url: str = ""
    openmetadata_token: SecretStr = SecretStr("")
    openmetadata_timeout_seconds: float = 15
    openmetadata_verify_tls: bool = True

    # Cube
    cube_api_url: str = ""
    cube_api_token: SecretStr = SecretStr("")
    cube_api_secret: SecretStr = SecretStr("")
    cube_timeout_seconds: float = 60
    cube_verify_tls: bool = True
    cube_max_rows: int = 10000

    # ClickHouse
    clickhouse_host: str = ""
    clickhouse_port: int = 8123
    clickhouse_user: str = "default"
    clickhouse_password: SecretStr = SecretStr("")
    clickhouse_database: str = "default"
    clickhouse_secure: bool = False
    clickhouse_verify: bool = True
    clickhouse_readonly: bool = True
    clickhouse_max_rows: int = 10000
    clickhouse_max_execution_time_seconds: int = 30

    @field_validator("token_secret")
    @classmethod
    def _strong_secret(cls, value: SecretStr) -> SecretStr:
        if len(value.get_secret_value()) < 32:
            raise ValueError("NEXA_TOKEN_SECRET must be at least 32 characters")
        return value

    @field_validator("settings_key")
    @classmethod
    def _strong_settings_key(cls, value: SecretStr) -> SecretStr:
        if value.get_secret_value() and len(value.get_secret_value()) < 32:
            raise ValueError("NEXA_SETTINGS_KEY must be empty or at least 32 characters")
        return value

    @property
    def encryption_key_material(self) -> str:
        return self.settings_key.get_secret_value() or self.token_secret.get_secret_value()

    @property
    def cors_origin_list(self) -> list[str]:
        return [o.strip() for o in self.cors_origins.split(",") if o.strip()]

    @property
    def llm_configured(self) -> bool:
        return bool(self.llm_base_url)

    @property
    def ldap_configured(self) -> bool:
        return bool(self.ldap_url and self.ldap_base_dn)

    @property
    def openmetadata_configured(self) -> bool:
        return bool(self.openmetadata_url)

    @property
    def cube_configured(self) -> bool:
        return bool(self.cube_api_url)

    @property
    def clickhouse_configured(self) -> bool:
        return bool(self.clickhouse_host)
