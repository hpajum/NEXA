"""Application container: builds every client from Settings once, at startup."""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

from .auth.ldap import LdapAuthenticator
from .auth.ratelimit import LoginRateLimiter
from .auth.tokens import TokenService
from .config import Settings
from .connectors.clickhouse import ClickHouseClient
from .connectors.cube import CubeClient
from .connectors.openmetadata import OpenMetadataClient
from .graph.deps import Dependencies
from .llm.factory import build_llm
from .memory.default import NoopMemory
from .policy.default import build_policy


@dataclass
class Container:
    settings: Settings
    tokens: TokenService
    limiter: LoginRateLimiter
    ldap: LdapAuthenticator | None
    openmetadata: OpenMetadataClient | None
    cube: CubeClient | None
    clickhouse: ClickHouseClient | None
    graph: Any = None  # compiled LangGraph, or None if the LLM is not configured
    closers: list = field(default_factory=list)

    async def aclose(self) -> None:
        for client in (self.openmetadata, self.cube, self.clickhouse):
            if client is not None:
                await client.aclose()


def build_container(s: Settings) -> Container:
    ldap = None
    if s.ldap_configured:
        ldap = LdapAuthenticator(
            url=s.ldap_url,
            base_dn=s.ldap_base_dn,
            bind_dn=s.ldap_bind_dn,
            bind_password=s.ldap_bind_password.get_secret_value(),
            user_filter=s.ldap_user_filter,
            required_group_dn=s.ldap_required_group_dn,
            netbios_domain=s.ldap_netbios_domain,
            tls_verify=s.ldap_tls_verify,
            ca_file=s.ldap_ca_file,
            timeout=s.ldap_timeout_seconds,
        )
    om = (
        OpenMetadataClient(
            s.openmetadata_url,
            s.openmetadata_token.get_secret_value(),
            s.openmetadata_timeout_seconds,
            s.openmetadata_verify_tls,
        )
        if s.openmetadata_configured
        else None
    )
    cube = (
        CubeClient(
            s.cube_api_url,
            s.cube_api_token.get_secret_value(),
            s.cube_api_secret.get_secret_value(),
            s.cube_timeout_seconds,
            s.cube_verify_tls,
            s.cube_max_rows,
        )
        if s.cube_configured
        else None
    )
    ch = (
        ClickHouseClient(
            s.clickhouse_host,
            s.clickhouse_port,
            s.clickhouse_user,
            s.clickhouse_password.get_secret_value(),
            s.clickhouse_database,
            s.clickhouse_secure,
            s.clickhouse_verify,
            s.clickhouse_readonly,
            s.clickhouse_max_rows,
            s.clickhouse_max_execution_time_seconds,
        )
        if s.clickhouse_configured
        else None
    )

    graph = None
    llm = build_llm(s)
    if llm is not None:
        from .graph.builder import build_graph

        graph = build_graph(
            Dependencies(
                llm=llm,
                policy=build_policy(s.policy_mode),
                memory=NoopMemory(),
                openmetadata=om,
                cube=cube,
                clickhouse=ch,
            )
        )

    return Container(
        settings=s,
        tokens=TokenService(s.token_secret.get_secret_value(), s.token_ttl_seconds),
        limiter=LoginRateLimiter(s.rate_limit_max_failures, s.rate_limit_window_seconds),
        ldap=ldap,
        openmetadata=om,
        cube=cube,
        clickhouse=ch,
        graph=graph,
    )
