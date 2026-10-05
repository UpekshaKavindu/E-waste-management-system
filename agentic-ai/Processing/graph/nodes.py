"""
Validator node — Component C's distinct Agentic AI contribution.

Deliberately deterministic (no LLM calls at all), per spec Section 9.1
("apply deterministic checks such as schema or business-rule validation")
and Section 12's rule that rule-based assertions must not be replaced by
LLM-as-judge alone. This is the migrated + expanded version of the logic
that used to live in agents/validator_agent.py — the difference is that the
actual IF/THEN rule evaluation now happens here, in the agent's own code,
rather than being delegated wholesale to a single backend endpoint. The
thresholds themselves still come from the backend (BusinessRules), so an
admin can retune them without redeploying this service.
"""

from graph.state import ValidatorState
from schemas import HAZARD_ORDER


def validate_node(state: ValidatorState) -> ValidatorState:
    analyzer = state["analyzer_result"]
    rules = state["rules"]
    reasons: list[str] = []

    # --- Completeness check ---
    if not analyzer.waste_category or analyzer.waste_category == "Uncategorized":
        reasons.append("Classification is incomplete or uncategorized.")

    # --- Hazard threshold ---
    item_hazard = HAZARD_ORDER.get(analyzer.hazard_level, HAZARD_ORDER["Critical"])
    ceiling = HAZARD_ORDER.get(rules.auto_hazard_ceiling, HAZARD_ORDER["Medium"])
    if item_hazard > ceiling:
        reasons.append(
            f"Hazard level '{analyzer.hazard_level}' exceeds the auto-approval "
            f"ceiling of '{rules.auto_hazard_ceiling}'."
        )

    # --- Confidence threshold ---
    if analyzer.confidence_score < rules.min_confidence_for_auto:
        reasons.append(
            f"Classification confidence {analyzer.confidence_score:.2f} is below "
            f"the auto-approval minimum of {rules.min_confidence_for_auto:.2f}."
        )

    # --- Value threshold ---
    if analyzer.estimated_value_lkr > rules.max_value_for_auto_lkr:
        reasons.append(
            f"Estimated value Rs. {analyzer.estimated_value_lkr:,.2f} exceeds the "
            f"auto-approval limit of Rs. {rules.max_value_for_auto_lkr:,.2f}."
        )

    requires_human_approval = len(reasons) > 0
    approved_for_auto_assignment = not requires_human_approval

    return {
        **state,
        "approved_for_auto_assignment": approved_for_auto_assignment,
        "requires_human_approval": requires_human_approval,
        "reasons": reasons,
    }
