"""Active Directory authentication.

Flow: bind with the service account -> find the user by sAMAccountName/UPN -> bind as the
user's DN with the supplied password -> check group membership (nested, via the AD
matching-rule-in-chain OID) -> return the canonical account id `NETBIOS\\sam`.
"""

from __future__ import annotations

import asyncio
import logging
import re
from dataclasses import dataclass

logger = logging.getLogger(__name__)

_LDAP_ESCAPES = {"\\": r"\5c", "*": r"\2a", "(": r"\28", ")": r"\29", "\x00": r"\00"}
_SAM_RE = re.compile(r"^[^\\/\[\]:;|=,+*?<>\"@\s][^\\/\[\]:;|=,+*?<>\"@]{0,63}$")
MATCHING_RULE_IN_CHAIN = "1.2.840.113556.1.4.1941"


class AuthenticationFailed(Exception):
    """Unknown user or wrong password."""


class NotAuthorized(Exception):
    """Authenticated, but not allowed to use NEXA."""


class DirectoryUnavailable(Exception):
    """AD could not be reached or is misconfigured."""


def escape_filter_value(value: str) -> str:
    return "".join(_LDAP_ESCAPES.get(ch, ch) for ch in value)


def extract_login_name(username: str) -> str:
    """Reduce DOMAIN\\user, user@upn-suffix, or bare name to the part to search for.

    The UPN form is kept whole (the filter also matches userPrincipalName); for DOMAIN\\user
    only the part after the backslash is used. The returned value is NOT filter-escaped.
    """
    name = username.strip()
    if "\\" in name:
        name = name.rsplit("\\", 1)[1]
    return name


def canonical_account(netbios_domain: str, sam_account_name: str) -> str:
    """Stable id for the same person regardless of the login format used."""
    return f"{netbios_domain.strip().upper()}\\{sam_account_name.strip().lower()}"


def is_plausible_username(username: str) -> bool:
    name = extract_login_name(username)
    return bool(name) and len(name) <= 256 and "\x00" not in name


@dataclass(frozen=True)
class DirectoryUser:
    dn: str
    sam_account_name: str
    display_name: str = ""


def _attr(entry, name: str) -> str:
    try:
        value = entry[name].value
    except Exception:  # attribute absent from the entry
        return ""
    if isinstance(value, (list, tuple)):
        value = value[0] if value else ""
    return str(value or "")


class LdapAuthenticator:
    """Blocking ldap3 calls are executed in a worker thread."""

    def __init__(
        self,
        url: str,
        base_dn: str,
        bind_dn: str,
        bind_password: str,
        user_filter: str,
        required_group_dn: str,
        netbios_domain: str,
        tls_verify: bool = True,
        ca_file: str = "",
        timeout: float = 10,
    ) -> None:
        if not netbios_domain:
            raise ValueError("NEXA_LDAP_NETBIOS_DOMAIN is required for stable account ids")
        self._url = url
        self._base_dn = base_dn
        self._bind_dn = bind_dn
        self._bind_password = bind_password
        self._user_filter = user_filter
        self._group_dn = required_group_dn
        self._domain = netbios_domain
        self._tls_verify = tls_verify
        self._ca_file = ca_file
        self._timeout = timeout

    async def authenticate(self, username: str, password: str) -> str:
        return await asyncio.to_thread(self._authenticate_sync, username, password)

    async def health(self) -> None:
        await asyncio.to_thread(self._health_sync)

    # --- directory lookups for the admin panel ---------------------------

    async def search_users(self, query: str, limit: int = 20) -> list[dict]:
        """Prefix/substring search over AD people. Returns canonical account, name, mail, disabled."""
        return await asyncio.to_thread(self._search_users_sync, query, limit)

    async def find_user(self, login: str) -> dict | None:
        """Exact lookup (sAMAccountName, UPN or DOMAIN\\sam). None when absent or ambiguous."""
        return await asyncio.to_thread(self._find_user_sync, login)

    def _search_users_sync(self, query: str, limit: int) -> list[dict]:
        from ldap3 import SUBTREE
        from ldap3.core.exceptions import LDAPException

        q = escape_filter_value(extract_login_name(query))
        if not q:
            return []
        limit = max(1, min(limit, 50))
        flt = (
            "(&(objectClass=user)(objectCategory=person)(sAMAccountName=*)"
            f"(|(sAMAccountName={q}*)(displayName=*{q}*)(userPrincipalName={q}*)(mail={q}*)))"
        )
        conn = self._service_connection()
        try:
            conn.search(
                self._base_dn,
                flt,
                search_scope=SUBTREE,
                attributes=["sAMAccountName", "displayName", "mail", "userAccountControl"],
                paged_size=limit,  # first page only; avoids the size-limit exception path
            )
            out = []
            for entry in conn.entries[:limit]:
                sam = _attr(entry, "sAMAccountName")
                if not sam:
                    continue
                try:
                    disabled = bool(int(_attr(entry, "userAccountControl") or 0) & 2)
                except ValueError:
                    disabled = False
                out.append(
                    {
                        "account": canonical_account(self._domain, sam),
                        "display_name": _attr(entry, "displayName"),
                        "mail": _attr(entry, "mail"),
                        "ad_disabled": disabled,
                    }
                )
            return out
        except LDAPException as exc:
            logger.error("LDAP search failed: %s", type(exc).__name__)
            raise DirectoryUnavailable("search failed") from exc
        finally:
            conn.unbind()

    def _find_user_sync(self, login: str) -> dict | None:
        from ldap3.core.exceptions import LDAPException

        if not is_plausible_username(login):
            return None
        conn = self._service_connection()
        try:
            user = self._find_user(conn, extract_login_name(login))
        except LDAPException as exc:
            raise DirectoryUnavailable("user lookup failed") from exc
        finally:
            conn.unbind()
        if user is None:
            return None
        return {
            "account": canonical_account(self._domain, user.sam_account_name),
            "display_name": user.display_name,
        }

    # --- internals -------------------------------------------------------

    def _server(self):
        import ssl

        from ldap3 import Server, Tls

        # ldap3 only uses the Tls object for ldaps:// (and StartTLS); harmless otherwise.
        tls = Tls(
            validate=ssl.CERT_REQUIRED if self._tls_verify else ssl.CERT_NONE,
            ca_certs_file=self._ca_file or None,
        )
        return Server(self._url, tls=tls, connect_timeout=self._timeout, get_info="NONE")

    def _service_connection(self):
        from ldap3 import Connection
        from ldap3.core.exceptions import LDAPException

        try:
            return Connection(
                self._server(),
                user=self._bind_dn,
                password=self._bind_password,
                auto_bind=True,
                receive_timeout=self._timeout,
                raise_exceptions=True,
            )
        except LDAPException as exc:
            logger.error("LDAP service bind failed: %s", type(exc).__name__)
            raise DirectoryUnavailable("service bind failed") from exc

    def _health_sync(self) -> None:
        conn = self._service_connection()
        conn.unbind()

    def _find_user(self, conn, login: str) -> DirectoryUser | None:
        from ldap3 import SUBTREE

        search_filter = self._user_filter.replace("{username}", escape_filter_value(login))
        conn.search(
            self._base_dn,
            search_filter,
            search_scope=SUBTREE,
            attributes=["sAMAccountName", "displayName"],
            size_limit=2,
        )
        entries = conn.entries
        if len(entries) != 1:  # none, or ambiguous -> refuse
            return None
        entry = entries[0]
        sam = _attr(entry, "sAMAccountName")
        if not sam:
            return None
        return DirectoryUser(dn=entry.entry_dn, sam_account_name=sam, display_name=_attr(entry, "displayName"))

    def _is_member(self, conn, user: DirectoryUser) -> bool:
        from ldap3 import SUBTREE

        if not self._group_dn:
            return True
        group = escape_filter_value(self._group_dn)
        conn.search(
            user.dn,
            f"(memberOf:{MATCHING_RULE_IN_CHAIN}:={group})",
            search_scope=SUBTREE,
            attributes=["distinguishedName"],
            size_limit=1,
        )
        return len(conn.entries) > 0

    def _authenticate_sync(self, username: str, password: str) -> str:
        from ldap3 import Connection
        from ldap3.core.exceptions import LDAPBindError, LDAPException

        if not password or not is_plausible_username(username):
            raise AuthenticationFailed()  # empty password would be an anonymous bind in LDAP
        login = extract_login_name(username)
        service = self._service_connection()
        try:
            try:
                user = self._find_user(service, login)
            except LDAPException as exc:
                raise DirectoryUnavailable("user lookup failed") from exc
            if user is None:
                raise AuthenticationFailed()
            try:
                user_conn = Connection(
                    self._server(),
                    user=user.dn,
                    password=password,
                    auto_bind=True,
                    receive_timeout=self._timeout,
                    raise_exceptions=True,
                )
                user_conn.unbind()
            except LDAPBindError as exc:
                raise AuthenticationFailed() from exc
            except LDAPException as exc:
                raise DirectoryUnavailable("user bind failed") from exc
            try:
                allowed = self._is_member(service, user)
            except LDAPException as exc:
                raise DirectoryUnavailable("group lookup failed") from exc
            if not allowed:
                raise NotAuthorized()
            return canonical_account(self._domain, user.sam_account_name)
        finally:
            service.unbind()
