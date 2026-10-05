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

public class JobReceiptServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ApplicationDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        _db = new ApplicationDbContext(options, new NoOpDomainEventDispatcher());
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<Guid> SeedLocationAsync()
    {
        var location = new WarehouseLocation { Name = "Receiving Bay" };
        _db.WarehouseLocations.Add(location);
        await _db.SaveChangesAsync();
        return location.Id;
    }

    private async Task<Guid> SeedCollectorAsync()
    {
        var user = new User { Email = $"{Guid.NewGuid()}@test.com", FullName = "Test Collector", PasswordHash = "x", Role = UserRole.Collector };
        var collector = new Collector { UserId = user.UserId, VehicleType = "Van", CapacityKg = 500 };
        _db.Users.Add(user);
        _db.Collectors.Add(collector);
        await _db.SaveChangesAsync();
        return collector.CollectorId;
    }

    private JobReceiptService CreateService(JobVerificationResult verification)
    {
        var rates = new RatePolicyLookupService(_db);
        var calculators = new IPaymentCalculator[] { new JobPaymentCalculator(rates), new ExtraWastePaymentCalculator(rates) };
        var paymentService = new CollectorPaymentService(_db, calculators);
        return new JobReceiptService(_db, new FakeJobVerificationService(verification), paymentService, new ItemTypeCatalogService(_db), new NotificationService(_db));
    }

    [Fact]
    public async Task ReceiveAsync_CompletedJobWithDistance_CreatesInventoryItemAndPayment()
    {
        var locationId = await SeedLocationAsync();
        var collectorId = await SeedCollectorAsync();
        var service = CreateService(new JobVerificationResult(true, true, ReportedWeightKg: 10.0m, DistanceKm: 5m, CollectorId: collectorId));

        var result = await service.ReceiveAsync(new ReceiveJobWasteRequest
        {
            JobId = Guid.NewGuid(), CollectorId = collectorId,
            ItemType = "Laptop", WarehouseLocationId = locationId, VerifiedWeightKg = 9.5m
        }, Guid.NewGuid());

        Assert.Equal(-0.5m, result.DiscrepancyKg);

        var payment = await _db.CollectorPayments.SingleAsync(p => p.SourceId == result.JobId);
        // 200 base + 9.5kg * 20/kg (GeneralCollection) + 5km * 15/km = 200 + 190 + 75 = 465.00
        Assert.Equal(465.00m, payment.Amount);
    }

    [Fact]
    public async Task ReceiveAsync_JobNotCompleted_ThrowsAndCreatesNoInventoryItemOrPayment()
    {
        var locationId = await SeedLocationAsync();
        var collectorId = await SeedCollectorAsync();
        var service = CreateService(new JobVerificationResult(true, false, null));

        var request = new ReceiveJobWasteRequest
        {
            JobId = Guid.NewGuid(), CollectorId = collectorId,
            ItemType = "Laptop", WarehouseLocationId = locationId, VerifiedWeightKg = 5m
        };

        await Assert.ThrowsAsync<JobNotCompletedException>(() => service.ReceiveAsync(request, Guid.NewGuid()));
        Assert.Equal(0, await _db.InventoryItems.CountAsync());
        Assert.Equal(0, await _db.CollectorPayments.CountAsync());
    }

    [Fact]
    public async Task ReceiveAsync_SameJobTwice_SecondCallThrowsDuplicateException()
    {
        var locationId = await SeedLocationAsync();
        var collectorId = await SeedCollectorAsync();
        var service = CreateService(new JobVerificationResult(true, true, null, CollectorId: collectorId));
        var request = new ReceiveJobWasteRequest
        {
            JobId = Guid.NewGuid(), CollectorId = collectorId,
            ItemType = "Laptop", WarehouseLocationId = locationId, VerifiedWeightKg = 5m
        };

        await service.ReceiveAsync(request, Guid.NewGuid());

        await Assert.ThrowsAsync<DuplicateJobReceiptException>(() => service.ReceiveAsync(request, Guid.NewGuid()));
    }

    [Fact]
    public async Task ReceiveAsync_UnknownCollector_ThrowsNotFoundAndCreatesNoInventoryItemOrPayment()
    {
        var locationId = await SeedLocationAsync();
        var service = CreateService(new JobVerificationResult(true, true, null));

        var request = new ReceiveJobWasteRequest
        {
            JobId = Guid.NewGuid(), CollectorId = Guid.NewGuid(),
            ItemType = "Laptop", WarehouseLocationId = locationId, VerifiedWeightKg = 5m
        };

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ReceiveAsync(request, Guid.NewGuid()));
        Assert.Equal(0, await _db.InventoryItems.CountAsync());
        Assert.Equal(0, await _db.CollectorPayments.CountAsync());
    }

    // ---------------------------------------------------------------- collector must be the job's own

    [Fact]
    public async Task ReceiveAsync_CorrectCollector_IsAcceptedAndThePaymentGoesToThatCollector()
    {
        var locationId = await SeedLocationAsync();
        var collectorId = await SeedCollectorAsync();
        var service = CreateService(new JobVerificationResult(true, true, ReportedWeightKg: 10m, DistanceKm: 5m, CollectorId: collectorId));

        var result = await service.ReceiveAsync(new ReceiveJobWasteRequest
        {
            JobId = Guid.NewGuid(), CollectorId = collectorId, ItemType = "Laptop", WarehouseLocationId = locationId, VerifiedWeightKg = 10m
        }, Guid.NewGuid());

        Assert.Equal(1, await _db.InventoryItems.CountAsync());
        var payment = await _db.CollectorPayments.SingleAsync(p => p.SourceId == result.JobId);
        Assert.Equal(collectorId, payment.CollectorId);
    }

    [Fact]
    public async Task ReceiveAsync_WrongCollector_IsRejectedAndCreatesNothing()
    {
        var locationId = await SeedLocationAsync();
        var jobsCollector = await SeedCollectorAsync();
        var otherCollector = await SeedCollectorAsync(); // a real collector — but not the one assigned to the job
        var service = CreateService(new JobVerificationResult(true, true, ReportedWeightKg: 10m, DistanceKm: 5m, CollectorId: jobsCollector));

        var ex = await Assert.ThrowsAsync<JobCollectorMismatchException>(() => service.ReceiveAsync(new ReceiveJobWasteRequest
        {
            JobId = Guid.NewGuid(), CollectorId = otherCollector, ItemType = "Laptop", WarehouseLocationId = locationId, VerifiedWeightKg = 10m
        }, Guid.NewGuid()));

        Assert.Contains("not the collector assigned", ex.Message);
        Assert.Equal(0, await _db.InventoryItems.CountAsync());
        Assert.Equal(0, await _db.CollectorPayments.CountAsync());
    }

    [Fact]
    public async Task ReceiveAsync_JobWithNoAssignedCollector_IsRejectedAndTheCollectorIsNeverGuessed()
    {
        var locationId = await SeedLocationAsync();
        var someCollector = await SeedCollectorAsync();
        var service = CreateService(new JobVerificationResult(true, true, ReportedWeightKg: 10m, DistanceKm: 5m, CollectorId: null));

        var ex = await Assert.ThrowsAsync<JobCollectorMismatchException>(() => service.ReceiveAsync(new ReceiveJobWasteRequest
        {
            JobId = Guid.NewGuid(), CollectorId = someCollector, ItemType = "Laptop", WarehouseLocationId = locationId, VerifiedWeightKg = 10m
        }, Guid.NewGuid()));

        Assert.Contains("no assigned collector", ex.Message);
        Assert.Equal(0, await _db.InventoryItems.CountAsync());
        Assert.Equal(0, await _db.CollectorPayments.CountAsync());
    }

    // ---------------------------------------------------------------- snapshot + audit on the created payment

    [Fact]
    public async Task ReceiveAsync_SavesTheCalculationSnapshot_AndTheAuthenticatedStaffAsCreator()
    {
        var locationId = await SeedLocationAsync();
        var collectorId = await SeedCollectorAsync();
        var service = CreateService(new JobVerificationResult(true, true, ReportedWeightKg: 10.0m, DistanceKm: 5m, CollectorId: collectorId));
        var staffId = Guid.NewGuid();

        var result = await service.ReceiveAsync(new ReceiveJobWasteRequest
        {
            JobId = Guid.NewGuid(), CollectorId = collectorId, ItemType = "Laptop", WarehouseLocationId = locationId, VerifiedWeightKg = 9.5m
        }, staffId);

        var payment = await _db.CollectorPayments.SingleAsync(p => p.SourceId == result.JobId);
        Assert.Equal(staffId, payment.CreatedByStaffId);
        Assert.Null(payment.PaidByStaffId);

        var job = PaymentSnapshotSerializer.TryDeserialize(payment.CalculationSnapshot)!.Job!;
        Assert.Equal(9.5m, job.VerifiedWeightKg);
        Assert.Equal(10.0m, job.ReportedWeightKg);
        Assert.Equal(20m, job.RatePerKg);
        Assert.Equal(190m, job.WeightAmount);
        Assert.Equal(200m, job.BaseFee);
        Assert.Equal(5m, job.DistanceUsedKm);
        Assert.Equal(15m, job.DistanceRatePerKm);
        Assert.Equal(75m, job.DistanceAmount);
        Assert.Equal(465.00m, payment.Amount);
    }

    // ---------------------------------------------------------------- receivable jobs (server-side filter)

    private async Task<Job> SeedJobAsync(Guid? collectorId, JobStatus status = JobStatus.Completed, DateTime? completedAt = null)
    {
        var job = new Job
        {
            SubmissionId = Guid.NewGuid(), CollectorId = collectorId, Status = status, PickupAddress = "1 Test Street",
            MeasuredWeightKg = 12m, EstimatedDistanceKm = 4m, CompletedAt = completedAt ?? DateTime.UtcNow
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    [Fact]
    public async Task GetReceivableJobsAsync_ReturnsCompletedJobsWithACollector_NotYetReceived()
    {
        var collectorId = await SeedCollectorAsync();
        var open = await SeedJobAsync(collectorId);
        await SeedJobAsync(collectorId, JobStatus.Assigned);   // not completed
        await SeedJobAsync(null);                               // completed but no collector

        var result = await CreateService(new JobVerificationResult(true, true, null)).GetReceivableJobsAsync();

        var job = Assert.Single(result);
        Assert.Equal(open.JobId, job.JobId);
        Assert.Equal(collectorId, job.CollectorId);
        Assert.Equal("Test Collector", job.CollectorName);
        Assert.Equal("Van", job.CollectorVehicleType);
        Assert.Equal(12m, job.ReportedWeightKg);
    }

    [Fact]
    public async Task GetReceivableJobsAsync_ExcludesJobsAlreadyReceived_EvenOnALaterRequest()
    {
        var locationId = await SeedLocationAsync();
        var collectorId = await SeedCollectorAsync();
        var received = await SeedJobAsync(collectorId, completedAt: DateTime.UtcNow.AddDays(-1));
        var stillOpen = await SeedJobAsync(collectorId);

        var service = CreateService(new JobVerificationResult(true, true, ReportedWeightKg: 12m, DistanceKm: 4m, CollectorId: collectorId));
        await service.ReceiveAsync(new ReceiveJobWasteRequest
        {
            JobId = received.JobId, CollectorId = collectorId, ItemType = "Laptop", WarehouseLocationId = locationId, VerifiedWeightKg = 12m
        }, Guid.NewGuid());

        // A brand-new service instance is what a page refresh does: nothing is remembered in memory.
        var result = await CreateService(new JobVerificationResult(true, true, null)).GetReceivableJobsAsync();

        Assert.Equal(new[] { stillOpen.JobId }, result.Select(j => j.JobId));
    }

    [Fact]
    public async Task GetReceivableJobsAsync_NewestCompletionFirst()
    {
        var collectorId = await SeedCollectorAsync();
        var older = await SeedJobAsync(collectorId, completedAt: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = await SeedJobAsync(collectorId, completedAt: new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc));

        var result = await CreateService(new JobVerificationResult(true, true, null)).GetReceivableJobsAsync();

        Assert.Equal(new[] { newer.JobId, older.JobId }, result.Select(j => j.JobId));
    }

    // ---------------------------------------------------------------- item type (no more "Mixed Job Collection")

    private async Task<Guid> SeedSubmissionAsync(string category)
    {
        var submission = new Submission { UserId = Guid.NewGuid(), Category = category, PickupAddress = "1 Test Street" };
        _db.Submissions.Add(submission);
        await _db.SaveChangesAsync();
        return submission.Id;
    }

    [Fact]
    public async Task ReceiveAsync_ChosenItemType_IsStoredWithTheListsSpelling()
    {
        var locationId = await SeedLocationAsync();
        var collectorId = await SeedCollectorAsync();
        var service = CreateService(new JobVerificationResult(true, true, 5m, 2m, collectorId));

        var result = await service.ReceiveAsync(new ReceiveJobWasteRequest
        {
            JobId = Guid.NewGuid(), CollectorId = collectorId, WarehouseLocationId = locationId,
            VerifiedWeightKg = 5m, ItemType = "  mobile phone "
        }, Guid.NewGuid());

        Assert.Equal("Mobile Phone", result.ItemType);
        var item = await _db.InventoryItems.SingleAsync();
        Assert.Equal("Mobile Phone", item.ItemType);
    }

    [Fact]
    public async Task ReceiveAsync_NoItemType_UsesTheSubmissionCategory()
    {
        var locationId = await SeedLocationAsync();
        var collectorId = await SeedCollectorAsync();
        var submissionId = await SeedSubmissionAsync("laptop");
        var service = CreateService(new JobVerificationResult(true, true, 5m, 2m, collectorId, submissionId));

        var result = await service.ReceiveAsync(new ReceiveJobWasteRequest
        {
            JobId = Guid.NewGuid(), CollectorId = collectorId, WarehouseLocationId = locationId, VerifiedWeightKg = 5m
        }, Guid.NewGuid());

        Assert.Equal("Laptop", result.ItemType);
    }

    [Fact]
    public async Task ReceiveAsync_NoItemTypeAndCategoryNotOnTheList_IsRejectedAndCreatesNothing()
    {
        var locationId = await SeedLocationAsync();
        var collectorId = await SeedCollectorAsync();
        var submissionId = await SeedSubmissionAsync("Assorted junk");
        var service = CreateService(new JobVerificationResult(true, true, 5m, 2m, collectorId, submissionId));

        await Assert.ThrowsAsync<ArgumentException>(() => service.ReceiveAsync(new ReceiveJobWasteRequest
        {
            JobId = Guid.NewGuid(), CollectorId = collectorId, WarehouseLocationId = locationId, VerifiedWeightKg = 5m
        }, Guid.NewGuid()));

        Assert.Equal(0, await _db.InventoryItems.CountAsync());
        Assert.Equal(0, await _db.CollectorPayments.CountAsync());
    }

    [Theory]
    [InlineData("Unicorn Parts")]
    [InlineData("GeneralCollection")]
    public async Task ReceiveAsync_ItemTypeNotOnTheList_IsRejectedAndCreatesNothing(string itemType)
    {
        var locationId = await SeedLocationAsync();
        var collectorId = await SeedCollectorAsync();
        var service = CreateService(new JobVerificationResult(true, true, 5m, 2m, collectorId));

        await Assert.ThrowsAsync<ArgumentException>(() => service.ReceiveAsync(new ReceiveJobWasteRequest
        {
            JobId = Guid.NewGuid(), CollectorId = collectorId, WarehouseLocationId = locationId,
            VerifiedWeightKg = 5m, ItemType = itemType
        }, Guid.NewGuid()));

        Assert.Equal(0, await _db.InventoryItems.CountAsync());
        Assert.Equal(0, await _db.CollectorPayments.CountAsync());
    }

    [Fact]
    public async Task GetReceivableJobsAsync_SuggestsTheItemTypeFromTheSubmissionCategory()
    {
        var collectorId = await SeedCollectorAsync();
        var matching = await SeedJobAsync(collectorId, completedAt: DateTime.UtcNow);
        matching.SubmissionId = await SeedSubmissionAsync("battery");
        var unmatched = await SeedJobAsync(collectorId, completedAt: DateTime.UtcNow.AddMinutes(-1));
        unmatched.SubmissionId = await SeedSubmissionAsync("Assorted junk");
        await _db.SaveChangesAsync();

        var result = await CreateService(new JobVerificationResult(true, true, null)).GetReceivableJobsAsync();

        var first = result.Single(j => j.JobId == matching.JobId);
        Assert.Equal("battery", first.SubmissionCategory);
        Assert.Equal("Battery", first.SuggestedItemType);

        var second = result.Single(j => j.JobId == unmatched.JobId);
        Assert.Equal("Assorted junk", second.SubmissionCategory);
        Assert.Null(second.SuggestedItemType);
    }
}
