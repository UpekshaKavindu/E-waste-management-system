using System.Data.Common;
using EWasteManagement.API.Features.Auth.Entities;
using EWasteManagement.API.Features.Sales.Entities;
using EWasteManagement.API.Features.Sales.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EWasteManagement.Tests.Sales;

/// <summary>
/// Database-contract tests for Component D (Sales).
///
/// Every other Sales test asserts what a <em>service</em> decides. These assert what the
/// <em>schema</em> guarantees, because the schema is the last line of defence once a rule
/// has more than one way in: a background sweep, a migration, an agent callback or a second
/// service can all write rows the HTTP layer never touches.
///
/// What is locked in here:
///  <list type="bullet">
///   <item>Check constraints on every money/quantity column (impossible rows cannot be stored).</item>
///   <item>Unique indexes: one buyer profile per corporate user, one revenue row per completed
///         order, one backorder order per material request.</item>
///   <item>Delete behaviour: Cascade from order to its lines, Restrict from a parent that
///         history or audit still points at.</item>
///   <item>The global soft-delete query filter, and that soft-deleting a buyer leaves its
///         order history intact.</item>
///   <item>Enum value converters store exactly the lowercase text the CHECK constraints allow.</item>
///  </list>
///
/// These use raw ADO.NET on purpose. EF would refuse to send some of these rows client-side
/// (an unknown enum cannot even be constructed), so a provider-level test has to speak SQL.
/// A positive control row accompanies the constraint cases to prove the statement is rejected
/// by the constraint and not by a typo in the SQL.
/// </summary>
public class SalesDatabaseIntegrationTests : SalesTestBase
{
    /// <summary>
    /// Microsoft.Data.Sqlite leaves <c>PRAGMA foreign_keys</c> OFF, which would make every
    /// Cascade/Restrict/SetNull assertion below vacuously pass. Turn it on before any test
    /// runs. The pragma is a no-op inside a transaction, so this must happen while the
    /// connection is idle — which is exactly where SalesTestBase invokes this hook.
    /// </summary>
    protected override async Task SetUpAsync()
    {
        await ExecuteAsync("PRAGMA foreign_keys = ON");
        Assert.Equal("1", await ReadScalarAsync("PRAGMA foreign_keys"));
    }

    // ---------- 1. Check constraints ----------

    [Fact]
    public async Task CheckConstraints_RejectImpossibleSalesMoneyRows()
    {
        var order = await SeedSalesOrderAsync(LocalBuyer, 10m, 100m);
        var today = MaterialPricingPolicy.Today;

        // Positive control: the exact shape used below, with legal values, must save.
        await ExecuteAsync(
            """
            INSERT INTO sales_orders
                (sales_order_id, buyer_id, order_date, total_amount, status, created_at, created_by_user_id)
            VALUES (@id, @buyerId, @now, 0, 'completed', @now, @userId)
            """,
            Param("id", Guid.NewGuid()), Param("buyerId", LocalBuyer.BuyerId),
            Param("now", DateTime.UtcNow), Param("userId", StaffUser.UserId));
        Assert.Equal(2, await Db.SalesOrders.IgnoreQueryFilters().CountAsync());

        // Money can never be negative on an order.
        await AssertConstraintFailsAsync(
            """
            INSERT INTO sales_orders
                (sales_order_id, buyer_id, order_date, total_amount, status, created_at, created_by_user_id)
            VALUES (@id, @buyerId, @now, -0.01, 'draft', @now, @userId)
            """,
            Param("id", Guid.NewGuid()), Param("buyerId", LocalBuyer.BuyerId),
            Param("now", DateTime.UtcNow), Param("userId", StaffUser.UserId));

        // "shipped" is a legal ExportOrderStatus but not a SalesOrderStatus.
        await AssertConstraintFailsAsync(
            """
            INSERT INTO sales_orders
                (sales_order_id, buyer_id, order_date, total_amount, status, created_at, created_by_user_id)
            VALUES (@id, @buyerId, @now, 100, 'shipped', @now, @userId)
            """,
            Param("id", Guid.NewGuid()), Param("buyerId", LocalBuyer.BuyerId),
            Param("now", DateTime.UtcNow), Param("userId", StaffUser.UserId));

        // An order line must move stock, and must be worth a non-negative amount.
        await AssertConstraintFailsAsync(
            """
            INSERT INTO sales_order_items
                (sales_order_item_id, sales_order_id, recovered_material_id, material_type, quantity_kg, unit_price, line_total)
            VALUES (@id, @orderId, @materialId, 'Copper', 0, 100, 0)
            """,
            Param("id", Guid.NewGuid()), Param("orderId", order.SalesOrderId),
            Param("materialId", Guid.NewGuid()));

        await AssertConstraintFailsAsync(
            """
            INSERT INTO sales_order_items
                (sales_order_item_id, sales_order_id, recovered_material_id, material_type, quantity_kg, unit_price, line_total)
            VALUES (@id, @orderId, @materialId, 'Copper', 5, -1, -5)
            """,
            Param("id", Guid.NewGuid()), Param("orderId", order.SalesOrderId),
            Param("materialId", Guid.NewGuid()));

        // A completed order books revenue; revenue is strictly positive.
        await AssertConstraintFailsAsync(
            """
            INSERT INTO revenue_transactions
                (revenue_id, transaction_type, reference_id, amount, transaction_date, recorded_by_user_id)
            VALUES (@id, 'localsale', @referenceId, 0, @now, @userId)
            """,
            Param("id", Guid.NewGuid()), Param("referenceId", order.SalesOrderId),
            Param("now", DateTime.UtcNow), Param("userId", StaffUser.UserId));

        // A price window has to run forwards in time.
        await AssertConstraintFailsAsync(
            """
            INSERT INTO material_pricing
                (pricing_id, material_type, price_per_kg, effective_date, expiry_date, status, created_by_user_id, created_at)
            VALUES (@id, 'Copper', 100, @today, @yesterday, 'draft', @userId, @now)
            """,
            Param("id", Guid.NewGuid()), Param("today", today), Param("yesterday", today.AddDays(-1)),
            Param("userId", StaffUser.UserId), Param("now", DateTime.UtcNow));

        // Same for the export side of the component.
        await AssertConstraintFailsAsync(
            """
            INSERT INTO export_orders
                (export_order_id, buyer_id, order_date, destination_country, shipment_date,
                 total_weight_kg, total_value, status, created_at, created_by_user_id)
            VALUES (@id, @buyerId, @now, 'IN', @shipment, 10, -1, 'draft', @now, @userId)
            """,
            Param("id", Guid.NewGuid()), Param("buyerId", ExportBuyer.BuyerId),
            Param("now", DateTime.UtcNow), Param("shipment", today.AddDays(7)),
            Param("userId", StaffUser.UserId));
    }

    // ---------- 2. Unique indexes ----------

    [Fact]
    public async Task UniqueIndexes_RefuseDuplicateBuyerRevenueAndBackorderRows()
    {
        // A corporate user is linked 1-to-1 with a buyer profile — two profiles would make
        // "which company is this account?" unanswerable.
        //
        // Detach first: BuyerUser is still tracked from the base fixture, and EF's 1:1
        // fixup would otherwise try to re-point the relationship at the new profile and
        // fail with a client-side error before ever reaching the unique index.
        Db.ChangeTracker.Clear();
        Db.Buyers.Add(new Buyer
        {
            UserId = BuyerUser.UserId,                       // already LocalBuyer's user
            CompanyName = "Impostor Metals",
            ContactPerson = "Nobody",
            Email = BuyerUser.Email
        });
        await AssertUniqueViolationAsync(() => Db.SaveChangesAsync());

        // One ledger row per completed order, so a re-run of completion cannot double-book.
        var order = await SeedSalesOrderAsync(LocalBuyer, 10m, 100m);
        Db.RevenueTransactions.Add(Revenue(RevenueType.LocalSale, order.SalesOrderId, 100m));
        await Db.SaveChangesAsync();

        Db.RevenueTransactions.Add(Revenue(RevenueType.LocalSale, order.SalesOrderId, 999m));   // same (type, reference)
        await AssertUniqueViolationAsync(() => Db.SaveChangesAsync());
        Assert.Equal(100m, (await Db.RevenueTransactions.AsNoTracking().SingleAsync()).Amount);

        // A different ledger may legitimately reuse the same id.
        Db.RevenueTransactions.Add(Revenue(RevenueType.Export, order.SalesOrderId, 100m));
        await Db.SaveChangesAsync();
        Assert.Equal(2, await Db.RevenueTransactions.CountAsync());

        // A material request produces at most one backorder order. Inserted as raw SQL: the
        // point is the index, and EF's 1:1 fixup against the tracked request would get in
        // the way of proving it.
        var request = await SeedMaterialRequestAsync(LocalBuyer, "Copper", 30m);
        Db.ChangeTracker.Clear();
        const string backorder = """
            INSERT INTO sales_orders
                (sales_order_id, buyer_id, order_date, total_amount, status, created_at, created_by_user_id, material_request_id)
            VALUES (@id, @buyerId, @now, 100, 'draft', @now, @userId, @requestId)
            """;

        await ExecuteAsync(backorder, Param("id", Guid.NewGuid()), Param("buyerId", LocalBuyer.BuyerId),
            Param("now", DateTime.UtcNow), Param("userId", StaffUser.UserId),
            Param("requestId", request.MaterialRequestId));

        await AssertUniqueViolationAsync(backorder, Param("id", Guid.NewGuid()), Param("buyerId", LocalBuyer.BuyerId),
            Param("now", DateTime.UtcNow), Param("userId", StaffUser.UserId),
            Param("requestId", request.MaterialRequestId));
    }

    // ---------- 3. Cascade and Restrict delete behaviour ----------

    [Fact]
    public async Task DeleteGraph_CascadesToOrderLines_AndRestrictsAuditedParents()
    {
        // --- Cascade: an order and its lines go together, in the database ---
        var order = await SeedSalesOrderAsync(LocalBuyer, 10m, 100m, lines: 2);
        Db.ChangeTracker.Clear();      // dependents unloaded, so only the DB can cascade
        Db.SalesOrders.Remove(await Db.SalesOrders.AsNoTracking().FirstAsync(o => o.SalesOrderId == order.SalesOrderId));

        await Db.SaveChangesAsync();

        Assert.Empty(await Db.SalesOrders.AsNoTracking().ToListAsync());
        Assert.Empty(await Db.SalesOrderItems.AsNoTracking().ToListAsync());

        // Same shape for exports.
        var export = await SeedExportOrderAsync(ExportBuyer, lines: 2);
        Db.ChangeTracker.Clear();
        Db.ExportOrders.Remove(await Db.ExportOrders.AsNoTracking().FirstAsync(o => o.ExportOrderId == export.ExportOrderId));

        await Db.SaveChangesAsync();

        Assert.Empty(await Db.ExportOrders.AsNoTracking().ToListAsync());
        Assert.Empty(await Db.ExportOrderItems.AsNoTracking().ToListAsync());

        // And for a plan's approval trail.
        var plan = await SeedPlanAsync(withActions: 2);
        Db.ChangeTracker.Clear();
        Db.CommercialPlans.Remove(await Db.CommercialPlans.AsNoTracking().FirstAsync(p => p.CommercialPlanId == plan.CommercialPlanId));

        await Db.SaveChangesAsync();

        Assert.Empty(await Db.CommercialPlans.AsNoTracking().ToListAsync());
        Assert.Empty(await Db.ApprovalActions.AsNoTracking().ToListAsync());

        // --- Restrict: history and audit keep their parents alive ---
        // A buyer needs its own corporate user — BuyerUser already owns LocalBuyer, and
        // buyers.user_id is unique.
        var keeperUser = await SeedUserAsync("keeper@example.test", UserRole.Corporate);
        var buyerWithOrders = await SeedBuyerAsync(keeperUser, company: "Keeper Metals Ltd");
        await SeedSalesOrderAsync(buyerWithOrders, 1m, 10m);
        await AssertForeignKeyFailsAsync(
            "DELETE FROM buyers WHERE buyer_id = @id", Param("id", buyerWithOrders.BuyerId));

        var recorder = await SeedUserAsync("recorder@example.test", UserRole.Staff, StaffType.Management);
        Db.RevenueTransactions.Add(Revenue(RevenueType.LocalSale, Guid.NewGuid(), 50m, recorder.UserId));
        await Db.SaveChangesAsync();
        await AssertForeignKeyFailsAsync(
            "DELETE FROM users WHERE user_id = @id", Param("id", recorder.UserId));

        // --- SetNull: dropping a request detaches the backorder instead of deleting it ---
        var request = await SeedMaterialRequestAsync(LocalBuyer, "Copper", 15m);
        var backorder = await SeedSalesOrderAsync(LocalBuyer, 15m, 100m, materialRequestId: request.MaterialRequestId);
        Db.ChangeTracker.Clear();

        await ExecuteAsync("DELETE FROM material_requests WHERE material_request_id = @id",
            Param("id", request.MaterialRequestId));

        Db.ChangeTracker.Clear();
        var surviving = await Db.SalesOrders.AsNoTracking().SingleAsync(o => o.SalesOrderId == backorder.SalesOrderId);
        Assert.Null(surviving.MaterialRequestId);      // FK nulled, order kept
        Assert.Equal(SalesOrderStatus.Draft, surviving.Status);
    }

    // ---------- 4. Soft-delete query filter ----------

    [Fact]
    public async Task SoftDeletedBuyers_AreHiddenByTheQueryFilter_ButOrderHistorySurvives()
    {
        await SeedSalesOrderAsync(LocalBuyer, 25m, 100m);

        LocalBuyer.IsDeleted = true;
        LocalBuyer.DeletedAt = DateTime.UtcNow;
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        // Hidden from every ordinary read, so no screen or dropdown can offer it.
        Assert.Empty(await Db.Buyers.Where(b => b.BuyerId == LocalBuyer.BuyerId).ToListAsync());
        Assert.DoesNotContain(await Db.Buyers.ToListAsync(), b => b.BuyerId == LocalBuyer.BuyerId);

        // Still a row, flagged — the order's FK never dangled.
        var hidden = await Db.Buyers.IgnoreQueryFilters().SingleAsync(b => b.BuyerId == LocalBuyer.BuyerId);
        Assert.True(hidden.IsDeleted);
        Assert.NotNull(hidden.DeletedAt);

        // ...and the history stays intact and reachable by raw join, which is the whole point
        // of soft-deleting instead of removing the row.
        Assert.Equal("1", await ReadScalarAsync(
            """
            SELECT COUNT(*) FROM sales_orders o
            JOIN buyers b ON b.buyer_id = o.buyer_id
            WHERE o.buyer_id = @buyerId
            """,
            Param("buyerId", LocalBuyer.BuyerId)));

        Assert.Single(await Db.SalesOrders.AsNoTracking()
            .Where(o => o.BuyerId == LocalBuyer.BuyerId).ToListAsync());
    }

    // ---------- 5. Enum storage form ----------

    [Fact]
    public async Task StatusEnums_RoundTripThroughTheirLowercaseTextForm()
    {
        var order = await SeedSalesOrderAsync(LocalBuyer, 10m, 100m, status: SalesOrderStatus.PendingPlanApproval);
        var export = await SeedExportOrderAsync(ExportBuyer, status: ExportOrderStatus.PendingApproval);
        var plan = await SeedPlanAsync(CommercialPlanStatus.RevisionRequested, CommercialRoute.Export);
        var request = await SeedMaterialRequestAsync(LocalBuyer, "PCB", 5m, MaterialRequestStatus.PlanGenerated);
        var action = await SeedApprovalActionAsync(plan.CommercialPlanId, ApprovalActionType.RevisionRequested);
        var pricing = await SeedApprovedPriceAsync("Copper", 100m);
        Db.RevenueTransactions.Add(Revenue(RevenueType.LocalSale, order.SalesOrderId, 100m));
        await Db.SaveChangesAsync();

        // The text on disk must be exactly what each CHECK constraint admits — the value
        // converters and the constraints are two halves of one contract, and neither one
        // validates the other at compile time.
        Assert.Equal("pendingplanapproval", await ReadScalarAsync(
            "SELECT status FROM sales_orders WHERE sales_order_id = @id", Param("id", order.SalesOrderId)));
        Assert.Equal("pendingapproval", await ReadScalarAsync(
            "SELECT status FROM export_orders WHERE export_order_id = @id", Param("id", export.ExportOrderId)));
        Assert.Equal("revisionrequested", await ReadScalarAsync(
            "SELECT status FROM commercial_plans WHERE commercial_plan_id = @id", Param("id", plan.CommercialPlanId)));
        Assert.Equal("export", await ReadScalarAsync(
            "SELECT recommended_route FROM commercial_plans WHERE commercial_plan_id = @id", Param("id", plan.CommercialPlanId)));
        Assert.Equal("plangenerated", await ReadScalarAsync(
            "SELECT status FROM material_requests WHERE material_request_id = @id", Param("id", request.MaterialRequestId)));
        Assert.Equal("revisionrequested", await ReadScalarAsync(
            "SELECT action_type FROM approval_actions WHERE approval_action_id = @id", Param("id", action.ApprovalActionId)));
        Assert.Equal("active", await ReadScalarAsync(
            "SELECT status FROM buyers WHERE buyer_id = @id", Param("id", LocalBuyer.BuyerId)));
        Assert.Equal("local", await ReadScalarAsync(
            "SELECT buyer_type FROM buyers WHERE buyer_id = @id", Param("id", LocalBuyer.BuyerId)));
        Assert.Equal("approved", await ReadScalarAsync(
            "SELECT status FROM material_pricing WHERE pricing_id = @id", Param("id", pricing.PricingId)));
        Assert.Equal("localsale", await ReadScalarAsync(
            "SELECT transaction_type FROM revenue_transactions LIMIT 1"));

        // And reading back through EF must land on the same enum members.
        Db.ChangeTracker.Clear();
        Assert.Equal(SalesOrderStatus.PendingPlanApproval,
            (await Db.SalesOrders.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(ExportOrderStatus.PendingApproval,
            (await Db.ExportOrders.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(MaterialRequestStatus.PlanGenerated,
            (await Db.MaterialRequests.AsNoTracking().SingleAsync()).Status);
        var reloadedPlan = await Db.CommercialPlans.AsNoTracking().SingleAsync();
        Assert.Equal(CommercialPlanStatus.RevisionRequested, reloadedPlan.Status);
        Assert.Equal(CommercialRoute.Export, reloadedPlan.RecommendedRoute);
        Assert.Equal(ApprovalActionType.RevisionRequested,
            (await Db.ApprovalActions.AsNoTracking().SingleAsync()).ActionType);
        // The base class seeds two buyers (Local + Export), so target LocalBuyer explicitly.
        var reloadedBuyer = await Db.Buyers.AsNoTracking().SingleAsync(b => b.BuyerId == LocalBuyer.BuyerId);
        Assert.Equal(BuyerStatus.Active, reloadedBuyer.Status);
        Assert.Equal(BuyerType.Local, reloadedBuyer.BuyerType);
        Assert.Equal(PricingStatus.Approved, (await Db.MaterialPricings.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(RevenueType.LocalSale, (await Db.RevenueTransactions.AsNoTracking().SingleAsync()).TransactionType);
    }

    // ---------- Seeding helpers ----------

    private SalesOrder NewSalesOrder(
        Buyer buyer, int lines = 0, SalesOrderStatus? status = null, Guid? materialRequestId = null)
    {
        var order = new SalesOrder
        {
            BuyerId = buyer.BuyerId,
            MaterialRequestId = materialRequestId,
            Status = status ?? SalesOrderStatus.Draft,
            CreatedByUserId = StaffUser.UserId
        };
        for (var i = 0; i < lines; i++)
        {
            order.Items.Add(new SalesOrderItem
            {
                RecoveredMaterialId = Guid.NewGuid(),
                MaterialType = "Copper",
                QuantityKg = 10m,
                UnitPrice = 100m,
                LineTotal = 1000m
            });
        }
        return order;
    }

    private async Task<SalesOrder> SeedSalesOrderAsync(
        Buyer buyer,
        decimal quantityKg = 10m,
        decimal unitPrice = 100m,
        int lines = 1,
        SalesOrderStatus? status = null,
        Guid? materialRequestId = null)
    {
        var order = NewSalesOrder(buyer, lines, status, materialRequestId);
        if (lines == 0)
        {
            order.Items.Add(new SalesOrderItem
            {
                RecoveredMaterialId = Guid.NewGuid(),
                MaterialType = "Copper",
                QuantityKg = quantityKg,
                UnitPrice = unitPrice,
                LineTotal = quantityKg * unitPrice
            });
        }
        order.TotalAmount = order.Items.Sum(i => i.LineTotal);
        Db.SalesOrders.Add(order);
        await Db.SaveChangesAsync();
        return order;
    }

    private async Task<ExportOrder> SeedExportOrderAsync(
        Buyer buyer, int lines = 1, ExportOrderStatus? status = null)
    {
        var order = new ExportOrder
        {
            BuyerId = buyer.BuyerId,
            DestinationCountry = "IN",
            ShipmentDate = MaterialPricingPolicy.Today.AddDays(7),
            Status = status ?? ExportOrderStatus.Draft,
            CreatedByUserId = StaffUser.UserId
        };
        for (var i = 0; i < lines; i++)
        {
            order.Items.Add(new ExportOrderItem
            {
                RecoveredMaterialId = Guid.NewGuid(),
                MaterialType = "Copper",
                QuantityKg = 25m,
                UnitPrice = 1000m,
                LineTotal = 25000m
            });
        }
        order.TotalWeightKg = order.Items.Sum(i => i.QuantityKg);
        order.TotalValue = order.Items.Sum(i => i.LineTotal);
        Db.ExportOrders.Add(order);
        await Db.SaveChangesAsync();
        return order;
    }

    private async Task<MaterialRequest> SeedMaterialRequestAsync(
        Buyer buyer, string materialType, decimal quantityKg,
        MaterialRequestStatus status = MaterialRequestStatus.Waiting)
    {
        var request = new MaterialRequest
        {
            BuyerId = buyer.BuyerId,
            MaterialType = materialType,
            QuantityKg = quantityKg,
            Status = status
        };
        Db.MaterialRequests.Add(request);
        await Db.SaveChangesAsync();
        return request;
    }

    private async Task<CommercialPlan> SeedPlanAsync(
        CommercialPlanStatus status = CommercialPlanStatus.PendingApproval,
        CommercialRoute route = CommercialRoute.LocalSale,
        int withActions = 0)
    {
        var plan = new CommercialPlan
        {
            WorkflowId = Guid.NewGuid(),
            RecommendedRoute = route,
            SelectedBuyerId = LocalBuyer.BuyerId,
            DestinationCountry = route == CommercialRoute.Export ? "IN" : null,
            MaterialsJson = """[{"materialType":"Copper","quantityKg":25.0}]""",
            ExpectedRevenue = 25000m,
            EstimatedCosts = 2000m,
            EstimatedNetValue = 23000m,
            ReasoningSummary = "Copper prices above the export break-even threshold.",
            ApprovalRequired = true,
            RiskFlags = """["export_quota_near_limit"]""",
            Status = status
        };
        for (var i = 0; i < withActions; i++)
        {
            plan.ApprovalActions.Add(new ApprovalAction
            {
                ActionType = ApprovalActionType.Submitted,
                PerformedByUserId = StaffUser.UserId,
                Comments = $"step {i}"
            });
        }
        Db.CommercialPlans.Add(plan);
        await Db.SaveChangesAsync();
        return plan;
    }

    private async Task<ApprovalAction> SeedApprovalActionAsync(Guid planId, ApprovalActionType type)
    {
        var action = new ApprovalAction
        {
            CommercialPlanId = planId,
            ActionType = type,
            PerformedByUserId = StaffUser.UserId,
            Comments = "Needs a cheaper shipping quote."
        };
        Db.ApprovalActions.Add(action);
        await Db.SaveChangesAsync();
        return action;
    }

    private RevenueTransaction Revenue(RevenueType type, Guid referenceId, decimal amount, Guid? recorderId = null)
        => new()
        {
            TransactionType = type,
            ReferenceId = referenceId,
            Amount = amount,
            RecordedByUserId = recorderId ?? StaffUser.UserId
        };

    // ---------- Raw ADO.NET plumbing ----------
    //
    // Bypasses EF on purpose: the entity model refuses to express some of these rows
    // (an unknown status cannot even be constructed), so the only way to prove the
    // *database* rejects them is to send the SQL ourselves.

    private static SqliteParameter Param(string name, object value) => new(name, value);

    private DbConnection Connection => Db.Database.GetDbConnection();

    private async Task<int> ExecuteAsync(string sql, params SqliteParameter[] parameters)
    {
        await using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync();
    }

    private async Task<string> ReadScalarAsync(string sql, params SqliteParameter[] parameters)
    {
        await using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
    }

    /// <summary>Asserts the statement was refused by a CHECK constraint, not by broken SQL.</summary>
    private async Task AssertConstraintFailsAsync(string sql, params SqliteParameter[] parameters)
    {
        var ex = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(sql, parameters));
        Assert.Contains("CHECK constraint failed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task AssertForeignKeyFailsAsync(string sql, params SqliteParameter[] parameters)
    {
        var ex = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(sql, parameters));
        Assert.Contains("FOREIGN KEY constraint failed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Asserts a unique index rejected the batch, then detaches the failed entries so the
    /// context is clean enough to keep testing on.
    /// </summary>
    private async Task AssertUniqueViolationAsync(Func<Task> saveChanges)
    {
        await Assert.ThrowsAsync<DbUpdateException>(saveChanges);
        Db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Same, for a raw INSERT: proves the index itself refuses the row, with EF entirely
    /// out of the picture.
    /// </summary>
    private async Task AssertUniqueViolationAsync(string sql, params SqliteParameter[] parameters)
    {
        var ex = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(sql, parameters));
        Assert.Contains("UNIQUE constraint failed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}