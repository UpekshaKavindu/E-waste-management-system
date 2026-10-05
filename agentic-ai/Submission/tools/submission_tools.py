"""Analyzer's allow-listed tools. Same shape as tools/commercial_tools.py in Sales/."""

from uuid import UUID

import httpx

from config import settings
from schemas import SubmissionSnapshot


def _headers() -> dict:
    return {"X-Agent-Key": settings.agent_api_key}


async def _get(path: str):
    async with httpx.AsyncClient(base_url=settings.api_base_url, timeout=30.0) as client:
        r = await client.get(path, headers=_headers())
        r.raise_for_status()
        return r.json()


async def _post(path: str, payload: dict):
    async with httpx.AsyncClient(base_url=settings.api_base_url, timeout=30.0) as client:
        r = await client.post(path, json=payload, headers=_headers())
        r.raise_for_status()
        # Some endpoints (e.g. /api/agent/execution-logs) return 200 with no body.
        return r.json() if r.content else None


# ---------- Tool 1: getSubmission ----------

async def get_submission(submission_id: UUID) -> SubmissionSnapshot:
    data = await _get(f"/api/agent/submissions/{submission_id}")
    return SubmissionSnapshot(**data)


# ---------- Tool 2: submitAnalysis ----------
# Replaces the old fire-and-forget POST to .../ai-callback — this is now the
# only write-tool this agent has, and it's synchronous.

async def submit_analysis(workflow_id: UUID, result: dict) -> dict:
    payload = {
        "wasteCategory": result["waste_category"],
        "hazardLevel": result["hazard_level"],
        "estimatedVolumeKg": result["estimated_volume_kg"],
        "estimatedValueLkr": result["estimated_value_lkr"],
        "confidenceScore": result["confidence_score"],
        "items": [
            {
                "itemName": i["item_name"],
                "quantity": i.get("quantity", 1),
                "wasteCategory": i["waste_category"],
                "hazardLevel": i["hazard_level"],
                "estimatedVolumeKg": i["estimated_volume_kg"],
                "estimatedValueLkr": i["estimated_value_lkr"],
                "confidenceScore": i["confidence_score"],
            }
            for i in result.get("items", [])
        ],
    }
    return await _post(f"/api/agent/workflows/{workflow_id}/analyzer-result", payload)


# ---------- Tool 3: logExecution ----------

async def log_execution(workflow_id: UUID, step_number: int, input_json: dict,
                         output_json: dict | None, succeeded: bool, error_message: str | None) -> None:
    payload = {
        "workflowId": str(workflow_id),
        "agentName": "Analyzer",
        "stepNumber": step_number,
        "inputJson": input_json,
        "outputJson": output_json,
        "succeeded": succeeded,
        "errorMessage": error_message,
    }
    try:
        await _post("/api/agent/execution-logs", payload)
    except Exception:
        pass
