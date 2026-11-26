"""Graph nodes tested with fakes (no LangGraph, network or LLM needed)."""

import asyncio
import json

from nexa_server.connectors.errors import ConnectorUnavailable, QueryNotAllowed
from nexa_server.graph import nodes
from nexa_server.graph.deps import Dependencies
from nexa_server.memory.default import NoopMemory
from nexa_server.policy.default import DenyAllPolicy, PermissiveDevPolicy


class FakeLlm:
    def __init__(self, replies):
        self.replies = replies
        self.calls = []

    async def complete(self, role, system, user):
        self.calls.append((role, user))
        return self.replies[role]


class FakeOM:
    def __init__(self):
        self.called = False

    async def search_tables(self, query, limit=5):
        self.called = True
        return [{"fqn": "db.sales", "description": "", "columns": []}]


class FakeCH:
    def __init__(self, error=None):
        self.queries = []
        self.error = error

    async def query(self, sql):
        self.queries.append(sql)
        if self.error:
            raise self.error
        return {"columns": ["n"], "rows": [[42]]}


def make_state(question="how many sales?"):
    return {"account": "EXAMPLE\\bob", "conversation_id": "c1", "question": question, "notes": []}


def run(coro):
    return asyncio.run(coro)


def apply(state, update):
    state.update(update)
    return state


def test_default_policy_denies_all_connectors():
    om, ch = FakeOM(), FakeCH()
    deps = Dependencies(llm=FakeLlm({}), policy=DenyAllPolicy(), memory=NoopMemory(), openmetadata=om, clickhouse=ch)
    state = make_state()
    apply(state, run(nodes.policy_gate(state, deps)))
    assert state["allowed"] == []
    apply(state, run(nodes.discover(state, deps)))
    assert om.called is False
    apply(state, run(nodes.plan_query(state, deps)))
    assert state["plan"]["kind"] == "none"
    apply(state, run(nodes.execute(state, deps)))
    assert ch.queries == []
    assert any("denied" in n for n in state["notes"])


def test_permissive_flow_runs_guarded_sql():
    plan = json.dumps({"kind": "sql", "sql": "SELECT count() AS n FROM db.sales"})
    llm = FakeLlm({"fast": "data", "sql": plan, "chat": "There are 42."})
    ch = FakeCH()
    deps = Dependencies(llm=llm, policy=PermissiveDevPolicy(), memory=NoopMemory(), openmetadata=FakeOM(), clickhouse=ch)
    state = make_state()
    for step in (nodes.load_memory, nodes.route, nodes.policy_gate, nodes.discover,
                 nodes.plan_query, nodes.execute, nodes.answer, nodes.save_memory):
        apply(state, run(step(state, deps)))
    assert state["intent"] == "data"
    assert ch.queries == ["SELECT count() AS n FROM db.sales"]
    assert state["result"]["rows"] == [[42]]
    assert state["answer"] == "There are 42."


def test_chat_intent_skips_data_path():
    llm = FakeLlm({"fast": "chat", "chat": "hello"})
    deps = Dependencies(llm=llm, policy=DenyAllPolicy(), memory=NoopMemory(), clickhouse=FakeCH())
    state = make_state("hi")
    apply(state, run(nodes.route(state, deps)))
    assert state["intent"] == "chat"
    assert nodes.after_route(state) == "answer"


def test_route_without_connectors_never_calls_llm():
    llm = FakeLlm({})
    deps = Dependencies(llm=llm, policy=DenyAllPolicy(), memory=NoopMemory())
    state = make_state()
    apply(state, run(nodes.route(state, deps)))
    assert state["intent"] == "chat" and llm.calls == []


def test_connector_failure_becomes_note_not_crash():
    plan = json.dumps({"kind": "sql", "sql": "SELECT 1"})
    llm = FakeLlm({"fast": "data", "sql": plan, "chat": "sorry"})
    ch = FakeCH(error=ConnectorUnavailable("down"))
    deps = Dependencies(llm=llm, policy=PermissiveDevPolicy(), memory=NoopMemory(), clickhouse=ch)
    state = make_state()
    for step in (nodes.route, nodes.policy_gate, nodes.discover, nodes.plan_query, nodes.execute):
        apply(state, run(step(state, deps)))
    assert state["result"] == {}
    assert any("query failed" in n for n in state["notes"])


def test_guard_violation_from_llm_sql_is_reported():
    llm = FakeLlm({"fast": "data", "sql": json.dumps({"kind": "sql", "sql": "DROP TABLE x"})})
    ch = FakeCH(error=QueryNotAllowed("only SELECT"))
    deps = Dependencies(llm=llm, policy=PermissiveDevPolicy(), memory=NoopMemory(), clickhouse=ch)
    state = make_state()
    for step in (nodes.route, nodes.policy_gate, nodes.discover, nodes.plan_query, nodes.execute):
        apply(state, run(step(state, deps)))
    assert any("QueryNotAllowed" in n for n in state["notes"])


def test_planner_cannot_pick_unavailable_source():
    llm = FakeLlm({"fast": "data", "sql": json.dumps({"kind": "cube", "query": {}})})
    deps = Dependencies(llm=llm, policy=PermissiveDevPolicy(), memory=NoopMemory(), clickhouse=FakeCH())
    state = make_state()
    for step in (nodes.policy_gate, nodes.plan_query):
        apply(state, run(step(state, deps)))
    assert state["plan"]["kind"] == "none"


def test_extract_json_handles_fences_and_garbage():
    assert nodes.extract_json('```json\n{"kind":"none"}\n```') == {"kind": "none"}
    assert nodes.extract_json("no json here") is None
    assert nodes.extract_json("{not json}") is None
