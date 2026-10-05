using System.Text.Json;
using EWasteManagement.API.Features.Collection.Entities;
using EWasteManagement.API.Features.Notifications.Entities;
using EWasteManagement.API.Features.Notifications.Services;
using EWasteManagement.API.Features.Processing.DTOs;
using EWasteManagement.API.Features.Processing.Entities;
using EWasteManagement.API.Features.Processing.Exceptions;
using EWasteManagement.API.Features.Workflow.DTOs;
using EWasteManagement.API.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EWasteManagement.API.Features.Processing.Services;

public class JobReceiptService : IJobReceiptService
{
    private readonly ApplicationDbContext _db;
    private readonly IJobVerificationService _jobVerification;
    private readonly ICollectorPaymentService _paymentService;
    private readonly IItemTypeCatalogService _itemTypes;
    private readonly INotificationService _notifications;

    public JobReceiptService(
        ApplicationDbContext db, IJobVerificationService jobVerification, ICollectorPaymentService paymentService,
        IItemTypeCatalogService itemTypes, INotificationService notifications)
    {
        _db = db;
        _jobVerification = jobVerification;
        _paymentService = paymentService;
        _itemTypes = itemTypes;
        _notifications = notifications;
    }
    
    // Single-job endpoint (no app uses it any more): receives a job as one whole item, which is only
    // allowed for a job whose submission has no items. Jobs with items go through ReceiveDeliveryAsync.
    public async Task<ReceiveJobWasteResponse> ReceiveAsync(
        ReceiveJobWasteRequest request, Guid receivedByStaffId, CancellationToken cancellationToken = default)
    {
        await EnsureLocationAndCollectorExistAsync(request.WarehouseLocationId, request.CollectorId, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var received = await ReceiveOneJobAsync(
            new DeliveryJobLine { JobId = request.JobId, VerifiedWeightKg = request.VerifiedWeightKg, ItemType = request.ItemType },
            request.CollectorId, request.WarehouseLocationId, receivedByStaffId, deliveryId: null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await NotifyCollectorOfReceiptAsync(request.CollectorId, 1, received.ReceivedQuantity, received.VerifiedWeightKg, received.PaymentAmount);

        var item = received.Items[0];
        return new ReceiveJobWasteResponse
        {
            InventoryItemId = item.InventoryItemId!.Value,
            JobId = received.JobId,
            ItemType = item.ItemType!,
            VerifiedWeightKg = received.VerifiedWeightKg,
            ReportedWeightKg = received.ReportedWeightKg,
            DiscrepancyKg = received.DiscrepancyKg,
            ReceivedAt = received.ReceivedAt
        };
    }

    // Several completed jobs brought by one collector in one visit. Each job is received exactly as a
    // single job would be (own item, own payment, same rules); all of it succeeds or none of it does.
    public async Task<ReceiveDeliveryResponse> ReceiveDeliveryAsync(
        ReceiveDeliveryRequest request, Guid receivedByStaffId, CancellationToken cancellationToken = default)
    {
        await EnsureLocationAndCollectorExistAsync(request.WarehouseLocationId, request.CollectorId, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var delivery = new CollectorDelivery
        {
            CollectorId = request.CollectorId,
            ReceivedByStaffId = receivedByStaffId,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };
        _db.CollectorDeliveries.Add(delivery);
        await _db.SaveChangesAsync(cancellationToken);

        var results = new List<DeliveryJobResult>();
        foreach (var line in request.Jobs)
        {
            var received = await ReceiveOneJobAsync(
                line, request.CollectorId, request.WarehouseLocationId, receivedByStaffId, delivery.Id, cancellationToken);
            results.Add(new DeliveryJobResult
            {
                JobId = received.JobId,
                VerifiedWeightKg = received.VerifiedWeightKg,
                ReportedWeightKg = received.ReportedWeightKg,
                DiscrepancyKg = received.DiscrepancyKg,
                ExpectedQuantity = received.ExpectedQuantity,
                ReceivedQuantity = received.ReceivedQuantity,
                Items = received.Items,
                PaymentId = received.PaymentId,
                PaymentAmount = received.PaymentAmount
            });
        }

        await transaction.CommitAsync(cancellationToken);
        await NotifyCollectorOfReceiptAsync(
            request.CollectorId, results.Count, results.Sum(r => r.ReceivedQuantity),
            results.Sum(r => r.VerifiedWeightKg), results.Sum(r => r.PaymentAmount));

        return new ReceiveDeliveryResponse
        {
            DeliveryId = delivery.Id,
            CollectorId = request.CollectorId,
            ReceivedAt = delivery.ReceivedAt,
            Jobs = results,
            TotalPendingAmount = results.Sum(r => r.PaymentAmount)
        };
    }

    // Collector bell: their load is off the vehicle and a payment is pending. Never throws — the
    // receipt is already committed.
    private async Task NotifyCollectorOfReceiptAsync(Guid collectorId, int jobCount, int itemCount, decimal weightKg, decimal paymentAmount)
    {
        try
        {
            var userId = await _db.Collectors
                .Where(c => c.CollectorId == collectorId)
                .Select(c => c.UserId)
                .FirstOrDefaultAsync();
            if (userId == default) return;

            await _notifications.NotifyAsync(
                userId,
                "Delivery received",
                $"The warehouse received {jobCount} job{(jobCount == 1 ? "" : "s")}: " +
                $"{itemCount} item{(itemCount == 1 ? "" : "s")}, {weightKg:0.##} kg. " +
                $"Payment of Rs. {paymentAmount:N2} is pending.",
                NotificationType.Success,
                link: "/collector");
        }
        catch
        {
            // notification is advisory
        }
    }

    private async Task EnsureLocationAndCollectorExistAsync(Guid locationId, Guid collectorId, CancellationToken cancellationToken)
    {
        var locationExists = await _db.WarehouseLocations.AnyAsync(l => l.Id == locationId, cancellationToken);
        if (!locationExists)
            throw new KeyNotFoundException($"WarehouseLocation '{locationId}' was not found.");

        var collectorExists = await _db.Collectors.AnyAsync(c => c.CollectorId == collectorId, cancellationToken);
        if (!collectorExists)
            throw new KeyNotFoundException($"Collector '{collectorId}' was not found.");
    }

    private sealed record ReceivedJob(
        Guid JobId, decimal VerifiedWeightKg, decimal? ReportedWeightKg, decimal? DiscrepancyKg,
        int ExpectedQuantity, int ReceivedQuantity, List<DeliveryItemResult> Items,
        DateTime ReceivedAt, Guid PaymentId, decimal PaymentAmount);

    private sealed record ReceivedRow(SubmissionItemRef? Source, int Expected, int Received, string? ItemType, decimal WeightKg);

    private sealed record SubmissionItemRef(Guid Id, string Name, int Quantity);

    // Runs inside the caller's transaction. Each item of the submission the collector brought becomes
    // its own inventory item (a lot when its quantity is above 1); the job still gets ONE payment,
    // on the total verified weight — the flat-rate formula is unchanged.
    private async Task<ReceivedJob> ReceiveOneJobAsync(
        DeliveryJobLine line, Guid collectorId, Guid locationId, Guid receivedByStaffId, Guid? deliveryId,
        CancellationToken cancellationToken)
    {
        var jobId = line.JobId;

        // App-level pre-check for the common case; the unique indexes are the backstop for a genuine
        // race between two simultaneous requests for the same job.
        var alreadyReceived = await _db.InventoryItems.AnyAsync(i => i.JobId == jobId, cancellationToken);
        if (alreadyReceived)
            throw new DuplicateJobReceiptException(jobId);

        var job = await _jobVerification.VerifyAsync(jobId, cancellationToken);
        if (!job.Found)
            throw new KeyNotFoundException($"Job '{jobId}' was not found.");
        if (!job.IsCompleted)
            throw new JobNotCompletedException(jobId);

        // The job already says who collected it. Never trust the collector id on the request, and
        // never guess one for a job that has none.
        if (job.CollectorId is null)
            throw JobCollectorMismatchException.NoCollectorAssigned(jobId);
        if (job.CollectorId.Value != collectorId)
            throw JobCollectorMismatchException.WrongCollector(jobId, collectorId);

        var submissionItems = job.SubmissionId is null
            ? new List<SubmissionItemRef>()
            : await _db.SubmissionItems.AsNoTracking()
                .Where(i => i.SubmissionId == job.SubmissionId.Value)
                .OrderBy(i => i.Position)
                .Select(i => new SubmissionItemRef(i.Id, i.ItemName, i.Quantity))
                .ToListAsync(cancellationToken);

        var rows = submissionItems.Count == 0
            ? await WholeJobRowAsync(line, job.SubmissionId, cancellationToken)
            : await ItemRowsAsync(line, submissionItems, cancellationToken);

        var totalWeight = rows.Sum(r => r.WeightKg);
        decimal? discrepancy = job.ReportedWeightKg.HasValue ? totalWeight - job.ReportedWeightKg.Value : null;
        var weightNote = discrepancy.HasValue
            ? $"Job total: reported {job.ReportedWeightKg:F2}kg vs verified {totalWeight:F2}kg (diff {discrepancy:F2}kg)."
            : "Reported weight unavailable.";

        var results = new List<DeliveryItemResult>();
        var created = new List<InventoryItem>();
        foreach (var row in rows)
        {
            var result = new DeliveryItemResult
            {
                SubmissionItemId = row.Source?.Id,
                ItemName = row.Source?.Name ?? row.ItemType ?? "Whole job",
                ExpectedQuantity = row.Expected,
                ReceivedQuantity = row.Received,
                ItemType = row.Received > 0 ? row.ItemType : null,
                VerifiedWeightKg = row.WeightKg,
            };
            results.Add(result);
            if (row.Received == 0) continue; // not brought: recorded on the payment, nothing to store

            var inventoryItem = new InventoryItem
            {
                OriginType = OriginType.JobCollection,
                JobId = jobId,
                SubmissionId = job.SubmissionId,
                SubmissionItemId = row.Source?.Id,
                Quantity = row.Received,
                ItemType = row.ItemType!,
                VerifiedWeightKg = row.WeightKg,
                CurrentLocationId = locationId
            };
            var what = row.Source is null
                ? "the whole job"
                : $"{row.Received} of {row.Expected} × {row.Source.Name}";
            inventoryItem.MarkReceived(receivedByStaffId, $"Received {what} from job {jobId}, collector {collectorId}. {weightNote}");
            _db.InventoryItems.Add(inventoryItem);
            created.Add(inventoryItem);
            results[^1].InventoryItemId = inventoryItem.Id;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var expected = rows.Sum(r => r.Expected);
        var receivedUnits = rows.Sum(r => r.Received);

        // The payment goes to the job's own collector (checked above) and is stamped with the
        // authenticated staff member who received the job.
        var payment = await _paymentService.CreatePaymentAsync(
            PaymentSourceType.Job,
            jobId,
            job.CollectorId.Value,
            new PaymentContext
            {
                TotalWeightKg = totalWeight,
                DistanceKm = job.DistanceKm,
                ReportedWeightKg = job.ReportedWeightKg,
                ExpectedQuantity = expected,
                ReceivedQuantity = receivedUnits,
                NotReceived = rows.Where(r => r.Received < r.Expected && r.Source is not null)
                    .Select(r => $"{r.Source!.Name} ({r.Received} of {r.Expected})")
                    .ToList()
            },
            receivedByStaffId,
            cancellationToken,
            deliveryId);

        return new ReceivedJob(
            jobId, totalWeight, job.ReportedWeightKg, discrepancy, expected, receivedUnits, results,
            created[0].CreatedAt, payment.Id, payment.Amount);
    }

    // A job whose submission has no items (or no submission at all) is received as one item.
    private async Task<List<ReceivedRow>> WholeJobRowAsync(DeliveryJobLine line, Guid? submissionId, CancellationToken cancellationToken)
    {
        if (line.Items.Count > 0)
            throw new ArgumentException($"Job {line.JobId} has no submission items — receive it as one whole item.");
        if (line.VerifiedWeightKg <= 0)
            throw new ArgumentException("Enter the verified weight.");

        var itemType = await ResolveItemTypeAsync(line.ItemType, submissionId, cancellationToken);
        return new List<ReceivedRow> { new(null, 1, 1, itemType, line.VerifiedWeightKg) };
    }

    // One row per submission item, in the customer's order. Items missing from the request were not brought.
    private async Task<List<ReceivedRow>> ItemRowsAsync(
        DeliveryJobLine line, IReadOnlyList<SubmissionItemRef> submissionItems, CancellationToken cancellationToken)
    {
        if (line.Items.Count == 0)
            throw new ArgumentException(
                $"Job {line.JobId} has {submissionItems.Count} item(s) — receive them item by item, each with its type and weight.");

        var known = submissionItems.Select(i => i.Id).ToHashSet();
        var stranger = line.Items.FirstOrDefault(i => !known.Contains(i.SubmissionItemId));
        if (stranger is not null)
            throw new ArgumentException($"Item {stranger.SubmissionItemId} does not belong to job {line.JobId}.");

        var byId = line.Items.ToDictionary(i => i.SubmissionItemId);
        var rows = new List<ReceivedRow>();
        foreach (var source in submissionItems)
        {
            var expected = Math.Max(source.Quantity, 1);
            if (!byId.TryGetValue(source.Id, out var given) || given.ReceivedQuantity == 0)
            {
                rows.Add(new ReceivedRow(source, expected, 0, null, 0m));
                continue;
            }

            if (given.ReceivedQuantity < 0 || given.ReceivedQuantity > expected)
                throw new ArgumentException(
                    $"{source.Name}: received {given.ReceivedQuantity}, but the submission lists {expected}. " +
                    "Anything extra is received as extra waste.");
            if (given.VerifiedWeightKg <= 0)
                throw new ArgumentException($"{source.Name}: enter the verified weight.");

            var itemType = string.IsNullOrWhiteSpace(given.ItemType)
                ? throw new ArgumentException($"{source.Name}: choose the item type.")
                : await _itemTypes.ResolveAsync(given.ItemType, cancellationToken)
                  ?? throw new ArgumentException(
                      $"{source.Name}: '{given.ItemType.Trim()}' is not a known item type. Choose a type from the item-type list.");

            rows.Add(new ReceivedRow(source, expected, given.ReceivedQuantity, itemType, given.VerifiedWeightKg));
        }

        if (rows.All(r => r.Received == 0))
            throw new ArgumentException($"Nothing from job {line.JobId} was received — untick the job instead.");
        return rows;
    }

    public async Task<IReadOnlyList<ReceivableJobResponse>> GetReceivableJobsAsync(CancellationToken cancellationToken = default)
    {
        // Filtered on the server: a job stops being offered as soon as an inventory item exists for it
        // (the same fact the receive call and its unique index rely on). Jobs without a collector
        // cannot be received, so they are not offered either.
        var jobs = await _db.Jobs.AsNoTracking()
            .Where(j => j.Status == JobStatus.Completed
                        && j.CollectorId != null
                        && !_db.InventoryItems.Any(i => i.JobId == j.JobId))
            .OrderByDescending(j => j.CompletedAt).ThenByDescending(j => j.CreatedAt)
            .ToListAsync(cancellationToken);

        var collectorIds = jobs.Select(j => j.CollectorId!.Value).Distinct().ToList();
        var collectors = await (from c in _db.Collectors.AsNoTracking()
                                join u in _db.Users.AsNoTracking() on c.UserId equals u.UserId
                                where collectorIds.Contains(c.CollectorId)
                                select new { c.CollectorId, u.FullName, c.VehicleType })
            .ToDictionaryAsync(x => x.CollectorId, cancellationToken);

        var submissionIds = jobs.Select(j => j.SubmissionId).Distinct().ToList();
        var categories = await _db.Submissions.AsNoTracking()
            .Where(s => submissionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Category, cancellationToken);
        var allowedTypes = await _itemTypes.GetAllowedTypesAsync(cancellationToken);

        var itemsBySubmission = (await _db.SubmissionItems.AsNoTracking()
                .Where(i => submissionIds.Contains(i.SubmissionId))
                .OrderBy(i => i.Position)
                .ToListAsync(cancellationToken))
            .GroupBy(i => i.SubmissionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // The Analyzer's per-item results, in item order — used for type suggestions and weight hints.
        var analysisBySubmission = (await _db.CollectionWorkflows.AsNoTracking()
                .Where(w => submissionIds.Contains(w.SubmissionId) && w.AnalyzerResultJson != null)
                .Select(w => new { w.SubmissionId, w.CreatedAt, w.AnalyzerResultJson })
                .ToListAsync(cancellationToken))
            .GroupBy(w => w.SubmissionId)
            .ToDictionary(g => g.Key, g => ParseAnalyzedItems(g.OrderByDescending(w => w.CreatedAt).First().AnalyzerResultJson));

        // Suggestions only come from received-item types (rate policies), never from material names.
        var suggestionTypes = await _db.RatePolicies.AsNoTracking()
            .Where(r => r.IsActive && r.ItemType != JobPaymentCalculator.GeneralCollectionItemType)
            .Select(r => r.ItemType)
            .Distinct()
            .ToListAsync(cancellationToken);

        return jobs.Select(j =>
        {
            collectors.TryGetValue(j.CollectorId!.Value, out var collector);
            var category = categories.GetValueOrDefault(j.SubmissionId);
            var items = itemsBySubmission.GetValueOrDefault(j.SubmissionId) ?? new();
            var analyzed = analysisBySubmission.GetValueOrDefault(j.SubmissionId) ?? new();
            // Per-item AI results line up with the items only when there is one result per item.
            var aiLinedUp = analyzed.Count == items.Count;

            return new ReceivableJobResponse
            {
                Items = items.Select((item, index) =>
                {
                    var ai = aiLinedUp ? analyzed[index] : null;
                    var (type, source) = ItemTypeSuggester.Suggest(
                        suggestionTypes, item.ItemName, item.CategoryHint, ai?.WasteCategory, category);
                    return new ReceivableJobItem
                    {
                        SubmissionItemId = item.Id,
                        ItemName = item.ItemName,
                        Description = item.Description,
                        Quantity = Math.Max(item.Quantity, 1),
                        ExpectedWeightKg = item.EstimatedWeightKg is { } unit
                            ? unit * Math.Max(item.Quantity, 1)
                            : ai is { EstimatedVolumeKg: > 0 } ? ai.EstimatedVolumeKg : null,
                        SuggestedItemType = type,
                        SuggestionSource = source,
                    };
                }).ToList(),
                JobId = j.JobId,
                CollectorId = j.CollectorId.Value,
                CollectorName = collector?.FullName,
                CollectorVehicleType = collector?.VehicleType,
                PickupAddress = j.PickupAddress,
                ReportedWeightKg = j.MeasuredWeightKg,
                EstimatedDistanceKm = j.EstimatedDistanceKm,
                CompletedAt = j.CompletedAt,
                SubmissionCategory = string.IsNullOrWhiteSpace(category) ? null : category,
                SuggestedItemType = MatchAllowed(allowedTypes, category)
            };
        }).ToList();
    }

    // The type the worker picked wins. Without one, the customer's submission category is used — but only
    // when it is on the item-type list, so every job item can later be classified and priced like any other.
    private async Task<string> ResolveItemTypeAsync(string? requested, Guid? submissionId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return await _itemTypes.ResolveAsync(requested, cancellationToken)
                ?? throw new ArgumentException($"'{requested.Trim()}' is not a known item type. Choose a type from the item-type list.");

        var category = submissionId is null
            ? null
            : await _db.Submissions.AsNoTracking()
                .Where(s => s.Id == submissionId.Value)
                .Select(s => s.Category)
                .FirstOrDefaultAsync(cancellationToken);

        return await _itemTypes.ResolveAsync(category, cancellationToken)
            ?? throw new ArgumentException(
                "Choose the item type for this job — the submission's category doesn't match a known item type.");
    }

    private static List<AnalyzedItem> ParseAnalyzedItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<AnalyzerResultRequest>(json)?.Items ?? new();
        }
        catch (JsonException)
        {
            return new(); // hints only — a result we can't read just means no AI suggestion
        }
    }

    private static string? MatchAllowed(IReadOnlyList<string> allowedTypes, string? category)
        => string.IsNullOrWhiteSpace(category)
            ? null
            : allowedTypes.FirstOrDefault(t => string.Equals(t, category.Trim(), StringComparison.OrdinalIgnoreCase));
}
