using EWasteManagement.API.Features.Sales.DTOs;
using EWasteManagement.API.Features.Sales.Entities;
using EWasteManagement.API.Features.Sales.Services;
using EWasteManagement.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;

namespace EWasteManagement.Tests.Sales;

/// <summary>
/// The export pipeline. Export is a high-impact action, so on top of the same pricing
/// and stock checks as a local sale it carries the shipment-minimum rule, an
/// export-buyer-only rule, and an approval state machine that a shipment must not skip.
/// </summary>
public class ExportOrderServiceTests : SalesTestBase
{
    private FakeRecoveredMaterialsProvider _materials = null!;
    private ExportOrderService _service = null!;
    private Guid _copperId;

    protected override Task SetUpAsync()
    {
        _copperId = Guid.NewGuid();
        _materials = new FakeRecoveredMaterialsProvider(SellableMaterial("Copper", 500m, _copperId));
        _service = new ExportOrderService(Db, _materials, new RevenueService(Db));
        return Task.CompletedTask;
    }

    // ---------- Create ----------

    [Fact]
    public async Task Create_SnapshotsPricesAndSumsWeightAndValue()
    {
        await SeedApprovedPriceAsync("Copper", 1200m);

        var order = await _service.CreateAsync(Order(ExportBuyer, _copperId, 50m), StaffUser.UserId);

        Assert.Equal(ExportOrderStatus.Draft.ToString(), order.Status);
        Assert.Equal("Shenzhen Reclaim Ltd", order.BuyerCompanyName);
        Assert.Equal(50m, order.TotalWeightKg);
        Assert.Equal(60000m, order.TotalValue);          // 50 kg × 1200
        Assert.Equal(1200m, Assert.Single(order.Items).UnitPrice);
    }

    [Fact]
    public async Task Create_ForALocalBuyer_IsRejected()
    {
        await SeedApprovedPriceAsync("Copper", 1200m);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(Order(LocalBuyer, _copperId, 50m), StaffUser.UserId));
        Assert.Contains("Only Export buyers", ex.Message);
    }

    [Fact]
    public async Task Create_ForAnInactiveBuyer_IsRejected()
    {
        ExportBuyer.Status = BuyerStatus.Suspended;
        await Db.SaveChangesAsync();
        await SeedApprovedPriceAsync("Copper", 1200m);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(Order(ExportBuyer, _copperId, 50m), StaffUser.UserId));
        Assert.Contains("not active", ex.Message);
    }

    [Fact]
    public async Task Create_BelowTheShipmentMinimum_IsRejected()
    {
        await SeedApprovedPriceAsync("Copper", 1200m);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(Order(ExportBuyer, _copperId, 19.5m), StaffUser.UserId));
        Assert.Contains("at least 20 kg", ex.Message);
    }

    [Fact]
    public async Task Create_WithTheSameMaterialTwice_IsRejected()
    {
        await SeedApprovedPriceAsync("Copper", 1200m);
        var request = Order(ExportBuyer, _copperId, 30m);
        request.Items.Add(new CreateExportOrderItemRequest { RecoveredMaterialId = _copperId, QuantityKg = 20m });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(request, StaffUser.UserId));
        Assert.Contains("only once", ex.Message);
    }

    [Fact]
    public async Task Create_WithAnExpiredPrice_IsRejected()
    {
        await SeedApprovedPriceAsync("Copper", 1200m,
            effectiveDate: MaterialPricingPolicy.Today.AddDays(-20),
            expiryDate: MaterialPricingPolicy.Today.AddDays(-2));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(Order(ExportBuyer, _copperId, 50m), StaffUser.UserId));
        Assert.Contains("No current price", ex.Message);
    }

    [Fact]
    public async Task Create_ForAnUnknownBuyer_IsRejected()
    {
        await SeedApprovedPriceAsync("Copper", 1200m);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(OrderWith(Guid.NewGuid(), _copperId, 50m), StaffUser.UserId));
    }

    [Fact]
    public async Task Create_ForAnUnknownMaterialBatch_IsRejected()
    {
        await SeedApprovedPriceAsync("Copper", 1200m);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(OrderWith(ExportBuyer.BuyerId, Guid.NewGuid(), 50m), StaffUser.UserId));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task Create_ForMoreThanTheBatchHolds_IsRejected()
    {
        await SeedApprovedPriceAsync("Copper", 1200m);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(Order(ExportBuyer, _copperId, 501m), StaffUser.UserId));
        Assert.Contains("only 500", ex.Message);
    }

    // ---------- UpdateStatus: the export approval state machine ----------

    [Fact]
    public async Task ApprovalChain_FromDraftToCompleted_RecordsExportRevenueExactlyOnce()
    {
        var order = await CreateDraftAsync(25m);   // 25 kg × 1000 = 25000

        await Move(order, "PendingApproval");
        await Move(order, "Approved");
        await Move(order, "Shipped");
        var completed = await Move(order, "Completed");

        Assert.Equal(ExportOrderStatus.Completed.ToString(), completed.Status);
        var revenue = Assert.Single(await Db.RevenueTransactions.AsNoTracking().ToListAsync());
        Assert.Equal(RevenueType.Export, revenue.TransactionType);
        Assert.Equal(order.ExportOrderId, revenue.ReferenceId);
        Assert.Equal(25000m, revenue.Amount);

        await Move(order, "Completed");
        Assert.Single(await Db.RevenueTransactions.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("Shipped")]     // skips human approval
    [InlineData("Completed")]   // skips approval and shipping
    public async Task UpdateStatus_JumpingAheadFromDraft_IsRejected(string target)
    {
        var order = await CreateDraftAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Move(order, target));
        Assert.Contains("Cannot move from", ex.Message);
    }

    [Fact]
    public async Task UpdateStatus_ACancelledExport_CannotBeReopened()
    {
        var order = await CreateDraftAsync();
        await Move(order, "Cancelled");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Move(order, "Draft"));
        Assert.Contains("cannot be reopened", ex.Message);
    }

    // ---------- Delete ----------

    [Fact]
    public async Task Delete_RemovesADraftExportOrder()
    {
        var order = await CreateDraftAsync();
        await _service.DeleteAsync(order.ExportOrderId);

        Assert.Empty(await Db.ExportOrders.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Delete_RefusesAnythingThatHasLeftDraft()
    {
        var order = await CreateDraftAsync();
        await Move(order, "PendingApproval");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteAsync(order.ExportOrderId));
        Assert.Contains("Only Draft export orders", ex.Message);
    }

    // ---------- Helpers ----------

    private static CreateExportOrderRequest Order(Buyer buyer, Guid materialId, decimal weightKg)
        => OrderWith(buyer.BuyerId, materialId, weightKg);

    private static CreateExportOrderRequest OrderWith(Guid buyerId, Guid materialId, decimal weightKg)
        => new()
        {
            BuyerId = buyerId,
            DestinationCountry = "IN",
            ShipmentDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7),
            Items = { new CreateExportOrderItemRequest { RecoveredMaterialId = materialId, QuantityKg = weightKg } }
        };

    private async Task<ExportOrderResponse> CreateDraftAsync(decimal weightKg = 25m)
    {
        await SeedApprovedPriceAsync("Copper", 1000m);
        return await _service.CreateAsync(Order(ExportBuyer, _copperId, weightKg), StaffUser.UserId);
    }

    private Task<ExportOrderResponse> Move(ExportOrderResponse order, string status)
        => _service.UpdateStatusAsync(order.ExportOrderId, new UpdateExportOrderStatusRequest { Status = status });
}
