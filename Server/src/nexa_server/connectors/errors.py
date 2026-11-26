"""Typed errors shared by all data connectors."""

from __future__ import annotations


class ConnectorError(Exception):
    """Base class. Messages must never contain credentials."""

    connector = "connector"


class ConnectorUnavailable(ConnectorError):
    """Network failure, timeout, or 5xx from the upstream service."""


class ConnectorRejected(ConnectorError):
    """Upstream rejected the request (4xx / invalid query / auth)."""


class QueryNotAllowed(ConnectorError):
    """The SQL guard refused the statement."""
