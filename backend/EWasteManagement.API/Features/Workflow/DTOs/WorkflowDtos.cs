namespace EWasteManagement.API.Features.Workflow.DTOs;

// ---- Agent-facing (POST /api/agent/...) ----

public class PlanResultRequest
{
    public object PlanJson { get; set; } = new();
    public bool SkipMatcher { get; set; }
    public string Reasoning { get; set; } = string.Empty;
}

// The top-level fields are the whole submission (worst hazard, total weight and value, lowest
// confidence) — what the Validator checks. Items holds each item's own classification.
public class AnalyzerResultRequest
{
    public string WasteCategory { get; set; } = string.Empty;
    public string HazardLevel { get; set; } = string.Empty;
    public decimal EstimatedVolumeKg { get; set; }
    public decimal EstimatedValueLkr { get; set; }
    public double ConfidenceScore { get; set; }
    public List<AnalyzedItem> Items { get; set; } = new();
}

public class AnalyzedItem
{
    public string ItemName { get; set; } = string.Empty;
    public string WasteCategory { get; set; } = string.Empty;
    public string HazardLevel { get; set; } = string.Empty;
    public decimal EstimatedVolumeKg { get; set; }   // whole row: per unit × quantity
    public decimal EstimatedValueLkr { get; set; }   // whole row
    public double ConfidenceScore { get; set; }
    public int Quantity { get; set; } = 1;
}

public class ValidatorResultRequest
{
    public bool ApprovedForAutoAssignment { get; set; }
    public bool RequiresHumanApproval { get; set; }
    public List<string> Reasons { get; set; } = new();
}

public class MatcherResultRequest
{
    public object RankedCollectors { get; set; } = new();
    public Guid? RecommendedCollectorId { get; set; }
    public bool AutoAssign { get; set; }
    public bool Ambiguous { get; set; }
    public string Reasoning { get; set; } = string.Empty;
}

public class FinalizeResultRequest
{
    public string FinalReasoningSummary { get; set; } = string.Empty;
    public bool ReadyForJobCreation { get; set; }
}

public class ExecutionLogRequest
{
    public Guid WorkflowId { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public int StepNumber { get; set; }
    public object InputJson { get; set; } = new();
    public object? OutputJson { get; set; }
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
}

public class BusinessRulesResponse
{
    public string AutoHazardCeiling { get; set; } = "Medium";
    public double MinConfidenceForAuto { get; set; } = 0.6;
    public decimal MaxValueForAutoLkr { get; set; } = 150000.0m;
    public List<string> RequiredFields { get; set; } = new() { "wasteCategory", "hazardLevel" };
}

public class SubmissionSnapshotResponse
{
    public Guid SubmissionId { get; set; }
    public string SubmissionType { get; set; } = "Household";
    public string Description { get; set; } = string.Empty;
    public List<string> ImageUrls { get; set; } = new();
    public string PickupAddress { get; set; } = string.Empty;

    // What the customer told us, as hints for the Analyzer (most useful when there is no photo).
    public string Category { get; set; } = string.Empty;
    public decimal EstimatedWeightKg { get; set; }

    // "Manual" items are classified with their photos; "Csv" rows as text in batches.
    public string Source { get; set; } = "Manual";

    // One entry per item / CSV row, in the order given — the Analyzer classifies each.
    public List<SubmissionSnapshotItem> Items { get; set; } = new();
}

public class SubmissionSnapshotItem
{
    public string ItemName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal? EstimatedWeightKg { get; set; }   // per unit, customer-supplied (CSV)
    public string? CategoryHint { get; set; }          // CSV category column, a hint only
}

// ---- Staff-facing (/api/workflows/...) ----

public class WorkflowResponse
{
    public Guid WorkflowId { get; set; }
    public Guid SubmissionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PlanJson { get; set; }
    public string? AnalyzerResultJson { get; set; }
    public string? ValidatorResultJson { get; set; }
    public string? MatcherResultJson { get; set; }
    public string? FinalReasoningSummary { get; set; }
    public bool ApprovalRequired { get; set; }
    public Guid? ResultingJobId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class WorkflowApprovalDecisionRequest
{
    public string? Comments { get; set; }
}

public class ExecutionLogEntryResponse
{
    public Guid LogId { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public int StepNumber { get; set; }
    public string InputJson { get; set; } = "{}";
    public string? OutputJson { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
}
