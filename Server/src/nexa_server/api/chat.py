"""POST /api/v1/chat: runs the LangGraph agent for the authenticated account.

`stream=true` returns Server-Sent Events: one `step` event per finished graph node
and a final `answer` event.
"""

from __future__ import annotations

import json
import uuid
from typing import AsyncIterator

from fastapi import APIRouter, Depends, HTTPException, Request
from fastapi.responses import StreamingResponse
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer
from pydantic import BaseModel, Field

from ..auth.tokens import InvalidToken

router = APIRouter()
bearer = HTTPBearer(auto_error=False)


class ChatRequest(BaseModel):
    message: str = Field(min_length=1, max_length=16000)
    conversation_id: str | None = Field(default=None, max_length=100)
    stream: bool = False


class ChatResponse(BaseModel):
    conversation_id: str
    answer: str
    notes: list[str] = []


def current_account(
    request: Request, credentials: HTTPAuthorizationCredentials | None = Depends(bearer)
) -> str:
    if credentials is None:
        raise HTTPException(status_code=401, detail="missing token", headers={"WWW-Authenticate": "Bearer"})
    try:
        account = request.app.state.container.tokens.verify(credentials.credentials).account
    except InvalidToken:
        raise HTTPException(status_code=401, detail="invalid token", headers={"WWW-Authenticate": "Bearer"})
    # Disabling a user in the admin panel takes effect immediately, not when the token expires.
    store = request.app.state.container.store
    if store is not None and not store.is_allowed(account):
        raise HTTPException(status_code=403, detail="not authorized for NEXA")
    return account


def _initial_state(account: str, body: ChatRequest) -> tuple[str, dict]:
    conversation_id = body.conversation_id or uuid.uuid4().hex
    return conversation_id, {
        "account": account,
        "conversation_id": conversation_id,
        "question": body.message,
        "notes": [],
    }


@router.post("/api/v1/chat")
async def chat(body: ChatRequest, request: Request, account: str = Depends(current_account)):
    graph = request.app.state.container.graph
    if graph is None:
        raise HTTPException(status_code=503, detail="language model is not configured")
    conversation_id, state = _initial_state(account, body)

    if not body.stream:
        final = await graph.ainvoke(state)
        return ChatResponse(
            conversation_id=conversation_id, answer=final.get("answer", ""), notes=final.get("notes", [])
        )

    async def events() -> AsyncIterator[str]:
        final: dict = {}
        async for update in graph.astream(state, stream_mode="updates"):
            for node, values in update.items():
                final.update(values or {})
                yield f"event: step\ndata: {json.dumps({'node': node})}\n\n"
        payload = {
            "conversation_id": conversation_id,
            "answer": final.get("answer", ""),
            "notes": final.get("notes", []),
        }
        yield f"event: answer\ndata: {json.dumps(payload, ensure_ascii=False)}\n\n"

    return StreamingResponse(events(), media_type="text/event-stream")
