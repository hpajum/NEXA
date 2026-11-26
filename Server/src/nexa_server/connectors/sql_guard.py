"""Read-only SQL guard for ClickHouse. Pure logic, no third-party imports.

Accepts exactly one SELECT / WITH ... SELECT statement. Defence in depth: the ClickHouse
client also sends readonly=2 and uses a read-only user; this guard is the first layer.
"""

from __future__ import annotations

import re

from .errors import QueryNotAllowed

MAX_SQL_LENGTH = 20_000

# Keywords that must never appear outside string literals / quoted identifiers.
_FORBIDDEN = {
    "INSERT", "UPDATE", "DELETE", "ALTER", "DROP", "TRUNCATE", "CREATE", "RENAME", "ATTACH",
    "DETACH", "GRANT", "REVOKE", "OPTIMIZE", "KILL", "SYSTEM", "SET", "USE", "EXCHANGE",
    "UNDROP", "BACKUP", "RESTORE", "INTO", "OUTFILE", "EXECUTE", "CALL",
}
# Table functions that read files, hit the network, or reach other servers.
_FORBIDDEN_FUNCTIONS = {
    "FILE", "URL", "URLCLUSTER", "S3", "S3CLUSTER", "HDFS", "HDFSCLUSTER", "REMOTE",
    "REMOTESECURE", "CLUSTER", "CLUSTERALLREPLICAS", "MYSQL", "POSTGRESQL", "MONGODB", "JDBC",
    "ODBC", "SQLITE", "REDIS", "AZUREBLOBSTORAGE", "INPUT", "EXECUTABLE", "DELTALAKE", "HUDI",
    "ICEBERG", "MERGETREEPROJECTION", "DICTGET",
}
_IDENT_RE = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")


def _strip_literals_and_comments(sql: str) -> str:
    """Replace string literals and quoted identifiers by spaces.

    Raises QueryNotAllowed on comments or unterminated quotes.
    """
    out: list[str] = []
    i, n = 0, len(sql)
    while i < n:
        ch = sql[i]
        nxt = sql[i + 1] if i + 1 < n else ""
        # Comments are rejected outright: the original text is forwarded to ClickHouse, and a
        # comment could hide a second statement from this scanner.
        if (ch == "-" and nxt == "-") or (ch == "/" and nxt == "*") or ch == "#":
            raise QueryNotAllowed("comments are not allowed")
        elif ch in ("'", '"', "`"):
            quote = ch
            i += 1
            while True:
                if i >= n:
                    raise QueryNotAllowed("unterminated quoted text")
                c = sql[i]
                if c == "\\" and quote == "'":  # ClickHouse allows backslash escapes in strings
                    i += 2
                    continue
                if c == quote:
                    if i + 1 < n and sql[i + 1] == quote:  # doubled quote escape
                        i += 2
                        continue
                    i += 1
                    break
                i += 1
            out.append(" ")
        else:
            out.append(ch)
            i += 1
    return "".join(out)


def validate_select(sql: str) -> str:
    """Return the cleaned statement (trailing ';' removed) or raise QueryNotAllowed."""
    if not isinstance(sql, str) or not sql.strip():
        raise QueryNotAllowed("empty query")
    if len(sql) > MAX_SQL_LENGTH:
        raise QueryNotAllowed("query too long")
    if "\x00" in sql:
        raise QueryNotAllowed("invalid character")

    stripped = _strip_literals_and_comments(sql)
    body = stripped.strip()
    while body.endswith(";"):
        body = body[:-1].rstrip()
    if ";" in body:
        raise QueryNotAllowed("multiple statements are not allowed")

    tokens = [t.upper() for t in _IDENT_RE.findall(body)]
    if not tokens or tokens[0] not in ("SELECT", "WITH"):
        raise QueryNotAllowed("only SELECT queries are allowed")
    if tokens[0] == "WITH" and "SELECT" not in tokens:
        raise QueryNotAllowed("WITH must be followed by SELECT")

    for token in tokens:
        if token in _FORBIDDEN:
            raise QueryNotAllowed(f"keyword not allowed: {token}")
    # Table functions: forbidden name directly followed by '('
    for match in re.finditer(r"([A-Za-z_][A-Za-z0-9_]*)\s*\(", body):
        if match.group(1).upper() in _FORBIDDEN_FUNCTIONS:
            raise QueryNotAllowed(f"function not allowed: {match.group(1).upper()}")

    # Return the ORIGINAL text (literals intact) without trailing semicolons.
    original = sql.strip()
    while original.endswith(";"):
        original = original[:-1].rstrip()
    return original
