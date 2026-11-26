"""Logging setup. Request bodies are never logged anywhere in this package."""

from __future__ import annotations

import logging


def configure_logging(level: str) -> None:
    logging.basicConfig(
        level=getattr(logging, level.upper(), logging.INFO),
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    )
    # httpx logs full request URLs at INFO; keep them out of normal logs.
    logging.getLogger("httpx").setLevel(logging.WARNING)
