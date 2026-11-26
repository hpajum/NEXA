"""Minimal pytest stand-in for sandboxes without pytest: `python tests/run_without_pytest.py`.

Supports the subset used here: plain test_* functions, @pytest.mark.parametrize,
pytest.raises, pytest.importorskip and the monkeypatch fixture. Normal use is `pytest`.
"""

import importlib.util
import inspect
import os
import sys
import types
import traceback

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "src"))


class Skip(Exception):
    pass


class Raises:
    def __init__(self, exc):
        self.exc = exc

    def __enter__(self):
        return self

    def __exit__(self, et, ev, tb):
        if et is None:
            raise AssertionError(f"DID NOT RAISE {self.exc.__name__}")
        return issubclass(et, self.exc)


class MonkeyPatch:
    def __init__(self):
        self._undo = []

    def setenv(self, k, v):
        self._undo.append((k, os.environ.get(k)))
        os.environ[k] = v

    def delenv(self, k):
        self._undo.append((k, os.environ.get(k)))
        os.environ.pop(k, None)

    def close(self):
        for k, v in reversed(self._undo):
            os.environ.pop(k, None) if v is None else os.environ.__setitem__(k, v)


def parametrize(names, values):
    def deco(fn):
        fn._params = (names, values)
        return fn
    return deco


def importorskip(name):
    try:
        return __import__(name)
    except ImportError:
        raise Skip(name)


pytest = types.ModuleType("pytest")
pytest.raises = Raises
pytest.importorskip = importorskip
pytest.mark = types.SimpleNamespace(parametrize=parametrize)
sys.modules["pytest"] = pytest

passed = failed = skipped = 0
tests_dir = os.path.join(ROOT, "tests")
for fname in sorted(os.listdir(tests_dir)):
    if not (fname.startswith("test_") and fname.endswith(".py")):
        continue
    spec = importlib.util.spec_from_file_location(fname[:-3], os.path.join(tests_dir, fname))
    mod = importlib.util.module_from_spec(spec)
    try:
        spec.loader.exec_module(mod)
    except Skip as s:
        print(f"SKIP {fname} (missing {s})")
        skipped += 1
        continue
    for name, fn in sorted(vars(mod).items()):
        if not (name.startswith("test_") and callable(fn)):
            continue
        cases = [((), {})]
        if hasattr(fn, "_params"):
            names, values = fn._params
            cases = [((v,), {}) if "," not in names else (tuple(v), {}) for v in values]
        for args, _ in cases:
            mp = MonkeyPatch()
            kwargs = {"monkeypatch": mp} if "monkeypatch" in inspect.signature(fn).parameters else {}
            try:
                fn(*args, **kwargs)
                passed += 1
            except Skip:
                skipped += 1
            except Exception:
                failed += 1
                print(f"FAIL {fname}::{name}{args!r:.80}")
                traceback.print_exc()
            finally:
                mp.close()

print(f"passed={passed} failed={failed} skipped={skipped}")
sys.exit(1 if failed else 0)
