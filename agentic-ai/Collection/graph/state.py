from typing import TypedDict

from schemas import CollectorMatch


class MatcherState(TypedDict, total=False):
    workflow_id: str
    estimated_value_lkr: float
    already_escalated: bool

    ranked: list[CollectorMatch]   # already ranked by the backend, real distance/ETA

    recommended_collector_id: str | None
    auto_assign: bool
    ambiguous: bool
    reasoning: str
