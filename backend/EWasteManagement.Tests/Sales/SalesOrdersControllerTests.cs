using EWasteManagement.API.Features.Sales.Controllers;
using EWasteManagement.API.Features.Sales.DTOs;
using EWasteManagement.API.Features.Sales.Services;
using EWasteManagement.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using static EWasteManagement.Tests.TestHelpers.ControllerTestExtensions;

namespace EWasteManagement.Tests.Sales;

/// <summary>
/// SalesOrdersController actions called directly as a fake logged-in staff user,
/// against the real service + SQLite. Checks the HTTP result each endpoint shapes
/// (routing and [Authorize] are enforced by middleware these tests don't run).
/// </summary>
public class SalesOrdersControllerTests : SalesTestBase
{
    private SalesOrderService _service = null!;
    private Guid _copperId;

    protected override Task SetUpAsync()
    {
        _copperId = Guid.NewGuid();
        var materials = new FakeRecoveredMaterialsProvider(SellableMaterial("Copper", 500m, _copperId));
        _service = new SalesOrderService(Db, materials, new RevenueService(Db));
        return Task.CompletedTask;
    }

    private SalesOrdersController Controller(Guid userId, string role = "Staff")
        => new SalesOrdersController(_service).WithUser(userId, role);

    private CreateSalesOrderRequest DraftRequest(decimal quantityKg = 10m) => new()
    {
        BuyerId = LocalBuyer.BuyerId,
        Items = { new CreateSalesOrderItemRequest { RecoveredMaterialId = _copperId, QuantityKg = quantityKg } }
    };

    [Fact]
    public async Task Create_Returns201AndStampsTheCallersUserId()
    {
        await SeedApprovedPriceAsync("Copper", 500m);

        var result = await Controller(StaffUser.UserId).Create(DraftRequest(), CancellationToken.None);

        Assert.Equal(201, StatusCodeOf(result));
        var body = ValueOf(result);
        Assert.Equal(StaffUser.UserId, body.CreatedByUserId);   // came from the token, not the payload
        Assert.Equal(5000m, body.TotalAmount);                  // 10 kg × 500
    }

    [Fact]
    public async Task GetAll_PassesTheFiltersThroughToTheService()
    {
        await SeedApprovedPriceAsync("Copper", 500m);
        var draft = await _service.CreateAsync(DraftRequest(5m), StaffUser.UserId);
        var toConfirm = await _service.CreateAsync(DraftRequest(6m), StaffUser.UserId);
        await _service.UpdateStatusAsync(toConfirm.SalesOrderId, new UpdateSalesOrderStatusRequest { Status = "Confirmed" });

        var confirmed = await Controller(StaffUser.UserId).GetAll(null, "confirmed", CancellationToken.None);
        var byBuyer = await Controller(StaffUser.UserId).GetAll(LocalBuyer.BuyerId, null, CancellationToken.None);

        Assert.Equal(200, StatusCodeOf(confirmed));
        Assert.Equal(toConfirm.SalesOrderId, Assert.Single(ValueOf(confirmed)).SalesOrderId);
        Assert.Equal(2, ValueOf(byBuyer).Count);
        Assert.Contains(ValueOf(byBuyer), o => o.SalesOrderId == draft.SalesOrderId);
    }

    [Fact]
    public async Task UpdateStatus_Returns200WithTheNewStatus()
    {
        await SeedApprovedPriceAsync("Copper", 500m);
        var order = await _service.CreateAsync(DraftRequest(), StaffUser.UserId);

        var result = await Controller(StaffUser.UserId).UpdateStatus(
            order.SalesOrderId, new UpdateSalesOrderStatusRequest { Status = "Confirmed" }, CancellationToken.None);

        Assert.Equal(200, StatusCodeOf(result));
        Assert.Equal("Confirmed", ValueOf(result).Status);
    }

    [Fact]
    public async Task GetById_Returns200ForADraftOrder()
    {
        await SeedApprovedPriceAsync("Copper", 500m);
        var order = await _service.CreateAsync(DraftRequest(), StaffUser.UserId);

        var result = await Controller(StaffUser.UserId).GetById(order.SalesOrderId, CancellationToken.None);

        Assert.Equal(200, StatusCodeOf(result));
        Assert.Equal(order.SalesOrderId, ValueOf(result).SalesOrderId);
    }

    [Fact]
    public async Task Delete_Returns204()
    {
        await SeedApprovedPriceAsync("Copper", 500m);
        var order = await _service.CreateAsync(DraftRequest(), StaffUser.UserId);

        var result = await Controller(StaffUser.UserId).Delete(order.SalesOrderId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }
}
