using EWasteManagement.API.Features.Sales.DTOs;
using EWasteManagement.API.Features.Sales.Entities;
using EWasteManagement.API.Features.Sales.Services;
using EWasteManagement.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;

namespace EWasteManagement.Tests.Sales;

/// <summary>
/// The local-sales order pipeline: pricing a basket of recovered material against the
/// live price list, the state machine an order walks (Draft → Confirmed → Completed),
/// and the revenue row that completion has to produce exactly once.
/// </summary>
public class SalesOrderServiceTests : SalesTestBase
{
    private FakeRecoveredMaterialsProvider _materials = null!;
    private SalesOrderService _service = null!;
    private Guid _copperId;

    protected override Task SetUpAsync()
    {
        _copperId = Guid.NewGuid();
        _materials = new FakeRecoveredMaterialsProvider(SellableMaterial("Copper", 500m, _copperId));
        _service = new SalesOrderService(Db, _materials, new RevenueService(Db));
        return Task.CompletedTask;
    }

    // ---------- Create: pricing and stock validation ----------

    [Fact]
    public async Task Create_SnapshotsTheLivePriceAndComputesLineAndOrderTotals()
    {
        await SeedApprovedPriceAsync("Copper", 950.50m);

        var order = await _service.CreateAsync(Order(LocalBuyer, _copperId, 12.5m), StaffUser.UserId);

        Assert.Equal(SalesOrderStatus.Draft.ToString(), order.Status);
        Assert.Equal("Colombo Metals Ltd", order.BuyerCompanyName);

        var item = Assert.Single(order.Items);
        Assert.Equal(950.50m, item.UnitPrice);      // locked-in snapshot, not a reference
        Assert.Equal(11881.25m, item.LineTotal);    // 12.5 kg × 950.50
        Assert.Equal(11881.25m, order.TotalAmount); // server-calculated, never set by the client
    }

    [Fact]
    public async Task Create_IgnoresAPriceThatIsStillOnlyADraft()
    {
        // Same material, newer date, but never approved — must not price anything.
        await SeedPriceAsync("Copper", 9999m, MaterialPricingPolicy.Today, PricingStatus.Draft);
        await SeedApprovedPriceAsync("Copper", 250m, effectiveDate: MaterialPricingPolicy.Today.AddDays(-3));

        var order = await _service.CreateAsync(Order(LocalBuyer, _copperId, 4m), StaffUser.UserId);

        Assert.Equal(250m, Assert.Single(order.Items).UnitPrice);
        Assert.Equal(1000m, order.TotalAmount);
    }

    [Fact]
    public async Task Create_ForAnUnknownBuyer_IsRejected()
    {
        await SeedApprovedPriceAsync("Copper", 100m);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(OrderWith(Guid.NewGuid(), _copperId, 5m), StaffUser.UserId));
    }

    [Fact]
    public async Task Create_ForAnInactiveBuyer_IsRejected()
    {
        await SeedApprovedPriceAsync("Copper", 100m);
        LocalBuyer.Status = BuyerStatus.Suspended;
        await Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(Order(LocalBuyer, _copperId, 5m), StaffUser.UserId));
        Assert.Contains("not active", ex.Message);
    }

    [Fact]
    public async Task Create_WithTheSameMaterialTwice_IsRejected()
    {
        var request = new CreateSalesOrderRequest
        {
            BuyerId = LocalBuyer.BuyerId,
            Items =
            {
                new CreateSalesOrderItemRequest { RecoveredMaterialId = _copperId, QuantityKg = 5m },
                new CreateSalesOrderItemRequest { RecoveredMaterialId = _copperId, QuantityKg = 3m }
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(request, StaffUser.UserId));
        Assert.Contains("more than once", ex.Message);
    }

    [Fact]
    public async Task Create_WithAnUnknownMaterialBatch_IsRejected()
    {
        await SeedApprovedPriceAsync("Copper", 100m);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(OrderWith(LocalBuyer.BuyerId, Guid.NewGuid(), 5m), StaffUser.UserId));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task Create_WithAMaterialThatIsNotReadyForSale_IsRejected()
    {
        var batchId = Guid.NewGuid();
        _materials.Add(new RecoveredMaterialResponse
        {
            RecoveredMaterialId = batchId,
            MaterialType = "Copper",
            QuantityKg = 100m,
            ProcessingStatus = "InProgress",   // still being dismantled
            SafetyValidated = false
        });
        await SeedApprovedPriceAsync("Copper", 100m);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(OrderWith(LocalBuyer.BuyerId, batchId, 5m), StaffUser.UserId));
        Assert.Contains("not sellable", ex.Message);
    }

    [Fact]
    public async Task Create_ForMoreThanTheBatchHolds_IsRejected()
    {
        await SeedApprovedPriceAsync("Copper", 100m);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(Order(LocalBuyer, _copperId, 501m), StaffUser.UserId));
        Assert.Contains("only 500", ex.Message);
    }

    [Fact]
    public async Task Create_WhenTheMaterialHasNoApprovedPrice_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(Order(LocalBuyer, _copperId, 5m), StaffUser.UserId));
        Assert.Contains("No current price", ex.Message);
    }

    [Fact]
    public async Task Create_IgnoresAnApprovedPriceWhoseExpiryHasPassed()
    {
        // The sweeper has not run, so the row still reads "Approved" — but its window is
        // closed and MaterialPricingPolicy must keep it out of new orders regardless.
        await SeedApprovedPriceAsync("Copper", 9999m,
            effectiveDate: MaterialPricingPolicy.Today.AddDays(-10),
            expiryDate: MaterialPricingPolicy.Today.AddDays(-1));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(Order(LocalBuyer, _copperId, 5m), StaffUser.UserId));
        Assert.Contains("No current price", ex.Message);
    }

    // ---------- UpdateStatus: the order state machine ----------

    [Fact]
    public async Task UpdateStatus_Draft_To_Confirmed_IsAllowed()
    {
        var order = await CreateDraftAsync();

        var updated = await _service.UpdateStatusAsync(order.SalesOrderId, Status("Confirmed"));

        Assert.Equal(SalesOrderStatus.Confirmed.ToString(), updated.Status);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Theory]
    [InlineData("Completed")]        // Draft must be Confirmed first
    [InlineData("WaitingForStock")]
    public async Task UpdateStatus_JumpingStraightOutOfDraft_IsRejected(string target)
    {
        var order = await CreateDraftAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateStatusAsync(order.SalesOrderId, Status(target)));
        Assert.Contains("Cannot move sales order", ex.Message);
    }

    [Fact]
    public async Task CompletingAnOrder_RecordsRevenueExactlyOnce()
    {
        var order = await CreateDraftAsync(quantityKg: 20m);   // 20 kg × 100 = 2000
        await _service.UpdateStatusAsync(order.SalesOrderId, Status("Confirmed"));

        var completed = await _service.UpdateStatusAsync(order.SalesOrderId, Status("Completed"));

        Assert.Equal(SalesOrderStatus.Completed.ToString(), completed.Status);
        var revenue = Assert.Single(await Db.RevenueTransactions.AsNoTracking().ToListAsync());
        Assert.Equal(RevenueType.LocalSale, revenue.TransactionType);
        Assert.Equal(order.SalesOrderId, revenue.ReferenceId);
        Assert.Equal(2000m, revenue.Amount);
        Assert.Equal(StaffUser.UserId, revenue.RecordedByUserId);

        // A repeat "Complete" is a no-op for the ledger — the unique (type, reference)
        // index would reject a second row anyway.
        await _service.UpdateStatusAsync(order.SalesOrderId, Status("Completed"));
        Assert.Single(await Db.RevenueTransactions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CancellingABackorder_AlsoCancelsItsMaterialRequest()
    {
        var (_, order) = await SeedBackorderAsync(SalesOrderStatus.WaitingForStock);

        await _service.UpdateStatusAsync(order.SalesOrderId, Status("Cancelled"));

        Db.ChangeTracker.Clear();
        Assert.Equal(SalesOrderStatus.Cancelled, (await Db.SalesOrders.SingleAsync()).Status);
        Assert.Equal(MaterialRequestStatus.Cancelled, (await Db.MaterialRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task UpdateStatus_ForAnUnknownOrder_IsRejected()
        => await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.UpdateStatusAsync(Guid.NewGuid(), Status("Confirmed")));

    // ---------- Reads ----------

    [Fact]
    public async Task GetAll_FiltersByStatusAndByBuyer()
    {
        await SeedApprovedPriceAsync("Copper", 100m);
        var localDraft = await _service.CreateAsync(Order(LocalBuyer, _copperId, 5m), StaffUser.UserId);
        var exportDraft = await _service.CreateAsync(Order(ExportBuyer, _copperId, 6m), StaffUser.UserId);
        await _service.UpdateStatusAsync(exportDraft.SalesOrderId, Status("Confirmed"));

        var all = await _service.GetAllAsync(new SalesOrderFilter());
        var confirmedOnly = await _service.GetAllAsync(new SalesOrderFilter { Status = "confirmed" });
        var localOnly = await _service.GetAllAsync(new SalesOrderFilter { BuyerId = LocalBuyer.BuyerId });

        Assert.Equal(2, all.Count);
        Assert.Equal(exportDraft.SalesOrderId, Assert.Single(confirmedOnly).SalesOrderId);
        Assert.Equal(localDraft.SalesOrderId, Assert.Single(localOnly).SalesOrderId);
    }

    [Fact]
    public async Task GetById_ForAnUnknownOrder_IsRejected()
        => await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetByIdAsync(Guid.NewGuid()));

    // ---------- Delete ----------

    [Fact]
    public async Task Delete_RemovesADraftOrderAndItsLines()
    {
        var order = await CreateDraftAsync();

        await _service.DeleteAsync(order.SalesOrderId);

        Assert.Empty(await Db.SalesOrders.AsNoTracking().ToListAsync());
        Assert.Empty(await Db.SalesOrderItems.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Delete_RefusesAnyOrderThatIsNotStillADraft()
    {
        var order = await CreateDraftAsync();
        await _service.UpdateStatusAsync(order.SalesOrderId, Status("Confirmed"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteAsync(order.SalesOrderId));
        Assert.Contains("Only Draft orders can be deleted", ex.Message);
    }

    [Fact]
    public async Task Delete_RefusesABackorderEvenWhileItIsADraft()
    {
        var (_, order) = await SeedBackorderAsync(SalesOrderStatus.Draft);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteAsync(order.SalesOrderId));
        Assert.Contains("Backorder", ex.Message);
    }

    // ---------- Helpers ----------

    private static CreateSalesOrderRequest Order(Buyer buyer, Guid materialId, decimal quantityKg)
        => OrderWith(buyer.BuyerId, materialId, quantityKg);

    private static CreateSalesOrderRequest OrderWith(Guid buyerId, Guid materialId, decimal quantityKg)
        => new()
        {
            BuyerId = buyerId,
            Items = { new CreateSalesOrderItemRequest { RecoveredMaterialId = materialId, QuantityKg = quantityKg } }
        };

    private static UpdateSalesOrderStatusRequest Status(string status) => new() { Status = status };

    /// <summary>A priced Draft order — the starting point most state-machine tests need.</summary>
    private async Task<SalesOrderResponse> CreateDraftAsync(decimal quantityKg = 10m)
    {
        await SeedApprovedPriceAsync("Copper", 100m);
        return await _service.CreateAsync(Order(LocalBuyer, _copperId, quantityKg), StaffUser.UserId);
    }

    /// <summary>
    /// A buyer material request plus the backorder order it created, exactly as
    /// MaterialRequestService leaves them.
    /// </summary>
    private async Task<(MaterialRequest Request, SalesOrder Order)> SeedBackorderAsync(SalesOrderStatus status)
    {
        var request = new MaterialRequest
        {
            BuyerId = LocalBuyer.BuyerId,
            MaterialType = "Copper",
            QuantityKg = 30m,
            Status = MaterialRequestStatus.Waiting
        };
        var order = new SalesOrder
        {
            BuyerId = LocalBuyer.BuyerId,
            MaterialRequest = request,
            PendingMaterialType = "Copper",
            PendingQuantityKg = 30m,
            Status = status,
            CreatedByUserId = BuyerUser.UserId,
            Notes = "Backorder created from buyer material request."
        };
        request.SalesOrder = order;
        Db.MaterialRequests.Add(request);
        Db.SalesOrders.Add(order);
        await Db.SaveChangesAsync();
        return (request, order);
    }
}
