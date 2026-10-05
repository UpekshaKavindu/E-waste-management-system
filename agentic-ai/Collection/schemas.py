from uuid import UUID
from pydantic import BaseModel, Field


# ---- CollectorMatchDto, mirrored exactly from the backend's shape ----
# (Features/Collection/DTOs/MatchingDtos.cs) — this is what POST
# /api/v1/collectors/match already returns. Real driving distance/ETA from
# the backend's IGeoService, not computed here.

class CollectorMatch(BaseModel):
    collector_id: UUID = Field(alias="collectorId")
    vehicle_type: str = Field(alias="vehicleType")
    capacity_kg: float = Field(alias="capacityKg")
    rating: float
    active_job_count: int = Field(alias="activeJobCount")
    distance_km: float | None = Field(alias="distanceKm", default=None)
    eta_minutes: int | None = Field(alias="etaMinutes", default=None)

    model_config = {"populate_by_name": True}


# ---- HTTP contract: /run ----

class MatcherRunRequest(BaseModel):
    workflow_id: UUID = Field(alias="workflowId")
    pickup_latitude: float = Field(alias="pickupLatitude")
    pickup_longitude: float = Field(alias="pickupLongitude")
    estimated_weight_kg: float = Field(alias="estimatedWeightKg")
    estimated_value_lkr: float = Field(alias="estimatedValueLkr", default=0.0)
    already_escalated: bool = Field(
        alias="alreadyEscalated", default=False,
        description="True if Validator already flagged this workflow for human review — "
                    "Matcher then never auto-assigns, only suggests.",
    )
    exclude_collector_ids: list[UUID] = Field(alias="excludeCollectorIds", default_factory=list)

    model_config = {"populate_by_name": True}


class MatcherRunResponse(BaseModel):
    workflow_id: UUID = Field(alias="workflowId")
    ranked_collectors: list[CollectorMatch] = Field(alias="rankedCollectors")
    recommended_collector_id: UUID | None = Field(alias="recommendedCollectorId", default=None)
    auto_assign: bool = Field(alias="autoAssign")
    ambiguous: bool = Field(
        description="True if the top two candidates were too close to call automatically.",
    )
    reasoning: str

    model_config = {"populate_by_name": True}
