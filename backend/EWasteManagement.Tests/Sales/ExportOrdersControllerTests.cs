using EWasteManagement.API.Features.Sales.Controllers;
using EWasteManagement.API.Features.Sales.DTOs;
using EWasteManagement.API.Features.Sales.Services;
using EWasteManagement.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using static EWasteManagement.Tests.TestHelpers.ControllerTestExtensions;

namespace EWasteManagement.Tests.Sales;

/// <summary>
/// The extra rule the export controller enforces on top of the service: moving an order
/// to PendingApproval is ordinary staff work, but the "Approved" transition is a
/// high-impact action (FR-D09) that only an Admin may perform.
/// </summary>
public class ExportOrdersControllerTests : SalesTestBase
{
    private ExportOrderService _service = null!;
    private Guid _copperId;

    protected override Task SetUpAsync()
    {
        _copperId = Guid.NewGuid();
        var materials = new FakeRecoveredMaterialsProvider(SellableMaterial("Copper", 500m, _copperId));
        _service = new ExportOrderService(Db, materials, new RevenueService(Db));
        return Task.CompletedTask;
    }

    private ExportOrdersController Controller(Guid userId, string role)
        => new ExportOrdersController(_service).WithUser(userId, role);

    private CreateExportOrderRequest DraftRequest(decimal weightKg = 30m) => new()
    {
        BuyerId = ExportBuyer.BuyerId,
        DestinationCountry = "IN",
        ShipmentDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7),
        Items = { new CreateExportOrderItemRequest { RecoveredMaterialId = _copperId, QuantityKg = weightKg } }
    };

    private async Task<Guid> SeedOrderAtAsync(params string[] statuses)
    {
        await SeedApprovedPriceAsync("Copper", 1000m);
        var order = await _service.CreateAsync(DraftRequest(), StaffUser.UserId);
        foreach (var status in statuses)
            await _service.UpdateStatusAsync(order.ExportOrderId, new UpdateExportOrderStatusRequest { Status = status });
        return order.ExportOrderId;
    }

    [Fact]
    public async Task Create_Returns201()
    {
        await SeedApprovedPriceAsync("Copper", 1000m);

        var result = await Controller(StaffUser.UserId, "Staff").Create(DraftRequest(), CancellationToken.None);

        Assert.Equal(201, StatusCodeOf(result));
        Assert.Equal(30000m, ValueOf(result).TotalValue);   // 30 kg × 1000
    }

    [Fact]
    public async Task StaffMayMoveAnOrderIntoPendingApproval()
    {
        var id = await SeedOrderAtAsync();

        var result = await Controller(StaffUser.UserId, "Staff").UpdateStatus(
            id, new UpdateExportOrderStatusRequest { Status = "PendingApproval" }, CancellationToken.None);

        Assert.Equal(200, StatusCodeOf(result));
        Assert.Equal("PendingApproval", ValueOf(result).Status);
    }

    [Fact]
    public async Task ApprovingAsStaff_IsForbidden()
    {
        var id = await SeedOrderAtAsync("PendingApproval");

        var result = await Controller(StaffUser.UserId, "Staff").UpdateStatus(
            id, new UpdateExportOrderStatusRequest { Status = "Approved" }, CancellationToken.None);

        // Forbid() is returned before the service is ever called — the order stays put.
        Assert.IsType<ForbidResult>(result.Result);
        Assert.Equal("PendingApproval", (await _service.GetByIdAsync(id)).Status);
    }

    [Fact]
    public async Task ApprovingAsAdmin_Succeeds()
    {
        var id = await SeedOrderAtAsync("PendingApproval");

        var result = await Controller(Guid.NewGuid(), "Admin").UpdateStatus(
            id, new UpdateExportOrderStatusRequest { Status = "Approved" }, CancellationToken.None);

        Assert.Equal(200, StatusCodeOf(result));
        Assert.Equal("Approved", ValueOf(result).Status);
    }
}
