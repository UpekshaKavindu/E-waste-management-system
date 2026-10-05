from typing import TypedDict

from schemas import SubmissionSnapshot


class AnalyzerState(TypedDict, total=False):
    workflow_id: str
    submission: SubmissionSnapshot

    waste_category: str
    hazard_level: str
    estimated_volume_kg: float
    estimated_value_lkr: float
    confidence_score: float

    # One dict per item / CSV row: item_name, quantity, waste_category, hazard_level, estimated_volume_kg,
    # estimated_value_lkr, confidence_score. The fields above are these combined.
    items: list[dict]

    errors: list[str]
