from uuid import UUID
from pydantic import BaseModel, Field


# ---- Input: Analyzer's output, passed straight through by the orchestrator ----

class AnalyzerResult(BaseModel):
    waste_category: str = Field(alias="wasteCategory")
    hazard_level: str = Field(alias="hazardLevel")
    estimated_volume_kg: float = Field(alias="estimatedVolumeKg")
    estimated_value_lkr: float = Field(alias="estimatedValueLkr")
    confidence_score: float = Field(alias="confidenceScore")

    model_config = {"populate_by_name": True}


# ---- Business rules, fetched from the backend so admins can tune thresholds
#      without redeploying this service ----

class BusinessRules(BaseModel):
    auto_hazard_ceiling: str = Field(alias="autoHazardCeiling", default="Medium")  # highest hazard level allowed to auto-approve
    min_confidence_for_auto: float = Field(alias="minConfidenceForAuto", default=0.6)
    max_value_for_auto_lkr: float = Field(alias="maxValueForAutoLkr", default=150000.0)
    required_fields: list[str] = Field(alias="requiredFields", default_factory=lambda: ["wasteCategory", "hazardLevel"])

    model_config = {"populate_by_name": True}


HAZARD_ORDER = {"Low": 0, "Medium": 1, "High": 2, "Critical": 3}


# ---- HTTP contract: /run ----

class ValidatorRunRequest(BaseModel):
    workflow_id: UUID = Field(alias="workflowId")
    submission_id: UUID = Field(alias="submissionId")
    analyzer_result: AnalyzerResult = Field(alias="analyzerResult")

    model_config = {"populate_by_name": True}


class ValidatorRunResponse(BaseModel):
    workflow_id: UUID = Field(alias="workflowId")
    approved_for_auto_assignment: bool = Field(alias="approvedForAutoAssignment")
    requires_human_approval: bool = Field(alias="requiresHumanApproval")
    reasons: list[str] = Field(default_factory=list)

    model_config = {"populate_by_name": True}
