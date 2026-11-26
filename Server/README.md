# سرور NEXA

سرور لینوکسی NEXA: احراز هویت کاربران با Active Directory و یک عامل LangGraph که از طریق OpenMetadata، Cube و ClickHouse به داده‌های سازمان دسترسی دارد. همهٔ تنظیمات از متغیرهای محیطی با پیشوند `NEXA_` خوانده می‌شود؛ فهرست کامل و توضیح هر متغیر در [`.env.example`](.env.example) است.

## اجرا

```bash
cp .env.example .env        # سپس مقادیر را ویرایش کنید؛ NEXA_TOKEN_SECRET الزامی است
docker compose up -d --build
curl http://127.0.0.1:8000/healthz
```

بدون Docker:

```bash
python -m venv .venv && source .venv/bin/activate
pip install -e ".[dev]"
nexa-server
pytest
```

کلاینت ویندوز فقط نشانی `https://` با گواهی معتبر می‌پذیرد؛ TLS را با یک reverse proxy یا با `NEXA_TLS_CERT_FILE` و `NEXA_TLS_KEY_FILE` فعال کنید.

## رابط‌ها

| مسیر | کار |
|---|---|
| `GET /healthz` | زنده بودن فرایند |
| `GET /readyz` | وضعیت AD و اتصال‌ها (بدون نمایش نشانی یا رمز) |
| `POST /api/v1/session` | ورود؛ قرارداد کلاینت: `{"username","password"}` ← `{"account":"DOMAIN\\sam","token":"..."}` با کدهای 401، 403 و 429 |
| `POST /api/v1/chat` | گفتگو با عامل؛ هدر `Authorization: Bearer <token>`، گزینهٔ `stream` برای SSE |

مقدار `account` همیشه به شکل `NETBIOS\sam` (نام دامنه با حروف بزرگ و نام کاربری با حروف کوچک) است و برای هر سه شکل ورود (`DOMAIN\user`، UPN و نام ساده) یکسان برمی‌گردد. بدنهٔ درخواست ورود هرگز لاگ نمی‌شود.

## مدل‌ها و krouter

سرور فقط به نام‌های مستعار مدل (`NEXA_MODEL_CHAT`، `NEXA_MODEL_SQL`، `NEXA_MODEL_FAST`) اشاره می‌کند و از طریق `NEXA_LLM_BASE_URL` با روتر OpenAI-سازگار حرف می‌زند. تغییر مدل پشت هر نام مستعار فقط در روتر انجام می‌شود و سرور نیازی به تنظیم دوباره ندارد.

## ساختار گراف

```
load_memory → route ─ chat ───────────────────────────────→ answer → save_memory
                    └ data → policy_gate → discover → plan_query → execute ─┘
```

پیش از هر استفاده از اتصال‌ها، `policy_gate` از `PolicyEngine` مجوز می‌گیرد. کوئری‌های ClickHouse از `connectors/sql_guard.py` می‌گذرند (فقط یک دستور SELECT/WITH، بدون کامنت و توابع جدولی خطرناک) و با `readonly=2` و محدودیت ردیف و زمان اجرا می‌شوند.

## وضعیت فعلی (فاز بعد)

سطح دسترسی و حافظه فعلاً فقط رابط (`policy/base.py` و `memory/base.py`) و پیاده‌سازی پیش‌فرض دارند. با `NEXA_POLICY_MODE=deny` (پیش‌فرض) هیچ اتصال دادهٔ سازمان استفاده نمی‌شود و فقط گفتگوی عمومی پاسخ می‌گیرد. `permissive` فقط برای توسعه است. حافظه (`NoopMemory`) چیزی ذخیره نمی‌کند.
