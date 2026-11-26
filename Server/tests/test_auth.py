from nexa_server.auth.ldap import (
    canonical_account,
    escape_filter_value,
    extract_login_name,
    is_plausible_username,
)
from nexa_server.auth.ratelimit import LoginRateLimiter
from nexa_server.auth.tokens import InvalidToken, TokenService

import pytest

SECRET = "x" * 40


def test_all_login_formats_resolve_to_same_account():
    # AD returns the same sAMAccountName for every format; the canonical id must be identical.
    assert extract_login_name("EXAMPLE\\JSmith") == "JSmith"
    assert extract_login_name("jsmith") == "jsmith"
    assert extract_login_name("jsmith@example.local") == "jsmith@example.local"  # matched via UPN filter
    ids = {canonical_account("example", sam) for sam in ("JSmith", "jsmith", "JSMITH")}
    assert ids == {"EXAMPLE\\jsmith"}


def test_filter_escaping_blocks_injection():
    assert escape_filter_value("a)(uid=*") == "a\\29\\28uid=\\2a"
    assert escape_filter_value("x\\y") == "x\\5cy"
    assert "*" not in escape_filter_value("*")


def test_plausible_username():
    assert not is_plausible_username("")
    assert not is_plausible_username("DOMAIN\\")
    assert not is_plausible_username("a\x00b")
    assert is_plausible_username("DOMAIN\\user")


def test_rate_limiter_blocks_then_expires():
    now = [0.0]
    limiter = LoginRateLimiter(max_failures=3, window_seconds=60, clock=lambda: now[0])
    for _ in range(3):
        assert limiter.retry_after("1.1.1.1", "Bob") == 0
        limiter.record_failure("1.1.1.1", "Bob")
    assert limiter.retry_after("1.1.1.1", "bob") > 0  # username is case-insensitive
    assert limiter.retry_after("1.1.1.1", "alice") == 0  # other user, same IP, still fine
    assert limiter.retry_after("2.2.2.2", "bob") == 0  # other IP
    now[0] = 61
    assert limiter.retry_after("1.1.1.1", "bob") == 0


def test_rate_limiter_success_resets_pair():
    limiter = LoginRateLimiter(max_failures=2, window_seconds=60, clock=lambda: 0.0)
    limiter.record_failure("1.1.1.1", "bob")
    limiter.record_success("1.1.1.1", "bob")
    limiter.record_failure("1.1.1.1", "bob")
    assert limiter.retry_after("1.1.1.1", "bob") == 0


def test_rate_limiter_per_ip_cap():
    limiter = LoginRateLimiter(max_failures=1, window_seconds=60, clock=lambda: 0.0)
    for i in range(4):  # 4 different users from one IP = 4x the pair limit
        limiter.record_failure("9.9.9.9", f"user{i}")
    assert limiter.retry_after("9.9.9.9", "someone-new") > 0


def test_token_roundtrip_and_tampering():
    svc = TokenService(SECRET, ttl_seconds=60)
    token = svc.issue("EXAMPLE\\jsmith")
    assert svc.verify(token).account == "EXAMPLE\\jsmith"
    with pytest.raises(InvalidToken):
        svc.verify(token + "x")
    with pytest.raises(InvalidToken):
        TokenService("y" * 40).verify(token)  # different secret


def test_token_expiry():
    svc = TokenService(SECRET, ttl_seconds=60)
    old = svc.issue("EXAMPLE\\jsmith", now=1000)
    with pytest.raises(InvalidToken):
        svc.verify(old)


def test_short_secret_rejected():
    with pytest.raises(ValueError):
        TokenService("short")
