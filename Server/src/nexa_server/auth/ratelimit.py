"""In-memory failed-login limiter keyed by client IP + username (and by IP alone)."""

from __future__ import annotations

import threading
import time
from collections import defaultdict, deque
from typing import Callable


class LoginRateLimiter:
    """Counts failures inside a sliding window. Successful logins clear the pair's counter.

    Per-process memory only: with several server workers each has its own counters.
    """

    def __init__(
        self,
        max_failures: int = 5,
        window_seconds: float = 300,
        clock: Callable[[], float] = time.monotonic,
    ) -> None:
        self._max = max_failures
        self._window = window_seconds
        self._clock = clock
        self._lock = threading.Lock()
        self._pair: dict[tuple[str, str], deque[float]] = defaultdict(deque)
        self._ip: dict[str, deque[float]] = defaultdict(deque)

    def _prune(self, bucket: deque[float], now: float) -> None:
        while bucket and now - bucket[0] >= self._window:
            bucket.popleft()

    def retry_after(self, ip: str, username: str) -> int:
        """Seconds to wait if blocked, else 0."""
        key = (ip, username.strip().lower())
        now = self._clock()
        with self._lock:
            waits = []
            for bucket, limit in ((self._pair[key], self._max), (self._ip[ip], self._max * 4)):
                self._prune(bucket, now)
                if len(bucket) >= limit:
                    waits.append(int(self._window - (now - bucket[0])) + 1)
            self._cleanup(key, ip)
            return max(waits) if waits else 0

    def record_failure(self, ip: str, username: str) -> None:
        key = (ip, username.strip().lower())
        now = self._clock()
        with self._lock:
            self._pair[key].append(now)
            self._ip[ip].append(now)

    def record_success(self, ip: str, username: str) -> None:
        key = (ip, username.strip().lower())
        with self._lock:
            self._pair.pop(key, None)

    def _cleanup(self, key: tuple[str, str], ip: str) -> None:
        if not self._pair[key]:
            self._pair.pop(key, None)
        if not self._ip[ip]:
            self._ip.pop(ip, None)
