using EWasteManagement.Api.Entities;
using EWasteManagement.API.Features.Auth.Entities;
using EWasteManagement.API.Features.Collection.Entities;
using EWasteManagement.API.Features.Collection.Services;
using EWasteManagement.API.Features.Processing.Entities;
using EWasteManagement.API.Features.Processing.Services;
using EWasteManagement.API.Infrastructure.Persistence;
using EWasteManagement.Tests.TestHelpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EWasteManagement.Tests.Collection;

/// <summary>
/// The collector's extra job info: customer contact only between accepting and finishing a job, and
/// the payment — an estimate with the warehouse formula until the real payment exists.
/// </summary>
public class CollectorJobInfoServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ApplicationDbContext _db = null!;
    private User _collectorUser = null!;
    private Collector _collector = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options,
            new NoOpDomainEventDispatcher());
        await _db.Database.EnsureCreatedAsync(); // seeds GeneralCollection at Rs. 20/kg

        _collectorUser = new User { Email = $"{Guid.NewGuid()}@test.com", FullName = "Driver", PasswordHash = "x", Role = UserRole.Collector };
        _collector = new Collector { UserId = _collectorUser.UserId, VehicleType = "Van", CapacityKg = 500 };
        _db.Users.Add(_collectorUser);
        _db.Collectors.Add(_collector);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync() { await _db.DisposeAsync(); await _connection.DisposeAsync(); }

    private CollectorJobInfoService Service()
    {
        var rates = new RatePolicyLookupService(_db);
        return new CollectorJobInfoService(_db, new IPaymentCalculator[] { new JobPaymentCalculator(rates), new ExtraWastePaymentCalculator(rates) });
    }

    private async Task<Job> SeedJobAsync(JobStatus status, decimal? requiredKg = 10m, decimal? distanceKm = 4m, Guid? collectorId = null)
    {
        var owner = new User { Email = $"{Guid.NewGuid()}@test.com", FullName = "Kamal Perera", Phone = "0110000000", PasswordHash = "x", Role = UserRole.Household };
        var submission = new Submission { UserId = owner.UserId, PickupAddress = "No 1", PhoneNumber = "0771234567", EstimatedWeight = 8m };
        var job = new Job
        {
            SubmissionId = submission.Id,
            CollectorId = collectorId ?? _collector.CollectorId,
            Status = status,
            PickupAddress = "No 1",
            RequiredCapacityKg = requiredKg,
            EstimatedDistanceKm = distanceKm
        };
        _db.Users.Add(owner);
        _db.Submissions.Add(submission);
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    [Fact]
    public async Task OfferedJob_HidesTheCustomerContact()
    {
        var job = await SeedJobAsync(JobStatus.Assigned);

        var info = await Service().GetAsync(job.JobId, _collectorUser.UserId);

        Assert.False(info!.ContactAvailable);
        Assert.Null(info.CustomerName);
        Assert.Null(info.CustomerPhone);
    }

    [Theory]
    [InlineData(JobStatus.Accepted)]
    [InlineData(JobStatus.InProgress)]
    public async Task AcceptedJob_ShowsTheNameAndTheSubmissionPhone(JobStatus status)
    {
        var job = await SeedJobAsync(status);

        var info = await Service().GetAsync(job.JobId, _collectorUser.UserId);

        Assert.True(info!.ContactAvailable);
        Assert.Equal("Kamal Perera", info.CustomerName);
        Assert.Equal("0771234567", info.CustomerPhone); // the pickup's number, not the account's
    }

    [Fact]
    public async Task CompletedJob_HidesTheContactAgain()
    {
        var job = await SeedJobAsync(JobStatus.Completed);

        var info = await Service().GetAsync(job.JobId, _collectorUser.UserId);

        Assert.False(info!.ContactAvailable);
        Assert.Null(info.CustomerPhone);
    }

    [Fact]
    public async Task NotYetReceived_EstimatesWithTheWarehouseFormula()
    {
        var job = await SeedJobAsync(JobStatus.Accepted, requiredKg: 10m, distanceKm: 4m);

        var info = await Service().GetAsync(job.JobId, _collectorUser.UserId);

        // 200 base + 10 kg × 20 + 4 km × 15
        Assert.True(info!.PaymentIsEstimate);
        Assert.Equal(460m, info.PaymentAmount);
        Assert.Equal(10m, info.EstimateWeightKg);
        Assert.Equal(200m, info.WeightAmount);
        Assert.Equal(60m, info.DistanceAmount);
    }

    [Fact]
    public async Task Estimate_UsesTheMeasuredWeightOnceCollected()
    {
        var job = await SeedJobAsync(JobStatus.Completed, requiredKg: 10m, distanceKm: 0m);
        job.MeasuredWeightKg = 12m;
        await _db.SaveChangesAsync();

        var info = await Service().GetAsync(job.JobId, _collectorUser.UserId);

        Assert.Equal(12m, info!.EstimateWeightKg);
        Assert.Equal(440m, info.PaymentAmount); // 200 + 12 × 20
    }

    [Fact]
    public async Task Received_ShowsTheRealPaymentInsteadOfAnEstimate()
    {
        var job = await SeedJobAsync(JobStatus.Completed);
        _db.CollectorPayments.Add(new CollectorPayment
        {
            SourceType = PaymentSourceType.Job,
            SourceId = job.JobId,
            CollectorId = _collector.CollectorId,
            Amount = 512.50m,
            Status = PaymentStatus.Paid
        });
        await _db.SaveChangesAsync();

        var info = await Service().GetAsync(job.JobId, _collectorUser.UserId);

        Assert.False(info!.PaymentIsEstimate);
        Assert.Equal(512.50m, info.PaymentAmount);
        Assert.Equal("Paid", info.PaymentStatus);
    }

    [Fact]
    public async Task SomeoneElsesJob_IsRefused()
    {
        var otherUser = new User { Email = $"{Guid.NewGuid()}@test.com", FullName = "Other", PasswordHash = "x", Role = UserRole.Collector };
        var other = new Collector { UserId = otherUser.UserId, VehicleType = "Bike", CapacityKg = 20 };
        _db.Users.Add(otherUser);
        _db.Collectors.Add(other);
        await _db.SaveChangesAsync();
        var job = await SeedJobAsync(JobStatus.Accepted, collectorId: other.CollectorId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service().GetAsync(job.JobId, _collectorUser.UserId));
    }
}
