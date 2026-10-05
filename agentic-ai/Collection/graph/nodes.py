"""
Matcher node — Component B's distinct Agentic AI contribution.

IMPORTANT: this does NOT rank collectors. Features/Collection/Services/
MatchingService.cs already does that, using real driving distance/ETA from
the backend's IGeoService — better than anything worth reimplementing here,
and it's the same ranking the reject-and-reassign flow already relies on.
Matcher's own contribution is the layer on top: given an already-ranked
list, decide whether the top pick is safe to auto-assign, or whether it
should just be a suggestion for staff to confirm.
"""

from graph.state import MatcherState
from schemas import CollectorMatch

# Simple, defensible for a student project: jobs at or below this value are
# routine enough to auto-assign; anything above always goes to a human,
# regardless of match quality.
ROUTINE_VALUE_THRESHOLD_LKR = 90000.0  # Sri Lankan Rupees


def _comparability_score(c: CollectorMatch) -> float:
    """
    Used ONLY to detect ambiguity between the top two candidates — not to
    re-rank them (the backend's order is authoritative). Lower distance,
    higher rating, lower current load = more clearly the right pick.
    """
    distance_component = (c.distance_km or 999) * 2
    load_component = c.active_job_count * 5
    return (c.rating * 20) - distance_component - load_component


# If the top two candidates' comparability scores are within this margin,
# treat the pick as ambiguous rather than pretending certainty the ranking
# doesn't have.
AMBIGUOUS_SCORE_MARGIN = 3.0


def decide_node(state: MatcherState) -> MatcherState:
    ranked = state["ranked"]

    if not ranked:
        return {
            **state,
            "recommended_collector_id": None,
            "auto_assign": False,
            "ambiguous": False,
            "reasoning": "No collector candidates were returned. Needs staff review.",
        }

    top = ranked[0]
    ambiguous = False
    if len(ranked) > 1:
        top_score = _comparability_score(top)
        second_score = _comparability_score(ranked[1])
        ambiguous = (top_score - second_score) < AMBIGUOUS_SCORE_MARGIN

    already_escalated = state.get("already_escalated", False)
    routine_value = state.get("estimated_value_lkr", 0.0) <= ROUTINE_VALUE_THRESHOLD_LKR

    auto_assign = (not already_escalated) and (not ambiguous) and routine_value

    distance_str = f"{top.distance_km} km" if top.distance_km is not None else "distance unresolved"
    reasons = [f"Top match: {top.vehicle_type} collector ({distance_str}, rating {top.rating})."]
    if already_escalated:
        reasons.append("Workflow was already flagged by Validator — suggestion only.")
    if ambiguous:
        reasons.append(f"Top two candidates within {AMBIGUOUS_SCORE_MARGIN} points — treated as ambiguous.")
    if not routine_value:
        reasons.append(f"Estimated value exceeds the Rs. {ROUTINE_VALUE_THRESHOLD_LKR:,.0f} routine threshold.")
    reasons.append("Auto-assigning." if auto_assign else "Leaving for staff to confirm.")

    return {
        **state,
        "recommended_collector_id": str(top.collector_id),
        "auto_assign": auto_assign,
        "ambiguous": ambiguous,
        "reasoning": " ".join(reasons),
    }
