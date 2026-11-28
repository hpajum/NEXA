"""Set or reset the admin panel password from the command line (e.g. `docker compose exec`).

    nexa-admin-password            # prompts twice
The new password is stored hashed; every existing admin session is invalidated.
"""

from __future__ import annotations

import getpass
import sys

from ..config import Settings
from .crypto import SecretBox, hash_password, password_problem
from .store import Store


def main() -> int:
    s = Settings()
    store = Store(s.admin_db_path, SecretBox(s.encryption_key_material))
    first = getpass.getpass("رمز جدید ادمین / New admin password: ")
    problem = password_problem(first)
    if problem:
        print(problem, file=sys.stderr)
        return 1
    if getpass.getpass("تکرار رمز / Repeat: ") != first:
        print("رمزها یکسان نیستند.", file=sys.stderr)
        return 1
    store.set_admin_password(hash_password(first), must_change=False)
    store.log("cli", "admin.password_reset", detail="password set from the command line")
    print("OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
