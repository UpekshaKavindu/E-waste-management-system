using EWasteManagement.API.Features.Notifications.Services;
using EWasteManagement.API.Features.Sales.DTOs;
using EWasteManagement.API.Features.Sales.Entities;
using EWasteManagement.API.Features.Sales.Services;
using Microsoft.EntityFrameworkCore;

namespace EWasteManagement.Tests.Sales;

/// <summary>
/// The human-in-the-loop approval workflow for an AI-proposed commercial plan:
/// staff approve / reject / ask for a revision, and the linked buyer backorder
/// follows the decision. Also covers marking an approved plan as executed.
/// </summary>
public class CommercialPlanServiceTests : SalesTestBase
{
    private CommercialPlanService _service = null!;

    protected override Task SetUpAsync()
    {
        _service = new CommercialPlanService(Db, new NotificationService(Db));
        return Task.CompletedTask;
    }

    // ---------- Create ----------

    [Fact]
    public async Task Create_LinksThePlanToItsBuyerAndLogsTheSubmission()
    {
        var created = await _service.CreateAsync(new CreateCommercialPlanRequest
        {
            WorkflowId = Guid.NewGuid(),
            RecommendedRoute = "LocalSale",
            SelectedBuyerId = LocalBuyer.BuyerId,
            MaterialsJson = """[{"materialType":"Copper","quantityKg":10}]""",
            ReasoningSummary = "Sell locally — best net value this week.",
            ExpectedRevenue = 5000m
        }, StaffUser.UserId);

        Assert.Equal(CommercialPlanStatus.PendingApproval.ToString(), created.Status);
        Assert.Equal("Colombo Metals Ltd", created.SelectedBuyerName);

        var submitted = Assert.Single(created.ApprovalActions);
        Assert.Equal(ApprovalActionType.Submitted.ToString(), submitted.ActionType);
    }

    [Fact]
    public async Task Create_WithAnUnknownSelectedBuyer_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(
            new CreateCommercialPlanRequest
            {
                WorkflowId = Guid.NewGuid(),
                RecommendedRoute = "LocalSale",
                SelectedBuyerId = Guid.NewGuid(),
                MaterialsJson = "[]",
                ReasoningSummary = "Test."
            }, StaffUser.UserId));
        Assert.Contains("does not exist", ex.Message);
    }

    // ---------- Decide ----------

    [Fact]
    public async Task Decide_Approved_TurnsTheBackorderIntoADraftOrderAndLogsTheDecision()
    {
        var (plan, request, order) = await SeedPlanWithBackorderAsync();

        var result = await _service.DecideAsync(plan.CommercialPlanId,
            new ApprovalDecisionRequest { Decision = "Approved" }, StaffUser.UserId);

        Assert.Equal(CommercialPlanStatus.Approved.ToString(), result.Status);
        Assert.Equal(MaterialRequestStatus.OrderPlaced, request.Status);
        Assert.Equal(SalesOrderStatus.Draft, order.Status);

        Db.ChangeTracker.Clear();
        var actions = await Db.ApprovalActions.AsNoTracking()
            .Where(a => a.CommercialPlanId == plan.CommercialPlanId).ToListAsync();
        Assert.Contains(actions, a => a.ActionType == ApprovalActionType.Approved);
    }

    [Fact]
    public async Task Decide_Rejected_CancelsTheBackorder()
    {
        var (plan, request, order) = await SeedPlanWithBackorderAsync();

        var result = await _service.DecideAsync(plan.CommercialPlanId,
            new ApprovalDecisionRequest { Decision = "Rejected", Comments = "Margin too thin." }, StaffUser.UserId);

        Assert.Equal(CommercialPlanStatus.Rejected.ToString(), result.Status);
        Assert.Equal(MaterialRequestStatus.Cancelled, request.Status);
        Assert.Equal(SalesOrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public async Task Decide_RevisionRequested_LeavesTheBackorderOpen()
    {
        var (plan, request, _) = await SeedPlanWithBackorderAsync();

        var result = await _service.DecideAsync(plan.CommercialPlanId,
            new ApprovalDecisionRequest { Decision = "RevisionRequested", Comments = "Try the export route." },
            StaffUser.UserId);

        Assert.Equal(CommercialPlanStatus.RevisionRequested.ToString(), result.Status);
        Assert.Equal(MaterialRequestStatus.PlanGenerated, request.Status);
    }

    [Fact]
    public async Task Decide_OnAPlanThatIsAlreadyDecided_IsRejected()
    {
        var (plan, _, _) = await SeedPlanWithBackorderAsync();
        await _service.DecideAsync(plan.CommercialPlanId,
            new ApprovalDecisionRequest { Decision = "Approved" }, StaffUser.UserId);
        Db.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DecideAsync(
            plan.CommercialPlanId, new ApprovalDecisionRequest { Decision = "Rejected" }, StaffUser.UserId));
        Assert.Contains("cannot be decided", ex.Message);
    }

    [Fact]
    public async Task Decide_WithAnUnsupportedDecision_IsRejected()
    {
        var (plan, _, _) = await SeedPlanWithBackorderAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DecideAsync(
            plan.CommercialPlanId, new ApprovalDecisionRequest { Decision = "Maybe" }, StaffUser.UserId));
    }

    // ---------- MarkExecuted ----------

    [Fact]
    public async Task MarkExecuted_MovesApprovedPlansToExecuted()
    {
        var (plan, _, _) = await SeedPlanWithBackorderAsync();
        await _service.DecideAsync(plan.CommercialPlanId,
            new ApprovalDecisionRequest { Decision = "Approved" }, StaffUser.UserId);

        var executed = await _service.MarkExecutedAsync(plan.CommercialPlanId, StaffUser.UserId);

        Assert.Equal(CommercialPlanStatus.Executed.ToString(), executed.Status);
    }

    [Fact]
    public async Task MarkExecuted_RefusesAPlanThatWasNeverApproved()
    {
        var (plan, _, _) = await SeedPlanWithBackorderAsync();   // still PendingApproval

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.MarkExecutedAsync(plan.CommercialPlanId, StaffUser.UserId));
        Assert.Contains("Only Approved plans", ex.Message);
    }

    // ---------- GetById ----------

    [Fact]
    public async Task GetById_ResolvesThePerformerNameForTheAuditTimeline()
    {
        var (plan, _, _) = await SeedPlanWithBackorderAsync();
        await _service.DecideAsync(plan.CommercialPlanId,
            new ApprovalDecisionRequest { Decision = "Approved" }, StaffUser.UserId);
        Db.ChangeTracker.Clear();

        var found = await _service.GetByIdAsync(plan.CommercialPlanId);

        Assert.Equal(2, found.ApprovalActions.Count);   // Submitted + Approved
        Assert.All(found.ApprovalActions, a => Assert.Equal(StaffUser.FullName, a.PerformedByName));
    }

    /// <summary>
    /// A PendingApproval plan plus the buyer material request and backorder order that
    /// MaterialRestockMatcher links to it when it generates a plan.
    /// </summary>
    private async Task<(CommercialPlan Plan, MaterialRequest Request, SalesOrder Order)> SeedPlanWithBackorderAsync()
    {
        var plan = new CommercialPlan
        {
            WorkflowId = Guid.NewGuid(),
            RecommendedRoute = CommercialRoute.LocalSale,
            SelectedBuyerId = LocalBuyer.BuyerId,
            MaterialsJson = """[{"materialType":"Copper","quantityKg":25}]""",
            ReasoningSummary = "Sell to the local buyer.",
            Status = CommercialPlanStatus.PendingApproval
        };
        Db.CommercialPlans.Add(plan);

        var request = new MaterialRequest
        {
            BuyerId = LocalBuyer.BuyerId,
            MaterialType = "Copper",
            QuantityKg = 25m,
            Status = MaterialRequestStatus.PlanGenerated,
            CommercialPlanId = plan.CommercialPlanId
        };
        var order = new SalesOrder
        {
            BuyerId = LocalBuyer.BuyerId,
            MaterialRequest = request,
            PendingMaterialType = "Copper",
            PendingQuantityKg = 25m,
            Status = SalesOrderStatus.PendingPlanApproval,
            CreatedByUserId = BuyerUser.UserId
        };
        request.SalesOrder = order;
        Db.MaterialRequests.Add(request);
        Db.SalesOrders.Add(order);

        Db.ApprovalActions.Add(new ApprovalAction
        {
            CommercialPlanId = plan.CommercialPlanId,
            ActionType = ApprovalActionType.Submitted,
            PerformedByUserId = StaffUser.UserId,
            Comments = "Plan submitted by AI agent."
        });

        await Db.SaveChangesAsync();
        return (plan, request, order);
    }
}
