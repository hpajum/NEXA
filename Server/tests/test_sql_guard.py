import pytest

from nexa_server.connectors.errors import QueryNotAllowed
from nexa_server.connectors.sql_guard import MAX_SQL_LENGTH, validate_select


@pytest.mark.parametrize(
    "sql",
    [
        "SELECT 1",
        "select a, b from db.t where x = 1 limit 10",
        "SELECT 1;",
        "SELECT 1 ;  ;",
        "WITH t AS (SELECT 1 AS a) SELECT a FROM t",
        "SELECT 'DROP TABLE x; --' AS s",  # forbidden words inside a literal are fine
        'SELECT "insert" FROM t',  # quoted identifier
        "SELECT `update` FROM t",
        "SELECT 'it''s' AS s",
        "SELECT 'a\\'; DROP TABLE x' AS s",  # backslash-escaped quote stays inside the literal
        "SELECT count() FROM t WHERE ts >= now() - INTERVAL 1 DAY",
        "SELECT 1\nFROM t\nWHERE a = 2",
    ],
)
def test_accepts_read_only(sql):
    assert validate_select(sql).strip()


@pytest.mark.parametrize(
    "sql",
    [
        "",
        "   ",
        "DROP TABLE t",
        "INSERT INTO t VALUES (1)",
        "SELECT 1; SELECT 2",
        "SELECT 1; DROP TABLE t",
        "SELECT 1 /* x */",
        "SELECT 1 -- x",
        "SELECT 1 # x",
        "SELECT 1 /* ; DROP TABLE t */",
        "SHOW TABLES",
        "DESCRIBE t",
        "EXPLAIN SELECT 1",
        "ALTER TABLE t DELETE WHERE 1",
        "CREATE TABLE t (a Int8) ENGINE = Memory",
        "SET readonly = 0",
        "SYSTEM RELOAD CONFIG",
        "KILL QUERY WHERE 1",
        "SELECT * FROM t INTO OUTFILE '/tmp/x'",
        "SELECT * FROM file('/etc/passwd', 'CSV')",
        "SELECT * FROM url('http://evil/x', 'CSV')",
        "SELECT * FROM s3('http://x/y', 'CSV')",
        "SELECT * FROM remote('other', db.t)",
        "SELECT * FROM mysql('h:3306', 'd', 't', 'u', 'p')",
        "SELECT 'unterminated",
        'SELECT "unterminated',
        "WITH x AS 1",
        "SELECT 1\x00",
        "SELECT " + "1," * MAX_SQL_LENGTH + "1",
    ],
)
def test_rejects(sql):
    with pytest.raises(QueryNotAllowed):
        validate_select(sql)


def test_lowercase_and_spacing_do_not_bypass():
    with pytest.raises(QueryNotAllowed):
        validate_select("select * from  URL ('http://x', CSV)")
    with pytest.raises(QueryNotAllowed):
        validate_select("sElEcT 1; dRoP table t")


def test_returns_original_text_without_trailing_semicolon():
    assert validate_select("  SELECT 'a;b' ; ") == "SELECT 'a;b'"
