using EWasteManagement.API.Features.Sales.DTOs;
using EWasteManagement.API.Features.Sales.Entities;
using EWasteManagement.API.Features.Sales.Services;
using Microsoft.EntityFrameworkCore;

namespace EWasteManagement.Tests.Sales;

/// <summary>
/// The revenue ledger: the idempotent "record revenue for a completed order" write that
/// SalesOrderService / ExportOrderService call, and the reads (list, summary, monthly
/// breakdown) the dashboard is built from.
/// </summary>
public class RevenueServiceTests : SalesTestBase
{
    private RevenueService _service = null!;

    protected override Task SetUpAsync()
    {
        _service = new RevenueService(Db);
        return Task.CompletedTask;
    }

    // ---------- Write ----------

    [Fact]
    public async Task RecordOrderRevenueAsync_CreatesALedgerRowForTheOrder()
    {
        var orderId = Guid.NewGuid();

        var created = await _service.RecordOrderRevenueAsync(
            RevenueType.LocalSale, orderId, 1234.50m, StaffUser.UserId, "recorded by test");
        await Db.SaveChangesAsync();

        var stored = Assert.Single(await Db.RevenueTransactions.AsNoTracking().ToListAsync());
        Assert.Equal(created.RevenueId, stored.RevenueId);
        Assert.Equal(RevenueType.LocalSale, stored.TransactionType);
        Assert.Equal(orderId, stored.ReferenceId);
        Assert.Equal(1234.50m, stored.Amount);
        Assert.Equal("recorded by test", stored.Remarks);
        Assert.Equal(StaffUser.UserId, stored.RecordedByUserId);
    }

    [Fact]
    public async Task RecordOrderRevenueAsync_IsIdempotentPerTypeAndReference()
    {
        var orderId = Guid.NewGuid();
        await _service.RecordOrderRevenueAsync(RevenueType.Export, orderId, 500m, StaffUser.UserId);
        await Db.SaveChangesAsync();

        // Second call (e.g. a re-run of the completion handler) must hand back the
        // original row rather than queue a duplicate — the unique index would reject it.
        var second = await _service.RecordOrderRevenueAsync(RevenueType.Export, orderId, 999m, StaffUser.UserId);
        await Db.SaveChangesAsync();

        var stored = Assert.Single(await Db.RevenueTransactions.AsNoTracking().ToListAsync());
        Assert.Equal(500m, stored.Amount);
        Assert.Equal(stored.RevenueId, second.RevenueId);
    }

    [Fact]
    public async Task RecordOrderRevenueAsync_TreatsTheSameOrderIdInBothLedgersAsDistinct()
    {
        // A local sale and an export could theoretically share an id only if the ids
        // collided; the (type, reference) key must keep them separate rows.
        var sharedId = Guid.NewGuid();
        await _service.RecordOrderRevenueAsync(RevenueType.LocalSale, sharedId, 100m, StaffUser.UserId);
        await _service.RecordOrderRevenueAsync(RevenueType.Export, sharedId, 200m, StaffUser.UserId);
        await Db.SaveChangesAsync();

        Assert.Equal(2, await Db.RevenueTransactions.CountAsync());
    }

    // ---------- Reads ----------

    [Fact]
    public async Task GetAll_FiltersByTypeAndDateRange()
    {
        var now = DateTime.UtcNow;
        await SeedRevenueAsync(RevenueType.LocalSale, 100m, now.AddDays(-3));
        await SeedRevenueAsync(RevenueType.Export, 200m, now.AddDays(-1));

        var all = await _service.GetAllAsync(new RevenueFilter());
        var exportOnly = await _service.GetAllAsync(new RevenueFilter { TransactionType = "export" });
        var recentOnly = await _service.GetAllAsync(new RevenueFilter { FromDate = now.AddDays(-2) });

        Assert.Equal(2, all.Count);
        Assert.Equal(200m, Assert.Single(exportOnly).Amount);
        Assert.Equal(200m, Assert.Single(recentOnly).Amount);
    }

    [Fact]
    public async Task GetAll_ResolvesTheNameOfWhoRecordedEachRow()
    {
        await SeedRevenueAsync(RevenueType.LocalSale, 450m, DateTime.UtcNow);

        Assert.Equal(StaffUser.FullName, Assert.Single(await _service.GetAllAsync(new RevenueFilter())).RecordedByName);
    }

    [Fact]
    public async Task GetById_ForAnUnknownTransaction_IsRejected()
        => await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetByIdAsync(Guid.NewGuid()));

    [Fact]
    public async Task GetById_ReturnsTheTransactionWithItsRecorderName()
    {
        var tx = await SeedRevenueAsync(RevenueType.LocalSale, 100m, DateTime.UtcNow);

        var found = await _service.GetByIdAsync(tx.RevenueId);

        Assert.Equal(tx.RevenueId, found.RevenueId);
        Assert.Equal(StaffUser.FullName, found.RecordedByName);
    }

    [Fact]
    public async Task GetSummary_AggregatesTotalsByTypeAndGroupsByMonth()
    {
        var january = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var february = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc);
        await SeedRevenueAsync(RevenueType.LocalSale, 300m, january);
        await SeedRevenueAsync(RevenueType.Export, 700m, january);
        await SeedRevenueAsync(RevenueType.LocalSale, 50m, february);

        var summary = await _service.GetSummaryAsync();

        Assert.Equal(1050m, summary.TotalRevenue);
        Assert.Equal(350m, summary.LocalSaleRevenue);
        Assert.Equal(700m, summary.ExportRevenue);
        Assert.Equal(3, summary.TransactionCount);
        Assert.Equal(2, summary.Monthly.Count);

        // Newest month first: February (one 50) then January (two rows summing to 1000).
        Assert.Equal("2026-02", summary.Monthly[0].Month);
        Assert.Equal(50m, summary.Monthly[0].Amount);
        Assert.Equal(1, summary.Monthly[0].Count);
        Assert.Equal("2026-01", summary.Monthly[1].Month);
        Assert.Equal(1000m, summary.Monthly[1].Amount);
        Assert.Equal(2, summary.Monthly[1].Count);
    }

    [Fact]
    public async Task GetSummary_WithNoRevenueYet_ReturnsZeroes()
    {
        var summary = await _service.GetSummaryAsync();

        Assert.Equal(0m, summary.TotalRevenue);
        Assert.Equal(0m, summary.LocalSaleRevenue);
        Assert.Equal(0m, summary.ExportRevenue);
        Assert.Equal(0, summary.TransactionCount);
        Assert.Empty(summary.Monthly);
    }

    private async Task<RevenueTransaction> SeedRevenueAsync(RevenueType type, decimal amount, DateTime when)
    {
        var tx = new RevenueTransaction
        {
            TransactionType = type,
            ReferenceId = Guid.NewGuid(),
            Amount = amount,
            TransactionDate = when,
            RecordedByUserId = StaffUser.UserId
        };
        Db.RevenueTransactions.Add(tx);
        await Db.SaveChangesAsync();
        return tx;
    }
}
