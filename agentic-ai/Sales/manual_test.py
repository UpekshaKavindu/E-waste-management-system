"""
Manual component test for the Sales agent - run it to demonstrate what this service does.

    .\\.venv\\Scripts\\python.exe manual_test.py

Needs no .NET backend, no Postgres and no API key: the four read endpoints the agent
calls (GET /api/agent/materials/available, /pricing/approved, /buyers/eligible,
/material-requests/open) plus POST /api/commercial-plans are swapped for fixed demo data.
Everything else is the real code - request parsing, all five graph nodes, the twelve
deterministic tools, response shaping - driven through FastAPI's TestClient exactly as
HTTP would drive it.

Exits 0 when every check passes, 1 otherwise.
"""

import sys
import uuid
from datetime import date, timedelta

from fastapi.testclient import TestClient

import main as agent_main
from schemas import ApprovedPrice, DemandCandidate, EligibleBuyer, MaterialBatch
from tools import commercial_tools as tools

# --------------------------------------------------------------------------
# Demo data - every expected value further down is derived from these numbers
# --------------------------------------------------------------------------

MAT_PRICE = 14500.0      # Gold,    Rs/kg
COPPER_PRICE = 620.0     # Copper,  Rs/kg
PLASTIC_PRICE = 45.0     # Plastic, Rs/kg
# Lithium deliberately has NO approved price: the agent must treat it as unsellable.

MATERIALS = [
    {"recoveredMaterialId": "aaaaaaaa-0000-0000-0000-000000000001",
     "materialType": "Gold", "quantityKg": 12.0, "qualityGrade": "A",
     "processingStatus": "Completed", "safetyValidated": True},
    {"recoveredMaterialId": "aaaaaaaa-0000-0000-0000-000000000002",
     "materialType": "Copper", "quantityKg": 45.0, "qualityGrade": "A",
     "processingStatus": "Completed", "safetyValidated": True},
    {"recoveredMaterialId": "aaaaaaaa-0000-0000-0000-000000000003",
     "materialType": "Plastic", "quantityKg": 300.0, "qualityGrade": "B",
     "processingStatus": "Completed", "safetyValidated": True},
    {"recoveredMaterialId": "aaaaaaaa-0000-0000-0000-000000000004",
     "materialType": "Lithium", "quantityKg": 8.0, "qualityGrade": "C",
     "processingStatus": "Completed", "safetyValidated": True},
]

TODAY = date.today()
PRICING = [
    {"materialType": "Gold", "pricePerKg": MAT_PRICE,
     "effectiveDate": TODAY.isoformat()},
    {"materialType": "Copper", "pricePerKg": COPPER_PRICE,
     "effectiveDate": TODAY.isoformat()},
    # 45 days old -> must raise the stale_pricing flag (threshold is 30 days).
    {"materialType": "Plastic", "pricePerKg": PLASTIC_PRICE,
     "effectiveDate": (TODAY - timedelta(days=45)).isoformat()},
]

BUYERS = [
    {"buyerId": "11111111-1111-1111-1111-111111111111",
     "companyName": "GreenCycle Recycling", "contactPerson": "Amara Silva",
     "email": "amara@greencycle.example", "buyerType": "Local"},
    {"buyerId": "22222222-2222-2222-2222-222222222222",
     "companyName": "GlobalMetals Trading", "contactPerson": "Ravi Menon",
     "email": "ravi@globalmetals.example", "buyerType": "Export"},
    {"buyerId": "33333333-3333-3333-3333-333333333333",
     "companyName": "EcoTech Plastics", "contactPerson": "Nila Perera",
     "email": "nila@echotech.example", "buyerType": "Local"},
]

LOCAL_BUYER_IDS = {BUYERS[0]["buyerId"], BUYERS[2]["buyerId"]}
EXPORT_BUYER_ID = BUYERS[1]["buyerId"]

OPEN_DEMAND = [
    # 1: big export order -> highest net value
    {"materialRequestId": "dddddddd-0000-0000-0000-000000000001",
     "buyerId": EXPORT_BUYER_ID, "buyerCompanyName": "GlobalMetals Trading",
     "buyerType": "Export", "materialType": "Gold", "quantityKg": 25.0,
     "pricePerKg": MAT_PRICE, "status": "Waiting", "waitingHours": 50.0},
    # 2: mid local order
    {"materialRequestId": "dddddddd-0000-0000-0000-000000000002",
     "buyerId": BUYERS[0]["buyerId"], "buyerCompanyName": "GreenCycle Recycling",
     "buyerType": "Local", "materialType": "Copper", "quantityKg": 45.0,
     "pricePerKg": COPPER_PRICE, "status": "Waiting", "waitingHours": 10.0},
    # 3: biggest kg AND longest wait
    {"materialRequestId": "dddddddd-0000-0000-0000-000000000003",
     "buyerId": BUYERS[2]["buyerId"], "buyerCompanyName": "EcoTech Plastics",
     "buyerType": "Local", "materialType": "Plastic", "quantityKg": 300.0,
     "pricePerKg": PLASTIC_PRICE, "status": "Waiting", "waitingHours": 100.0},
    # 4: blocked - below the 20 kg export minimum
    {"materialRequestId": "dddddddd-0000-0000-0000-000000000004",
     "buyerId": EXPORT_BUYER_ID, "buyerCompanyName": "GlobalMetals Trading",
     "buyerType": "Export", "materialType": "Gold", "quantityKg": 15.0,
     "pricePerKg": MAT_PRICE, "status": "Waiting", "waitingHours": 200.0},
    # 5: blocked - no live approved price
    {"materialRequestId": "dddddddd-0000-0000-0000-000000000005",
     "buyerId": BUYERS[0]["buyerId"], "buyerCompanyName": "GreenCycle Recycling",
     "buyerType": "Local", "materialType": "Lithium", "quantityKg": 30.0,
     "pricePerKg": None, "status": "WaitingForPrice", "waitingHours": 5.0},
]

SUBMITTED_PLANS = []
# --------------------------------------------------------------------------
# Fake backend: replaces the two HTTP helpers the tools call
# --------------------------------------------------------------------------

async def fake_get(path, params=None):
    if path == "/api/agent/materials/available":
        return MATERIALS
    if path == "/api/agent/pricing/approved":
        return PRICING
    if path == "/api/agent/buyers/eligible":
        return BUYERS
    if path == "/api/agent/material-requests/open":
        wanted = (params or {}).get("materialType")
        if not wanted:
            return OPEN_DEMAND
        return [r for r in OPEN_DEMAND if r["materialType"].lower() == wanted.lower()]
    raise AssertionError(f"unexpected GET {path}")


async def fake_post(path, payload):
    if path == "/api/commercial-plans":
        SUBMITTED_PLANS.append(payload)
        return {"commercialPlanId": str(uuid.uuid4()),
                "status": "PendingApproval", "echo": payload}
    raise AssertionError(f"unexpected POST {path}")


tools._get = fake_get
tools._post = fake_post

client = TestClient(agent_main.app)


# --------------------------------------------------------------------------
# Minimal assertion runner (pytest is not needed)
# --------------------------------------------------------------------------

PASSED, FAILED = [], []


def check(name, condition, detail=""):
    if condition:
        PASSED.append(name)
        print(f"  PASS  {name}")
    else:
        FAILED.append(name)
        print(f"  FAIL  {name}   -> {detail}")


def close(a, b, tol=0.01):
    return abs(float(a) - float(b)) <= tol


def section(title):
    print(f"\n=== {title} ===")
def test_health():
    section("1. GET /health  - is the service up?")
    r = client.get("/health")
    check("returns 200 with status ok",
          r.status_code == 200 and r.json() == {"status": "ok"}, r.text)
    print(f"        response: {r.json()}")


def test_full_plan():
    section("2. POST /run  (no filters - plan everything sellable)")
    r = client.post("/run", json={})
    check("returns 200", r.status_code == 200, r.text)
    body = r.json()
    # 12*14500 + 45*620 + 300*45 = 215,400  (Lithium has no price -> skipped)
    check("Lithium skipped: no approved price means not sellable",
          close(body["expectedRevenue"], 215400.0),
          f"revenue={body.get('expectedRevenue')}")
    check("local route chosen (5% cost beats 12%)",
          body["recommendedRoute"] == "LocalSale", body["recommendedRoute"])
    check("net = revenue - 5% local costs",
          close(body["estimatedNetValue"], 215400.0 * 0.95),
          f"net={body.get('estimatedNetValue')}")
    # NB: AgentRunResponse does not expose selectedBuyerId — the chosen buyer is named
    # in reasoningSummary, which is what the approval screen shows a human.
    summary = body["reasoningSummary"]
    check("a Local-typed buyer was named in the summary",
          any(name in summary for name in ("GreenCycle Recycling",
                                           "EcoTech Plastics")), summary)
    check("both routes scored and returned for audit",
          len(body["routeScores"]) == 2, str(body["routeScores"]))
    check("45-day-old plastic price raises stale_pricing",
          "stale_pricing" in body["riskFlags"], str(body["riskFlags"]))
    check("pricing age reported in days",
          close(body["pricingAgeDays"], 45.0), str(body.get("pricingAgeDays")))
    check("approval required - a human still signs it off",
          body["approvalRequired"] is True)

    print(f"        route      : {body['recommendedRoute']}")
    print(f"        revenue    : Rs. {body['expectedRevenue']:,.2f}")
    print(f"        net value  : Rs. {body['estimatedNetValue']:,.2f}")
    print(f"        objective  : {body['priorityObjective']}")
    print(f"        risk flags : {body['riskFlags']}")
    print(f"        summary    : {summary}")
    for route in body["routeScores"]:
        print(f"        route score: {route['route']:9s} "
              f"net Rs. {route['estimatedNetValue']:>12,.2f}  "
              f"Rs.{route['netPerKg']:,.2f}/kg")

    check("plan was POSTed to the backend", len(SUBMITTED_PLANS) == 1)
    check("submitted materialsJson excludes Lithium",
          "Lithium" not in SUBMITTED_PLANS[-1]["materialsJson"])
    check("submitted with approvalRequired = true",
          SUBMITTED_PLANS[-1]["approvalRequired"] is True)


def test_quantity_cap():
    section("3. POST /run  (maxQuantityKg=100 - best value/kg first, batch split)")
    body = client.post("/run", json={"maxQuantityKg": 100}).json()
    # Gold 12kg (174,000) + Copper 45kg (27,900) + 43kg of Plastic (1,935)
    check("cap filled by value/kg, not by the order the backend listed",
          close(body["expectedRevenue"], 203835.0),
          f"revenue={body['expectedRevenue']} expected 203835.0")
    print(f"        revenue    : Rs. {body['expectedRevenue']:,.2f} for 100 kg")
    print(f"        summary    : {body['reasoningSummary']}")


def test_preferred_route():
    section("4. POST /run  (preferredRoute=Export - caller override wins)")
    body = client.post("/run", json={"preferredRoute": "Export"}).json()
    check("Export honoured even though it nets less",
          body["recommendedRoute"] == "Export", body["recommendedRoute"])
    check("the Export-typed buyer was named in the summary",
          "GlobalMetals Trading" in body["reasoningSummary"],
          body["reasoningSummary"])
    check("export net = revenue - 12%",
          close(body["estimatedNetValue"], 215400.0 * 0.88),
          f"net={body['estimatedNetValue']}")
    print(f"        net value  : Rs. {body['estimatedNetValue']:,.2f} "
          f"(worse than local - the caller's choice, not the tool's)")
def test_goal_filters():
    section("5. POST /run  (targetMaterialTypes=['Copper'])")
    body = client.post("/run", json={"targetMaterialTypes": ["Copper"]}).json()
    check("only Copper planned (45 kg x 620)",
          close(body["expectedRevenue"], 27900.0),
          f"revenue={body['expectedRevenue']}")
    print(f"        revenue    : Rs. {body['expectedRevenue']:,.2f}")

    section("6. POST /run  (goal matches nothing sellable)")
    body = client.post("/run", json={"targetMaterialTypes": ["Lithium"]}).json()
    check("returns a zero-value plan, not an error",
          close(body["expectedRevenue"], 0.0))
    check("raises no_sellable_materials risk flag",
          "no_sellable_materials" in body["riskFlags"], str(body["riskFlags"]))
    print(f"        summary    : {body['reasoningSummary']}")


def test_priority_objectives():
    section("7. POST /prioritize-demand  (one backlog, five policies)")
    order = {}
    for objective in ("net_value", "throughput", "margin_per_kg", "fifo",
                      "export_first"):
        r = client.post("/prioritize-demand", json={"objective": objective})
        check(f"{objective}: 200", r.status_code == 200, r.text)
        ranked = r.json()["ranked"]
        order[objective] = [x["materialRequestId"][-1] for x in ranked if x["feasible"]]
        top = next(x for x in ranked if x["feasible"])
        print(f"    {objective:14s} -> top #{top['priorityRank']} "
              f"{top['buyerCompanyName']} / {top['materialType']} "
              f"{top['quantityKg']:g} kg  net Rs. {top['expectedNetValue']:>11,.2f}  "
              f"score {top['priorityScore']:,.2f}")

    check("net_value -> biggest net first (export Gold, Rs. 319,000)",
          order["net_value"][0] == "1", str(order["net_value"]))
    check("throughput -> most kilograms first (Plastic, 300 kg)",
          order["throughput"][0] == "3", str(order["throughput"]))
    check("fifo -> longest wait first (Plastic, 100 h)",
          order["fifo"][0] == "3", str(order["fifo"]))
    check("margin_per_kg -> best rate first (Gold)",
          order["margin_per_kg"][0] == "1", str(order["margin_per_kg"]))
    check("export_first -> export demand in front",
          order["export_first"][0] == "1", str(order["export_first"]))
    check("blocked requests always sink to the bottom",
          len(order["net_value"]) == 3, str(order["net_value"]))


def test_blocked_reasons():
    section("8. POST /prioritize-demand  (why is a request blocked?)")
    data = client.post("/prioritize-demand", json={"objective": "net_value"}).json()
    blocked = {x["materialRequestId"][-1]: x["rationale"]
               for x in data["ranked"] if not x["feasible"]}
    check("15 kg export blocked by the 20 kg export minimum",
          "4" in blocked and "export minimum" in blocked.get("4", ""), str(blocked))
    check("unpriced Lithium blocked as unpriceable",
          "5" in blocked and "approved price" in blocked.get("5", ""), str(blocked))
    for key, why in sorted(blocked.items()):
        print(f"        blocked #{key}: {why.split('Blocked:')[-1].strip()}")
    check("no model configured -> deterministic net_value, agentModel null",
          data["strategy"] == "net_value" and data["agentModel"] is None,
          f"{data['strategy']} / {data['agentModel']}")
    print(f"        strategy   : {data['strategy']} - {data['strategyReason']}")
    print(f"        summary    : {data['reasoningSummary']}")


def test_capacity():
    section("9. POST /prioritize-demand  (capacityKg=50)")
    data = client.post("/prioritize-demand",
                       json={"objective": "net_value", "capacityKg": 50}).json()
    by_id = {x["materialRequestId"][-1]: x for x in data["ranked"]}
    check("300 kg Plastic now blocked by stock on hand",
          by_id["3"]["feasible"] is False and "unclaimed" in by_id["3"]["rationale"],
          by_id["3"]["rationale"])
    check("25 kg export order still served", by_id["1"]["feasible"] is True)
    # Greedy walk over the 50 kg: Gold 25 kg is served (25 left), Copper's 45 kg no
    # longer fits so it is skipped even though it passed feasibility on its own.
    check("servedKg follows the greedy walk, not the feasible count",
          close(data["servedKg"], 25.0), f"servedKg={data['servedKg']} expected 25.0")
    print(f"        servedKg   : {data['servedKg']:g}")
    print(f"        summary    : {data['reasoningSummary']}")


def test_supplied_candidates():
    section("10. POST /prioritize-demand  (candidates from the .NET matcher)")
    data = client.post("/prioritize-demand",
                       json={"materialType": "Gold", "candidates": OPEN_DEMAND[:1]}
                       ).json()
    check("only the supplied candidate is ranked",
          data["candidatesConsidered"] == 1,
          str(data["candidatesConsidered"]))
    check("material filter echoed in the summary",
          "Gold" in data["reasoningSummary"], data["reasoningSummary"])
    print(f"        summary    : {data['reasoningSummary']}")
def test_tools_directly():
    section("11. Deterministic tools called directly (the arithmetic)")
    check("cost_rate_for('Export') = 0.12", tools.cost_rate_for("Export") == 0.12)
    check("cost_rate_for('Local')  = 0.05", tools.cost_rate_for("Local") == 0.05)
    check("pricing_age_days([]) is None", tools.pricing_age_days([]) is None)

    row = tools._score_demand(DemandCandidate(
        **{"materialRequestId": str(uuid.uuid4()), "buyerId": str(uuid.uuid4()),
           "buyerType": "Local", "materialType": "Copper", "quantityKg": 10.0,
           "pricePerKg": 100.0, "waitingHours": 1.0}), None, 0)
    check("_score_demand: 10kg x 100 x (1 - 0.05) = 950 net, 95/kg",
          close(row["expected_net_value"], 950.0) and close(row["net_per_kg"], 95.0),
          str(row))

    planned = tools.calculate_commercial_value(
        [MaterialBatch(**m) for m in MATERIALS],
        [ApprovedPrice(**p) for p in PRICING])
    check("calculate_commercial_value drops the unpriced batch",
          len(planned) == 3, f"{len(planned)} batches kept")
    check("line value = quantity x unit price",
          close(planned[0].line_value, 12.0 * MAT_PRICE), str(planned[0]))
    print(f"        line values: {[p.line_value for p in planned]}")

    buyers = [EligibleBuyer(**b) for b in BUYERS]
    comparison = tools.compare_commercial_options(planned, buyers)
    check("compare_commercial_options: 357 kg, both routes feasible",
          close(comparison["total_kg"], 357.0)
          and comparison["local"]["feasible"]
          and comparison["export"]["feasible"], str(comparison))
    check("rank_route_options ranks LocalSale first",
          tools.rank_route_options(comparison, "net_value")[0]["route"] == "LocalSale")
    print(f"        totals     : {comparison['total_kg']:g} kg, "
          f"Rs. {comparison['total_revenue']:,.2f} revenue")

    capped = tools.select_materials_within_cap(planned, 20.0)
    check("select_materials_within_cap splits the batch that straddles the cap",
          close(sum(m.quantity_kg for m in capped), 20.0)
          and close(capped[-1].quantity_kg, 8.0),
          str([(m.material_type, m.quantity_kg) for m in capped]))
    print(f"        capped 20kg: {[(m.material_type, m.quantity_kg) for m in capped]}")

    picked = tools.select_buyer_for_route(
        buyers, "Export", [b["buyerId"] for b in BUYERS])
    check("select_buyer_for_route picks the Export-typed buyer",
          picked == EXPORT_BUYER_ID, str(picked))


def test_validation():
    section("12. Input validation - bad input is rejected, not crashed")
    r = client.post("/run", json={"maxQuantityKg": "not-a-number"})
    check("POST /run rejects a non-numeric cap with 422",
          r.status_code == 422, f"got {r.status_code}")
    check("unknown priority objective falls back instead of erroring",
          client.post("/prioritize-demand",
                      json={"objective": "nonsense"}).status_code == 200)


def main():
    print("Sales agent - manual component test")
    print("=" * 72)
    for fn in (test_health, test_full_plan, test_quantity_cap, test_preferred_route,
               test_goal_filters, test_priority_objectives, test_blocked_reasons,
               test_capacity, test_supplied_candidates, test_tools_directly,
               test_validation):
        fn()

    print("\n" + "=" * 72)
    print(f"PASSED: {len(PASSED)}    FAILED: {len(FAILED)}")
    if FAILED:
        print("\nFailures:")
        for name in FAILED:
            print(f"  - {name}")
        return 1
    print("All Sales component checks passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())