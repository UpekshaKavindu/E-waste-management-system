using System.Text.Json;
using EWasteManagement.Api.Entities;
using EWasteManagement.API.Features.Collection.DTOs;
using EWasteManagement.API.Features.Collection.Entities;
using EWasteManagement.API.Features.Collection.Services;
using EWasteManagement.API.Features.Workflow.DTOs;
using EWasteManagement.API.Features.Workflow.Entities;
using EWasteManagement.API.Features.Workflow.Services;
using EWasteManagement.API.Features.Notifications.Services;
using EWasteManagement.API.Infrastructure.BackgroundTasks;
using EWasteManagement.API.Infrastructure.ExternalServices;
using EWasteManagement.Tests.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EWasteManagement.Tests.Workflow;

/// <summary>
/// The Finalize step with the real JobService/MatchingService/WorkflowService
/// on SQLite. Guards two things:
///  - a Finalize that runs again for the same submission (e.g. re-queued by
///    startup recovery after the job was created) reuses the job instead of
///    creating a second one;
///  - the Matcher's recommended collector still reaches job creation
///    (PreferredCollectorId) and wins over automatic matching.
/// </summary>
public class WorkflowFinalizeTests : CollectionTestBase
{
    // Collector positions; FakeGeoService distances are keyed by these.
    private const decimal NearLat = 6.9100m, NearLng = 79.8600m;
    private const decimal FarLat = 6.9500m, FarLng = 79.9000m;

    private WorkflowOrchestrationService CreateOrchestrator(IJobService? jobService = null, IMatcherAgentClient? matcher = null) => new(
        new WorkflowService(Db, new ConfigurationBuilder().Build(), new NotificationService(Db)),
        new WorkflowBackgroundQueue(),
        new ReadyPlanner(),
        new UnusedAgents(),
        new UnusedAgents(),
        matcher ?? new UnusedAgents(),
        jobService ?? CreateJobService(),
        Geo,
        NullLogger<WorkflowOrchestrationService>.Instance);

    private async Task<(Collector Near, Collector Far)> SeedTwoCollectorsAsync()
    {
        var near = await SeedCollectorAsync(latitude: NearLat, longitude: NearLng);
        var far = await SeedCollectorAsync(latitude: FarLat, longitude: FarLng);
        Geo.SetDistanceFrom(NearLat, NearLng, (2m, 5));
        Geo.SetDistanceFrom(FarLat, FarLng, (10m, 25));
        return (near, far);
    }

    // A submission plus a workflow parked at Finalizing, with every earlier
    // step's result already stored, i.e. exactly what a crash right before
    // (or during) Finalize leaves behind.
    private async Task<CollectionWorkflow> SeedWorkflowAtFinalizingAsync(
        Guid? recommendedCollectorId, bool? autoAssign = null, string reasoning = "test")
    {
        var submission = new Submission
        {
            UserId = Guid.NewGuid(),
            Category = "IT Equipment",
            PickupAddress = "12 Galle Road, Colombo 03",
            PhoneNumber = "0771234567",
            Items = { new SubmissionItem { ItemName = "Laptop", Description = "Old laptop" } },
        };
        Db.Submissions.Add(submission);

        var workflow = new CollectionWorkflow
        {
            SubmissionId = submission.Id,
            Status = WorkflowStatus.Finalizing,
            AnalyzerResultJson = JsonSerializer.Serialize(new AnalyzerResultRequest
            {
                WasteCategory = "IT Equipment", HazardLevel = "Low",
                EstimatedVolumeKg = 2m, EstimatedValueLkr = 40m, ConfidenceScore = 0.95,
            }),
            ValidatorResultJson = JsonSerializer.Serialize(new ValidatorResultRequest
            {
                ApprovedForAutoAssignment = true,
            }),
            MatcherResultJson = JsonSerializer.Serialize(new MatcherResultRequest
            {
                RecommendedCollectorId = recommendedCollectorId,
                AutoAssign = autoAssign ?? recommendedCollectorId is not null,
                Reasoning = reasoning,
            }),
        };
        Db.CollectionWorkflows.Add(workflow);
        await Db.SaveChangesAsync();
        return workflow;
    }

    private Task<List<Job>> JobsForAsync(Guid submissionId) =>
        Db.Jobs.AsNoTracking().Where(j => j.SubmissionId == submissionId).ToListAsync();

    private Task<CollectionWorkflow> ReloadAsync(Guid workflowId) =>
        Db.CollectionWorkflows.AsNoTracking().SingleAsync(w => w.WorkflowId == workflowId);

    // ---------- duplicate-job guard ----------

    [Fact]
    public async Task Finalize_reuses_an_existing_job_instead_of_creating_a_second_one()
    {
        var (near, far) = await SeedTwoCollectorsAsync();
        var workflow = await SeedWorkflowAtFinalizingAsync(recommendedCollectorId: far.CollectorId);

        // The job an earlier, interrupted Finalize already created.
        var existing = new Job
        {
            SubmissionId = workflow.SubmissionId, PickupAddress = "12 Galle Road, Colombo 03",
            CollectorId = near.CollectorId, Status = JobStatus.Accepted,
        };
        Db.Jobs.Add(existing);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        var geocodeCallsBefore = Geo.GeocodeCalls;

        await CreateOrchestrator().RunChainAsync(workflow.WorkflowId);

        var job = Assert.Single(await JobsForAsync(workflow.SubmissionId));
        Assert.Equal(existing.JobId, job.JobId);
        // Untouched: not re-matched to the recommended collector, not reset.
        Assert.Equal(near.CollectorId, job.CollectorId);
        Assert.Equal(JobStatus.Accepted, job.Status);
        Assert.Equal(geocodeCallsBefore, Geo.GeocodeCalls);   // CreateAndAssignAsync never ran

        var finished = await ReloadAsync(workflow.WorkflowId);
        Assert.Equal(WorkflowStatus.Completed, finished.Status);
        Assert.Equal(existing.JobId, finished.ResultingJobId);
    }

    [Fact]
    public async Task Finalize_running_twice_after_a_crash_still_leaves_exactly_one_job()
    {
        var (_, far) = await SeedTwoCollectorsAsync();
        var workflow = await SeedWorkflowAtFinalizingAsync(recommendedCollectorId: far.CollectorId);

        await CreateOrchestrator().RunChainAsync(workflow.WorkflowId);
        var first = Assert.Single(await JobsForAsync(workflow.SubmissionId));

        // Simulate a crash after the job was created but before the workflow
        // was marked Completed: startup recovery would re-queue it at Finalizing.
        var stuck = await Db.CollectionWorkflows.SingleAsync(w => w.WorkflowId == workflow.WorkflowId);
        stuck.Status = WorkflowStatus.Finalizing;
        stuck.ResultingJobId = null;
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        await CreateOrchestrator().RunChainAsync(workflow.WorkflowId);

        var job = Assert.Single(await JobsForAsync(workflow.SubmissionId));
        Assert.Equal(first.JobId, job.JobId);
        Assert.Equal(far.CollectorId, job.CollectorId);
        var finished = await ReloadAsync(workflow.WorkflowId);
        Assert.Equal(WorkflowStatus.Completed, finished.Status);
        Assert.Equal(first.JobId, finished.ResultingJobId);
    }

    // ---------- PreferredCollectorId still takes effect ----------

    [Fact]
    public async Task Finalize_assigns_the_matchers_recommended_collector_over_the_nearest_one()
    {
        var (near, far) = await SeedTwoCollectorsAsync();
        var workflow = await SeedWorkflowAtFinalizingAsync(recommendedCollectorId: far.CollectorId);

        await CreateOrchestrator().RunChainAsync(workflow.WorkflowId);

        var job = Assert.Single(await JobsForAsync(workflow.SubmissionId));
        Assert.Equal(far.CollectorId, job.CollectorId);
        Assert.NotEqual(near.CollectorId, job.CollectorId);
        Assert.Equal(JobStatus.Assigned, job.Status);

        var history = await Db.JobAssignmentHistory.AsNoTracking().SingleAsync(h => h.JobId == job.JobId);
        Assert.Equal(far.CollectorId, history.CollectorId);
        Assert.Equal(JobService.MatcherRecommendationFollowed, history.Reason);

        var finished = await ReloadAsync(workflow.WorkflowId);
        Assert.Equal(WorkflowStatus.Completed, finished.Status);
        Assert.Equal(job.JobId, finished.ResultingJobId);
    }

    [Fact]
    public async Task Without_a_recommendation_Finalize_falls_back_to_the_nearest_collector()
    {
        // Control for the test above: shows the recommendation is what moved
        // the job to the farther collector.
        var (near, _) = await SeedTwoCollectorsAsync();
        var workflow = await SeedWorkflowAtFinalizingAsync(recommendedCollectorId: null, autoAssign: true);

        await CreateOrchestrator().RunChainAsync(workflow.WorkflowId);

        var job = Assert.Single(await JobsForAsync(workflow.SubmissionId));
        Assert.Equal(near.CollectorId, job.CollectorId);
    }

    // ---------- Matcher didn't auto-assign: staff pick the collector ----------

    private const string AmbiguousReasoning = "Top two candidates within 3.0 points — treated as ambiguous. Leaving for staff to confirm.";

    [Fact]
    public async Task Finalize_without_auto_assign_creates_an_unassigned_job_awaiting_staff()
    {
        var (_, far) = await SeedTwoCollectorsAsync();
        var workflow = await SeedWorkflowAtFinalizingAsync(far.CollectorId, autoAssign: false, reasoning: AmbiguousReasoning);

        await CreateOrchestrator().RunChainAsync(workflow.WorkflowId);

        var job = Assert.Single(await JobsForAsync(workflow.SubmissionId));
        Assert.Equal(JobStatus.AwaitingStaffAssignment, job.Status);
        Assert.Null(job.CollectorId);
        Assert.Equal(AmbiguousReasoning, job.MatcherReasoning);
        Assert.Empty(await Db.JobAssignmentHistory.AsNoTracking().Where(h => h.JobId == job.JobId).ToListAsync());

        var finished = await ReloadAsync(workflow.WorkflowId);
        Assert.Equal(WorkflowStatus.Completed, finished.Status);
        Assert.Equal(job.JobId, finished.ResultingJobId);
    }

    [Fact]
    public async Task Finalize_without_auto_assign_and_nobody_eligible_is_no_collector_available()
    {
        var workflow = await SeedWorkflowAtFinalizingAsync(null, autoAssign: false,
            reasoning: "No collector candidates were returned. Needs staff review.");

        await CreateOrchestrator().RunChainAsync(workflow.WorkflowId);

        var job = Assert.Single(await JobsForAsync(workflow.SubmissionId));
        Assert.Equal(JobStatus.NoCollectorAvailable, job.Status);
        Assert.Null(job.CollectorId);
        Assert.Equal("No collector candidates were returned. Needs staff review.", job.MatcherReasoning);
    }

    [Fact]
    public async Task Matcher_not_auto_assigning_goes_to_finalize_instead_of_admin_approval()
    {
        var (near, _) = await SeedTwoCollectorsAsync();
        var workflow = await SeedWorkflowAtFinalizingAsync(null);
        var atMatching = await Db.CollectionWorkflows.SingleAsync(w => w.WorkflowId == workflow.WorkflowId);
        atMatching.Status = WorkflowStatus.Matching;
        atMatching.MatcherResultJson = null;
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var matcher = new FixedMatcher(new MatcherAgentResult
        {
            RecommendedCollectorId = near.CollectorId, AutoAssign = false, Reasoning = AmbiguousReasoning,
        });
        await CreateOrchestrator(matcher: matcher).RunChainAsync(workflow.WorkflowId);

        var finished = await ReloadAsync(workflow.WorkflowId);
        Assert.Equal(WorkflowStatus.Completed, finished.Status);
        Assert.False(finished.ApprovalRequired);

        var job = Assert.Single(await JobsForAsync(workflow.SubmissionId));
        Assert.Equal(JobStatus.AwaitingStaffAssignment, job.Status);
        Assert.Equal(AmbiguousReasoning, job.MatcherReasoning);
        Assert.Equal(finished.ResultingJobId, job.JobId);
    }

    [Fact]
    public async Task Staff_can_assign_a_job_the_matcher_left_for_them()
    {
        var (_, far) = await SeedTwoCollectorsAsync();
        var workflow = await SeedWorkflowAtFinalizingAsync(far.CollectorId, autoAssign: false, reasoning: AmbiguousReasoning);
        await CreateOrchestrator().RunChainAsync(workflow.WorkflowId);
        var job = Assert.Single(await JobsForAsync(workflow.SubmissionId));

        var result = await CreateJobService().ReassignAsync(job.JobId, new ReassignJobDto { CollectorId = far.CollectorId });

        Assert.Equal(nameof(JobStatus.Assigned), result.Status);
        Assert.Equal(far.CollectorId, result.CollectorId);
    }

    [Fact]
    public async Task Finalize_passes_the_recommended_collector_to_job_creation()
    {
        var (_, far) = await SeedTwoCollectorsAsync();
        var workflow = await SeedWorkflowAtFinalizingAsync(recommendedCollectorId: far.CollectorId);
        var recording = new RecordingJobService(CreateJobService());

        await CreateOrchestrator(recording).RunChainAsync(workflow.WorkflowId);

        var dto = Assert.Single(recording.CreateCalls);
        Assert.Equal(far.CollectorId, dto.PreferredCollectorId);
        Assert.Equal(workflow.SubmissionId, dto.SubmissionId);
        Assert.Equal("12 Galle Road, Colombo 03", dto.PickupAddress);
        Assert.Equal(2m, dto.RequiredCapacityKg);
    }

    // ---------- fakes ----------

    // Finalize is the only agent call these tests should make.
    private sealed class ReadyPlanner : IPlannerAgentClient
    {
        public Task<PlanAgentResult> PlanAsync(Guid workflowId, Guid submissionId, CancellationToken ct = default)
            => throw new InvalidOperationException("Plan should not run in a Finalize test.");

        public Task<FinalizeAgentResult> FinalizeAsync(Guid workflowId, object analyzerResult, object validatorResult,
            object? matcherResult, CancellationToken ct = default)
            => Task.FromResult(new FinalizeAgentResult
            {
                WorkflowId = workflowId, ReadyForJobCreation = true, FinalReasoningSummary = "Ready.",
            });
    }

    private sealed class UnusedAgents : IAnalyzerAgentClient, IValidatorAgentClient, IMatcherAgentClient
    {
        public Task<AnalyzerAgentResult> RunAsync(Guid workflowId, Guid submissionId, CancellationToken ct = default)
            => throw new InvalidOperationException("Analyzer should not run in a Finalize test.");

        public Task<ValidatorAgentResult> RunAsync(Guid workflowId, Guid submissionId, AnalyzerAgentResult analyzerResult, CancellationToken ct = default)
            => throw new InvalidOperationException("Validator should not run in a Finalize test.");

        public Task<MatcherAgentResult> RunAsync(Guid workflowId, decimal pickupLatitude, decimal pickupLongitude,
            decimal estimatedWeightKg, decimal estimatedValueLkr, bool alreadyEscalated,
            List<Guid>? excludeCollectorIds = null, CancellationToken ct = default)
            => throw new InvalidOperationException("Matcher should not run in a Finalize test.");
    }

    private sealed class FixedMatcher : IMatcherAgentClient
    {
        private readonly MatcherAgentResult _result;
        public FixedMatcher(MatcherAgentResult result) => _result = result;

        public Task<MatcherAgentResult> RunAsync(Guid workflowId, decimal pickupLatitude, decimal pickupLongitude,
            decimal estimatedWeightKg, decimal estimatedValueLkr, bool alreadyEscalated,
            List<Guid>? excludeCollectorIds = null, CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    // Records what Finalize hands to job creation, then delegates to the real service.
    private sealed class RecordingJobService : IJobService
    {
        private readonly IJobService _inner;
        public RecordingJobService(IJobService inner) => _inner = inner;
        public List<CreateJobDto> CreateCalls { get; } = new();

        public Task<JobResponseDto> CreateAndAssignAsync(
            CreateJobDto dto)
        {
            CreateCalls.Add(dto);
            return _inner.CreateAndAssignAsync(dto);
        }

        public Task<Guid?> FindJobIdForSubmissionAsync(Guid submissionId) => _inner.FindJobIdForSubmissionAsync(submissionId);

        public Task<JobResponseDto> AcceptAsync(Guid jobId, Guid requestingUserId) => throw new NotSupportedException();
        public Task<JobResponseDto> RejectAsync(Guid jobId, Guid requestingUserId, RejectJobDto dto) => throw new NotSupportedException();
        public Task<JobResponseDto> StartAsync(Guid jobId, Guid requestingUserId) => throw new NotSupportedException();
        public Task<JobResponseDto> CompleteAsync(Guid jobId, Guid requestingUserId, CompleteJobDto dto) => throw new NotSupportedException();
        public Task<List<JobResponseDto>> GetMyJobsAsync(Guid requestingUserId, JobStatus? status) => throw new NotSupportedException();
        public Task<JobResponseDto?> GetByIdAsync(Guid jobId, Guid requestingUserId, bool isPrivileged) => throw new NotSupportedException();
        public Task<JobRouteDto?> GetRouteAsync(Guid jobId, Guid requestingUserId, decimal? fromLat, decimal? fromLng) => throw new NotSupportedException();
        public Task<List<JobResponseDto>> GetAllAsync(JobStatus? status) => throw new NotSupportedException();
        public Task<List<JobAssignmentHistoryDto>> GetHistoryAsync(Guid jobId) => throw new NotSupportedException();
        public Task<JobResponseDto> UpdateAddressAsync(Guid jobId, UpdateJobAddressDto dto) => throw new NotSupportedException();
        public Task<JobResponseDto> ReassignAsync(Guid jobId, ReassignJobDto dto) => throw new NotSupportedException();
        public Task<JobResponseDto> CancelAsync(Guid jobId) => throw new NotSupportedException();
    }
}
