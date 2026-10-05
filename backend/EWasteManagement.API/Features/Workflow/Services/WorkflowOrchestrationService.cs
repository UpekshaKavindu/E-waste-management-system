using System.Text.Json;
using EWasteManagement.API.Features.Collection.DTOs;
using EWasteManagement.API.Features.Collection.Services;
using EWasteManagement.API.Features.Workflow.DTOs;
using EWasteManagement.API.Features.Workflow.Entities;
using EWasteManagement.API.Infrastructure.BackgroundTasks;
using EWasteManagement.API.Infrastructure.ExternalServices;

namespace EWasteManagement.API.Features.Workflow.Services;

public class WorkflowOrchestrationService : IWorkflowOrchestrationService
{
    private readonly IWorkflowService _workflows;
    private readonly IWorkflowBackgroundQueue _queue;
    private readonly IPlannerAgentClient _planner;
    private readonly IAnalyzerAgentClient _analyzer;
    private readonly IValidatorAgentClient _validator;
    private readonly IMatcherAgentClient _matcher;
    private readonly IJobService _jobService;
    private readonly IGeoService _geoService;
    private readonly ILogger<WorkflowOrchestrationService> _logger;

    public WorkflowOrchestrationService(
        IWorkflowService workflows,
        IWorkflowBackgroundQueue queue,
        IPlannerAgentClient planner,
        IAnalyzerAgentClient analyzer,
        IValidatorAgentClient validator,
        IMatcherAgentClient matcher,
        IJobService jobService,
        IGeoService geoService,
        ILogger<WorkflowOrchestrationService> logger)
    {
        _workflows = workflows;
        _queue = queue;
        _planner = planner;
        _analyzer = analyzer;
        _validator = validator;
        _matcher = matcher;
        _jobService = jobService;
        _geoService = geoService;
        _logger = logger;
    }

    public void Start(Guid workflowId) => _queue.Enqueue(workflowId);

    public async Task RunChainAsync(Guid workflowId, CancellationToken ct = default)
    {
        // Loop rather than recursion: each stage reloads the workflow after
        // writing, so a resume-after-approval call just re-enters this same
        // loop and picks up wherever the stored Status says to.
        while (true)
        {
            var workflow = await _workflows.GetAsync(workflowId, ct)
                ?? throw new KeyNotFoundException($"Workflow {workflowId} not found.");

            _logger.LogInformation("Workflow {WorkflowId} at status {Status}", workflowId, workflow.Status);

            switch (workflow.Status)
            {
                case WorkflowStatus.Planning:
                    await RunPlanStepAsync(workflow, ct);
                    continue;

                case WorkflowStatus.Analyzing:
                    await RunAnalyzerStepAsync(workflow, ct);
                    continue;

                case WorkflowStatus.Validating:
                    await RunValidatorStepAsync(workflow, ct);
                    continue;

                case WorkflowStatus.Matching:
                    if (ReadSkipMatcher(workflow))
                    {
                        // Plan said this submission doesn't need collector
                        // matching (e.g. a pre-scheduled corporate pickup) —
                        // skip straight to finalize.
                        await _workflows.SetStatusAsync(workflowId, WorkflowStatus.Finalizing, ct);
                        continue;
                    }
                    await RunMatcherStepAsync(workflow, ct);
                    continue;

                case WorkflowStatus.Finalizing:
                    await RunFinalizeStepAsync(workflow, ct);
                    continue;

                case WorkflowStatus.PendingApproval:
                case WorkflowStatus.Completed:
                case WorkflowStatus.Rejected:
                case WorkflowStatus.Failed:
                    // Nothing to do — PendingApproval waits for a human;
                    // the other three are terminal. Safe no-op if this gets
                    // called again (e.g. a duplicate enqueue).
                    return;

                default:
                    throw new InvalidOperationException($"Unhandled workflow status: {workflow.Status}");
            }
        }
    }

    // ---------- Individual steps ----------

    private async Task RunPlanStepAsync(CollectionWorkflow workflow, CancellationToken ct)
    {
        var result = await _planner.PlanAsync(workflow.WorkflowId, workflow.SubmissionId, ct);
        await _workflows.RecordPlanAsync(workflow.WorkflowId, new PlanResultRequest
        {
            PlanJson = result.Steps,
            SkipMatcher = result.SkipMatcher,
            Reasoning = result.Reasoning,
        }, ct);
    }

    private async Task RunAnalyzerStepAsync(CollectionWorkflow workflow, CancellationToken ct)
    {
        var result = await _analyzer.RunAsync(workflow.WorkflowId, workflow.SubmissionId, ct);
        await _workflows.RecordAnalyzerResultAsync(workflow.WorkflowId, new AnalyzerResultRequest
        {
            WasteCategory = result.WasteCategory,
            HazardLevel = result.HazardLevel,
            EstimatedVolumeKg = result.EstimatedVolumeKg,
            EstimatedValueLkr = result.EstimatedValueLkr,
            ConfidenceScore = result.ConfidenceScore,
            Items = result.Items,
        }, ct);
    }

    private async Task RunValidatorStepAsync(CollectionWorkflow workflow, CancellationToken ct)
    {
        var analyzerResult = DeserializeAnalyzerResult(workflow.AnalyzerResultJson);
        var result = await _validator.RunAsync(workflow.WorkflowId, workflow.SubmissionId, analyzerResult, ct);
        await _workflows.RecordValidatorResultAsync(workflow.WorkflowId, new ValidatorResultRequest
        {
            ApprovedForAutoAssignment = result.ApprovedForAutoAssignment,
            RequiresHumanApproval = result.RequiresHumanApproval,
            Reasons = result.Reasons,
        }, ct);
    }

    private async Task RunMatcherStepAsync(CollectionWorkflow workflow, CancellationToken ct)
    {
        var snapshot = await _workflows.GetSubmissionSnapshotAsync(workflow.SubmissionId, ct)
            ?? throw new InvalidOperationException($"Submission {workflow.SubmissionId} not found.");

        var coordinates = await _geoService.GeocodeAsync(snapshot.PickupAddress);
        if (coordinates is null)
        {
            // Same philosophy as JobService: an unresolved address is a
            // safe-failure case for staff, not something to guess through.
            await _workflows.RecordMatcherResultAsync(workflow.WorkflowId, new MatcherResultRequest
            {
                AutoAssign = false,
                Ambiguous = false,
                Reasoning = "Pickup address could not be geocoded — needs staff review.",
            }, ct);
            return;
        }

        var analyzerResult = DeserializeAnalyzerResult(workflow.AnalyzerResultJson);
        var result = await _matcher.RunAsync(
            workflow.WorkflowId,
            coordinates.Value.Latitude, coordinates.Value.Longitude,
            analyzerResult.EstimatedVolumeKg, analyzerResult.EstimatedValueLkr,
            alreadyEscalated: workflow.ApprovalRequired,
            ct: ct);

        await _workflows.RecordMatcherResultAsync(workflow.WorkflowId, new MatcherResultRequest
        {
            RecommendedCollectorId = result.RecommendedCollectorId,
            AutoAssign = result.AutoAssign,
            Ambiguous = result.Ambiguous,
            Reasoning = result.Reasoning,
        }, ct);
    }

    private async Task RunFinalizeStepAsync(CollectionWorkflow workflow, CancellationToken ct)
    {
        var analyzerResult = DeserializeAnalyzerResult(workflow.AnalyzerResultJson);
        var validatorResult = workflow.ValidatorResultJson is null
            ? new { } as object
            : JsonSerializer.Deserialize<ValidatorResultRequest>(workflow.ValidatorResultJson)!;
        var matcherRecommendation = workflow.MatcherResultJson is null
            ? null
            : JsonSerializer.Deserialize<MatcherResultRequest>(workflow.MatcherResultJson);
        var matcherResult = matcherRecommendation as object;

        var finalizeResult = await _planner.FinalizeAsync(
            workflow.WorkflowId,
            new { wasteCategory = analyzerResult.WasteCategory, hazardLevel = analyzerResult.HazardLevel },
            validatorResult,
            matcherResult,
            ct);

        Guid? resultingJobId = null;

        // A workflow re-queued at startup (WorkflowStartupRecovery) can re-run
        // Finalize after it already created the job but before the workflow
        // was marked Completed. Reuse that job instead of creating a second one.
        if (finalizeResult.ReadyForJobCreation)
            resultingJobId = await _jobService.FindJobIdForSubmissionAsync(workflow.SubmissionId);

        if (finalizeResult.ReadyForJobCreation && resultingJobId is null)
        {
            var snapshot = await _workflows.GetSubmissionSnapshotAsync(workflow.SubmissionId, ct);
            var job = await _jobService.CreateAndAssignAsync(new CreateJobDto
            {
                SubmissionId = workflow.SubmissionId,
                PickupAddress = snapshot?.PickupAddress ?? string.Empty,
                RequiredCapacityKg = analyzerResult.EstimatedVolumeKg,
                // Hand the Matcher agent's pick to job creation. JobService
                // uses it if that collector is still eligible, and records
                // it in the job history either way.
                PreferredCollectorId = matcherRecommendation?.RecommendedCollectorId,
                SkipAutoAssign = matcherRecommendation is { AutoAssign: false },
                MatcherReasoning = matcherRecommendation is { AutoAssign: false } ? matcherRecommendation.Reasoning : null,
            });
            resultingJobId = job.JobId;
        }

        await _workflows.RecordFinalizeAsync(workflow.WorkflowId, new FinalizeResultRequest
        {
            FinalReasoningSummary = finalizeResult.FinalReasoningSummary,
            ReadyForJobCreation = finalizeResult.ReadyForJobCreation,
        }, resultingJobId, ct);
    }

    public async Task ResumeAfterApprovalAsync(Guid workflowId, CancellationToken ct = default)
    {
        var workflow = await _workflows.GetAsync(workflowId, ct)
            ?? throw new KeyNotFoundException($"Workflow {workflowId} not found.");

        // Which stage caused the pause tells us where to resume:
        //  - Matcher hasn't run yet  -> this was Validator escalating -> go to Matching.
        //  - Matcher already ran     -> a workflow paused by the Matcher before it
        //                               stopped gating on approval -> go straight to Finalizing.
        // Finalize passes Matcher's recommended collector to JobService as
        // PreferredCollectorId, so an approved recommendation is honoured if
        // that collector is still eligible when the job is created.
        var nextStatus = workflow.MatcherResultJson is null
            ? WorkflowStatus.Matching
            : WorkflowStatus.Finalizing;

        await _workflows.SetStatusAsync(workflowId, nextStatus, ct);
        _queue.Enqueue(workflowId);
    }

    // ---------- Helpers ----------

    private static bool ReadSkipMatcher(CollectionWorkflow workflow)
    {
        if (workflow.PlanJson is null) return false;
        using var doc = JsonDocument.Parse(workflow.PlanJson);
        return doc.RootElement.TryGetProperty("skipMatcher", out var flag) && flag.GetBoolean();
    }

    private static AnalyzerAgentResult DeserializeAnalyzerResult(string? json)
    {
        if (json is null) throw new InvalidOperationException("Analyzer result is missing — Analyzer must run before this step.");
        return JsonSerializer.Deserialize<AnalyzerAgentResult>(json)!;
    }
}
