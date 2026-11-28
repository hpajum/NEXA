"""FastAPI entry point: `nexa-server` or `uvicorn nexa_server.main:create_app --factory`."""

from __future__ import annotations

from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from .api import chat, health, session
from .config import Settings
from .deps import build_container
from .logging_setup import configure_logging


def create_app(settings: Settings | None = None) -> FastAPI:
    base = settings or Settings()
    configure_logging(base.log_level)

    runtime = None
    effective = base
    if base.admin_enabled:
        from .admin.runtime import create_runtime, initial_settings

        runtime = create_runtime(base)
        effective = initial_settings(base, runtime.store)  # env + values saved in the panel
    container = build_container(effective, runtime.store if runtime else None)

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        yield
        await app.state.container.aclose()  # whichever container is current
        if runtime is not None:
            runtime.store.close()

    app = FastAPI(title="NEXA Server", version="0.1.0", lifespan=lifespan, docs_url=None, redoc_url=None)
    app.state.container = container
    app.state.admin = runtime
    if base.cors_origin_list:
        app.add_middleware(
            CORSMiddleware,
            allow_origins=base.cors_origin_list,
            allow_methods=["GET", "POST"],
            allow_headers=["Authorization", "Content-Type"],
        )
    app.include_router(health.router)
    app.include_router(session.router)
    app.include_router(chat.router)
    if runtime is not None:
        from .admin import api as admin_api, web as admin_web

        app.include_router(admin_api.router)
        app.include_router(admin_web.router)
    return app


def run() -> None:
    import uvicorn

    s = Settings()
    uvicorn.run(
        create_app(s),
        host=s.host,
        port=s.port,
        log_level=s.log_level.lower(),
        ssl_certfile=s.tls_cert_file or None,
        ssl_keyfile=s.tls_key_file or None,
        access_log=False,  # access logs would record client addresses and paths needlessly
    )


if __name__ == "__main__":
    run()
