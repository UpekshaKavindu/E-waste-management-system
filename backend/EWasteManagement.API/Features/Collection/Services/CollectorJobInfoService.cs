using EWasteManagement.API.Features.Collection.DTOs;
using EWasteManagement.API.Features.Collection.Entities;
using EWasteManagement.API.Features.Processing.Entities;
using EWasteManagement.API.Features.Processing.Services;
using EWasteManagement.API.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EWasteManagement.API.Features.Collection.Services;

public interface ICollectorJobInfoService
{
    /// <summary>Null when the job does not exist; throws UnauthorizedAccessException for someone else's job.</summary>
    Task<CollectorJobInfoDto?> GetAsync(Guid jobId, Guid requestingUserId, CancellationToken ct = default);
}

/// <summary>
/// What the assigned collector sees on a job besides the job itself: who to call at the pickup and
/// what the company will pay for it. Kept off JobResponseDto so customer contact details only ever
/// leave the server through this one, ownership-checked call.
/// </summary>
public class CollectorJobInfoService : ICollectorJobInfoService
{
    // Contact details are needed only between accepting a job and finishing it. A collector who
    // has merely been offered the job (and may reject it) does not get the customer's phone number.
    private static readonly JobStatus[] ContactStatuses = { JobStatus.Accepted, JobStatus.InProgress };

    private readonly ApplicationDbContext _db;
    private readonly IEnumerable<IPaymentCalculator> _calculators;

    public CollectorJobInfoService(ApplicationDbContext db, IEnumerable<IPaymentCalculator> calculators)
    {
        _db = db;
        _calculators = calculators;
    }

    public async Task<CollectorJobInfoDto?> GetAsync(Guid jobId, Guid requestingUserId, CancellationToken ct = default)
    {
        var job = await _db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.JobId == jobId, ct);
        if (job is null) return null;

        var collector = await _db.Collectors.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == requestingUserId, ct);
        if (collector is null || job.CollectorId != collector.CollectorId)
            throw new UnauthorizedAccessException("You do not have permission to view this job.");

        var submission = await _db.Submissions.AsNoTracking()
            .Where(s => s.Id == job.SubmissionId)
            .Select(s => new { s.UserId, s.PhoneNumber, s.EstimatedWeight })
            .FirstOrDefaultAsync(ct);

        var dto = new CollectorJobInfoDto { ContactAvailable = ContactStatuses.Contains(job.Status) };

        if (dto.ContactAvailable && submission is not null)
        {
            var owner = await _db.Users.AsNoTracking()
                .Where(u => u.UserId == submission.UserId)
                .Select(u => new { u.FullName, u.Phone })
                .FirstOrDefaultAsync(ct);
            dto.CustomerName = owner?.FullName;
            // The number given on the submission is the one for this pickup; the account phone is a fallback.
            dto.CustomerPhone = string.IsNullOrWhiteSpace(submission.PhoneNumber) ? owner?.Phone : submission.PhoneNumber;
        }

        // Received at the warehouse: the real payment exists, show it as it is.
        var payment = await _db.CollectorPayments.AsNoTracking()
            .Where(p => p.SourceType == PaymentSourceType.Job && p.SourceId == jobId)
            .Select(p => new { p.Amount, p.Status })
            .FirstOrDefaultAsync(ct);
        if (payment is not null)
        {
            dto.PaymentAmount = payment.Amount;
            dto.PaymentIsEstimate = false;
            dto.PaymentStatus = payment.Status.ToString();
            return dto;
        }

        // Not received yet: estimate with the warehouse's own formula. Same distance it will use;
        // the weight is the best one known so far (the warehouse weighs it again on receipt).
        var weight = job.MeasuredWeightKg ?? job.RequiredCapacityKg
            ?? (submission is { EstimatedWeight: > 0 } ? submission.EstimatedWeight : (decimal?)null);
        var calculator = _calculators.FirstOrDefault(c => c.Handles == PaymentSourceType.Job);
        if (weight is null || calculator is null) return dto;

        try
        {
            var result = await calculator.CalculateWithBreakdownAsync(
                new PaymentContext { TotalWeightKg = weight.Value, DistanceKm = job.EstimatedDistanceKm }, ct);
            var parts = result.Snapshot.Job;
            dto.PaymentAmount = result.Amount;
            dto.PaymentIsEstimate = true;
            dto.EstimateWeightKg = weight;
            if (parts is not null)
            {
                dto.BaseFee = parts.BaseFee;
                dto.WeightAmount = parts.WeightAmount;
                dto.RatePerKg = parts.RatePerKg;
                dto.DistanceAmount = parts.DistanceAmount;
                dto.DistanceKm = parts.DistanceUsedKm;
            }
        }
        catch (KeyNotFoundException)
        {
            // No active rate policy yet — leave the payment out rather than failing the screen.
        }

        return dto;
    }
}
