"""LangGraph assembly. `build_graph(deps)` returns a compiled graph."""

from __future__ import annotations

from functools import partial

from langgraph.graph import END, START, StateGraph

from . import nodes
from .deps import Dependencies
from .state import AgentState


def build_graph(deps: Dependencies):
    graph = StateGraph(AgentState)
    for name in ("load_memory", "route", "policy_gate", "discover", "plan_query", "execute", "answer", "save_memory"):
        graph.add_node(name, partial(getattr(nodes, name), deps=deps))

    graph.add_edge(START, "load_memory")
    graph.add_edge("load_memory", "route")
    graph.add_conditional_edges("route", nodes.after_route, {"policy_gate": "policy_gate", "answer": "answer"})
    graph.add_edge("policy_gate", "discover")
    graph.add_edge("discover", "plan_query")
    graph.add_edge("plan_query", "execute")
    graph.add_edge("execute", "answer")
    graph.add_edge("answer", "save_memory")
    graph.add_edge("save_memory", END)
    return graph.compile()
