using System.Text.Json;
using EWasteManagement.Api.Entities;
using EWasteManagement.API.Features.Auth.Entities;
using EWasteManagement.API.Features.Collection.Entities;
using EWasteManagement.API.Features.Notifications.Services;
using EWasteManagement.API.Features.Processing.DTOs;
using EWasteManagement.API.Features.Processing.Entities;
using EWasteManagement.API.Features.Processing.Exceptions;
using EWasteManagement.API.Features.Processing.Services;
using EWasteManagement.API.Features.Workflow.DTOs;
using EWasteManagement.API.Features.Workflow.Entities;
using EWasteManagement.API.Infrastructure.Persistence;
using EWasteManagement.Tests.TestHelpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EWasteManagement.Tests.Processing;

/// <summary>
/// Receiving a job item by item: each submission item / CSV row the collector brought becomes its own
/// inventory item (a lot when its quantity is above 1), items not brought are recorded on the
/// payment, and the job still gets one flat-rate payment on the total verified weight.
/// </summary>
public class PerItemJobReceivingTests : IAsyncLifetime
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
        await _db.Database.EnsureCreatedAsync(); // seeds Battery, Laptop, Mobile Phone, GeneralCollection (Rs. 20/kg)…

        var location = new WarehouseLocation { Name = "Receiving Bay" };
        var user = new User { Email = $"{Guid.NewGuid()}@test.com", FullName = "Driver", PasswordHash = "x", Role = UserRole.Collector };
        var collector = new Collector { UserId = user.UserId, VehicleType = "Van", CapacityKg = 500 };
        _db.AddRange(location, user, collector);
        await _db.SaveChangesAsync();
        _locationId = location.Id;
        _collectorId = collector.CollectorId;
    }

    public async Task DisposeAsync() { await _db.DisposeAsync(); await _connection.DisposeAsync(); }

    private JobReceiptService Service()
    {
        var rates = new RatePolicyLookupService(_db);
        var payments = new CollectorPaymentService(_db, new IPaymentCalculator[] { new JobPaymentCalculator(rates), new ExtraWastePaymentCalculator(rates) });
        return new JobReceiptService(_db, new JobVerificationService(_db), payments, new ItemTypeCatalogService(_db), new NotificationService(_db));
    }

    private sealed record Seeded(Guid JobId, List<SubmissionItem> Items);

    private async Task<Seeded> SeedJobAsync(string category, params (string Name, int Qty, decimal? UnitKg, string? Hint)[] items)
    {
        var submission = new Submission
        {
            UserId = Guid.NewGuid(), Category = category, PickupAddress = "1 Test Street",
            Source = items.Any(i => i.Qty > 1) ? "Csv" : "Manual",
            Items = items.Select((i, index) => new SubmissionItem
            {
                Position = index, ItemName = i.Name, Quantity = i.Qty, EstimatedWeightKg = i.UnitKg, CategoryHint = i.Hint,
            }).ToList(),
        };
        var job = new Job
        {
            SubmissionId = submission.Id, CollectorId = _collectorId, Status = JobStatus.Completed,
            PickupAddress = "1 Test Street", MeasuredWeightKg = 30m, EstimatedDistanceKm = 4m, CompletedAt = DateTime.UtcNow,
        };
        _db.Submissions.Add(submission);
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return new Seeded(job.JobId, submission.Items.OrderBy(i => i.Position).ToList());
    }

    private ReceiveDeliveryRequest Delivery(Guid jobId, params DeliveryItemLine[] items) => new()
    {
        CollectorId = _collectorId,
        WarehouseLocationId = _locationId,
        Jobs = { new DeliveryJobLine { JobId = jobId, Items = items.ToList() } },
    };

    private static DeliveryItemLine Line(SubmissionItem item, int received, string? type, decimal kg) =>
        new() { SubmissionItemId = item.Id, ReceivedQuantity = received, ItemType = type, VerifiedWeightKg = kg };

    [Fact]
    public async Task Each_item_brought_becomes_its_own_inventory_item_and_a_lot_keeps_its_quantity()
    {
        var job = await SeedJobAsync("Other",
            ("Dell laptop", 50, 2.5m, "IT Equipment"), ("UPS battery", 10, 12m, "Batteries"), ("Router", 1, null, null));

        var result = await Service().ReceiveDeliveryAsync(Delivery(job.JobId,
            Line(job.Items[0], 50, "laptop", 120m),     // case-insensitive → the list's spelling
            Line(job.Items[1], 8, "Battery", 96m)), Guid.NewGuid());   // 2 short; the router isn't listed at all

        var stored = await _db.InventoryItems.Where(i => i.JobId == job.JobId).OrderBy(i => i.ItemType).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.Equal(("Battery", 8, 96m), (stored[0].ItemType, stored[0].Quantity, stored[0].VerifiedWeightKg));
        Assert.Equal(("Laptop", 50, 120m), (stored[1].ItemType, stored[1].Quantity, stored[1].VerifiedWeightKg));
        Assert.All(stored, i => Assert.NotNull(i.SubmissionItemId));

        var jobResult = Assert.Single(result.Jobs);
        Assert.Equal(216m, jobResult.VerifiedWeightKg);
        Assert.Equal((61, 58), (jobResult.ExpectedQuantity, jobResult.ReceivedQuantity));
        Assert.Equal(3, jobResult.Items.Count);
        Assert.Null(jobResult.Items[2].InventoryItemId); // the router: recorded, nothing stored
    }

    [Fact]
    public async Task One_flat_rate_payment_per_job_on_the_total_weight_with_the_shortfall_recorded()
    {
        var job = await SeedJobAsync("Other", ("Dell laptop", 50, 2.5m, null), ("Router", 1, null, null));

        var result = await Service().ReceiveDeliveryAsync(Delivery(job.JobId, Line(job.Items[0], 50, "Laptop", 120m)), Guid.NewGuid());

        var payment = Assert.Single(await _db.CollectorPayments.ToListAsync());
        // 200 base + 120 kg × 20 (GeneralCollection, the flat rate — not Laptop's own rate) + 4 km × 15
        Assert.Equal(200m + 120m * 20m + 4m * 15m, payment.Amount);
        Assert.Equal(payment.Amount, result.Jobs[0].PaymentAmount);

        var snapshot = PaymentSnapshotSerializer.TryDeserialize(payment.CalculationSnapshot)!;
        Assert.Equal(51, snapshot.Job!.ExpectedQuantity);
        Assert.Equal(50, snapshot.Job.ReceivedQuantity);
        Assert.Equal(new[] { "Router (0 of 1)" }, snapshot.Job.NotReceived);
    }

    [Fact]
    public async Task A_received_job_is_no_longer_offered_and_cannot_be_received_twice()
    {
        var job = await SeedJobAsync("Other", ("Laptop", 1, null, null));
        await Service().ReceiveDeliveryAsync(Delivery(job.JobId, Line(job.Items[0], 1, "Laptop", 2m)), Guid.NewGuid());

        Assert.DoesNotContain(await Service().GetReceivableJobsAsync(), j => j.JobId == job.JobId);
        await Assert.ThrowsAsync<DuplicateJobReceiptException>(() =>
            Service().ReceiveDeliveryAsync(Delivery(job.JobId, Line(job.Items[0], 1, "Laptop", 2m)), Guid.NewGuid()));
    }

    [Fact]
    public async Task A_job_with_items_cannot_be_received_as_one_whole_item()
    {
        var job = await SeedJobAsync("Other", ("Laptop", 1, null, null));
        var request = new ReceiveDeliveryRequest
        {
            CollectorId = _collectorId, WarehouseLocationId = _locationId,
            Jobs = { new DeliveryJobLine { JobId = job.JobId, VerifiedWeightKg = 2m, ItemType = "Laptop" } },
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Service().ReceiveDeliveryAsync(request, Guid.NewGuid()));
        Assert.Contains("item by item", ex.Message);
    }

    [Theory]
    [InlineData(2, "Laptop", 2.0, "the submission lists 1")]      // more than submitted → extra waste
    [InlineData(1, null, 2.0, "choose the item type")]
    [InlineData(1, "Spaceship", 2.0, "not a known item type")]
    [InlineData(1, "Laptop", 0.0, "enter the verified weight")]
    public async Task Bad_item_lines_are_refused_and_nothing_is_saved(int received, string? type, double kg, string message)
    {
        var job = await SeedJobAsync("Other", ("Laptop", 1, null, null));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Service().ReceiveDeliveryAsync(Delivery(job.JobId, Line(job.Items[0], received, type, (decimal)kg)), Guid.NewGuid()));

        Assert.Contains(message, ex.Message);
        Assert.Equal(0, await _db.InventoryItems.CountAsync());
        Assert.Equal(0, await _db.CollectorPayments.CountAsync());
    }

    [Fact]
    public async Task Receiving_nothing_from_a_job_is_refused()
    {
        var job = await SeedJobAsync("Other", ("Laptop", 1, null, null));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service().ReceiveDeliveryAsync(Delivery(job.JobId, Line(job.Items[0], 0, null, 0m)), Guid.NewGuid()));
    }

    [Fact]
    public async Task An_item_from_another_submission_is_refused()
    {
        var job = await SeedJobAsync("Other", ("Laptop", 1, null, null));
        var other = await SeedJobAsync("Other", ("Phone", 1, null, null));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service().ReceiveDeliveryAsync(Delivery(job.JobId, Line(other.Items[0], 1, "Mobile Phone", 0.2m)), Guid.NewGuid()));
    }

    [Fact]
    public async Task Receivable_jobs_list_their_items_with_a_suggested_type_and_where_it_came_from()
    {
        var job = await SeedJobAsync("Household Electronics",
            ("Old Dell laptop", 1, null, null),          // name
            ("Power supply unit", 3, 1.5m, "Batteries"), // CSV category hint
            ("Grey box", 1, null, null),                 // the Analyzer's category for this item
            ("Thing", 1, null, null));                   // nothing specific → the submission's category
        _db.CollectionWorkflows.Add(new CollectionWorkflow
        {
            SubmissionId = job.Items[0].SubmissionId,
            AnalyzerResultJson = JsonSerializer.Serialize(new AnalyzerResultRequest
            {
                Items = new()
                {
                    new() { WasteCategory = "IT Equipment", EstimatedVolumeKg = 2.4m },
                    new() { WasteCategory = "Batteries", EstimatedVolumeKg = 5m },
                    new() { WasteCategory = "Mobile Phones", EstimatedVolumeKg = 0.3m },
                    new() { WasteCategory = "Uncategorized" },
                },
            }),
        });
        await _db.SaveChangesAsync();

        var items = (await Service().GetReceivableJobsAsync()).Single(j => j.JobId == job.JobId).Items;

        Assert.Equal(new[] { "Old Dell laptop", "Power supply unit", "Grey box", "Thing" }, items.Select(i => i.ItemName));
        Assert.Equal(("Laptop", "name"), (items[0].SuggestedItemType, items[0].SuggestionSource));
        Assert.Equal(("Battery", "category"), (items[1].SuggestedItemType, items[1].SuggestionSource));
        Assert.Equal(("Mobile Phone", "ai"), (items[2].SuggestedItemType, items[2].SuggestionSource));
        Assert.Equal(("General Household Electronics", "submission"), (items[3].SuggestedItemType, items[3].SuggestionSource));

        Assert.Equal(3, items[1].Quantity);
        Assert.Equal(4.5m, items[1].ExpectedWeightKg); // CSV 1.5 kg/unit × 3 beats the AI's 5 kg
        Assert.Equal(2.4m, items[0].ExpectedWeightKg); // no CSV weight → the AI's estimate
    }
}
