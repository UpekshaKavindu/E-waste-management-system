from uuid import UUID
from pydantic import BaseModel, Field


# ---- Structured LLM output ----

class ItemClassification(BaseModel):
    item_number: int = Field(description="The item's or row's number exactly as given in the request")
    waste_category: str = Field(description="e.g. Household Electronics, Batteries, IT Equipment, Heavy Appliances")
    hazard_level: str = Field(description="Low, Medium, High, or Critical")
    estimated_volume_kg: float = Field(description="Estimated weight of ONE unit of this item, in kg")
    estimated_value_lkr: float = Field(
        description="Estimated recovery/resale value of ONE unit of this item in Sri Lankan Rupees (LKR), "
                    "at local Sri Lankan e-waste and scrap prices"
    )
    confidence_score: float = Field(description="0.0 to 1.0 — how confident the classification of this item is")


class SubmissionClassificationOutput(BaseModel):
    items: list[ItemClassification] = Field(description="Exactly one entry per item in the request, in the same order")


# ---- HTTP contract: /run ----

class AnalyzerRunRequest(BaseModel):
    workflow_id: UUID = Field(alias="workflowId")
    submission_id: UUID = Field(alias="submissionId")

    model_config = {"populate_by_name": True}


class AnalyzedItem(BaseModel):
    """One item or CSV row; weight and value are for the whole row (per unit × quantity)."""
    item_name: str = Field(alias="itemName")
    quantity: int = 1
    waste_category: str = Field(alias="wasteCategory")
    hazard_level: str = Field(alias="hazardLevel")
    estimated_volume_kg: float = Field(alias="estimatedVolumeKg")
    estimated_value_lkr: float = Field(alias="estimatedValueLkr")
    confidence_score: float = Field(alias="confidenceScore")

    model_config = {"populate_by_name": True}


class AnalyzerRunResponse(BaseModel):
    """Top-level fields describe the whole submission (worst hazard, totals, lowest confidence)."""
    workflow_id: UUID = Field(alias="workflowId")
    waste_category: str = Field(alias="wasteCategory")
    hazard_level: str = Field(alias="hazardLevel")
    estimated_volume_kg: float = Field(alias="estimatedVolumeKg")
    estimated_value_lkr: float = Field(alias="estimatedValueLkr")
    confidence_score: float = Field(alias="confidenceScore")
    items: list[AnalyzedItem] = Field(default_factory=list)

    model_config = {"populate_by_name": True}


# ---- Internal: the raw submission fetched from the backend ----

class SnapshotItem(BaseModel):
    item_name: str = Field(alias="itemName", default="")
    description: str | None = None
    image_url: str | None = Field(alias="imageUrl", default=None)
    quantity: int = 1
    # CSV only: the customer's per-unit weight (preferred over the model's) and category hint.
    estimated_weight_kg: float | None = Field(alias="estimatedWeightKg", default=None)
    category_hint: str | None = Field(alias="categoryHint", default=None)

    model_config = {"populate_by_name": True}


class SubmissionSnapshot(BaseModel):
    submission_id: UUID = Field(alias="submissionId")
    description: str = ""
    image_urls: list[str] = Field(alias="imageUrls", default_factory=list)
    # What the customer chose — hints for the model, most useful when an item has no photo.
    category: str = ""
    estimated_weight_kg: float = Field(alias="estimatedWeightKg", default=0.0)
    # "Manual" (item form, photos) or "Csv" (corporate spreadsheet, text only).
    source: str = "Manual"
    items: list[SnapshotItem] = Field(default_factory=list)

    model_config = {"populate_by_name": True}
