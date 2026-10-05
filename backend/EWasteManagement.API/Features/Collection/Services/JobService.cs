using EWasteManagement.API.Features.Auth.Entities;
using EWasteManagement.API.Features.Collection.DTOs;
using EWasteManagement.API.Features.Collection.Entities;
using EWasteManagement.API.Features.Notifications.Entities;
using EWasteManagement.API.Features.Notifications.Services;
using EWasteManagement.API.Infrastructure.ExternalServices;
using EWasteManagement.API.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EWasteManagement.API.Features.Collection.Services;

public interface IJobService
{
    Task<JobResponseDto> CreateAndAssignAsync(CreateJobDto dto);

    // The most recent job created for a submission, if any. Lets the workflow
    // Finalize step reuse a job instead of creating a duplicate when it runs
    // again for the same submission.
    Task<Guid?> FindJobIdForSubmissionAsync(Guid submissionId);
    Task<JobResponseDto> AcceptAsync(Guid jobId, Guid requestingUserId);
    Task<JobResponseDto> RejectAsync(Guid jobId, Guid requestingUserId, RejectJobDto dto);
    Task<JobResponseDto> StartAsync(Guid jobId, Guid requestingUserId);
    Task<JobResponseDto> CompleteAsync(Guid jobId, Guid requestingUserId, CompleteJobDto dto);
    Task<List<JobResponseDto>> GetMyJobsAsync(Guid requestingUserId, JobStatus? status);
    Task<JobResponseDto?> GetByIdAsync(Guid jobId, Guid requestingUserId, bool isPrivileged);
    Task<JobRouteDto?> GetRouteAsync(Guid jobId, Guid requestingUserId, decimal? fromLat, decimal? fromLng);
    Task<List<JobResponseDto>> GetAllAsync(JobStatus? status);

    // Staff/admin
    Task<List<JobAssignmentHistoryDto>> GetHistoryAsync(Guid jobId);
    Task<JobResponseDto> UpdateAddressAsync(Guid jobId, UpdateJobAddressDto dto);
    Task<JobResponseDto> ReassignAsync(Guid jobId, ReassignJobDto dto);
    Task<JobResponseDto> CancelAsync(Guid jobId);
}

public class JobService : IJobService
{
    private readonly ApplicationDbContext _db;
    private readonly IGeoService _geoService;
    private readonly IMatchingService _matchingService;
    private readonly INotificationService _notifications;

    public JobService(ApplicationDbContext db, IGeoService geoService, IMatchingService matchingService, INotificationService notifications)
    {
        _db = db;
        _geoService = geoService;
        _matchingService = matchingService;
        _notifications = notifications;
    }

    public async Task<JobResponseDto> CreateAndAssignAsync(CreateJobDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.PickupAddress))
            throw new ArgumentException("PickupAddress is required.");

        var job = new Job
        {
            SubmissionId = dto.SubmissionId,
            PickupAddress = dto.PickupAddress,
            RequiredCapacityKg = dto.RequiredCapacityKg,
            ScheduledWindowStart = dto.ScheduledWindowStart,
            ScheduledWindowEnd = dto.ScheduledWindowEnd,
            MatcherReasoning = dto.MatcherReasoning
        };

        var coordinates = await _geoService.GeocodeAsync(dto.PickupAddress);

        if (coordinates is null)
        {
            // Address couldn't be resolved — flag for staff rather than
            // guessing or blocking. No point trying to match a collector
            // against a location we don't have.
            job.Status = JobStatus.PickupLocationUnresolved;
            _db.Jobs.Add(job);
            await _db.SaveChangesAsync();
            await NotifyAttentionAsync(job, ct: CancellationToken.None);
            return await ToDtoAsync(job);
        }

        job.PickupLatitude = coordinates.Value.Latitude;
        job.PickupLongitude = coordinates.Value.Longitude;

        if (dto.SkipAutoAssign)
        {
            var anyEligible = await FindBestCandidateAsync(job, excludeCollectorIds: new List<Guid>());
            job.Status = anyEligible is null ? JobStatus.NoCollectorAvailable : JobStatus.AwaitingStaffAssignment;
            _db.Jobs.Add(job);
            await _db.SaveChangesAsync();
            await NotifyAttentionAsync(job, ct: CancellationToken.None);
            return await ToDtoAsync(job);
        }

        var (candidate, historyReason) = dto.PreferredCollectorId is Guid preferredId
            ? await ChoosePreferredOrBestAsync(job, preferredId)
            : (await FindBestCandidateAsync(job, excludeCollectorIds: new List<Guid>()), (string?)null);

        ApplyAssignmentOutcome(job, candidate);

        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        if (candidate is not null)
            await LogHistoryAsync(job.JobId, candidate.CollectorId, AssignmentOutcome.Assigned, historyReason);
        else
            await NotifyAttentionAsync(job, ct: CancellationToken.None);

        return await ToDtoAsync(job);
    }

    public Task<Guid?> FindJobIdForSubmissionAsync(Guid submissionId) =>
        _db.Jobs
            .Where(j => j.SubmissionId == submissionId)
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => (Guid?)j.JobId)
            .FirstOrDefaultAsync();

    public async Task<JobResponseDto> AcceptAsync(Guid jobId, Guid requestingUserId)
    {
        var job = await GetOwnedJobAsync(jobId, requestingUserId);

        if (job.Status != JobStatus.Assigned)
            throw new InvalidOperationException($"Cannot accept a job in status '{job.Status}'.");

        job.Status = JobStatus.Accepted;
        job.RespondedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await LogHistoryAsync(job.JobId, job.CollectorId!.Value, AssignmentOutcome.Accepted);

        return await ToDtoAsync(job);
    }

    public async Task<JobResponseDto> RejectAsync(Guid jobId, Guid requestingUserId, RejectJobDto dto)
    {
        var job = await GetOwnedJobAsync(jobId, requestingUserId);

        if (job.Status != JobStatus.Assigned)
            throw new InvalidOperationException($"Cannot reject a job in status '{job.Status}'.");

        var rejectingCollectorId = job.CollectorId!.Value;

        job.Status = JobStatus.Rejected;
        job.RejectionReason = dto.Reason;
        job.RespondedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await LogHistoryAsync(jobId, rejectingCollectorId, AssignmentOutcome.Rejected, dto.Reason);

        // Reassignment: exclude every collector who's already been offered
        // this job (rejecters, and — belt and braces — anyone already
        // assigned), then try again.
        var alreadyOffered = await _db.JobAssignmentHistory
            .Where(h => h.JobId == jobId)
            .Select(h => h.CollectorId)
            .Distinct()
            .ToListAsync();

        var nextCandidate = job.PickupLatitude is null || job.PickupLongitude is null
            ? null
            : await FindBestCandidateAsync(job, excludeCollectorIds: alreadyOffered);

        job.RejectionReason = null; // stale once we move to a new assignment attempt; history keeps the real record
        ApplyAssignmentOutcome(job, nextCandidate);
        await _db.SaveChangesAsync();

        if (nextCandidate is not null)
            await LogHistoryAsync(jobId, nextCandidate.CollectorId, AssignmentOutcome.Assigned);
        else
            await NotifyAttentionAsync(job);

        return await ToDtoAsync(job);
    }

    // Collector sets off for the pickup (the app calls this when they tap
    // Navigate). Calling it again while already InProgress is harmless, so a
    // double tap doesn't show the collector an error.
    public async Task<JobResponseDto> StartAsync(Guid jobId, Guid requestingUserId)
    {
        var job = await GetOwnedJobAsync(jobId, requestingUserId);

        if (job.Status == JobStatus.InProgress)
            return await ToDtoAsync(job);

        if (job.Status != JobStatus.Accepted)
            throw new InvalidOperationException($"Cannot start a job in status '{job.Status}'. Accept it first.");

        job.Status = JobStatus.InProgress;
        job.StartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return await ToDtoAsync(job);
    }

    public async Task<JobResponseDto> CompleteAsync(Guid jobId, Guid requestingUserId, CompleteJobDto dto)
    {
        var job = await GetOwnedJobAsync(jobId, requestingUserId);

        if (job.Status is not (JobStatus.Accepted or JobStatus.InProgress))
            throw new InvalidOperationException($"Cannot complete a job in status '{job.Status}'.");

        if (string.IsNullOrWhiteSpace(dto.PhotoUrl))
            throw new ArgumentException("PhotoUrl is required to confirm a pickup.");

        if (dto.MeasuredWeightKg <= 0)
            throw new ArgumentException("MeasuredWeightKg must be greater than zero.");

        job.PhotoUrl = dto.PhotoUrl;
        job.MeasuredWeightKg = dto.MeasuredWeightKg;
        job.Notes = dto.Notes;
        job.Status = JobStatus.Completed;
        job.CompletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        // The submitter's pickup is done — tell their bell (advisory, see helper).
        await NotifyOwnerSafeAsync(
            job.SubmissionId,
            "Pickup completed",
            $"A collector picked up your submission ({dto.MeasuredWeightKg} kg measured).",
            NotificationType.Success);

        return await ToDtoAsync(job);
    }

    public async Task<List<JobResponseDto>> GetMyJobsAsync(Guid requestingUserId, JobStatus? status)
    {
        var collector = await _db.Collectors.FirstOrDefaultAsync(c => c.UserId == requestingUserId)
            ?? throw new KeyNotFoundException("No collector profile exists for this user.");

        var query = _db.Jobs.Where(j => j.CollectorId == collector.CollectorId);

        if (status is not null)
            query = query.Where(j => j.Status == status);

        var jobs = await query.OrderByDescending(j => j.CreatedAt).ToListAsync();
        return await ToDtosAsync(jobs);
    }

    public async Task<JobResponseDto?> GetByIdAsync(Guid jobId, Guid requestingUserId, bool isPrivileged)
    {
        var job = await _db.Jobs.FindAsync(jobId);
        if (job is null) return null;

        if (isPrivileged) return await ToDtoAsync(job);

        // Non-privileged callers (collectors) can only view their own job.
        var collector = await _db.Collectors.FirstOrDefaultAsync(c => c.UserId == requestingUserId);
        if (collector is null || job.CollectorId != collector.CollectorId)
            throw new UnauthorizedAccessException("You do not have permission to view this job.");

        return await ToDtoAsync(job);
    }

    // Route from the collector to the pickup. The app sends the phone's position; without one the
    // last location the collector reported is used.
    public async Task<JobRouteDto?> GetRouteAsync(Guid jobId, Guid requestingUserId, decimal? fromLat, decimal? fromLng)
    {
        var job = await _db.Jobs.FindAsync(jobId);
        if (job is null) return null;

        var collector = await _db.Collectors.FirstOrDefaultAsync(c => c.UserId == requestingUserId);
        if (collector is null || job.CollectorId != collector.CollectorId)
            throw new UnauthorizedAccessException("You do not have permission to view this job.");

        var originLat = fromLat ?? collector.CurrentLatitude;
        var originLng = fromLng ?? collector.CurrentLongitude;

        var dto = new JobRouteDto
        {
            OriginLatitude = originLat,
            OriginLongitude = originLng,
            PickupLatitude = job.PickupLatitude,
            PickupLongitude = job.PickupLongitude
        };

        if (originLat is null || originLng is null || job.PickupLatitude is null || job.PickupLongitude is null)
            return dto;

        var route = await _geoService.GetRouteAsync(originLat.Value, originLng.Value, job.PickupLatitude.Value, job.PickupLongitude.Value);
        if (route is null) return dto;

        dto.DistanceKm = route.DistanceKm;
        dto.DurationMinutes = route.DurationMinutes;
        dto.Points = route.Points.Select(p => new[] { p.Lat, p.Lng }).ToList();
        return dto;
    }

    public async Task<List<JobResponseDto>> GetAllAsync(JobStatus? status)
    {
        var query = _db.Jobs.AsQueryable();

        if (status is not null)
            query = query.Where(j => j.Status == status);

        var jobs = await query.OrderByDescending(j => j.CreatedAt).ToListAsync();
        return await ToDtosAsync(jobs);
    }

    // --- staff actions -------------------------------------------------

    public async Task<List<JobAssignmentHistoryDto>> GetHistoryAsync(Guid jobId)
    {
        if (!await _db.Jobs.AnyAsync(j => j.JobId == jobId))
            throw new KeyNotFoundException("Job not found.");

        var rows = await (
            from h in _db.JobAssignmentHistory
            where h.JobId == jobId
            join c in _db.Collectors on h.CollectorId equals c.CollectorId into cs
            from c in cs.DefaultIfEmpty()
            join u in _db.Users on c.UserId equals u.UserId into us
            from u in us.DefaultIfEmpty()
            orderby h.Timestamp
            select new { h, Name = u != null ? u.FullName : null }
        ).ToListAsync();

        return rows.Select(x => new JobAssignmentHistoryDto
        {
            HistoryId = x.h.HistoryId,
            CollectorId = x.h.CollectorId,
            CollectorName = x.Name ?? "Unknown collector",
            Outcome = x.h.Outcome.ToString(),
            Reason = x.h.Reason,
            Timestamp = x.h.Timestamp
        }).ToList();
    }

    // Fixes an address that couldn't be geocoded (or that geocoded somewhere
    // no collector can reach), then immediately retries matching.
    public async Task<JobResponseDto> UpdateAddressAsync(Guid jobId, UpdateJobAddressDto dto)
    {
        var job = await _db.Jobs.FindAsync(jobId)
            ?? throw new KeyNotFoundException("Job not found.");

        if (job.Status is not (JobStatus.PickupLocationUnresolved or JobStatus.NoCollectorAvailable
                or JobStatus.AwaitingStaffAssignment))
            throw new InvalidOperationException(
                $"The address can only be changed while a job is unresolved or unassigned (current status: '{job.Status}').");

        if (string.IsNullOrWhiteSpace(dto.PickupAddress))
            throw new ArgumentException("PickupAddress is required.");

        var coordinates = await _geoService.GeocodeAsync(dto.PickupAddress.Trim());
        if (coordinates is null)
            throw new ArgumentException(
                "That address still couldn't be located. Try adding the street name, town, or a nearby landmark.");

        job.PickupAddress = dto.PickupAddress.Trim();
        job.PickupLatitude = coordinates.Value.Latitude;
        job.PickupLongitude = coordinates.Value.Longitude;

        // The Matcher left this one for staff to choose, so a corrected
        // address doesn't auto-assign it.
        if (job.Status == JobStatus.AwaitingStaffAssignment)
        {
            await _db.SaveChangesAsync();
            return await ToDtoAsync(job);
        }

        var candidate = await FindBestCandidateAsync(job,
            excludeCollectorIds: await GetRejectedCollectorIdsAsync(jobId));

        ApplyAssignmentOutcome(job, candidate);
        await _db.SaveChangesAsync();

        if (candidate is not null)
            await LogHistoryAsync(jobId, candidate.CollectorId, AssignmentOutcome.Assigned,
                "Assigned after staff corrected the pickup address");

        return await ToDtoAsync(job);
    }

    // Staff override. With a CollectorId, hands the job to that collector
    // directly. Without one, re-runs automatic matching — useful when a job
    // hit NoCollectorAvailable and collectors have since come online, or
    // when the assigned collector isn't responding.
    public async Task<JobResponseDto> ReassignAsync(Guid jobId, ReassignJobDto dto)
    {
        var job = await _db.Jobs.FindAsync(jobId)
            ?? throw new KeyNotFoundException("Job not found.");

        if (job.Status is not (JobStatus.Assigned or JobStatus.NoCollectorAvailable or JobStatus.AwaitingStaffAssignment))
            throw new InvalidOperationException(
                $"Only jobs that are Assigned, NoCollectorAvailable or AwaitingStaffAssignment can be reassigned (current status: '{job.Status}').");

        if (job.PickupLatitude is null || job.PickupLongitude is null)
            throw new InvalidOperationException("This job has no pickup coordinates. Fix the address first.");

        if (dto.CollectorId is Guid chosenId)
        {
            var collector = await _db.Collectors.FindAsync(chosenId)
                ?? throw new KeyNotFoundException("Collector not found.");

            if (job.CollectorId == chosenId)
                throw new InvalidOperationException("This job is already assigned to that collector.");

            // Deliberately not enforcing the load cap or capacity here: this
            // is a human override, and the UI shows load/capacity so staff
            // can make that call. Availability is enforced, because an
            // offline collector would never see the job.
            if (!collector.IsAvailable)
                throw new InvalidOperationException("That collector is currently offline.");

            var route = collector.CurrentLatitude is null || collector.CurrentLongitude is null
                ? null
                : await _geoService.GetDistanceAsync(
                    collector.CurrentLatitude.Value, collector.CurrentLongitude.Value,
                    job.PickupLatitude.Value, job.PickupLongitude.Value);

            ApplyAssignmentOutcome(job, new CollectorMatchDto
            {
                CollectorId = collector.CollectorId,
                DistanceKm = route?.DistanceKm,
                EtaMinutes = route?.DurationMinutes
            });
            job.RejectionReason = null;
            await _db.SaveChangesAsync();

            await LogHistoryAsync(jobId, collector.CollectorId, AssignmentOutcome.Assigned, "Manually assigned by staff");
            return await ToDtoAsync(job);
        }

        // Automatic re-match: skip everyone who rejected this job, and the
        // current assignee (staff are moving it away from them).
        var exclude = await GetRejectedCollectorIdsAsync(jobId);
        if (job.CollectorId is Guid currentId)
            exclude.Add(currentId);

        var candidate = await FindBestCandidateAsync(job, excludeCollectorIds: exclude);

        // Don't take a job away from its current collector just to leave it
        // with nobody — tell staff instead and leave the job as it was.
        if (candidate is null && job.Status == JobStatus.Assigned)
            throw new InvalidOperationException("No other collector is available right now. The job was left with its current collector.");

        ApplyAssignmentOutcome(job, candidate);
        job.RejectionReason = null;
        await _db.SaveChangesAsync();

        if (candidate is not null)
            await LogHistoryAsync(jobId, candidate.CollectorId, AssignmentOutcome.Assigned, "Re-matched by staff");

        return await ToDtoAsync(job);
    }

    public async Task<JobResponseDto> CancelAsync(Guid jobId)
    {
        var job = await _db.Jobs.FindAsync(jobId)
            ?? throw new KeyNotFoundException("Job not found.");

        if (job.Status is JobStatus.Completed or JobStatus.Cancelled)
            throw new InvalidOperationException($"A job that is already '{job.Status}' can't be cancelled.");

        // CollectorId is kept so the record still shows who had it; Cancelled
        // isn't an active status, so it no longer counts towards their load.
        job.Status = JobStatus.Cancelled;
        await _db.SaveChangesAsync();

        return await ToDtoAsync(job);
    }

    // --- notifications -------------------------------------------------

    // Staff bell: the job landed somewhere automation can't finish it
    // (bad address, nobody free, manual assignment requested).
    private async Task NotifyAttentionAsync(Job job, CancellationToken ct = default)
    {
        if (job.Status is not (JobStatus.PickupLocationUnresolved
            or JobStatus.NoCollectorAvailable
            or JobStatus.AwaitingStaffAssignment))
            return;

        var (title, message) = job.Status switch
        {
            JobStatus.PickupLocationUnresolved => (
                "Job needs an address fix",
                $"The pickup address for job {ShortId(job.JobId)} couldn't be located on the map."),
            JobStatus.NoCollectorAvailable => (
                "No collector available",
                $"Job {ShortId(job.JobId)} has no eligible collector — reassignment is needed."),
            _ => (
                "Job awaiting staff assignment",
                $"Job {ShortId(job.JobId)} is waiting for a staff pick of the collector."),
        };

        try
        {
            await _notifications.NotifyRolesAsync(
                new[] { UserRole.Staff, UserRole.Admin },
                title,
                message,
                NotificationType.Warning,
                link: $"/collection/jobs/{job.JobId}",
                ct: ct);
        }
        catch
        {
            // notification is advisory — the job row is already committed
        }
    }

    // Collector bell: a job was offered to them (auto-match, re-match or staff pick). Never throws.
    private async Task NotifyCollectorOfAssignmentAsync(Guid jobId, Guid collectorId)
    {
        try
        {
            var userId = await _db.Collectors
                .Where(c => c.CollectorId == collectorId)
                .Select(c => c.UserId)
                .FirstOrDefaultAsync();
            var address = await _db.Jobs
                .Where(j => j.JobId == jobId)
                .Select(j => j.PickupAddress)
                .FirstOrDefaultAsync();
            if (userId == default) return;

            await _notifications.NotifyAsync(
                userId,
                "New job assigned",
                $"Pickup at {address}. Accept or reject it from My Jobs.",
                NotificationType.Info,
                link: $"/collector/jobs/{jobId}");
        }
        catch
        {
            // notification is advisory — the assignment is already committed
        }
    }

    // Owner bell: a state change on their submission's pickup job. Never
    // throws, so a notification outage can't fail the job operation.
    private async Task NotifyOwnerSafeAsync(Guid submissionId, string title, string message, NotificationType type)
    {
        try
        {
            var ownerId = await _db.Submissions
                .Where(s => s.Id == submissionId)
                .Select(s => s.UserId)
                .FirstOrDefaultAsync();
            if (ownerId == default) return;

            await _notifications.NotifyAsync(ownerId, title, message, type, link: "/submissions/mine");
        }
        catch
        {
            // notification is advisory — the caller's write already committed
        }
    }

    private static string ShortId(Guid id) => $"{id.ToString()[..8]}…";

    // --- helpers -----------------------------------------------------

    // Every matching run for a job uses the job's own RequiredCapacityKg,
    // so capacity is respected on the first assignment and on every
    // re-match after it.
    private Task<List<CollectorMatchDto>> RankCandidatesAsync(Job job, List<Guid> excludeCollectorIds, int maxResults) =>
        _matchingService.FindCandidatesAsync(new MatchRequestDto
        {
            PickupLatitude = job.PickupLatitude!.Value,
            PickupLongitude = job.PickupLongitude!.Value,
            RequiredCapacityKg = job.RequiredCapacityKg,
            ExcludeCollectorIds = excludeCollectorIds,
            MaxResults = maxResults
        });

    private async Task<CollectorMatchDto?> FindBestCandidateAsync(Job job, List<Guid> excludeCollectorIds) =>
        (await RankCandidatesAsync(job, excludeCollectorIds, maxResults: 1)).FirstOrDefault();

    // Big enough to include every eligible collector in practice, so the
    // Matcher's pick is found if they're still eligible at all.
    private const int MaxRankedCandidates = 50;

    public const string MatcherRecommendationFollowed = "Recommended by the Matcher agent";

    // Uses the Matcher agent's recommended collector if they still pass the
    // same rules automatic matching uses (online, has a location, big enough
    // vehicle, under the job cap). Time can pass between the recommendation
    // and job creation (e.g. while staff approve), so if they no longer
    // qualify, fall back to the best available collector and say so in the
    // history instead of silently replacing the agent's choice.
    private async Task<(CollectorMatchDto? Candidate, string? HistoryReason)> ChoosePreferredOrBestAsync(Job job, Guid preferredId)
    {
        var ranked = await RankCandidatesAsync(job, new List<Guid>(), MaxRankedCandidates);

        var preferred = ranked.FirstOrDefault(c => c.CollectorId == preferredId);
        if (preferred is not null)
            return (preferred, MatcherRecommendationFollowed);

        var fallback = ranked.FirstOrDefault();
        if (fallback is null)
            return (null, null);

        var preferredName = await (
            from c in _db.Collectors
            where c.CollectorId == preferredId
            join u in _db.Users on c.UserId equals u.UserId
            select u.FullName
        ).FirstOrDefaultAsync() ?? "a collector";

        return (fallback, $"Matcher recommended {preferredName}, who could no longer take this job (offline, at the job limit, or vehicle too small). Chosen by automatic matching instead");
    }

    private static void ApplyAssignmentOutcome(Job job, CollectorMatchDto? candidate)
    {
        if (candidate is null)
        {
            job.CollectorId = null;
            job.Status = JobStatus.NoCollectorAvailable;
            job.EstimatedDistanceKm = null;
            job.EstimatedEtaMinutes = null;
            return;
        }

        job.CollectorId = candidate.CollectorId;
        job.Status = JobStatus.Assigned;
        job.EstimatedDistanceKm = candidate.DistanceKm;
        job.EstimatedEtaMinutes = candidate.EtaMinutes;
    }

    private async Task LogHistoryAsync(Guid jobId, Guid collectorId, AssignmentOutcome outcome, string? reason = null)
    {
        _db.JobAssignmentHistory.Add(new JobAssignmentHistory
        {
            JobId = jobId,
            CollectorId = collectorId,
            Outcome = outcome,
            Reason = reason
        });
        await _db.SaveChangesAsync();

        if (outcome == AssignmentOutcome.Assigned)
            await NotifyCollectorOfAssignmentAsync(jobId, collectorId);
    }

    // Every write endpoint (accept/reject/complete) goes through this —
    // a collector can only act on their own job, never someone else's,
    // even if they guess another job's id.
    private async Task<Job> GetOwnedJobAsync(Guid jobId, Guid requestingUserId)
    {
        var job = await _db.Jobs.FindAsync(jobId)
            ?? throw new KeyNotFoundException("Job not found.");

        var collector = await _db.Collectors.FirstOrDefaultAsync(c => c.UserId == requestingUserId)
            ?? throw new UnauthorizedAccessException("No collector profile exists for this user.");

        if (job.CollectorId != collector.CollectorId)
            throw new UnauthorizedAccessException("You do not have permission to act on this job.");

        return job;
    }

    private async Task<List<Guid>> GetRejectedCollectorIdsAsync(Guid jobId) =>
        await _db.JobAssignmentHistory
            .Where(h => h.JobId == jobId && h.Outcome == AssignmentOutcome.Rejected)
            .Select(h => h.CollectorId)
            .Distinct()
            .ToListAsync();

    private async Task<JobResponseDto> ToDtoAsync(Job job) =>
        (await ToDtosAsync(new List<Job> { job }))[0];

    // Resolves collector names (and which completed jobs the warehouse has received) in one query each.
    private async Task<List<JobResponseDto>> ToDtosAsync(List<Job> jobs)
    {
        var completedIds = jobs.Where(j => j.Status == JobStatus.Completed).Select(j => j.JobId).ToList();
        var receivedIds = completedIds.Count == 0
            ? new HashSet<Guid>()
            : (await _db.InventoryItems
                .Where(i => i.JobId != null && completedIds.Contains(i.JobId.Value))
                .Select(i => i.JobId!.Value)
                .ToListAsync()).ToHashSet();

        var collectorIds = jobs.Where(j => j.CollectorId != null)
            .Select(j => j.CollectorId!.Value).Distinct().ToList();

        var names = collectorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await (
                from c in _db.Collectors
                where collectorIds.Contains(c.CollectorId)
                join u in _db.Users on c.UserId equals u.UserId
                select new { c.CollectorId, u.FullName }
            ).ToDictionaryAsync(x => x.CollectorId, x => x.FullName);

        return jobs.Select(j =>
        {
            var dto = ToDto(j);
            if (j.CollectorId is Guid id && names.TryGetValue(id, out var name))
                dto.CollectorName = name;
            dto.ReceivedAtWarehouse = receivedIds.Contains(j.JobId);
            return dto;
        }).ToList();
    }

    private static JobResponseDto ToDto(Job j) => new()
    {
        JobId = j.JobId,
        SubmissionId = j.SubmissionId,
        CollectorId = j.CollectorId,
        Status = j.Status.ToString(),
        PickupAddress = j.PickupAddress,
        PickupLatitude = j.PickupLatitude,
        PickupLongitude = j.PickupLongitude,
        RequiredCapacityKg = j.RequiredCapacityKg,
        ScheduledWindowStart = j.ScheduledWindowStart,
        ScheduledWindowEnd = j.ScheduledWindowEnd,
        EstimatedEtaMinutes = j.EstimatedEtaMinutes,
        EstimatedDistanceKm = j.EstimatedDistanceKm,
        PhotoUrl = j.PhotoUrl,
        MeasuredWeightKg = j.MeasuredWeightKg,
        Notes = j.Notes,
        RejectionReason = j.RejectionReason,
        MatcherReasoning = j.MatcherReasoning,
        CreatedAt = j.CreatedAt,
        RespondedAt = j.RespondedAt,
        StartedAt = j.StartedAt,
        CompletedAt = j.CompletedAt
    };
}
