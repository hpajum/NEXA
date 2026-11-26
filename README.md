# NEXA

دستیار هوشمند سازمانی برای رایانه‌های ویندوزی کاربران، با ورود از طریق Active Directory و سرور لینوکسی مبتنی بر LangGraph.

| پوشه | محتوا |
| --- | --- |
| `Client` | برنامهٔ ویندوز (WPF، فارسی، کنار ساعت). راهنما: [Client/README.md](Client/README.md) |
| `Server` | سرور لینوکسی (FastAPI و LangGraph) با اتصال به krouter، OpenMetadata، Cube و ClickHouse. راهنما: [Server/README.md](Server/README.md) |
| `docs` | ساخت و نصب کلاینت و سایر مستندات. [docs/build-and-deploy.md](docs/build-and-deploy.md) |

قرارداد میان این دو بخش `POST /api/v1/session` است که در هر دو README شرح داده شده است.
