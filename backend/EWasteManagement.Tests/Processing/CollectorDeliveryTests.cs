using EWasteManagement.Api.Entities;
using EWasteManagement.API.Features.Auth.Entities;
using EWasteManagement.API.Features.Collection.Entities;
using EWasteManagement.API.Features.Notifications.Services;
using EWasteManagement.API.Features.Processing.DTOs;
using EWasteManagement.API.Features.Processing.Entities;
using EWasteManagement.API.Features.Processing.Exceptions;
using EWasteManagement.API.Features.Processing.Services;
using EWasteManagement.API.Infrastructure.Persistence;
using EWasteManagement.Tests.TestHelpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EWasteManagement.Tests.Processing;

/// <summary>
/// A collector bringing several completed jobs in one visit: each job keeps its own item and payment
/// (same formula as a single job), the payments are grouped under one delivery, and it is all-or-nothing.
/// </summary>
public class CollectorDeliveryTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ApplicationDbContext _db = null!;
    private Guid _locationId;
    private Guid _collectorId;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options,
            new NoOpDomainEventDispatcher());
        await _db.Database.EnsureCreatedAsync();

        var location = new WarehouseLocation { Name = "Receiving Bay" };
        var user = new User { Email = $"{Guid.NewGuid()}@test.com", FullName = "Test Collector", PasswordHash = "x", Role = UserRole.Collector };
        var collector = new Collector { UserId = user.UserId, VehicleType = "Van", CapacityKg = 500 };
        _db.WarehouseLocations.Add(location);
        _db.Users.Add(user);
        _db.Collectors.Add(collector);
        await _db.SaveChangesAsync();
        _locationId = location.Id;
        _collectorId = collector.CollectorId;
    }

    public async Task DisposeAsync() { await _db.DisposeAsync(); await _connection.DisposeAsync(); }

    private sealed class PerJobVerification : IJobVerificationService
    {
        private readonly Dictionary<Guid, JobVerificationResult> _jobs;
        public PerJobVerification(Dictionary<Guid, JobVerificationResult> jobs) => _jobs = jobs;
        public Task<JobVerificationResult> VerifyAsync(Guid jobId, CancellationToken cancellationToken = default)
            => Task.FromResult(_jobs.GetValueOrDefault(jobId) ?? new JobVerificationResult(false, false, null));
    }

    private CollectorPaymentService PaymentService()
    {
        var rates = new RatePolicyLookupService(_db);
        return new CollectorPaymentService(_db, new IPaymentCalculator[] { new JobPaymentCalculator(rates), new ExtraWastePaymentCalculator(rates) });
    }

    private JobReceiptService Service(Dictionary<Guid, JobVerificationResult> jobs)
        => new(_db, new PerJobVerification(jobs), PaymentService(), new ItemTypeCatalogService(_db), new NotificationService(_db));

    private JobVerificationResult Completed(decimal distanceKm) => new(true, true, null, DistanceKm: distanceKm, CollectorId: _collectorId);

    [Fact]
    public async Task ReceiveDelivery_SeveralJobs_OnePaymentEach_GroupedWithTheTotal()
    {
        var jobA = Guid.NewGuid();
        var jobB = Guid.NewGuid();
        var service = Service(new() { [jobA] = Completed(5m), [jobB] = Completed(0m) });

        var result = await service.ReceiveDeliveryAsync(new ReceiveDeliveryRequest
        {
            CollectorId = _collectorId,
            WarehouseLocationId = _locationId,
            Jobs =
            {
                new DeliveryJobLine { JobId = jobA, VerifiedWeightKg = 9.5m, ItemType = "Laptop" },
                new DeliveryJobLine { JobId = jobB, VerifiedWeightKg = 10m, ItemType = "Laptop" }
            }
        }, Guid.NewGuid());

        // Per job, unchanged formula: 200 + kg * 20 + km * 15 → 465 and 400.
        Assert.Equal(new[] { 465m, 400m }, result.Jobs.Select(j => j.PaymentAmount));
        Assert.Equal(865m, result.TotalPendingAmount);
        Assert.Equal(2, await _db.InventoryItems.CountAsync());
        Assert.Equal(2, await _db.CollectorPayments.CountAsync(p => p.DeliveryId == result.DeliveryId));
    }

    [Fact]
    public async Task ReceiveDelivery_OneBadJob_NothingIsSaved()
    {
        var good = Guid.NewGuid();
        var notCompleted = Guid.NewGuid();
        var service = Service(new()
        {
            [good] = Completed(1m),
            [notCompleted] = new JobVerificationResult(true, false, null, CollectorId: _collectorId)
        });

        await Assert.ThrowsAsync<JobNotCompletedException>(() => service.ReceiveDeliveryAsync(new ReceiveDeliveryRequest
        {
            CollectorId = _collectorId,
            WarehouseLocationId = _locationId,
            Jobs =
            {
                new DeliveryJobLine { JobId = good, VerifiedWeightKg = 2m, ItemType = "Laptop" },
                new DeliveryJobLine { JobId = notCompleted, VerifiedWeightKg = 2m, ItemType = "Laptop" }
            }
        }, Guid.NewGuid()));

        _db.ChangeTracker.Clear();
        Assert.Equal(0, await _db.InventoryItems.CountAsync());
        Assert.Equal(0, await _db.CollectorPayments.CountAsync());
        Assert.Equal(0, await _db.CollectorDeliveries.CountAsync());
    }

    [Fact]
    public async Task ReceiveDelivery_JobOfAnotherCollector_IsRejected()
    {
        var job = Guid.NewGuid();
        var service = Service(new() { [job] = new JobVerificationResult(true, true, null, CollectorId: Guid.NewGuid()) });

        await Assert.ThrowsAsync<JobCollectorMismatchException>(() => service.ReceiveDeliveryAsync(new ReceiveDeliveryRequest
        {
            CollectorId = _collectorId,
            WarehouseLocationId = _locationId,
            Jobs = { new DeliveryJobLine { JobId = job, VerifiedWeightKg = 2m, ItemType = "Laptop" } }
        }, Guid.NewGuid()));
    }

    [Fact]
    public async Task PayDelivery_PaysEveryPendingPayment_AndRecordsWhoPaid()
    {
        var jobA = Guid.NewGuid();
        var jobB = Guid.NewGuid();
        var delivery = await Service(new() { [jobA] = Completed(0m), [jobB] = Completed(0m) }).ReceiveDeliveryAsync(new ReceiveDeliveryRequest
        {
            CollectorId = _collectorId,
            WarehouseLocationId = _locationId,
            Jobs =
            {
                new DeliveryJobLine { JobId = jobA, VerifiedWeightKg = 1m, ItemType = "Laptop" },
                new DeliveryJobLine { JobId = jobB, VerifiedWeightKg = 1m, ItemType = "Laptop" }
            }
        }, Guid.NewGuid());
        var payer = Guid.NewGuid();
        var payments = PaymentService();

        var summary = await payments.MarkDeliveryPaidAsync(delivery.DeliveryId, payer);

        Assert.Equal(0m, summary.PendingAmount);
        Assert.Equal(delivery.TotalPendingAmount, summary.PaidAmount);
        Assert.All(await _db.CollectorPayments.ToListAsync(), p =>
        {
            Assert.Equal(PaymentStatus.Paid, p.Status);
            Assert.Equal(payer, p.PaidByStaffId);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => payments.MarkDeliveryPaidAsync(delivery.DeliveryId, payer));
    }

    [Fact]
    public async Task PaymentDetail_ShowsItsDelivery()
    {
        var job = Guid.NewGuid();
        var delivery = await Service(new() { [job] = Completed(0m) }).ReceiveDeliveryAsync(new ReceiveDeliveryRequest
        {
            CollectorId = _collectorId,
            WarehouseLocationId = _locationId,
            Jobs = { new DeliveryJobLine { JobId = job, VerifiedWeightKg = 1m, ItemType = "Laptop" } }
        }, Guid.NewGuid());

        var detail = await PaymentService().GetDetailAsync(delivery.Jobs[0].PaymentId);

        Assert.Equal(delivery.DeliveryId, detail.Delivery?.DeliveryId);
        Assert.Equal("Test Collector", detail.Delivery?.CollectorName);
    }
}
