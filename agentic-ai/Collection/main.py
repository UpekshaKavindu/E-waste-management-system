import logging
import time

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware

from graph.nodes import decide_node
from graph.state import MatcherState
from schemas import MatcherRunRequest, MatcherRunResponse
from tools import collection_tools as tools

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger("matcher-agent")

app = FastAPI(title="Matcher Agent — Component B", version="0.2.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.get("/health")
async def health():
    return {"status": "ok", "service": "Matcher"}


@app.post("/run", response_model=MatcherRunResponse)
async def run(req: MatcherRunRequest) -> MatcherRunResponse:
    started = time.time()

    ranked = await tools.get_ranked_candidates(
        pickup_latitude=req.pickup_latitude,
        pickup_longitude=req.pickup_longitude,
        required_capacity_kg=req.estimated_weight_kg,
        exclude_collector_ids=req.exclude_collector_ids,
        max_results=3,
    )

    state: MatcherState = {
        "workflow_id": str(req.workflow_id),
        "estimated_value_lkr": req.estimated_value_lkr,
        "already_escalated": req.already_escalated,
        "ranked": ranked,
    }

    try:
        state = decide_node(state)
    except Exception as exc:
        await tools.log_execution(req.workflow_id, 4, req.model_dump(mode="json"), None, False, str(exc))
        log.exception("Matcher run failed")
        raise HTTPException(status_code=500, detail=str(exc)) from exc

    result = {
        "ranked": state["ranked"],
        "recommended_collector_id": state["recommended_collector_id"],
        "auto_assign": state["auto_assign"],
        "ambiguous": state["ambiguous"],
        "reasoning": state["reasoning"],
    }
    await tools.submit_matching_result(req.workflow_id, result)
    await tools.log_execution(
        req.workflow_id, 4, req.model_dump(mode="json"),
        {"recommended_collector_id": result["recommended_collector_id"], "auto_assign": result["auto_assign"]},
        True, None,
    )
    log.info("Matcher run done in %.2fs (workflow=%s, auto_assign=%s)",
              time.time() - started, req.workflow_id, result["auto_assign"])

    return MatcherRunResponse(
        workflowId=req.workflow_id,
        rankedCollectors=state["ranked"],
        recommendedCollectorId=state["recommended_collector_id"],
        autoAssign=state["auto_assign"],
        ambiguous=state["ambiguous"],
        reasoning=state["reasoning"],
    )


if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host="0.0.0.0", port=8005, reload=True)

